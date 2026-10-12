using System.Collections.Immutable;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Web mode's state, owned by one panel and dying with it: the selection and
/// its conversion cache, the opening book in force, the projections, and the
/// last download — the umbrella's <c>SPEC-notices.md</c> §7.3, which is the
/// statement of every rule here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two owners.</b> The selection owns its read outcomes, its conversions
/// and "this selection" (<see cref="SelectionSkips"/>); the last download
/// (<see cref="LastDownload"/>) is an attempt's own record. A new pick, or the
/// selection's removal, ends both.
/// </para>
/// <para>
/// <b>Read once, convert once per key.</b> A pick reads each file once
/// (<see cref="XgSourceReader.ReadAsync"/>, format <see cref="XgSourceFormat.Xg"/>)
/// and retains each decoded file, and its exact bytes only where an export
/// copies it verbatim, so no output reads or parses a file again. Conversion
/// results are cached per selection and per key — the representation and the
/// opening book (the ranking is the run's one, fixed) — in the states
/// <see cref="ConversionState"/> names. Rows convert on a pick and on a book
/// change; records only when an output needs them
/// (halheinrich/backgammon#74). A format switch selects the other key's
/// result, requesting it when it was never requested; it never discards a
/// result.
/// </para>
/// <para>
/// <b>Ownership decides publication.</b> Every asynchronous operation carries
/// what it started with — a ticket — and its completion publishes only while
/// its owner says so: a pick or a book load while no newer one has begun; a
/// conversion while its selection, and its key's book, are still current —
/// and a conversion's failure by that same owner, whichever gesture asked for
/// the conversion (<see cref="Owns"/>). A result for a key that is no longer
/// the selected output's is cached, not shown, because what is shown is read
/// from the selected key's state. A superseded completion publishes nothing.
/// </para>
/// <para>
/// Not thread-safe: Blazor WebAssembly runs it on one thread, and its awaits
/// are where other gestures interleave — which is why the tickets exist.
/// </para>
/// </remarks>
internal sealed class WebSession
{
    private readonly ISourceConverter _converter;

    private object? _pickOwner;
    private object? _bookOwner;
    private object? _downloadOwner;

    // The newest pick and book load begun, whether or not they have published:
    // what owns a gesture's outcome after its completion has run.
    private PickTicket? _latestPick;
    private BookTicket? _latestBookLoad;

    private readonly ConversionSlot<DecisionRow> _rows = new();
    private readonly ConversionSlot<BgDecisionData> _records = new();

    // Each converted row's decoded file, by reference: the XGP export prepares
    // a decision's payload from the file the decision came from.
    private IReadOnlyDictionary<DecisionRow, DecodedSource> _rowSources =
        new Dictionary<DecisionRow, DecodedSource>(ReferenceEqualityComparer.Instance);

    /// <summary>A session converting through <paramref name="converter"/>, for <paramref name="ranking"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="converter"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ranking"/> is not a defined ranking.</exception>
    public WebSession(ISourceConverter converter, PlayRanking ranking)
    {
        ArgumentNullException.ThrowIfNull(converter);
        _converter = converter;
        Projections = new FilteredRowCache(ranking);
    }

    /// <summary>The ranking every row is built for and every record is filtered under — the run's one ranking.</summary>
    public PlayRanking Ranking => Projections.Ranking;

    /// <summary>
    /// The current key's converted rows and records, unfiltered, and their
    /// projections through the filter in effect.
    /// </summary>
    public FilteredRowCache Projections { get; }

    /// <summary>The current selection; <see langword="null"/> while none is published.</summary>
    public WebSelection? Selection { get; private set; }

    /// <summary>The opening book in force; <see langword="null"/> for none.</summary>
    public OpeningBook? Book { get; private set; }

    /// <summary>Whether the last book load published was refused as not an opening book.</summary>
    public bool BookLoadFailed { get; private set; }

    /// <summary>The last download attempt's record — "the last download"; <see langword="null"/> for none.</summary>
    public WebDownload? LastDownload { get; private set; }

    // ── Picks ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Begins a pick, which supersedes any earlier one. The selection ends
    /// here — its conversions, its projections and its last download with it —
    /// so a gesture that selects nothing leaves no selection.
    /// </summary>
    /// <returns>The pick's ticket, for <see cref="CompletePickAsync"/>.</returns>
    public PickTicket BeginPick()
    {
        var ticket = new PickTicket();
        _pickOwner = ticket;
        _latestPick = ticket;
        _downloadOwner = null;
        Selection = null;
        LastDownload = null;
        ResetConversions();
        return ticket;
    }

