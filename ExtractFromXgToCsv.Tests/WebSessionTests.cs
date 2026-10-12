using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// <see cref="WebSession"/>'s rules, directly (the umbrella's
/// <c>SPEC-notices.md</c> §7.3): a pick reads each file once and keeps
/// verbatim bytes only where an export needs them; conversions are cached per
/// key, records deferred until asked for; and every asynchronous completion
/// publishes only while its owner says so. A held conversion
/// (<see cref="ScriptedConverter"/>) makes the late completions deterministic.
/// The panel's routing through the session is pinned by the panel's own tests.
/// </summary>
public class WebSessionTests
{
    private const string Xg = "MTCH4064.xg";
    private const string OtherXg = "ajhhBG0407.xg";
    private const string Xgp = "MTCH4064_1_22.xgp";

    private static int s_tokens;
    private static FilterSourceToken NewToken() => FilterSourceToken.FromGeneration(-Interlocked.Increment(ref s_tokens));

    private readonly ScriptedConverter _converter = new();

    private WebSession NewSession() => new(_converter, AppRanking.Ranking);

    private static PickedFile Picked(string name, byte[] bytes) =>
        new(name, _ => Task.FromResult<Stream>(new MemoryStream(bytes)));

    private static PickedFile Fixture(string name, string? asName = null) =>
        Picked(asName ?? name, FixtureHelper.ReadFixture(name));

    private static PickedFile Malformed(string name) =>
        Picked(name, System.Text.Encoding.ASCII.GetBytes("This is not an XG file; its header magic is wrong."));

    private static async Task<WebSession> PickedAsync(WebSession session, params PickedFile[] files)
    {
        Assert.True(await session.CompletePickAsync(session.BeginPick(), NewToken(), files, CancellationToken.None));
        return session;
    }

    // ── A pick reads once ───────────────────────────────────────────────────

    [Fact]
    public async Task APick_ReadsEachFileOnce_NamesAFailedReadWithTheReadersMessage_AndConvertsRowsOnly()
    {
        var session = await PickedAsync(NewSession(), Malformed("broken.xg"), Fixture(Xg));

        var selection = session.Selection!;
        Assert.Equal(2, selection.Entries.Length);
        var skip = Assert.Single(selection.ReadSkips);
        Assert.Equal("broken.xg", skip.FileName);
        Assert.Null(skip.DecisionId);
        Assert.False(string.IsNullOrWhiteSpace(skip.Reason));
        Assert.Equal(Xg, Assert.Single(selection.Decoded).Name);

        Assert.Equal(ConversionState.Completed, session.StateOf(Representation.Rows));
        Assert.Equal(ConversionState.NotRequested, session.StateOf(Representation.Records));
        Assert.Equal([Xg], _converter.RowsCalls.Select(c => c.File));
        Assert.Empty(_converter.RecordsCalls);
        Assert.Equal(WebSources.Rows(WebSources.Decode(Xg)).Select(r => r.Id), session.Projections.Rows.Select(r => r.Id));
    }

    [Fact]
    public async Task APick_KeepsVerbatimBytesOnlyWhereAnExportCopiesTheFile()
    {
        // The .xgp's decisions are copied verbatim, the .xg's sliced — the
        // producer's stamped ids say which. A file whose rows could not be
        // built keeps its bytes: a later book's rows might yet need them.
        _converter.FailRows = (source, _) => source.Name == "unconvertible.xg" ? new InvalidDataException("No rows.") : null;
        var session = await PickedAsync(NewSession(), Fixture(Xg), Fixture(Xgp), Fixture(OtherXg, "unconvertible.xg"));

        var decoded = session.Selection!.Decoded.ToDictionary(d => d.Name);
        Assert.Null(decoded[Xg].VerbatimBytes);
        Assert.Equal(FixtureHelper.ReadFixture(Xgp), decoded[Xgp].VerbatimBytes!.Value.ToArray());
        Assert.NotNull(decoded["unconvertible.xg"].VerbatimBytes);
    }

    [Fact]
    public async Task AnAllFailedPick_StillHasASelectionOwningItsFailures()
    {
        var session = await PickedAsync(NewSession(), Malformed("a.xg"), Malformed("b.xg"));

        Assert.Empty(session.Selection!.Decoded);
        Assert.Equal(["a.xg", "b.xg"], session.SelectionSkips(Representation.Rows).Select(s => s.FileName));
        Assert.Equal(ConversionState.Completed, session.StateOf(Representation.Rows));
    }

