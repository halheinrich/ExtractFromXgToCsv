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
/// </summary>
public class LocalFolderProcessor
{
    private readonly ILogger<LocalFolderProcessor> _logger;

    public LocalFolderProcessor(ILogger<LocalFolderProcessor> logger)
    {
        _logger = logger;
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

    public async Task ProcessAsync(
        string folderPath,
        string outputPath,
        DecisionFilterSet filterSet,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken = default)
    {
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
                    FilesPerSec = filesPerSec
                });
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                using var ms = new MemoryStream(bytes);
                var xgFile = XgFileReader.ReadStream(ms);
                var rows = XgDecisionIterator.Iterate(xgFile, fileName);

                foreach (var row in rows.Where(r => filterSet.Matches(r)))
                {
                    await writer.WriteLineAsync(row.ToCsvLine());
                    totalRows++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping {File}", fileName);
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
            FilesPerSec = finalFilesPerSec
        });
    }
    public async Task ProcessDiagramAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            CancellationToken cancellationToken = default)
    {
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
                    FilesPerSec = filesPerSec
                });
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                using var ms = new MemoryStream(bytes);
                var xgFile = XgFileReader.ReadStream(ms);
                var items = XgDecisionIterator.IterateDiagramRequests(xgFile, fileName);

                foreach (var item in items.Where(r => filterSet.Matches(r)))
                {
                    allItems.Add(item);
                    totalRows++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping {File}", fileName);
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
            FilesPerSec = finalFilesPerSec
        });
    }

    /// <summary>
    /// Xgp pathway: writes one .xgp position file per filtered decision into
    /// the <paramref name="outputPath"/> <b>folder</b> (created if absent;
    /// same-named files are overwritten — counter discipline is the
    /// client's). Decisions from .xg sources are sliced via
    /// <see cref="XgpExporter"/> (analysis carried through); decisions from
    /// .xgp sources are copied verbatim, mirroring the Web-mode rule in
    /// <c>XgProcessingService.BuildXgpZip</c>.
    /// <para>
    /// When <paramref name="anonymize"/> is <see langword="true"/>, every
    /// written position has its player names rewritten to the neutral preset
    /// (<see cref="XgpSliceOptions.Anonymized"/> — the producer's SSOT): an
    /// .xg slice takes the options-bearing overload, an .xgp source takes the
    /// whole-file anonymize-copy (comments and rollouts preserved, only the
    /// header names change), so the toggle covers every entry.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="options"/> fails validation (surfaces to
    /// the client through the job's ErrorMessage channel).
    /// </exception>
    public async Task ProcessXgpAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            XgpExportOptions options,
            IProgress<ProcessingProgress> progress,
            bool anonymize = false,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.TryValidate(out var optionsError))
            throw new ArgumentException(optionsError, nameof(options));

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
                FilesPerSec = elapsed > 0 ? (int)(i / elapsed) : 0
            });

            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            var fileName = Path.GetFileName(file);

            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                using var ms = new MemoryStream(bytes);
                var xgFile = XgFileReader.ReadStream(ms);
                var rows = XgDecisionIterator.Iterate(xgFile, fileName);

                foreach (var row in rows.Where(r => filterSet.Matches(r)))
                {
                    var target = Path.Combine(outputPath, options.EntryName(totalRows));
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
                        totalRows++; // failed decisions don't consume a number
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex,
                            "Skipping decision {DecisionId} in {File}", row.Id, fileName);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping {File}", fileName);
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
            FilesPerSec = totalElapsed > 0 ? (int)(files.Count / totalElapsed) : 0
        });
    }

    public Task ProcessPptxAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            CancellationToken cancellationToken = default)
        => ProcessDeckAsync(
            folderPath, outputPath, filterSet, progress,
            (reqs, opts) => DiagramRasterRenderer.RenderPptx(reqs, opts),
            "PPTX", cancellationToken);

    public Task ProcessPdfAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            CancellationToken cancellationToken = default)
        => ProcessDeckAsync(
            folderPath, outputPath, filterSet, progress,
            (reqs, opts) => DiagramRasterRenderer.RenderPdf(reqs, opts),
            "PDF", cancellationToken);

    private async Task ProcessDeckAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            Func<IEnumerable<DiagramRequest>, DiagramOptions, byte[]> renderer,
            string formatLabel,
            CancellationToken cancellationToken)
    {
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
                    FilesPerSec = filesPerSec
                });
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                using var ms = new MemoryStream(bytes);
                var xgFile = XgFileReader.ReadStream(ms);
                var items = XgDecisionIterator.IterateDiagramRequests(xgFile, fileName, logger: _logger);

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping {File}", fileName);
            }
        }

        // Render and write are atomic — the renderer returns the full byte[]
        // and mid-render cancellation isn't supported. Cancellation gating
        // happens during the per-file collect loop above.
        progress.Report(new ProcessingProgress
        {
            Current = files.Count,
            Total = files.Count,
            FileName = $"Rendering {formatLabel} ({totalRows} decisions, {requests.Count} slides)…",
            TotalRows = totalRows,
            ElapsedSec = stopwatch.Elapsed.TotalSeconds,
            FilesPerSec = stopwatch.Elapsed.TotalSeconds > 0
                ? (int)(files.Count / stopwatch.Elapsed.TotalSeconds) : 0
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
            FilesPerSec = finalFilesPerSec
        });
    }
}