    /// <summary>
    /// Reads each of <paramref name="files"/> once, converts the decoded ones
    /// to rows under the book in force, and — unless a newer pick has begun —
    /// publishes the selection under <paramref name="token"/> with those rows.
    /// </summary>
    /// <remarks>
    /// A file that cannot be read is a read skip, named with the reader's
    /// message; a file whose rows cannot be built is a conversion skip of the
    /// rows key, and stays decoded. Each decoded file's exact bytes are kept
    /// only where an export copies the file verbatim, which the rows the
    /// producer stamped say; when its rows could not be built they are kept,
    /// since a later book's rows might yet need them.
    /// </remarks>
    /// <returns>Whether the pick published.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled; nothing published.</exception>
    public async Task<bool> CompletePickAsync(
        PickTicket ticket, FilterSourceToken token, IReadOnlyList<PickedFile> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(files);

        // The one read of each file, keeping its bytes until its rows say
        // whether an export needs them.
        var reads = new List<(string Name, DecodedSource? Decoded, SkippedItem? Failure)>(files.Count);
        foreach (var file in files)
        {
            var read = await XgSourceReader.ReadAsync(
                file.Name, file.Open, XgSourceFormat.Xg, retainBytes: true, cancellationToken);
            reads.Add(read.Match<(string, DecodedSource?, SkippedItem?)>(
                decoded => (file.Name, new DecodedSource(file.Name, decoded.File, decoded.Bytes), null),
                failed => (file.Name, null, new SkippedItem(file.Name, null, failed.Exception.Message))));

            // A superseded pick publishes nothing, so it reads no further.
            if (!ReferenceEquals(ticket, _pickOwner))
                return false;
        }

        // Rows convert on a pick, under the book in force now.
        var book = Book;
        var rowResults = await ConvertEachAsync(
            reads.Select(r => r.Decoded).OfType<DecodedSource>(),
            (source, ct) => _converter.RowsAsync(source, Ranking, book, ct),
            cancellationToken);

        // The retained files, with bytes only where an export copies the file
        // verbatim; the rows are re-keyed to them.
        var retained = new Dictionary<DecodedSource, DecodedSource>(ReferenceEqualityComparer.Instance);
        var rekeyed = ImmutableArray.CreateBuilder<SourceConversion<DecisionRow>>(rowResults.Length);
        foreach (var result in rowResults)
        {
            var keepBytes = result.Skip is not null || result.Items.Any(row => XgpPayload.CopiesSourceVerbatim(row.Id));
            var kept = new DecodedSource(result.Source.Name, result.Source.File, keepBytes ? result.Source.VerbatimBytes : null);
            retained[result.Source] = kept;
            rekeyed.Add(result.For(kept));
        }

        var selection = new WebSelection(token, reads.Select(r => r.Decoded is { } decoded
            ? SelectionEntry.Of(retained[decoded])
            : SelectionEntry.Skipped(r.Failure!)));

        if (!ReferenceEquals(ticket, _pickOwner))
            return false;
        _pickOwner = null;
        Selection = selection;
        if (ReferenceEquals(book, Book))
            _rows.Complete(rekeyed.MoveToImmutable(), PublishRows);
        return true;
    }

    // ── The opening book ────────────────────────────────────────────────────

    /// <summary>Begins a book load, which supersedes any earlier one.</summary>
    public BookTicket BeginBookLoad()
    {
        var ticket = new BookTicket();
        _bookOwner = ticket;
        _latestBookLoad = ticket;
        return ticket;
    }

    /// <summary>
    /// Whether <paramref name="ticket"/>'s pick is the newest begun — so its
    /// gesture still owns what it reports, its failure included. A newer pick,
    /// or a selection cleared since, supersedes it for good.
    /// </summary>
    public bool IsLatest(PickTicket ticket) => ReferenceEquals(ticket, _latestPick);

    /// <summary>
    /// Whether <paramref name="ticket"/>'s book load is the newest begun — so
    /// its gesture still owns what it reports, its failure included.
    /// </summary>
    public bool IsLatest(BookTicket ticket) => ReferenceEquals(ticket, _latestBookLoad);

