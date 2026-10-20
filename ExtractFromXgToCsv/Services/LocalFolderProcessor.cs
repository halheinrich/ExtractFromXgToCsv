using BackgammonDiagram_Lib;
using BackgammonDiagram_Lib.ExportRaster;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ExtractFromXgToCsv.Client.Shared;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Server-side processor for local mode: one output run over the
/// <c>.xg</c>/<c>.xgp</c> files of a folder, in four pathways (CSV, Diagram
/// JSON, XGP positions, and the PPTX/PDF deck).
/// </summary>
/// <remarks>
/// <para>
/// <b>Each file is read once and filtered by the producer</b>
/// (halheinrich/backgammon#319, item 1). A run reads each discovered file
/// with ConvertXgToJson_Lib's <see cref="XgSourceReader"/>, the one read of
/// one source, and filters each decoded file through XgFilter_Lib's filtering
/// operation — a <see cref="FilteredDecisionIterator"/> built with the run's
/// filters, ranking and opening book, and its
/// <see cref="FilteredDecisionIterator.IterateFile"/> or
/// <see cref="FilteredDecisionIterator.IterateFileDiagrams"/>. No pathway
/// iterates the producer or evaluates the filters itself.
/// </para>
/// <para>
/// <b>Every file passes the same boundaries</b> (the umbrella's
/// <c>SPEC-notices.md</c> §7.1), in one loop all four pathways share:
/// </para>
/// <list type="bullet">
/// <item>a failed read is a <i>read skip</i>;</item>
/// <item>the file's contribution to the output — its rows, records or deck
/// pairs — is staged whole, and a failure converting it is a <i>conversion
/// skip</i>, so a file that fails midway contributes nothing;</item>
/// <item>the staged contribution is then committed, outside that boundary. A
/// failure committing — the output file or folder, the CSV writer, the JSON
/// write, the deck build, an XGP file write — is the destination's: it fails
/// the run and is never a skip.</item>
/// </list>
/// <para>
/// On the XGP pathway a decision's payload is prepared within its own
/// boundary (a failure is an <i>export skip</i> of that decision, and consumes
/// no number) and written outside it.
/// </para>
/// <para>
/// Every skip is recorded through one seam, <see cref="RecordSkip"/>, onto
/// the run's <see cref="RunRecord"/>, whose skips every snapshot carries as
/// <see cref="ProcessingProgress.Skipped"/>; a caller passes its own record to
/// read it after a run-ending failure. Cancellation is never a skip: the read
/// propagates it, and every skip catch excludes
/// <see cref="OperationCanceledException"/>.
/// </para>
/// </remarks>
public class LocalFolderProcessor
{
    /// <summary>The streaming cadence: the client polls once a second, so every file need not report.</summary>
    private const int ReportEvery = 10;

    private readonly ILogger<LocalFolderProcessor> _logger;

    /// <summary>
    /// The filtering operation's logger, which it also hands the producer, so
    /// the producer's per-decision warnings (an illegal play, say) surface on
    /// every pathway.
    /// </summary>
    private readonly ILogger<FilteredDecisionIterator> _iteratorLogger;

    // The run's one ranking, as the composition root hands it in (the
    // application's, AppRanking). It is stated once per run, to the filtering
    // operation, which builds every row for it and filters every record
    // through its view for it; the deck draws each diagram for it too. No
    // producer default stands in.
    private readonly PlayRanking _ranking;

    // The opening book OpeningBookProvider resolved once, handed to every
    // run's filtering operation, so book-analysed decisions are enriched
    // consistently across all pathways. Null when none is available (provider
    // absent, disabled, or the file missing or unreadable) — book hits then
    // state the unenriched "Book V2" / level Unknown form.
    private readonly OpeningBook? _openingBook;

    private readonly XgpPayloadPreparation _preparePayload;

    // Where every output file is written: the destination (SPEC-notices.md
    // §7.1), whose failures fail the run.
    private readonly IOutputDestination _destination;

