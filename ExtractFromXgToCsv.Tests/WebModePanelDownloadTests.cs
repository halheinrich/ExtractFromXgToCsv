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

    private IRenderedComponent<WebModePanel> RenderPicked(OutputFormat format = OutputFormat.Xgp)
    {
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, format)
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.XgpOptions, Options)
            .Add(c => c.XgpAnonymize, false)
            .Add(c => c.OnXgpExported, (XgpExportCommit c) => _committed.Add(c)));
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
        Assert.Equal("The last download", list.QuerySelector(".skip-list-label")!.TextContent);
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
        Assert.Equal("The last download (failed)", cut.Find($"{DownloadList} .skip-list-label").TextContent);
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