    /// <summary>
    /// Publishes a book load's outcome unless a newer load has begun: the book
    /// it loaded, or — refused as not an opening book — none. A book that
    /// differs from the one in force changes both conversion keys, so their
    /// results for the old book are dropped; rows reconvert when
    /// <see cref="EnsureConversionsAsync"/> next runs, records when an output
    /// needs them.
    /// </summary>
    /// <returns>Whether the load published.</returns>
    public bool CompleteBookLoad(BookTicket ticket, OpeningBook? book, bool loadFailed)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!ReferenceEquals(ticket, _bookOwner))
            return false;
        _bookOwner = null;
        BookLoadFailed = loadFailed;
        if (!ReferenceEquals(book, Book))
        {
            Book = book;
            ResetConversions();
        }
        return true;
    }

    // ── Conversions ─────────────────────────────────────────────────────────

    /// <summary>The state of <paramref name="representation"/>'s key for the current selection and book.</summary>
    public ConversionState StateOf(Representation representation) =>
        Selection is null ? ConversionState.NotRequested : SlotStateOf(representation);

    /// <summary>
    /// Whether the selected output needs a conversion nobody has requested:
    /// rows, always — they convert on a pick and on a book change — and
    /// records while <paramref name="selected"/> is records.
    /// </summary>
    public bool Needs(Representation selected) => NextNeeded(selected) is not null;

    /// <summary>
    /// Converts what the selected output needs and nobody has requested,
    /// reading <paramref name="selected"/> afresh after each conversion — the
    /// format can switch while one runs — until nothing is left to request.
    /// Each conversion publishes only while its selection and its key's book
    /// are current.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled; the conversion in hand published nothing.</exception>
    /// <exception cref="ConversionFailedException">A conversion failed; whether its failure is shown is <see cref="Owns"/>'s.</exception>
    public async Task EnsureConversionsAsync(Func<Representation> selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        while (NextNeeded(selected()) is { } next)
        {
            if (next == Representation.Rows)
                await ConvertAsync(_rows, (source, book, ct) => _converter.RowsAsync(source, Ranking, book, ct), PublishRows, cancellationToken);
            else
                await ConvertAsync(_records, (source, book, ct) => _converter.RecordsAsync(source, Ranking, book, ct), PublishRecords, cancellationToken);
        }
    }

    /// <summary>
    /// "This selection" (<c>SPEC-notices.md</c> §7.3, projection A): the
    /// selection's read skips and the completed conversion skips of the
    /// <paramref name="selected"/> key, in pick order. A pick, a book change, a
    /// format switch or a conversion completing changes it; refiltering never
    /// does.
    /// </summary>
    public SkipRecord SelectionSkips(Representation selected)
    {
        if (Selection is not { } selection)
            return SkipRecord.Empty;

        var conversionSkips = selected == Representation.Rows ? _rows.SkipsBySource() : _records.SkipsBySource();
        return SkipRecord.Of(selection.Entries
            .Select(entry => entry.ReadSkip
                ?? (conversionSkips.TryGetValue(entry.Decoded!, out var skip) ? skip : null))
            .OfType<SkippedItem>());
    }

    // ── The filter ──────────────────────────────────────────────────────────

    /// <summary>Makes <paramref name="filter"/> the filter in effect over everything retained.</summary>
    public void Refilter(FilterConfig filter) => Projections.Refilter(filter);

    // ── Downloads ───────────────────────────────────────────────────────────

    /// <summary>
    /// Begins a download attempt from <paramref name="representation"/>, which
    /// supersedes any earlier attempt: the last download's record is replaced
    /// from this attempt's start. The ticket captures what the attempt builds
    /// from — that representation's filtered projection as it stands now — so
    /// nothing that changes while the attempt runs reaches its file.
    /// </summary>
    /// <returns>
    /// The attempt's ticket; <see langword="null"/>, beginning nothing, when
    /// there is no selection or <paramref name="representation"/>'s conversion
    /// has not completed — an attempt never builds from a representation that
    /// is not converted.
    /// </returns>
    public DownloadTicket? BeginDownload(Representation representation)
    {
        if (Selection is not { } selection || SlotStateOf(representation) != ConversionState.Completed)
            return null;
        var ticket = representation == Representation.Records
            ? new DownloadTicket(selection, representation, [], Projections.FilteredDiagramRows)
            : new DownloadTicket(selection, representation, XgpDecisionsFor(Projections.FilteredRows), []);
        _downloadOwner = ticket;
        LastDownload = null;
        return ticket;
    }

    /// <summary>
    /// Publishes an attempt's record as "the last download", unless a newer
    /// attempt has begun or the selection it started from has been replaced
    /// or removed. Accounting for what it committed is not this: an exported
    /// archive's numbering is recorded by its owner whether or not this
    /// publishes.
    /// </summary>
    /// <returns>Whether the record published.</returns>
    public bool CompleteDownload(DownloadTicket ticket, WebDownload record)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(record);
        if (!ReferenceEquals(ticket, _downloadOwner) || !ReferenceEquals(ticket.Selection, Selection))
            return false;
        _downloadOwner = null;
        LastDownload = record;
        return true;
    }

    /// <summary>Each of <paramref name="rows"/> — converted rows of this session — with the decoded file it came from.</summary>
    /// <exception cref="ArgumentException">A row is not one of the current selection's converted rows.</exception>
    public IReadOnlyList<XgpDecision> XgpDecisionsFor(IEnumerable<DecisionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. rows.Select(row => _rowSources.TryGetValue(row, out var source)
            ? new XgpDecision(row, source)
            : throw new ArgumentException($"Row {row.Id} is not one of the selection's converted rows.", nameof(rows)))];
    }

    // ── The machinery ───────────────────────────────────────────────────────

    private ConversionState SlotStateOf(Representation representation) =>
        representation == Representation.Rows ? _rows.State : _records.State;

    private Representation? NextNeeded(Representation selected) =>
        Selection is null ? null
        : _rows.State == ConversionState.NotRequested ? Representation.Rows
        : selected == Representation.Records && _records.State == ConversionState.NotRequested ? Representation.Records
        : null;

    private void ResetConversions()
    {
        _rows.Reset();
        _records.Reset();
        PublishRows([]);
        PublishRecords([]);
    }

    private void PublishRows(ImmutableArray<SourceConversion<DecisionRow>> results)
    {
        var rowSources = new Dictionary<DecisionRow, DecodedSource>(ReferenceEqualityComparer.Instance);
        foreach (var result in results)
            foreach (var row in result.Items)
                rowSources[row] = result.Source;
        _rowSources = rowSources;
        Projections.ReplaceRows(results.SelectMany(result => result.Items));
    }

    private void PublishRecords(ImmutableArray<SourceConversion<BgDecisionData>> results) =>
        Projections.ReplaceRecords(results.SelectMany(result => result.Items));

    /// <summary>
    /// One key's conversion over the current selection's decoded files, owned
    /// by its ticket: it publishes into <paramref name="slot"/> only while that
    /// selection and that book are still current, and is abandoned — back to
    /// not requested — if it ends any other way. Its failure carries the same
    /// owner (<see cref="ConversionFailedException"/>).
    /// </summary>
    /// <exception cref="ConversionFailedException">Anything but a cancellation of <paramref name="cancellationToken"/> ended it.</exception>
    private async Task ConvertAsync<T>(
        ConversionSlot<T> slot,
        Func<DecodedSource, OpeningBook?, CancellationToken, Task<IReadOnlyList<T>>> convert,
        Action<ImmutableArray<SourceConversion<T>>> publish,
        CancellationToken cancellationToken)
    {
        var selection = Selection!;
        var book = Book;
        var ticket = slot.Begin();
        try
        {
            var results = await ConvertEachAsync(
                selection.Decoded, (source, ct) => convert(source, book, ct), cancellationToken);
            if (ReferenceEquals(selection, Selection) && ReferenceEquals(book, Book))
                slot.Complete(ticket, results, publish);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            throw new ConversionFailedException(selection, book, ex);
        }
        finally
        {
            slot.Abandon(ticket);
        }
    }

    /// <summary>
    /// Whether <paramref name="failure"/> is still its gesture's to show: while
    /// the selection and the book it converted for are current — the owner its
    /// result would have published under (SPEC-notices.md §7.3).
    /// </summary>
    public bool Owns(ConversionFailedException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return ReferenceEquals(failure.Selection, Selection) && ReferenceEquals(failure.Book, Book);
    }

    /// <summary>
    /// The per-file boundary every conversion shares: each file's
    /// representation staged whole, and a failure — never a cancellation —
    /// that file's conversion skip.
    /// </summary>
    private static async Task<ImmutableArray<SourceConversion<T>>> ConvertEachAsync<T>(
        IEnumerable<DecodedSource> sources,
        Func<DecodedSource, CancellationToken, Task<IReadOnlyList<T>>> convert,
        CancellationToken cancellationToken)
    {
        var results = ImmutableArray.CreateBuilder<SourceConversion<T>>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(SourceConversion<T>.Converted(source, await convert(source, cancellationToken)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(SourceConversion<T>.Failed(source, ex));
            }
        }
        return results.ToImmutable();
    }

    /// <summary>
    /// One representation's conversion for the current selection and book:
    /// its state, the ticket of the conversion running, and its completed
    /// results.
    /// </summary>
    private sealed class ConversionSlot<T>
    {
        private object? _running;
        private ImmutableArray<SourceConversion<T>> _results = [];

        public ConversionState State { get; private set; } = ConversionState.NotRequested;

        /// <summary>Marks a conversion running and returns its ticket.</summary>
        public object Begin()
        {
            var ticket = new object();
            _running = ticket;
            State = ConversionState.Running;
            return ticket;
        }

        /// <summary>Publishes the running conversion's results, if <paramref name="ticket"/> is still it.</summary>
        public void Complete(object ticket, ImmutableArray<SourceConversion<T>> results, Action<ImmutableArray<SourceConversion<T>>> publish)
        {
            if (!ReferenceEquals(ticket, _running))
                return;
            Complete(results, publish);
        }

        /// <summary>Publishes completed results, whatever was running.</summary>
        public void Complete(ImmutableArray<SourceConversion<T>> results, Action<ImmutableArray<SourceConversion<T>>> publish)
        {
            _running = null;
            _results = results;
            State = ConversionState.Completed;
            publish(results);
        }

        /// <summary>Returns a conversion that ended without publishing to not requested.</summary>
        public void Abandon(object ticket)
        {
            if (!ReferenceEquals(ticket, _running))
                return;
            _running = null;
            State = ConversionState.NotRequested;
        }

        /// <summary>Back to not requested, any running conversion disowned.</summary>
        public void Reset()
        {
            _running = null;
            _results = [];
            State = ConversionState.NotRequested;
        }

        /// <summary>The completed conversion skips, by file; empty unless completed.</summary>
        public IReadOnlyDictionary<DecodedSource, SkippedItem> SkipsBySource()
        {
            var skips = new Dictionary<DecodedSource, SkippedItem>(ReferenceEqualityComparer.Instance);
            if (State == ConversionState.Completed)
                foreach (var result in _results)
                    if (result.Skip is { } skip)
                        skips[result.Source] = skip;
            return skips;
        }
    }
}

