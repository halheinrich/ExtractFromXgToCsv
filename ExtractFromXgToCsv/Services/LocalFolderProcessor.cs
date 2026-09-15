using BackgammonDiagram_Lib;
using BackgammonDiagram_Lib.ExportRaster;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Shared;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Server-side processor for local mode.
/// Streams through .xg/.xgp files in a folder one at a time,
/// applies a DecisionFilterSet, and writes CSV rows as it goes.
/// Never holds more than one file's rows in memory at a time.
/// <para>
/// A file that fails (and, on the Xgp pathway, a decision whose write fails)
/// is skipped, not fatal, and every skip is recorded through one seam,
/// <see cref="RecordSkip"/>, onto the run's <see cref="SkipRecord"/>, which
/// every snapshot carries as <see cref="ProcessingProgress.Skipped"/>. A
/// caller passes its own record to read it after a run-ending failure.
/// Cancellation is never a skip: every skip catch excludes
/// <see cref="OperationCanceledException"/>.
/// </para>
/// </summary>
public class LocalFolderProcessor
{
    private readonly ILogger<LocalFolderProcessor> _logger;

    // The opening-book iterator options, resolved once by OpeningBookProvider
    // and passed to every XgDecisionIterator call so book-analysed decisions are
    // enriched consistently across all pathways. Null when no book is available
    // (provider absent, disabled, or the file was missing/unreadable) — the
    // iterator reads null as "default behaviour" and stamps the unenriched
    // "Book V2" / level Unknown form. Single field, single source: every
    // extraction pathway below passes this same value.
    private readonly XgIteratorOptions? _iteratorOptions;

    /// <summary>
    /// Creates the processor. <paramref name="logger"/> records per-file skip
    /// warnings; <paramref name="bookProvider"/> supplies the loaded opening
    /// book (optional — a null provider, like a provider that loaded no book,
    /// runs the pipeline unenriched).
    /// </summary>
    public LocalFolderProcessor(
        ILogger<LocalFolderProcessor> logger,
        OpeningBookProvider? bookProvider = null)
    {
        _logger = logger;
        _iteratorOptions = bookProvider?.IteratorOptions;
    }

    /// <summary>
    /// Discovers the .xg/.xgp input files under <paramref name="folderPath"/>,
    /// recursively. The extension set and ordering are the producer's contract
    /// (<see cref="XgFileReader.EnumerateXgFormatFiles(string, SearchOption)"/>):
    /// ascending full path, <c>OrdinalIgnoreCase</c> with an <c>Ordinal</c>
    /// tiebreak — culture-independent, so the resulting numbering order is
    /// stable across machines. The single source of discovery for every
    /// pathway in this processor; the friendly not-found / empty-folder
    /// messages are consumer-owned preludes and stay here (both surface to the
    /// client through the job's ErrorMessage channel).
    /// </summary>
    private static IReadOnlyList<string> DiscoverInputFiles(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        var files = XgFileReader
            .EnumerateXgFormatFiles(folderPath, SearchOption.AllDirectories)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException("No .xg or .xgp files found in folder.");

        return files;
    }

    /// <summary>
    /// The run's skip record: the caller's, when it passed one to read after
    /// a failure, otherwise a fresh one of the run's own.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="skipRecord"/> already holds entries — a record belongs
    /// to one run, and a reused one would report another run's skips as this
    /// run's.
    /// </exception>
    private static SkipRecord RecordFor(SkipRecord? skipRecord)
    {
        if (skipRecord is { IsEmpty: false })
            throw new ArgumentException(
                "A skip record belongs to one run; pass a fresh one.", nameof(skipRecord));
        return skipRecord ?? new SkipRecord();
    }