    /// <summary>
    /// Creates the processor. <paramref name="logger"/> records per-file skip
    /// warnings; <paramref name="iteratorLogger"/> is the filtering
    /// operation's, which carries the producer's per-decision warnings;
    /// <paramref name="ranking"/> is the ranking every run is interpreted
    /// under — the application passes its one ranking,
    /// <see cref="AppRanking.Ranking"/>, from the composition root;
    /// <paramref name="bookProvider"/> supplies the loaded opening book
    /// (optional — a null provider, like a provider that loaded no book, runs
    /// the pipeline unenriched).
    /// </summary>
    /// <remarks>
    /// The ranking is a required argument, not read here, so that no pathway
    /// can silently run under a default. Stating it also lets a test run a
    /// pathway under another ranking and see that the pathway honours the one
    /// it was given.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="iteratorLogger"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ranking"/> is not a defined ranking.</exception>
    public LocalFolderProcessor(
        ILogger<LocalFolderProcessor> logger,
        ILogger<FilteredDecisionIterator> iteratorLogger,
        PlayRanking ranking,
        OpeningBookProvider? bookProvider = null)
        : this(logger, iteratorLogger, ranking, bookProvider, XgpPayload.Prepare, FileSystemDestination.Instance)
    {
    }

    /// <summary>
    /// Creates the processor with its two seams supplied: the XGP pathway's
    /// payload preparation — <see cref="XgpPayload.Prepare"/> in the
    /// application, or a test's stand-in that fails a chosen decision's
    /// preparation — and the destination every output file is written to —
    /// <see cref="FileSystemDestination"/> in the application, or a test's that
    /// fails a write or a close. No real input is known to fail either, and
    /// the export-skip and destination boundaries are pinned through these.
    /// </summary>
    /// <inheritdoc cref="LocalFolderProcessor(ILogger{LocalFolderProcessor}, ILogger{FilteredDecisionIterator}, PlayRanking, OpeningBookProvider?)"/>
    internal LocalFolderProcessor(
        ILogger<LocalFolderProcessor> logger,
        ILogger<FilteredDecisionIterator> iteratorLogger,
        PlayRanking ranking,
        OpeningBookProvider? bookProvider,
        XgpPayloadPreparation preparePayload,
        IOutputDestination destination)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(iteratorLogger);
        ArgumentNullException.ThrowIfNull(preparePayload);
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enum.IsDefined(ranking))
            throw new ArgumentOutOfRangeException(nameof(ranking), ranking, "Not a defined ranking.");
        _logger = logger;
        _iteratorLogger = iteratorLogger;
        _ranking = ranking;
        _openingBook = bookProvider?.Book;
        _preparePayload = preparePayload;
        _destination = destination;
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
    /// The run's record: the caller's, when it passed one to read after a
    /// failure, otherwise a fresh one of the run's own.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="runRecord"/> already holds a record — a record belongs
    /// to one run, and a reused one would report another run's skips as this
    /// run's.
    /// </exception>
    private static RunRecord RecordFor(RunRecord? runRecord)
    {
        if (runRecord is { IsFresh: false })
            throw new ArgumentException(
                "A run record belongs to one run; pass a fresh one.", nameof(runRecord));
        return runRecord ?? new RunRecord();
    }

    /// <summary>
    /// The run's filtering operation: its filters, under its one ranking, with
    /// the opening book, logging through the operation's own logger.
    /// </summary>
    private FilteredDecisionIterator FilteringFor(DecisionFilterSet filterSet) =>
        new(filterSet, _ranking, _iteratorLogger, _openingBook);

    /// <summary>Creates the folder an output file goes into, if it has one.</summary>
    private static void CreateParentFolder(string outputPath)
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);
    }

    /// <summary>
    /// Creates a new file for <paramref name="name"/> in
    /// <paramref name="folder"/>: under that name, or, where a file of it
    /// exists, under the next free name of its sequence
    /// (<see cref="NumberedFileName.Next"/>). A Local output is never
    /// overwritten (the umbrella's <c>SPEC-notices.md</c> §7.5), and the run
    /// carries on under the numbered name.
    /// </summary>
    /// <remarks>
    /// Each attempt is one create-new, never an existence check followed by a
    /// write; only the destination's own classification of its refusal as a
    /// collision moves on to the next name. Any other failure to create the
    /// file is the run's destination failure, and creates nothing.
    /// </remarks>
    /// <returns>The created file, open for writing, and the path it was created at.</returns>
    private (Stream File, string Path) CreateUnderFreeName(string folder, NumberedFileName name)
    {
        while (true)
        {
            var path = Path.Combine(folder, name.Name);
            try
            {
                return (_destination.CreateNew(path), path);
            }
            catch (OutputFileExistsException)
            {
                name = name.Next();
            }
        }
    }

    /// <summary>
    /// Creates the one output file a CSV, Diagram JSON or deck run writes: at
    /// <paramref name="outputPath"/>, the path the user named, or under the
    /// next free name beside it (<see cref="CreateUnderFreeName"/>).
    /// </summary>
    /// <returns>The created file, open for writing, and the path it was created at.</returns>
    private (Stream File, string Path) CreateOutput(string outputPath) =>
        CreateUnderFreeName(
            Path.GetDirectoryName(outputPath) ?? string.Empty,
            NumberedFileName.FirstOf(Path.GetFileName(outputPath)));

    /// <summary>
    /// Writes <paramref name="bytes"/> as the one output file
    /// <paramref name="outputPath"/> names, created new
    /// (<see cref="CreateOutput"/>), and closes it before returning — so a
    /// failure finishing the file is the caller's, before it reports success.
    /// </summary>
    /// <returns>The path the file was written at, once it is written and closed.</returns>
    private async Task<string> WriteOutputAsync(string outputPath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var (file, path) = CreateOutput(outputPath);
        await WriteThenCloseAsync(file, path, () => file.WriteAsync(bytes, cancellationToken).AsTask());
        return path;
    }

    /// <summary>
    /// Writes the one output file <paramref name="path"/> names with
    /// <paramref name="write"/>, then closes <paramref name="file"/> whatever
    /// the write did — so the file is finished, or the failure to finish it
    /// is known, before the caller reports anything.
    /// </summary>
    /// <remarks>
    /// The write's own ending stands. A cancellation stays a cancellation and
    /// a failure stays that failure, with a failure closing the file reported
    /// beside it, never in its place (the umbrella's <c>SPEC-notices.md</c>
    /// §7.1). A close that fails after a write that succeeded is the run's
    /// error. Every failure here is the destination's, never a skip.
    /// </remarks>
    /// <exception cref="CancelledWithUnfinishedOutputException">The write was cancelled, and closing the file failed.</exception>
    /// <exception cref="UnfinishedOutputException">The write failed, and closing the file failed.</exception>
    private static async Task WriteThenCloseAsync(IAsyncDisposable file, string path, Func<Task> write)
    {
        Exception? ending = null;
        try
        {
            await write();
        }
        catch (Exception ex)
        {
            ending = ex;
        }

        Exception? closeFailure = null;
        try
        {
            await file.DisposeAsync();
        }
        catch (Exception ex)
        {
            closeFailure = ex;
        }

        if (closeFailure is null)
        {
            if (ending is not null)
                ExceptionDispatchInfo.Throw(ending);
            return;
        }

        var fileName = Path.GetFileName(path);
        switch (ending)
        {
            case null:
                ExceptionDispatchInfo.Throw(closeFailure);
                break;
            case OperationCanceledException cancellation:
                throw new CancelledWithUnfinishedOutputException(fileName, cancellation, closeFailure);
            default:
                throw new UnfinishedOutputException(fileName, ending, closeFailure);
        }
    }

    /// <summary>
    /// Writes <paramref name="payload"/> as a new XGP position file for
    /// <paramref name="name"/> in <paramref name="folder"/> — under that name,
    /// or the next free one where a file of it exists
    /// (<see cref="CreateUnderFreeName"/>) — and closes it before returning, so
    /// the caller counts it only once it is written and closed. Not
    /// cancellable: a file is the cancellation's granularity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file belongs to the run only once its create-new succeeds, under
    /// the name it was created with. A failure to create it removes nothing,
    /// and a file that existed before the run is never touched.
    /// </para>
    /// <para>
    /// If writing or closing it fails, the run removes it, under the name it
    /// was created with, and fails with that failure; a failure to close or
    /// remove it is reported beside that one, never in its place
    /// (<see cref="UnfinishedFileException"/>). Every failure here is the
    /// destination's, never a skip.
    /// </para>
    /// </remarks>
    /// <param name="folder">The run's output folder.</param>
    /// <param name="name">The name the allocator issued the decision.</param>
    /// <param name="payload">Its whole content.</param>
    /// <exception cref="UnfinishedFileException">The file was created, and writing or closing it failed.</exception>
    private async Task WriteNewFileAsync(string folder, NumberedFileName name, ReadOnlyMemory<byte> payload)
    {
        var (file, path) = CreateUnderFreeName(folder, name);

        var writing = true;
        Exception primary;
        try
        {
            await file.WriteAsync(payload);
            writing = false;
            await file.DisposeAsync();
            return;
        }
        catch (Exception ex)
        {
            primary = ex;
        }

        // Unfinished: close it if its write failed, then remove it.
        Exception? closeFailure = null;
        if (writing)
        {
            try
            {
                await file.DisposeAsync();
            }
            catch (Exception ex)
            {
                closeFailure = ex;
            }
        }

        Exception? removalFailure = null;
        try
        {
            _destination.Delete(path);
        }
        catch (Exception ex)
        {
            removalFailure = ex;
        }

        throw new UnfinishedFileException(Path.GetFileName(path), primary, closeFailure, removalFailure);
    }

    /// <summary>
    /// The one skip seam: every read, conversion and payload failure in this
    /// processor comes here, and nothing logs a skip on its own. Appends the
    /// skip to <paramref name="record"/>, the run's record, which every
    /// snapshot the run reports carries as it stands
    /// (<see cref="ProcessingProgress.Skipped"/>), and then logs it. The
    /// exception's message is the recorded reason. A skip never ends a run,
    /// and nor does logging one: a logger that fails is contained here, after
    /// the record, so the batch goes on with its record whole.
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
    /// <param name="record">The current run's record.</param>
    /// <param name="ex">What skipped the input; never an <see cref="OperationCanceledException"/>.</param>
    /// <param name="folderPath">The run's input folder, which the recorded name is relative to.</param>
    /// <param name="file">The source file's full path, as discovered.</param>
    /// <param name="decisionId">The skipped decision, or <see langword="null"/> when the whole file was skipped.</param>
    private void RecordSkip(
        RunRecord record,
        Exception ex,
        string folderPath,
        string file,
        DecisionId? decisionId = null)
    {
        record.Skip(new SkippedItem(Path.GetRelativePath(folderPath, file), decisionId, ex.Message));

        var fileName = Path.GetFileName(file);
        try
        {
            if (decisionId is null)
                _logger.LogWarning(ex, "Skipping {File}", fileName);
            else
                _logger.LogWarning(ex, "Skipping decision {DecisionId} in {File}", decisionId, fileName);
        }
        catch (Exception)
        {
            // The skip is recorded, and the user sees it there; a failure to
            // log it has nowhere else to go.
        }
    }

    /// <summary>
    /// The one walk every pathway runs: for each file, the read, the staged
    /// conversion within the file's boundary, and the commit outside it (see
    /// the class remarks).
    /// </summary>
    /// <param name="folderPath">The run's input folder.</param>
    /// <param name="files">The discovered files, in discovery order.</param>
    /// <param name="record">The run's record.</param>
    /// <param name="run">The run's progress, reported as the walk goes.</param>
    /// <param name="retainBytes">Whether the read keeps each file's exact bytes, for a pathway that copies a source verbatim.</param>
    /// <param name="stage">
    /// Converts a decoded file, by its bare name, to its whole contribution.
    /// A failure is the file's conversion skip.
    /// </param>
    /// <param name="commit">
    /// Commits a staged contribution, given the file's full path. A failure is
    /// the destination's and propagates.
    /// </param>
    /// <param name="cancellationToken">Ends the run between files and during a read.</param>
    private async Task ForEachFileAsync<TStaged>(
        string folderPath,
        IReadOnlyList<string> files,
        RunRecord record,
        RunProgress run,
        bool retainBytes,
        Func<XgSourceDecoded, string, TStaged> stage,
        Func<TStaged, string, Task> commit,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var fileName = Path.GetFileName(file);

            cancellationToken.ThrowIfCancellationRequested();
            if (i % ReportEvery == 0)
                run.Report(i, fileName);

            // The one read of this file. A failed read is the file's read
            // skip; a cancellation propagates out of the read, which no catch
            // here encloses.
            var read = await XgSourceReader.ReadFileAsync(
                file, XgSourceFormat.Xg, retainBytes, cancellationToken);
            var decoded = read.Match<XgSourceDecoded?>(
                decoded => decoded,
                failed =>
                {
                    RecordSkip(record, failed.Exception, folderPath, file);
                    return null;
                });
            if (decoded is null)
                continue;

            // The file's whole contribution, staged within its boundary: a
            // file that fails midway contributes nothing.
            TStaged staged;
            try
            {
                staged = stage(decoded, fileName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordSkip(record, ex, folderPath, file);
                continue;
            }

            // The commit, outside the boundary: a destination failure fails
            // the run.
            await commit(staged, file);
        }
    }

    /// <summary>
    /// CSV pathway: reads the folder's <c>.xg</c>/<c>.xgp</c> files one at a
    /// time, filters each through the run's filtering operation, and appends
    /// each file's matching <see cref="DecisionRow"/>s to
    /// <paramref name="outputPath"/> as CSV once the whole file has converted,
    /// reporting through <paramref name="progress"/>. Never holds more than
    /// one file's rows in memory at a time. Pass <paramref name="runRecord"/>
    /// (fresh) to read what the run skipped after a failure has ended it; the
    /// snapshots carry the record either way.
    /// </summary>
    public async Task ProcessAsync(
        string folderPath,
        string outputPath,
        DecisionFilterSet filterSet,
        IProgress<ProcessingProgress> progress,
        RunRecord? runRecord = null,
        CancellationToken cancellationToken = default)
    {
        var record = RecordFor(runRecord);
        var files = DiscoverInputFiles(folderPath);
        var filtering = FilteringFor(filterSet);

        // The destination, opened before any file is read; it and every write
        // below are outside the files' boundaries. It is finished — flushed
        // whole and closed — before the run reports success, so a failure
        // finishing it is the run's error, never a success already published;
        // and a failure finishing it after a cancellation or a failure is
        // reported beside that ending, never in its place.
        CreateParentFolder(outputPath);
        var run = new RunProgress(files.Count, progress, record);
        var (file, path) = CreateOutput(outputPath);
        var writer = new StreamWriter(file, Encoding.UTF8);
        await WriteThenCloseAsync(writer, path, async () =>
        {
            await writer.WriteLineAsync(CsvLayout.Header);
            await ForEachFileAsync(
                folderPath, files, record, run,
                retainBytes: false,
                stage: (decoded, fileName) => filtering.IterateFile(decoded.File, fileName).ToList(),
                commit: async (rows, _) =>
                {
                    foreach (var row in rows)
                        await writer.WriteLineAsync(CsvLayout.Line(row));
                    run.Add(rows.Count);
                },
                cancellationToken);
        });

        run.ReportDone(path);
    }

    /// <summary>
    /// Diagram-JSON pathway: the same per-file walk as
    /// <see cref="ProcessAsync"/>, but each file's matching decisions are
    /// collected, and all of them are written to <paramref name="outputPath"/>
    /// as a single indented JSON array once every file has been read.
    /// <paramref name="runRecord"/> as on <see cref="ProcessAsync"/>.
    /// </summary>
    public async Task ProcessDiagramAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            RunRecord? runRecord = null,
            CancellationToken cancellationToken = default)
    {
        var record = RecordFor(runRecord);
        var files = DiscoverInputFiles(folderPath);
        var filtering = FilteringFor(filterSet);
        CreateParentFolder(outputPath);

        var allItems = new List<BgDecisionData>();
        var run = new RunProgress(files.Count, progress, record);
        await ForEachFileAsync(
            folderPath, files, record, run,
            retainBytes: false,
            stage: (decoded, fileName) => filtering.IterateFileDiagrams(decoded.File, fileName).ToList(),
            commit: (items, _) =>
            {
                allItems.AddRange(items);
                run.Add(items.Count);
                return Task.CompletedTask;
            },
            cancellationToken);

        // The commit to the destination.
        var written = await WriteOutputAsync(
            outputPath,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(allItems, new JsonSerializerOptions { WriteIndented = true })),
            cancellationToken);

        run.ReportDone(written);
    }

    /// <summary>
    /// Xgp pathway: writes one .xgp position file per filtered decision into
    /// the <paramref name="outputPath"/> <b>folder</b> (created if absent).
    /// A file is never overwritten: each is created only if no file of its
    /// name exists, and where one does it is created under the next free name
    /// of its sequence, <c>" (2)"</c>, <c>" (3)"</c>, …, and the run carries
    /// on (the umbrella's <c>SPEC-notices.md</c> §7.5). Filenames
    /// come from <paramref name="options"/>' pattern via
    /// an <see cref="XgpNameAllocator"/>: batch-constant tokens draw from
    /// <paramref name="filters"/>, per-item tokens from each decision, and
    /// duplicate rendered names within the run get Windows-style
    /// <c>" (2)"</c> suffixes. Each payload is prepared by
    /// <see cref="XgpPayload"/> — sliced from an .xg source, an .xgp source
    /// copied verbatim, both anonymized when <paramref name="anonymize"/> is
    /// set — within the decision's own boundary, and written outside it.
    /// <para>
    /// The allocator's Peek/Commit split keeps the counter honest: a
    /// decision's slot is consumed only once its file is written, so a
    /// decision whose preparation failed — an export skip — burns no number.
    /// The run's record holds its <see cref="XgpExportCommit"/>, replaced as
    /// each file is written, so a run that ends — completed, cancelled, or by
    /// a write that failed — accounts for exactly the files it wrote.
    /// </para>
    /// <para>
    /// <paramref name="runRecord"/> as on <see cref="ProcessAsync"/>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="options"/> fails validation (surfaces to
    /// the client through the job's ErrorMessage channel), or when
    /// <paramref name="runRecord"/> is not fresh.
    /// </exception>
    public async Task ProcessXgpAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            XgpExportOptions options,
            FilterConfig filters,
            IProgress<ProcessingProgress> progress,
            bool anonymize = false,
            RunRecord? runRecord = null,
            CancellationToken cancellationToken = default)
    {
        // Create validates the options — the single options-validation throw
        // point shared with the Web-mode pathway.
        var allocator = XgpNameAllocator.Create(options, filters);
        var record = RecordFor(runRecord);
        var files = DiscoverInputFiles(folderPath);
        var filtering = FilteringFor(filterSet);

        // The run's commit, from here on the accounting of every file it
        // writes, whatever ends it.
        record.Commit(new XgpExportCommit(options, allocator.CommittedCount));

        Directory.CreateDirectory(outputPath);

        var run = new RunProgress(files.Count, progress, record);
        await ForEachFileAsync(
            folderPath, files, record, run,
            // A source copied verbatim needs its exact bytes; an anonymized
            // copy is re-emitted from the decoded file instead.
            retainBytes: !anonymize,
            stage: (decoded, fileName) => (Source: decoded, Rows: filtering.IterateFile(decoded.File, fileName).ToList()),
            commit: async (staged, file) =>
            {
                foreach (var row in staged.Rows)
                {
                    // The decision's payload, prepared within its own
                    // boundary: a failure skips this decision alone.
                    ReadOnlyMemory<byte> payload;
                    try
                    {
                        payload = _preparePayload(row.Id, staged.Source.File, staged.Source.Bytes, anonymize);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        RecordSkip(record, ex, folderPath, file, row.Id);
                        continue;
                    }

                    // The commit: the file, created new, written and closed
                    // outside the decision's boundary, so a failure fails the
                    // run. The slot is consumed, and the run's commit counts
                    // the file, only once it is written and closed. Not
                    // cancellable: a file is the cancellation's granularity.
                    await WriteNewFileAsync(outputPath, allocator.Peek(row), payload);
                    allocator.Commit(row);
                    record.Commit(new XgpExportCommit(options, allocator.CommittedCount));
                    run.Add(1);
                }
            },
            cancellationToken);

        run.ReportDone(outputPath);
    }

    /// <summary>
    /// PPTX pathway: collects filtered decisions into a Problem/Solution slide
    /// deck at <paramref name="outputPath"/>. Thin wrapper over the shared deck
    /// helper (PDF twin: <see cref="ProcessPdfAsync"/>). Local mode only —
    /// rendering is server-side. <paramref name="runRecord"/> as on
    /// <see cref="ProcessAsync"/>.
    /// </summary>
    public Task ProcessPptxAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            RunRecord? runRecord = null,
            CancellationToken cancellationToken = default)
        => ProcessDeckAsync(
            folderPath, outputPath, filterSet, progress,
            (reqs, opts) => DiagramRasterRenderer.RenderPptx(reqs, opts),
            "PPTX", runRecord, cancellationToken);

    /// <summary>
    /// PDF pathway: collects filtered decisions into a Problem/Solution page
    /// deck at <paramref name="outputPath"/>. Thin wrapper over the shared deck
    /// helper (the PDF twin of <see cref="ProcessPptxAsync"/>). Local mode only
    /// — rendering is server-side. <paramref name="runRecord"/> as on
    /// <see cref="ProcessAsync"/>.
    /// </summary>
    public Task ProcessPdfAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            RunRecord? runRecord = null,
            CancellationToken cancellationToken = default)
        => ProcessDeckAsync(
            folderPath, outputPath, filterSet, progress,
            (reqs, opts) => DiagramRasterRenderer.RenderPdf(reqs, opts),
            "PDF", runRecord, cancellationToken);

    private async Task ProcessDeckAsync(
            string folderPath,
            string outputPath,
            DecisionFilterSet filterSet,
            IProgress<ProcessingProgress> progress,
            Func<IEnumerable<DiagramRequest>, DiagramOptions, byte[]> renderer,
            string formatLabel,
            RunRecord? runRecord,
            CancellationToken cancellationToken)
    {
        var record = RecordFor(runRecord);
        var files = DiscoverInputFiles(folderPath);
        var filtering = FilteringFor(filterSet);
        CreateParentFolder(outputPath);

        var requests = new List<DiagramRequest>();
        var run = new RunProgress(files.Count, progress, record);
        await ForEachFileAsync(
            folderPath, files, record, run,
            retainBytes: false,
            // Each decision becomes a Problem/Solution pair — the reader
            // considers the problem slide, then advances to the solution
            // slide for the answer. The diagram orders and scores the
            // candidates by the ranking the decision was just filtered under.
            stage: (decoded, fileName) => filtering.IterateFileDiagrams(decoded.File, fileName)
                .Select(item => DiagramRequest.ForDecision(item, _ranking).ToProblemSolutionPair())
                .ToList(),
            commit: (pairs, _) =>
            {
                foreach (var (problem, solution) in pairs)
                {
                    requests.Add(problem);
                    requests.Add(solution);
                }
                run.Add(pairs.Count);
                return Task.CompletedTask;
            },
            cancellationToken);

        // Render and write are atomic — the renderer returns the full byte[]
        // and mid-render cancellation isn't supported. Cancellation gating
        // happens during the per-file walk above. Both are the commit: a
        // failure fails the run.
        //
        // This is the last snapshot the client sees until the run finishes, and
        // the render dominates the wall clock (minutes, at corpus scale), so it
        // is stamped JobPhase.Rendering: the counter and the elapsed/throughput
        // figures have stopped advancing, and the phase is what tells the
        // client to stop presenting them as live.
        run.ReportRendering($"Rendering {formatLabel} ({run.Rows} decisions, {requests.Count} slides)…");

        if (requests.Count == 0)
            throw new InvalidOperationException(
                "No decisions matched the filter — nothing to render.");

        var deckBytes = renderer(requests, new DiagramOptions());
        var written = await WriteOutputAsync(outputPath, deckBytes, cancellationToken);

        run.ReportDone(written);
    }

    /// <summary>
    /// One run's progress: its clock and row count, and the snapshots it
    /// reports, each carrying the run's record — its skips and its XGP commit
    /// — as it stands.
    /// </summary>
    private sealed class RunProgress(int total, IProgress<ProcessingProgress> progress, RunRecord record)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>Rows or decisions the run has produced so far.</summary>
        public int Rows { get; private set; }

        /// <summary>Counts <paramref name="count"/> more rows or decisions.</summary>
        public void Add(int count) => Rows += count;

        /// <summary>Reports the run about to take file <paramref name="index"/> (0-based).</summary>
        public void Report(int index, string fileName)
        {
            var elapsed = _clock.Elapsed.TotalSeconds;
            progress.Report(new ProcessingProgress
            {
                Current = index + 1,
                Total = total,
                FileName = fileName,
                TotalRows = Rows,
                ElapsedSec = elapsed,
                FilesPerSec = elapsed > 0 ? (int)(index / elapsed) : 0,
                Skipped = record.Skips,
                XgpCommit = record.XgpCommit,
            });
        }

        /// <summary>Reports the deck's atomic render, under <paramref name="line"/>.</summary>
        public void ReportRendering(string line) => progress.Report(Final(JobPhase.Rendering, line, complete: false));

        /// <summary>
        /// Reports the run's successful end, with <paramref name="writtenPath"/>
        /// — the one file as it was created, or an XGP run's folder — as
        /// <see cref="ProcessingProgress.WrittenPath"/>. Called only once the
        /// output is written and closed.
        /// </summary>
        public void ReportDone(string writtenPath) =>
            progress.Report(Final(JobPhase.Processing, "Done", complete: true) with { WrittenPath = writtenPath });

        private ProcessingProgress Final(JobPhase phase, string line, bool complete)
        {
            var elapsed = _clock.Elapsed.TotalSeconds;
            return new ProcessingProgress
            {
                Current = total,
                Total = total,
                Phase = phase,
                FileName = line,
                TotalRows = Rows,
                Complete = complete,
                ElapsedSec = elapsed,
                FilesPerSec = elapsed > 0 ? (int)(total / elapsed) : 0,
                Skipped = record.Skips,
                XgpCommit = record.XgpCommit,
            };
        }
    }
}

/// <summary>
/// Prepares one decision's <c>.xgp</c> payload from its decoded source — the
/// shape of <see cref="XgpPayload.Prepare"/>, which the application supplies,
/// and a test's stand-in.
/// </summary>
internal delegate ReadOnlyMemory<byte> XgpPayloadPreparation(
    DecisionId id, XgFile source, ReadOnlyMemory<byte>? sourceBytes, bool anonymize);