/// <summary>
/// A conversion's failure — anything that ended it but a cancellation of its
/// caller — with the owner its result would have published under: the
/// selection and the book it converted for. <see cref="WebSession.Owns"/> says
/// whether that owner is still current. Its message is the failure's own.
/// </summary>
internal sealed class ConversionFailedException : Exception
{
    internal ConversionFailedException(WebSelection selection, OpeningBook? book, Exception failure)
        : base(failure.Message, failure)
    {
        Selection = selection;
        Book = book;
    }

    /// <summary>The selection the conversion converted for.</summary>
    internal WebSelection Selection { get; }

    /// <summary>The book it converted under; <see langword="null"/> for none.</summary>
    internal OpeningBook? Book { get; }
}

/// <summary>A pick's ownership of its completion: valid until a newer pick begins.</summary>
internal sealed class PickTicket;

/// <summary>A book load's ownership of its completion: valid until a newer load begins.</summary>
internal sealed class BookTicket;

/// <summary>
/// A download attempt's ownership of its record — valid until a newer attempt
/// begins or <see cref="Selection"/>, the selection it started from, is
/// replaced or removed — and what the attempt builds from, captured when it
/// began.
/// </summary>
internal sealed class DownloadTicket
{
    internal DownloadTicket(
        WebSelection selection,
        Representation representation,
        IReadOnlyList<XgpDecision> decisions,
        IReadOnlyList<BgDecisionData> records)
    {
        Selection = selection;
        Representation = representation;
        Decisions = decisions;
        Rows = [.. decisions.Select(decision => decision.Row)];
        Records = records;
    }

    /// <summary>The selection the attempt started from.</summary>
    public WebSelection Selection { get; }

    /// <summary>The representation the attempt builds from: completed when it began.</summary>
    public Representation Representation { get; }

    /// <summary>
    /// For a rows attempt, the filtered rows when it began, each with the
    /// decoded file it came from; empty for a records attempt.
    /// </summary>
    public IReadOnlyList<XgpDecision> Decisions { get; }

    /// <summary>The rows of <see cref="Decisions"/>, in order.</summary>
    public IReadOnlyList<DecisionRow> Rows { get; }

    /// <summary>For a records attempt, the filtered records when it began; empty for a rows attempt.</summary>
    public IReadOnlyList<BgDecisionData> Records { get; }
}