    /// <summary>
    /// The one skip seam: every per-file and per-decision catch in this
    /// processor calls it, and none logs on its own. Logs the skip and appends
    /// it to <paramref name="skips"/>, the run's record, which every snapshot
    /// the run reports carries a copy of (<see cref="ProcessingProgress.Skipped"/>).
    /// The exception's message is the recorded reason.
    /// <para>
    /// The two names differ on purpose. The record names the file by its path
    /// relative to <paramref name="folderPath"/>, because discovery is
    /// recursive and two subfolders can hold files of the same name; the
    /// record is the only place a skipped file is named, so it must be
    /// unambiguous. The log names it by its bare name, like everything else
    /// the run emits: rows and decision ids carry the bare filename (the
    /// producer's convention — <see cref="DecisionId.Filename"/> holds no
    /// directory), and the log line sits among them.
    /// </para>
    /// </summary>
    /// <param name="skips">The current run's record.</param>
    /// <param name="ex">What skipped the input; never an <see cref="OperationCanceledException"/> — the catches exclude it.</param>
    /// <param name="folderPath">The run's input folder, which the recorded name is relative to.</param>
    /// <param name="file">The source file's full path, as discovered.</param>
    /// <param name="decisionId">The skipped decision, or <see langword="null"/> when the whole file was skipped.</param>
    private void RecordSkip(
        SkipRecord skips,
        Exception ex,
        string folderPath,
        string file,
        DecisionId? decisionId = null)
    {
        var fileName = Path.GetFileName(file);
        if (decisionId is null)
            _logger.LogWarning(ex, "Skipping {File}", fileName);
        else
            _logger.LogWarning(ex, "Skipping decision {DecisionId} in {File}", decisionId, fileName);

        skips.Add(new SkippedItem(Path.GetRelativePath(folderPath, file), decisionId, ex.Message));
    }

    /// <summary>
    /// CSV pathway: streams the folder's <c>.xg</c>/<c>.xgp</c> files one at a
    /// time, applies <paramref name="filterSet"/>, and writes matching
    /// <see cref="DecisionRow"/>s to <paramref name="outputPath"/> as CSV,
    /// reporting through <paramref name="progress"/>. Pass
    /// <paramref name="skipRecord"/> (fresh) to read what the run skipped after
    /// a failure has ended it; the snapshots carry the record either way.
    /// </summary>
    public async Task ProcessAsync(
        string folderPath,
        string outputPath,
        DecisionFilterSet filterSet,
        IProgress<ProcessingProgress> progress,
        SkipRecord? skipRecord = null,
        CancellationToken cancellationToken = default)
    {
        var skips = RecordFor(skipRecord);
        var files = DiscoverInputFiles(folderPath);

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        await using var writer = new StreamWriter(outputPath, append: false, Encoding.UTF8);
        await writer.WriteLineAsync(DecisionRow.CsvHeader);

        int totalRows = 0;
        var stopwatch = Stopwatch.StartNew();
        const int reportEvery = 10; // client polls every second; no need to update on every file

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            var fileName = Path.GetFileName(file);

            if (i % reportEvery == 0)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var filesPerSec = elapsed > 0 ? (int)(i / elapsed) : 0;

                progress.Report(new ProcessingProgress
                {
                    Current = i + 1,
                    Total = files.Count,
                    FileName = fileName,
                    TotalRows = totalRows,
                    ElapsedSec = elapsed,
                    FilesPerSec = filesPerSec,
                    Skipped = skips.Snapshot()
                });
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                using var ms = new MemoryStream(bytes);
                var xgFile = XgFileReader.ReadStream(ms);
                var rows = XgDecisionIterator.Iterate(xgFile, fileName, options: _iteratorOptions);

                foreach (var row in rows.Where(r => filterSet.Matches(r)))
                {
                    await writer.WriteLineAsync(row.ToCsvLine());
                    totalRows++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordSkip(skips, ex, folderPath, file);
            }
        }

        var totalElapsed = stopwatch.Elapsed.TotalSeconds;
        var finalFilesPerSec = totalElapsed > 0 ? (int)(files.Count / totalElapsed) : 0;

