using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Web mode's conversion cache through the panel (halheinrich/backgammon#74,
/// the umbrella's <c>SPEC-notices.md</c> §7.3): rows convert on a pick, records
/// only when Diagram JSON is selected — so CSV-only work never calls the
/// diagram conversion, which a timing alone could not show — and a format
/// switch reuses a completed result. Readiness and counts read the selected
/// key's state: a row conversion failure never hides converted records, and
/// "not converted yet" is never zero matches. A late completion owned by a
/// replaced selection publishes nothing.
/// </summary>
/// <remarks>
/// The panel converts through <see cref="ScriptedConverter"/>, the real
/// conversion wrapped so the calls can be counted, a file's conversion failed,
/// or a records conversion held. A gesture held inside the converter is
/// started off the test thread: bUnit's upload waits for the handler to end.
/// </remarks>
public class WebModePanelConversionTests : BunitContext
{
    private const string A = "MTCH4064.xg";
    private const string B = "ajhhBG0407.xg";

    private readonly ScriptedConverter _converter = new();

    public WebModePanelConversionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
        Services.AddSingleton<ISourceConverter>(_converter);
    }

    private IRenderedComponent<WebModePanel> RenderPanel(OutputFormat format, FilterConfig? filter = null) =>
        Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, format)
            .Add(c => c.FilterInEffect, filter ?? new FilterConfig())
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

    private static void Pick(IRenderedComponent<WebModePanel> cut, params string[] fixtures) =>
        cut.FindComponents<InputFile>()[0].UploadFiles(
            fixtures.Select(f => InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(f), f)).ToArray());

    private static void SwitchTo(IRenderedComponent<WebModePanel> cut, OutputFormat format) =>
        cut.Render(p => p.Add(c => c.OutputFormat, format));

    private static AngleSharp.Dom.IElement DownloadButton(IRenderedComponent<WebModePanel> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Download") || b.TextContent.Contains("Building"));

    private static void WaitForIdle(IRenderedComponent<WebModePanel> cut) =>
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".busy-notice")), TimeSpan.FromSeconds(10));

    // ── Records are deferred ────────────────────────────────────────────────

    [Fact]
    public async Task ACsvOnlyPick_NeverCallsTheRecordsConversion()
    {
        var cut = RenderPanel(OutputFormat.Csv);

        Pick(cut, A, B);
        await DownloadButton(cut).ClickAsync(new MouseEventArgs());

        Assert.Equal([A, B], _converter.RowsCalls.Select(c => c.File));
        Assert.Empty(_converter.RecordsCalls);
        Assert.Equal(ConversionState.NotRequested, cut.Instance.Session.StateOf(Representation.Records));
        JSInterop.VerifyInvoke("downloadFile");
    }

    [Fact]
    public void SwitchingToDiagramJson_ConvertsRecordsOnce_AndLaterSwitchesReuseBothResults()
    {
        var cut = RenderPanel(OutputFormat.Csv);
        Pick(cut, A, B);

        SwitchTo(cut, OutputFormat.DiagramJson);
        WaitForIdle(cut);
        Assert.Equal([A, B], _converter.RecordsCalls.Select(c => c.File));
        Assert.Contains("decisions match current filters", cut.Find("#matchCount").TextContent);

        SwitchTo(cut, OutputFormat.Csv);
        WaitForIdle(cut);
        SwitchTo(cut, OutputFormat.DiagramJson);
        WaitForIdle(cut);

        Assert.Equal(2, _converter.RowsCalls.Count);
        Assert.Equal(2, _converter.RecordsCalls.Count);
        Assert.Equal(
            cut.Instance.RowCache.FilteredRows.Select(r => r.Id),
            cut.Instance.RowCache.FilteredDiagramRows.Select(r => r.Id));
    }

    // ── Readiness follows the selected key ──────────────────────────────────

    [Fact]
    public void ARowConversionFailure_WithRecordsConverted_KeepsDiagramJsonAvailable()
    {
        _converter.FailRows = (_, _) => new InvalidDataException("Rows refused.");
        var cut = RenderPanel(OutputFormat.DiagramJson);

        Pick(cut, A);
        WaitForIdle(cut);

        Assert.Empty(cut.Instance.RowCache.Rows);
        Assert.NotEmpty(cut.Instance.RowCache.Records);
        Assert.False(DownloadButton(cut).HasAttribute("disabled"));
        Assert.Contains($"of {cut.Instance.RowCache.Records.Count} decisions match", cut.Find("#matchCount").TextContent);
        // The rows' failure is not this output's loss.
        Assert.Empty(cut.FindAll("div.selection-skips"));

        // For CSV it is: the rows are the output, so the file is named and
        // nothing can be downloaded.
        SwitchTo(cut, OutputFormat.Csv);
        Assert.Contains("Rows refused.", cut.Find("div.selection-skips").TextContent);
        Assert.True(DownloadButton(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#matchCount"));
    }

    [Fact]
    public async Task NotConvertedYet_IsNeverShownAsZeroMatches()
    {
        // A filter that excludes everything: once a representation is
        // converted, its zero is a real zero-match.
        var cut = RenderPanel(OutputFormat.Csv, new FilterConfig { Players = ["__nobody__"] });
        Pick(cut, A);
        Assert.Single(cut.FindAll(".zero-match-notice"));

        _converter.HoldRecordsCall(1);
        var switching = Task.Run(() => SwitchTo(cut, OutputFormat.DiagramJson));
        await _converter.RecordsHeld.WaitAsync(TimeSpan.FromSeconds(10));

        // Records not converted yet: no zero-match, no count, no download.
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".busy-notice")));
        Assert.Empty(cut.FindAll(".zero-match-notice"));
        Assert.Empty(cut.FindAll("#matchCount"));
        Assert.True(DownloadButton(cut).HasAttribute("disabled"));

        _converter.ReleaseRecords();
        await switching.WaitAsync(TimeSpan.FromSeconds(10));
        WaitForIdle(cut);
        Assert.Single(cut.FindAll(".zero-match-notice"));
    }

    /// <summary>
    /// Under XGP, rows not converted yet — a book's re-extract under way — are
    /// never "No decisions match the filters": the hint reads the selected
    /// key's state. Before the correction it read the empty projection and
    /// said nothing matched. Once the rows are converted, a real zero is said.
    /// </summary>
    [Fact]
    public async Task UnderXgp_RowsNotConvertedYet_AreNeverNothingMatches()
    {
        var cut = RenderPanel(OutputFormat.Xgp, new FilterConfig { Players = ["__nobody__"] });
        Pick(cut, A);
        Assert.Equal("No decisions match the filters", cut.Find("#xgpBlockedHint").TextContent.Trim());

        _converter.HoldRowsCall(2);
        var bookLoad = Task.Run(() => cut.FindComponents<InputFile>()[1].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture("OpeningBookV2.ob"), "OpeningBookV2.ob")));
        await _converter.RowsHeld.WaitAsync(TimeSpan.FromSeconds(10));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".busy-notice")));
        Assert.Equal(ConversionState.Running, cut.Instance.Session.StateOf(Representation.Rows));

        // A render while the rows convert — the host re-renders the panel on
        // a filter change, say — shows the state as it stands.
        cut.Render();
        Assert.Empty(cut.FindAll("#xgpBlockedHint"));
        Assert.True(DownloadButton(cut).HasAttribute("disabled"));

        _converter.ReleaseRows();
        await bookLoad.WaitAsync(TimeSpan.FromSeconds(10));
        WaitForIdle(cut);
        Assert.Equal("No decisions match the filters", cut.Find("#xgpBlockedHint").TextContent.Trim());
    }

    /// <summary>
    /// The preview shows the rows an output is built from, so it follows the
    /// selected representation: there for CSV, gone for Diagram JSON — whose
    /// records convert on their own and could describe a different set — and
    /// back for CSV.
    /// </summary>
    [Fact]
    public void ThePreview_FollowsTheSelectedRepresentation()
    {
        var cut = RenderPanel(OutputFormat.Csv);
        Pick(cut, A);
        Assert.Contains("Preview", cut.Find("h5").TextContent);

        SwitchTo(cut, OutputFormat.DiagramJson);
        WaitForIdle(cut);
        Assert.Empty(cut.FindAll("h5"));
        Assert.Empty(cut.FindAll("table"));

        SwitchTo(cut, OutputFormat.Csv);
        Assert.Contains("Preview", cut.Find("h5").TextContent);
    }

    // ── A late completion owned by a replaced selection ─────────────────────

    /// <summary>
    /// A pick while records convert: the conversion belonged to the replaced
    /// selection, so its completion publishes nothing — the records are the
    /// new selection's, converted for it.
    /// </summary>
    [Fact]
    public async Task ARecordsConversionOverlappedByAPick_PublishesNothing()
    {
        var cut = RenderPanel(OutputFormat.DiagramJson);
        _converter.HoldRecordsCall(1);
        var first = Task.Run(() => Pick(cut, A));
        await _converter.RecordsHeld.WaitAsync(TimeSpan.FromSeconds(10));

        Pick(cut, B);
        _converter.ReleaseRecords();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        WaitForIdle(cut);

        Assert.Equal([A, B], _converter.RecordsCalls.Select(c => c.File));
        Assert.NotEmpty(cut.Instance.RowCache.Records);
        Assert.All(cut.Instance.RowCache.Records, r => Assert.Equal(B, r.Id.Filename));
        Assert.All(cut.Instance.RowCache.FilteredDiagramRows, r => Assert.Equal(B, r.Id.Filename));
    }

    /// <summary>
    /// A book change while records convert: the conversion's key is no longer
    /// current, so its completion publishes nothing, and the records shown are
    /// the ones converted under the new book.
    /// </summary>
    [Fact]
    public async Task ARecordsConversionOverlappedByABookChange_PublishesNothing()
    {
        var cut = RenderPanel(OutputFormat.DiagramJson);
        _converter.HoldRecordsCall(1);
        var pick = Task.Run(() => Pick(cut, B));
        await _converter.RecordsHeld.WaitAsync(TimeSpan.FromSeconds(10));

        cut.FindComponents<InputFile>()[1].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture("OpeningBookV2.ob"), "OpeningBookV2.ob"));
        _converter.ReleaseRecords();
        await pick.WaitAsync(TimeSpan.FromSeconds(10));
        WaitForIdle(cut);

        Assert.Equal(2, _converter.RecordsCalls.Count);
        Assert.Null(_converter.RecordsCalls[0].Book);
        Assert.NotNull(_converter.RecordsCalls[1].Book);
        // The held conversion finished last, under no book, and is not what
        // is shown: the one converted under the new book is.
        var obsolete = Assert.Single(_converter.RecordsResults, r => r.Book is null);
        var current = Assert.Single(_converter.RecordsResults, r => r.Book is not null);
        Assert.Same(obsolete.Records, _converter.RecordsResults[^1].Records);
        Assert.Same(current.Records[0], cut.Instance.RowCache.Records[0]);
        Assert.NotSame(obsolete.Records[0], cut.Instance.RowCache.Records[0]);
    }

    // ── Refiltering converts nothing and changes no skip ────────────────────

    [Fact]
    public void Refiltering_ChangesNoSkip_AndConvertsNothing()
    {
        var cut = RenderPanel(OutputFormat.Csv);
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(A), A),
            InputFileContent.CreateFromBinary(System.Text.Encoding.ASCII.GetBytes("not an XG file"), "broken.xg"));
        var before = cut.Find("div.selection-skips").TextContent;
        var calls = _converter.RowsCalls.Count;

        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig { Players = ["__nobody__"] }));
        Assert.Equal(before, cut.Find("div.selection-skips").TextContent);
        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig { ErrorMin = 0.05 }));
        Assert.Equal(before, cut.Find("div.selection-skips").TextContent);

        Assert.Equal(calls, _converter.RowsCalls.Count);
        Assert.Empty(_converter.RecordsCalls);
    }
}