    [Fact]
    public async Task ASupersededPick_PublishesNothing()
    {
        var session = NewSession();
        var first = session.BeginPick();
        var second = session.BeginPick();

        Assert.False(await session.CompletePickAsync(first, NewToken(), [Fixture(Xg)], CancellationToken.None));
        Assert.Null(session.Selection);
        Assert.True(await session.CompletePickAsync(second, NewToken(), [Fixture(OtherXg)], CancellationToken.None));
        Assert.Equal(OtherXg, Assert.Single(session.Selection!.Decoded).Name);
    }

    // ── Conversions: cached, deferred, owned ────────────────────────────────

    [Fact]
    public async Task Records_ConvertOnlyWhenAsked_AndACompletedResultIsReused()
    {
        var session = await PickedAsync(NewSession(), Fixture(Xg));
        Assert.False(session.Needs(Representation.Rows));
        Assert.True(session.Needs(Representation.Records));

        await session.EnsureConversionsAsync(() => Representation.Records, CancellationToken.None);
        await session.EnsureConversionsAsync(() => Representation.Rows, CancellationToken.None);
        await session.EnsureConversionsAsync(() => Representation.Records, CancellationToken.None);

        Assert.Single(_converter.RowsCalls);
        Assert.Single(_converter.RecordsCalls);
        Assert.Equal(ConversionState.Completed, session.StateOf(Representation.Records));
        Assert.Equal(session.Projections.Rows.Count, session.Projections.Records.Count);
    }

    /// <summary>
    /// A pick while records convert: the conversion belonged to the replaced
    /// selection, so its late completion publishes nothing — the records shown
    /// are the new selection's alone.
    /// </summary>
    [Fact]
    public async Task ARecordsConversionOverlappedByAPick_PublishesNothing()
    {
        var session = await PickedAsync(NewSession(), Fixture(Xg));
        _converter.HoldRecordsCall(1);
        var selected = Representation.Records;
        var converting = session.EnsureConversionsAsync(() => selected, CancellationToken.None);
        await _converter.RecordsHeld;
        Assert.Equal(ConversionState.Running, session.StateOf(Representation.Records));

        await PickedAsync(session, Fixture(OtherXg));
        selected = Representation.Rows;
        _converter.ReleaseRecords();
        await converting;

        Assert.Equal(ConversionState.NotRequested, session.StateOf(Representation.Records));
        Assert.Empty(session.Projections.Records);
        Assert.All(session.Projections.Rows, row => Assert.Equal(OtherXg, row.SourceFile));
    }

    /// <summary>
    /// A book change while records convert: the conversion's key — its book —
    /// is no longer current, so its late completion publishes nothing, and
    /// the records shown are converted under the new book.
    /// </summary>
    [Fact]
    public async Task ARecordsConversionOverlappedByABookChange_PublishesNothing_AndTheNewBookConverts()
    {
        var session = await PickedAsync(NewSession(), Fixture(OtherXg));
        _converter.HoldRecordsCall(1);
        var converting = session.EnsureConversionsAsync(() => Representation.Records, CancellationToken.None);
        await _converter.RecordsHeld;

        var book = LoadBook();
        Assert.True(session.CompleteBookLoad(session.BeginBookLoad(), book, loadFailed: false));
        _converter.ReleaseRecords();
        await converting;

        // The held conversion ran under no book; what is shown is the one that
        // ran under the new book, which the loop requested after it.
        Assert.Equal([(OtherXg, (OpeningBook?)null), (OtherXg, book)], _converter.RecordsCalls);
        var current = Assert.Single(_converter.RecordsResults, r => r.Book is not null);
        Assert.Same(current.Records[0], session.Projections.Records[0]);
        Assert.Equal(ConversionState.Completed, session.StateOf(Representation.Records));
    }

    /// <summary>
    /// A format switch while records convert: the selection and book still
    /// stand, so the result is cached — completed — though it is not the
    /// selected key's, and nothing is converted again when records are
    /// selected once more.
    /// </summary>
    [Fact]
    public async Task ALateCompletionForAKeyNoLongerSelected_IsCached()
    {
        var session = await PickedAsync(NewSession(), Fixture(Xg));
        _converter.HoldRecordsCall(1);
        var converting = session.EnsureConversionsAsync(() => Representation.Records, CancellationToken.None);
        await _converter.RecordsHeld;

        // The format goes back to CSV: nothing more is needed for it.
        Assert.False(session.Needs(Representation.Rows));
        _converter.ReleaseRecords();
        await converting;

        Assert.Equal(ConversionState.Completed, session.StateOf(Representation.Records));
        Assert.False(session.Needs(Representation.Records));
        Assert.Single(_converter.RecordsCalls);
    }

