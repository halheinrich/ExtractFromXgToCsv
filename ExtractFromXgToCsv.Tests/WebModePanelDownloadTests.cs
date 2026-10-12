using System.IO.Compression;
using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// A Web-mode download attempt (the umbrella's <c>SPEC-notices.md</c> §7.1 and
/// §7.3, F1 and F2 of §7.4): an XGP decision whose payload cannot be prepared
/// is skipped and consumes no number; the download commits once, at the
/// finalized archive's hand-off to the browser, so a failed hand-off exports
/// nothing and advances no number; the attempt captures its options at its
/// start; and its record — "the last download" — is replaced from the next
/// attempt's start and cleared by a new pick. The host's numbering receives
/// the attempt's commit, never the selected count.
/// </summary>
/// <remarks>
/// A payload that cannot be prepared is made by having the converter add a
/// decision the source does not hold, which the producer's slice refuses: no
/// real fixture's decision fails its preparation.
/// </remarks>
public class WebModePanelDownloadTests : BunitContext
{
    private const string Fixture = "MTCH4064.xg";
    private const string DownloadList = "div.skipped-notice.download-skips";

    private static readonly XgpExportOptions Options = new() { NamePattern = "pos{n}", StartNumber = 1, SuffixLength = 3 };

    private readonly ScriptedConverter _converter = new();
    private readonly List<XgpExportCommit> _committed = [];