        progress.Report(new ProcessingProgress
        {
            Current = files.Count,
            Total = files.Count,
            FileName = "Done",
            TotalRows = totalRows,
            Complete = true,
            ElapsedSec = totalElapsed,
            FilesPerSec = finalFilesPerSec,
            Skipped = skips.Snapshot()
        });
    }
    /// <summary>
    /// Diagram-JSON pathway: same per-file streaming as <see cref="ProcessAsync"/>,
    /// but buffers matching decisions and writes them to
    /// <paramref name="outputPath"/> as a single indented JSON array.
    /// <paramref name="skipRecord"/> as on <see cref="ProcessAsync"/>.
    /// </summary>
    public async Task ProcessDiagramAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            SkipRecord? skipRecord = null,
            CancellationToken cancellationToken = default)
    {
        var skips = RecordFor(skipRecord);
        var files = DiscoverInputFiles(folderPath);

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var allItems = new List<BgDecisionData>();
        int totalRows = 0;
        var stopwatch = Stopwatch.StartNew();
        const int reportEvery = 10;

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            var fileName = Path.GetFileName(file);

            if (i % reportEvery == 0)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var filesPerSec = elapsed > 0 ? (int)(i / elapsed) : 0;

                progress.Report(new ProcessingProgress
                {
                    Current = i + 1,
                    Total = files.Count,
                    FileName = fileName,
                    TotalRows = totalRows,
                    ElapsedSec = elapsed,
                    FilesPerSec = filesPerSec,
                    Skipped = skips.Snapshot()
                });
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                using var ms = new MemoryStream(bytes);
                var xgFile = XgFileReader.ReadStream(ms);
                var items = XgDecisionIterator.IterateDiagramRequests(
                    xgFile, fileName, options: _iteratorOptions);

                foreach (var item in items.Where(r => filterSet.Matches(r)))
                {
                    allItems.Add(item);
                    totalRows++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordSkip(skips, ex, folderPath, file);
            }
        }

        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(allItems, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        var totalElapsed = stopwatch.Elapsed.TotalSeconds;
        var finalFilesPerSec = totalElapsed > 0 ? (int)(files.Count / totalElapsed) : 0;

        progress.Report(new ProcessingProgress
        {
            Current = files.Count,
            Total = files.Count,
            FileName = "Done",
            TotalRows = totalRows,
            Complete = true,
            ElapsedSec = totalElapsed,
            FilesPerSec = finalFilesPerSec,
            Skipped = skips.Snapshot()
        });
    }

    /// <summary>
    /// Xgp pathway: writes one .xgp position file per filtered decision into
    /// the <paramref name="outputPath"/> <b>folder</b> (created if absent;
    /// same-named files are overwritten — counter discipline is the
    /// client's). Filenames come from <paramref name="options"/>' pattern via
    /// an <see cref="XgpNameAllocator"/>: batch-constant tokens draw from
    /// <paramref name="filters"/>, per-item tokens from each decision, and
    /// duplicate rendered names within the run get Windows-style
    /// <c>" (2)"</c> suffixes. The allocator's Peek/Commit split keeps the
    /// counter honest: a decision's name is computed before the write and its
    /// slot consumed only after the write succeeds, so failed decisions don't
    /// burn a number. Decisions from .xg sources are sliced via
    /// <see cref="XgpExporter"/> (analysis carried through); decisions from
    /// .xgp sources are copied verbatim, mirroring the Web-mode rule in
    /// <c>XgProcessingService.BuildXgpZip</c>.
    /// <para>
    /// When <paramref name="anonymize"/> is <see langword="true"/>, every
    /// written position has its player names rewritten to the anonymize preset
    /// (<see cref="XgpSliceOptions.Anonymized"/> — the producer's SSOT): an
    /// .xg slice takes the options-bearing overload, an .xgp source takes the
    /// whole-file anonymize-copy (comments and rollouts preserved, only the
    /// header names change), so the toggle covers every entry. Every written
    /// position is a single decision, so the preset names by role — "On-roll"
    /// for the decision-maker (a cube decision's doubler), "Opponent" for the
    /// other — with the producer resolving which header slot each role lands
    /// in, per position.
    /// </para>
    /// <para>
    /// <paramref name="skipRecord"/> as on <see cref="ProcessAsync"/>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="options"/> fails validation (surfaces to
    /// the client through the job's ErrorMessage channel), or when
    /// <paramref name="skipRecord"/> is not fresh.
    /// </exception>
    public async Task ProcessXgpAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            XgpExportOptions options,
            FilterConfig filters,
            IProgress<ProcessingProgress> progress,
            bool anonymize = false,
            SkipRecord? skipRecord = null,
            CancellationToken cancellationToken = default)
    {
        // Create validates the options — the single options-validation throw
        // point shared with the Web-mode pathway.
        var allocator = XgpNameAllocator.Create(options, filters);
        var skips = RecordFor(skipRecord);

        var files = DiscoverInputFiles(folderPath);

        Directory.CreateDirectory(outputPath);

        // The service owns the bool -> producer-type mapping: the request
        // carries only intent. null keeps the current byte-for-byte behaviour;
        // the preset is the producer's single source of "anonymized".
        var nameOverrides = anonymize ? XgpSliceOptions.Anonymized : null;

        int totalRows = 0;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < files.Count; i++)
        {
            // Unlike the siblings' reportEvery=10, this pathway reports on
            // EVERY file, and reports before the cancellation check: the
            // client persists its numbering counter from the last reported
            // TotalRows, so a cancelled job must leave an exact count behind.
            var elapsed = stopwatch.Elapsed.TotalSeconds;
            progress.Report(new ProcessingProgress
            {
                Current = i + 1,
                Total = files.Count,
                FileName = Path.GetFileName(files[i]),
                TotalRows = totalRows,
                ElapsedSec = elapsed,
                FilesPerSec = elapsed > 0 ? (int)(i / elapsed) : 0,
                Skipped = skips.Snapshot()
            });

            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            var fileName = Path.GetFileName(file);

            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                using var ms = new MemoryStream(bytes);
                var xgFile = XgFileReader.ReadStream(ms);
                var rows = XgDecisionIterator.Iterate(xgFile, fileName, options: _iteratorOptions);

                foreach (var row in rows.Where(r => filterSet.Matches(r)))
                {
                    // Peek, don't consume: the slot is committed only after
                    // the write succeeds, so a failed decision doesn't burn
                    // a name (the client persists its numbering counter from
                    // the reported TotalRows).
                    var target = Path.Combine(outputPath, allocator.Peek(row));
                    try
                    {
                        switch (row.Id)
                        {
                            case XgpDecisionId:
                                // Already a single-position analyzed .xgp.
                                // Not anonymizing: copy verbatim (same rule as
                                // Web mode). Anonymizing: whole-file re-emit
                                // with only the header names rewritten
                                // (comments and rollouts preserved). Both are
                                // deliberately not cancellable: cancel
                                // granularity is the file boundary, so the
                                // reported TotalRows never lags a partial
                                // batch of written decisions.
                                if (nameOverrides is null)
                                    await File.WriteAllBytesAsync(target, bytes);
                                else
                                    XgpExporter.WriteFile(xgFile, nameOverrides, target);
                                break;
                            case XgDecisionId xgId:
                                // Pass the typed Id straight to the producer's
                                // Id overload (coordinates destructured
                                // internally; Filename ignored — the source is
                                // already resolved). Without overrides this is
                                // byte-identical to the coordinate overload.
                                if (nameOverrides is null)
                                    XgpExporter.WriteFile(xgFile, xgId, target);
                                else
                                    XgpExporter.WriteFile(xgFile, xgId, nameOverrides, target);
                                break;
                            default:
                                throw new NotSupportedException(
                                    $"Unsupported DecisionId shape '{row.Id.GetType().Name}' for .xgp export.");
                        }
                        allocator.Commit(row);
                        totalRows++; // failed decisions don't consume a number
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        RecordSkip(skips, ex, folderPath, file, row.Id);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordSkip(skips, ex, folderPath, file);
            }
        }

        var totalElapsed = stopwatch.Elapsed.TotalSeconds;

        progress.Report(new ProcessingProgress
        {
            Current = files.Count,
            Total = files.Count,
            FileName = "Done",
            TotalRows = totalRows,
            Complete = true,
            ElapsedSec = totalElapsed,
            FilesPerSec = totalElapsed > 0 ? (int)(files.Count / totalElapsed) : 0,
            Skipped = skips.Snapshot()
        });
    }

    /// <summary>
    /// PPTX pathway: collects filtered decisions into a Problem/Solution slide
    /// deck at <paramref name="outputPath"/>. Thin wrapper over the shared deck
    /// helper (PDF twin: <see cref="ProcessPdfAsync"/>). Local mode only —
    /// rendering is server-side. <paramref name="skipRecord"/> as on
    /// <see cref="ProcessAsync"/>.
    /// </summary>
    public Task ProcessPptxAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            SkipRecord? skipRecord = null,
            CancellationToken cancellationToken = default)
        => ProcessDeckAsync(
            folderPath, outputPath, filterSet, progress,
            (reqs, opts) => DiagramRasterRenderer.RenderPptx(reqs, opts),
            "PPTX", skipRecord, cancellationToken);

    /// <summary>
    /// PDF pathway: collects filtered decisions into a Problem/Solution page
    /// deck at <paramref name="outputPath"/>. Thin wrapper over the shared deck
    /// helper (the PDF twin of <see cref="ProcessPptxAsync"/>). Local mode only
    /// — rendering is server-side. <paramref name="skipRecord"/> as on
    /// <see cref="ProcessAsync"/>.
    /// </summary>
    public Task ProcessPdfAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            SkipRecord? skipRecord = null,
            CancellationToken cancellationToken = default)
        => ProcessDeckAsync(
            folderPath, outputPath, filterSet, progress,
            (reqs, opts) => DiagramRasterRenderer.RenderPdf(reqs, opts),
            "PDF", skipRecord, cancellationToken);

    private async Task ProcessDeckAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            Func<IEnumerable<DiagramRequest>, DiagramOptions, byte[]> renderer,
            string formatLabel,
            SkipRecord? skipRecord,
            CancellationToken cancellationToken)
    {
        var skips = RecordFor(skipRecord);
        var files = DiscoverInputFiles(folderPath);

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var requests = new List<DiagramRequest>();
        int totalRows = 0;
        var stopwatch = Stopwatch.StartNew();
        const int reportEvery = 10;

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            var fileName = Path.GetFileName(file);

            if (i % reportEvery == 0)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var filesPerSec = elapsed > 0 ? (int)(i / elapsed) : 0;

                progress.Report(new ProcessingProgress
                {
                    Current = i + 1,
                    Total = files.Count,
                    FileName = fileName,
                    TotalRows = totalRows,
                    ElapsedSec = elapsed,
                    FilesPerSec = filesPerSec,
                    Skipped = skips.Snapshot()
                });
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                using var ms = new MemoryStream(bytes);
                var xgFile = XgFileReader.ReadStream(ms);
                var items = XgDecisionIterator.IterateDiagramRequests(
                    xgFile, fileName, options: _iteratorOptions, logger: _logger);

                foreach (var item in items.Where(r => filterSet.Matches(r)))
                {
                    // Each decision becomes a Problem/Solution pair — the
                    // reader considers the problem slide, then advances to the
                    // solution slide for the answer.
                    var (problem, solution) = DiagramRequest
                        .FromDecisionData(item)
                        .ToProblemSolutionPair();
                    requests.Add(problem);
                    requests.Add(solution);
                    totalRows++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordSkip(skips, ex, folderPath, file);
            }
        }

        // Render and write are atomic — the renderer returns the full byte[]
        // and mid-render cancellation isn't supported. Cancellation gating
        // happens during the per-file collect loop above.
        //
        // This is the last snapshot the client sees until the run finishes, and
        // the render dominates the wall clock (minutes, at corpus scale), so it
        // is stamped JobPhase.Rendering: the counter and the elapsed/throughput
        // figures below have stopped advancing, and the phase is what tells the
        // client to stop presenting them as live.
        progress.Report(new ProcessingProgress
        {
            Current = files.Count,
            Total = files.Count,
            Phase = JobPhase.Rendering,
            FileName = $"Rendering {formatLabel} ({totalRows} decisions, {requests.Count} slides)…",
            TotalRows = totalRows,
            ElapsedSec = stopwatch.Elapsed.TotalSeconds,
            FilesPerSec = stopwatch.Elapsed.TotalSeconds > 0
                ? (int)(files.Count / stopwatch.Elapsed.TotalSeconds) : 0,
            Skipped = skips.Snapshot()
        });

        if (requests.Count == 0)
            throw new InvalidOperationException(
                "No decisions matched the filter — nothing to render.");

        var deckBytes = renderer(requests, new DiagramOptions());
        await File.WriteAllBytesAsync(outputPath, deckBytes, cancellationToken);

        var totalElapsed = stopwatch.Elapsed.TotalSeconds;
        var finalFilesPerSec = totalElapsed > 0 ? (int)(files.Count / totalElapsed) : 0;

        progress.Report(new ProcessingProgress
        {
            Current = files.Count,
            Total = files.Count,
            FileName = "Done",
            TotalRows = totalRows,
            Complete = true,
            ElapsedSec = totalElapsed,
            FilesPerSec = finalFilesPerSec,
            Skipped = skips.Snapshot()
        });
    }
}
