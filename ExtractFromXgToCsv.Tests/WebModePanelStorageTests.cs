using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="WebModePanel"/>'s own browser storage — the file-name and
/// folder-hint keys — under a browser that refuses it
/// (halheinrich/backgammon#102, Extract's portion; halheinrich/backgammon#374).
/// <para>
/// <b>The blocked upload.</b> The panel used to await an unguarded
/// <c>setItem</c> for the folder hint before reading any selected file, so a
/// browser that refused storage failed every Web-mode upload: the write threw
/// out of the selection handler and the files were never read. The hint is a
/// convenience; a refused write of it is reported to the storage condition and
/// the upload goes on.
/// </para>
/// <para>
/// Storage is the subject here, so it is planned: every call the panel makes
/// is declared on a <see cref="BrowserStoragePlan"/>, in the accessor's terms,
/// and each test ends with <see cref="BrowserStoragePlan.Verify"/>.
/// </para>
/// </summary>
public class WebModePanelStorageTests : BunitContext
{
    private const string Fixture = "MTCH4064.xg";

    private readonly BrowserStoragePlan _storage;

    public WebModePanelStorageTests()
    {
        Services.AddClientServices();
        _storage = BrowserStoragePlan.On(JSInterop);
    }

    private BrowserStorageCondition Condition => Services.GetRequiredService<BrowserStorageCondition>();

    private IRenderedComponent<WebModePanel> RenderPanel()
    {
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));
        // The panel's restore runs in its first OnAfterRenderAsync. Each read
        // is answered at the call, so it has run by now; Verify holds each
        // test to both reads having been made.
        return cut;
    }

    private static void Upload(IRenderedComponent<WebModePanel> cut) =>
        // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(Fixture), Fixture));

    private void ExpectRestore(BrowserStorageReadAnswer answer)
    {
        _storage.ExpectRead(BrowserStorageArea.Local, "xg_csvFileName", answer);
        _storage.ExpectRead(BrowserStorageArea.Local, "xg_lastFolderHint", answer);
    }

    private const string HintAfterUpload = "1 file(s) — first: " + Fixture;

    /// <summary>
    /// The regression pin: the folder-hint write is refused, and the upload
    /// still reads its files and extracts their decisions. At dd978a9 the
    /// refused write threw out of the handler before any file was read.
    /// </summary>
    [Fact]
    public void RefusedFolderHintWrite_TheUploadStillReadsItsFiles()
    {
        ExpectRestore(BrowserStorageReadAnswer.Absent);
        _storage.ExpectWrite(BrowserStorageArea.Local, "xg_lastFolderHint", HintAfterUpload,
            BrowserStorageWriteAnswer.Refused);
        var cut = RenderPanel();

        Upload(cut);

        Assert.NotEmpty(cut.Instance.RowCache.Rows);
        Assert.NotEmpty(cut.Instance.RowCache.FilteredRows);
        // No run error, nor any other notice: the selection handler ran to
        // its end.
        Assert.Empty(NoticeBox.AllIn(cut));
        // The hint is still shown for this visit — only its saving failed —
        // and the refusal reached the app's one storage condition.
        Assert.Contains(HintAfterUpload, cut.Markup);
        Assert.True(Condition.HasOccurred);
        _storage.Verify();
    }

    [Fact]
    public void RefusedRestore_LandsOnTheDefaults_AndIsReported()
    {
        ExpectRestore(BrowserStorageReadAnswer.Refused);

        var cut = RenderPanel();

        // The panel rendered, with no folder hint to show: the refused read
        // is "nothing stored" to it, and the refusal was reported.
        cut.WaitForAssertion(() => Assert.True(Condition.HasOccurred));
        Assert.DoesNotContain("Last:", cut.Markup);
        _storage.Verify();
    }

    [Fact]
    public void WorkingStorage_RestoresAndSaves_AndReportsNothing()
    {
        // The control for both cases above: answered reads restore, a landed
        // write is silent, and the condition never begins.
        _storage.ExpectRead(BrowserStorageArea.Local, "xg_csvFileName",
            BrowserStorageReadAnswer.Stored("mine.csv"));
        _storage.ExpectRead(BrowserStorageArea.Local, "xg_lastFolderHint",
            BrowserStorageReadAnswer.Stored("2 file(s) — first: old.xg"));
        _storage.ExpectWrite(BrowserStorageArea.Local, "xg_lastFolderHint", HintAfterUpload,
            BrowserStorageWriteAnswer.Succeeded);

        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));
        cut.WaitForAssertion(() => Assert.Contains("old.xg", cut.Markup));
        Upload(cut);

        Assert.NotEmpty(cut.Instance.RowCache.Rows);
        Assert.Contains("value=\"mine.csv\"", cut.Markup);
        Assert.False(Condition.HasOccurred);
        _storage.Verify();
    }
}