    public WebModePanelDownloadTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
        Services.AddSingleton<ISourceConverter>(_converter);
    }

    /// <summary>The decision the converter adds after the file's first: one the file does not hold.</summary>
    private static readonly DecisionRow Nowhere = TestRecords.Row(TestRecords.CheckerPlay(
        id: new XgDecisionId(Fixture, Game: 99, MoveNumber: 1, IsCube: false)));

    private void AddADecisionWhosePreparationFails() =>
        _converter.RewriteRows = (_, rows) => [rows[0], Nowhere, .. rows.Skip(1)];

    private IRenderedComponent<WebModePanel> RenderPicked(
        OutputFormat format = OutputFormat.Xgp, Action<XgpExportCommit>? onExported = null)
    {
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, format)
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.XgpOptions, Options)
            .Add(c => c.XgpAnonymize, false)
            .Add(c => c.OnXgpExported, onExported ?? (c => _committed.Add(c))));
        Pick(cut);
        return cut;
    }

    private static void Pick(IRenderedComponent<WebModePanel> cut) =>
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(Fixture), Fixture));

    private static AngleSharp.Dom.IElement DownloadButton(IRenderedComponent<WebModePanel> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Download") || b.TextContent.Contains("Building"));

    private static List<string> EntryNames(byte[] zipBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        return [.. zip.Entries.Select(e => e.FullName)];
    }

    [Fact]
    public async Task ASkippedPreparation_ConsumesNoNumber_TheCommitCountsTheArchive_AndTheLastDownloadNamesIt()
    {
        AddADecisionWhosePreparationFails();
        var cut = RenderPicked();
        var selected = cut.Instance.RowCache.FilteredRows.Count;

        await DownloadButton(cut).ClickAsync(new MouseEventArgs());

        var names = EntryNames((byte[])JSInterop.VerifyInvoke("downloadFile").Arguments[2]!);
        Assert.Equal(selected - 1, names.Count);
        Assert.Equal(Enumerable.Range(1, selected - 1).Select(n => $"pos{n:D3}.xgp"), names);

        // The host receives what was committed, under the captured options —
        // not the selected count.
        Assert.Equal(new XgpExportCommit(Options, selected - 1), Assert.Single(_committed));

        var record = cut.Instance.Session.LastDownload!;
        Assert.Equal(DownloadStatus.Completed, record.Status);
        Assert.Equal(selected - 1, record.Committed);
        Assert.Equal(Nowhere.Id, Assert.Single(record.ExportSkips).DecisionId);

        var list = cut.Find(DownloadList);
        Assert.Equal("The last download (XGP)", list.QuerySelector(".skip-list-label")!.TextContent);
        Assert.Contains("Skipped 1 decision", list.TextContent);
        Assert.Contains("decision " + Nowhere.Id, list.TextContent);
        Assert.DoesNotContain(NoticeBox.AllIn(cut), b => b.Kind == NoticeKind.Error);
    }

    /// <summary>
    /// The commit is the hand-off: every entry was prepared, and the browser
    /// refused the file. Nothing counts as exported, no number advances, and
    /// the attempt's record keeps its diagnostics, labelled as failed.
    /// </summary>
    [Fact]
    public async Task AFailedHandOff_ExportsNothing_AdvancesNoNumber_AndKeepsTheAttemptsSkips()
    {
        AddADecisionWhosePreparationFails();
        JSInterop.SetupVoid("downloadFile", _ => true).SetException(new JSException("The download was blocked."));
        var cut = RenderPicked();

        await DownloadButton(cut).ClickAsync(new MouseEventArgs());

        Assert.Empty(_committed);
        var record = cut.Instance.Session.LastDownload!;
        Assert.Equal(DownloadStatus.Failed, record.Status);
        Assert.Equal(0, record.Committed);
        Assert.Null(record.Numbering);
        Assert.Single(record.ExportSkips);
        Assert.Equal("The last download (XGP, failed)", cut.Find($"{DownloadList} .skip-list-label").TextContent);
        Assert.Contains("The download was blocked.",
            Assert.Single(NoticeBox.AllIn(cut), b => b.Kind == NoticeKind.Error).Content.TextContent);
    }

    /// <summary>
    /// A control changed while the hand-off is pending: the attempt captured
    /// its options at its start, so its archive, its record and the commit the
    /// host receives are those options', whatever the controls now show.
    /// </summary>
    [Fact]
    public async Task OptionsChangedWhileTheHandOffIsPending_TheAttemptsCapturedOptionsStand()
    {
        var handOff = JSInterop.SetupVoid("downloadFile", _ => true);
        var cut = RenderPicked();
        var selected = cut.Instance.RowCache.FilteredRows.Count;

        var click = DownloadButton(cut).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(JSInterop.Invocations["downloadFile"]));
        cut.Render(p => p.Add(c => c.XgpOptions, Options with { NamePattern = "later{n}", StartNumber = 50 }));
        handOff.SetVoidResult();
        await click.WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal("pos001.xgp", EntryNames((byte[])JSInterop.VerifyInvoke("downloadFile").Arguments[2]!)[0]);
        Assert.Equal(new XgpExportCommit(Options, selected), Assert.Single(_committed));
        Assert.Equal(new XgpExportCommit(Options, selected), cut.Instance.Session.LastDownload!.Numbering);
    }

    /// <summary>
    /// A switch to Diagram JSON while a CSV attempt's hand-off is pending: the
    /// attempt builds and records the CSV it captured, and the gesture ends by
    /// converting what the selected output now needs — so Diagram JSON is
    /// converted and ready, never left unconverted with Download dark.
    /// </summary>
    [Fact]
    public async Task AFormatSwitchDuringTheHandOff_TheAttemptKeepsItsFormat_AndTheGestureEndsConverted()
    {
        var handOff = JSInterop.SetupVoid("downloadFile", _ => true);
        var cut = RenderPicked(OutputFormat.Csv);
        var rows = cut.Instance.RowCache.FilteredRows.Count;

        var click = DownloadButton(cut).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(JSInterop.Invocations["downloadFile"]));
        cut.Render(p => p.Add(c => c.OutputFormat, OutputFormat.DiagramJson));
        Assert.Equal(ConversionState.NotRequested, cut.Instance.Session.StateOf(Representation.Records));

        handOff.SetVoidResult();
        await click.WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal("text/csv", JSInterop.VerifyInvoke("downloadFile").Arguments[1]);
        var record = cut.Instance.Session.LastDownload!;
        Assert.Equal(OutputFormat.Csv, record.Format);
        Assert.Equal(rows, record.Committed);

        Assert.Equal(ConversionState.Completed, cut.Instance.Session.StateOf(Representation.Records));
        Assert.Empty(cut.FindAll(".busy-notice"));
        Assert.False(DownloadButton(cut).HasAttribute("disabled"));
        Assert.Contains("decisions match current filters", cut.Find("#matchCount").TextContent);
    }

    /// <summary>
    /// A switch to Diagram JSON in the window between the click and the
    /// attempt's work — the busy wrapper's yield, which hands the thread back
    /// so the busy notice can paint. The attempt began at the click, so it
    /// builds the CSV it was clicked for. Before the correction it read the
    /// format after the yield and built "[]" from the unconverted records.
    /// The switch is made in the same dispatcher turn as the click, after the
    /// gesture has yielded and before its work resumes.
    /// </summary>
    [Fact]
    public async Task AFormatSwitchBeforeTheAttemptRuns_TheAttemptBuildsTheFormatItWasClickedFor()
    {
        var cut = RenderPicked(OutputFormat.Csv);
        var rows = cut.Instance.RowCache.FilteredRows.Count;

        Task click = Task.CompletedTask;
        await cut.InvokeAsync(() =>
        {
            click = cut.Instance.DownloadForTest();
            cut.Render(p => p.Add(c => c.OutputFormat, OutputFormat.DiagramJson));
        });
        await click.WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal("text/csv", JSInterop.VerifyInvoke("downloadFile").Arguments[1]);
        var record = cut.Instance.Session.LastDownload!;
        Assert.Equal(OutputFormat.Csv, record.Format);
        Assert.Equal(rows, record.Committed);
        Assert.Equal(ConversionState.Completed, cut.Instance.Session.StateOf(Representation.Records));
    }

    /// <summary>
    /// A click that reaches the download while the selected format is not
    /// converted — Diagram JSON's records still converting — begins no
    /// attempt: nothing is built or handed over, and no record is made. Before
    /// the correction it built "[]" from the unconverted records and recorded
    /// it as a completed download.
    /// </summary>
    [Fact]
    public async Task AClickWhileTheSelectedFormatIsNotConverted_BuildsNothing()
    {
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.DiagramJson)
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.XgpOptions, Options)
            .Add(c => c.XgpAnonymize, false)
            .Add(c => c.OnXgpExported, (XgpExportCommit c) => _committed.Add(c)));
        _converter.HoldRecordsCall(1);
        var pick = Task.Run(() => Pick(cut));
        await _converter.RecordsHeld.WaitAsync(NoticeTesting.OperationBound);
        Assert.Equal(ConversionState.Running, cut.Instance.Session.StateOf(Representation.Records));

        await cut.InvokeAsync(() => cut.Instance.DownloadForTest());

        Assert.Empty(JSInterop.Invocations["downloadFile"]);
        Assert.Null(cut.Instance.Session.LastDownload);

        _converter.ReleaseRecords();
        await pick.WaitAsync(NoticeTesting.OperationBound);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".busy-notice")), NoticeTesting.OperationBound);
        Assert.Equal(ConversionState.Completed, cut.Instance.Session.StateOf(Representation.Records));
    }

    /// <summary>
    /// A held download's hand-off fails after a newer pick has replaced its
    /// selection: the attempt no longer owns its outcome, so its failure is not
    /// the run error, as its record is not "the last download". Before the
    /// correction the failure was shown whatever had happened since.
    /// </summary>
    [Fact]
    public async Task AHeldDownloadThatFailsAfterANewerPick_ShowsNoError()
    {
        var handOff = JSInterop.SetupVoid("downloadFile", _ => true);
        var cut = RenderPicked(OutputFormat.Csv);

        var click = DownloadButton(cut).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(JSInterop.Invocations["downloadFile"]));
        Pick(cut);
        handOff.SetException(new JSException("The download was blocked."));
        await click.WaitAsync(NoticeTesting.OperationBound);

        Assert.DoesNotContain(NoticeBox.AllIn(cut), b => b.Kind == NoticeKind.Error);
        Assert.Null(cut.Instance.Session.LastDownload);
    }

    /// <summary>
    /// A successful XGP hand-off whose record a newer pick has made obsolete
    /// still reaches the host's numbering: the archive was delivered, so the
    /// numbers it used are recorded, whatever is shown (SPEC-notices.md §7.1,
    /// §7.2).
    /// </summary>
    [Fact]
    public async Task AnObsoleteAttemptsSuccessfulHandOff_StillReachesTheHostsNumbering()
    {
        var handOff = JSInterop.SetupVoid("downloadFile", _ => true);
        var cut = RenderPicked();

        var click = DownloadButton(cut).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(JSInterop.Invocations["downloadFile"]));
        Pick(cut);
        handOff.SetVoidResult();
        await click.WaitAsync(NoticeTesting.OperationBound);

        Assert.Null(cut.Instance.Session.LastDownload);
        var commit = Assert.Single(_committed);
        Assert.Equal(Options, commit.Options);
        Assert.True(commit.Count > 0);
    }

    /// <summary>
    /// Recording what a delivered archive committed is the host's accounting,
    /// after the hand-off, not the attempt's: when it fails, the failure is
    /// the run error, and the download stays completed.
    /// </summary>
    [Fact]
    public async Task AnAccountingFailureAfterTheHandOff_IsShown_AndTheDownloadStaysCompleted()
    {
        var cut = RenderPicked(onExported: _ => throw new InvalidOperationException("The numbering could not be recorded."));

        await DownloadButton(cut).ClickAsync(new MouseEventArgs());

        Assert.Contains("The numbering could not be recorded.",
            Assert.Single(NoticeBox.AllIn(cut), b => b.Kind == NoticeKind.Error).Content.TextContent);
        Assert.Equal(DownloadStatus.Completed, cut.Instance.Session.LastDownload!.Status);
    }

    [Fact]
    public async Task TheNextAttempt_ReplacesTheLastDownload()
    {
        AddADecisionWhosePreparationFails();
        var cut = RenderPicked();
        await DownloadButton(cut).ClickAsync(new MouseEventArgs());
        Assert.Single(cut.FindAll(DownloadList));

        cut.Render(p => p.Add(c => c.OutputFormat, OutputFormat.Csv));
        await DownloadButton(cut).ClickAsync(new MouseEventArgs());

        // A CSV download prepares no payloads, so it skipped nothing.
        Assert.Empty(cut.FindAll(DownloadList));
        Assert.Equal(OutputFormat.Csv, cut.Instance.Session.LastDownload!.Format);
    }

    [Fact]
    public async Task ANewPick_ClearsTheLastDownload()
    {
        AddADecisionWhosePreparationFails();
        var cut = RenderPicked();
        await DownloadButton(cut).ClickAsync(new MouseEventArgs());
        Assert.Single(cut.FindAll(DownloadList));

        Pick(cut);

        Assert.Empty(cut.FindAll(DownloadList));
        Assert.Null(cut.Instance.Session.LastDownload);
    }
}