    [Fact]
    public async Task ACancelledConversion_PublishesNothing_AndIsNotRequestedAgainUntilAsked()
    {
        var session = await PickedAsync(NewSession(), Fixture(Xg));
        using var cts = new CancellationTokenSource();
        _converter.HoldRecordsCall(1);
        var converting = session.EnsureConversionsAsync(() => Representation.Records, cts.Token);
        await _converter.RecordsHeld;

        cts.Cancel();
        _converter.ReleaseRecords();

        // The real conversion observes the token after the hold.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converting);
        Assert.Equal(ConversionState.NotRequested, session.StateOf(Representation.Records));
        Assert.Empty(session.Projections.Records);
    }

    // ── Projection A ────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectionSkips_AreTheReadSkipsAndTheSelectedKeysConversionSkips_InPickOrder_AndRefilteringChangesNone()
    {
        _converter.FailRecords = (source, _) => source.Name == OtherXg ? new InvalidDataException("No records.") : null;
        var session = await PickedAsync(NewSession(), Fixture(OtherXg), Malformed("broken.xg"), Fixture(Xg));
        await session.EnsureConversionsAsync(() => Representation.Records, CancellationToken.None);

        Assert.Equal(["broken.xg"], session.SelectionSkips(Representation.Rows).Select(s => s.FileName));
        var forRecords = session.SelectionSkips(Representation.Records);
        Assert.Equal([OtherXg, "broken.xg"], forRecords.Select(s => s.FileName));
        Assert.Equal("No records.", forRecords[0].Reason);

        // Refiltering reads and converts nothing, and changes no skip.
        session.Refilter(new FilterConfig { Players = ["__nobody__"] });
        session.Refilter(new FilterConfig());
        Assert.Equal(forRecords, session.SelectionSkips(Representation.Records));
        Assert.Single(_converter.RowsCalls, c => c.File == Xg);
    }

    // ── The last download ───────────────────────────────────────────────────

    private static WebDownload Completed() =>
        new(OutputFormat.Csv, "decisions.csv", DownloadStatus.Completed, 3, null, SkipRecord.Empty);

    [Fact]
    public async Task TheLastDownload_IsReplacedFromTheNextAttemptsStart_AndANewerAttemptOrPickRefusesALateRecord()
    {
        var session = await PickedAsync(NewSession(), Fixture(Xg));
        var first = session.BeginDownload()!;
        Assert.True(session.CompleteDownload(first, Completed()));
        Assert.NotNull(session.LastDownload);

        var second = session.BeginDownload()!;
        Assert.Null(session.LastDownload);
        var third = session.BeginDownload()!;
        Assert.False(session.CompleteDownload(second, Completed()));
        Assert.Null(session.LastDownload);

        await PickedAsync(session, Fixture(OtherXg));
        Assert.False(session.CompleteDownload(third, Completed()));
        Assert.Null(session.LastDownload);
    }

    [Fact]
    public async Task ANewPick_ClearsTheLastDownload()
    {
        var session = await PickedAsync(NewSession(), Fixture(Xg));
        Assert.True(session.CompleteDownload(session.BeginDownload()!, Completed()));

        session.BeginPick();

        Assert.Null(session.LastDownload);
        Assert.Null(session.BeginDownload());
    }

    [Fact]
    public async Task XgpDecisionsFor_PairsEachRowWithTheFileItCameFrom()
    {
        var session = await PickedAsync(NewSession(), Fixture(Xg), Fixture(Xgp));
        var rows = session.Projections.Rows;

        var decisions = session.XgpDecisionsFor(rows);

        Assert.All(decisions, d => Assert.Equal(d.Row.SourceFile, d.Source.Name));
        Assert.Throws<ArgumentException>(() => session.XgpDecisionsFor(
            [TestRecords.Row(TestRecords.CheckerPlay())]));
    }

    private static OpeningBook LoadBook()
    {
        Assert.True(new XgProcessingService().TryLoadOpeningBook(
            FixtureHelper.ReadFixture("OpeningBookV2.ob"), out var book));
        return book!;
    }
}
