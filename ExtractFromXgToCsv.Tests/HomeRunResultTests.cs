using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Razor;
using XgFilter_Razor.TestSupport;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins, through the real page, that a Local run's outcome is judged before it
/// is published (halheinrich/backgammon#374, Arc 2). An XGP run ends with
/// bookkeeping — Home advances and saves the export counter — and that write
/// can take as long as the browser does. A run whose inputs were superseded
/// while it ran must not show "Done" for the old folder while the new one is
/// on screen: not while the write is pending, nor after it completes. The job
/// still does its own accounting: the counter advances by what it wrote.
/// <para>
/// Storage is planned: the bookkeeping's first write is held, which is what
/// opens the window the earlier panel test could not see — it waited for the
/// whole run before asserting.
/// </para>
/// </summary>
public class HomeRunResultTests : BunitContext
{
    private const string FolderA = @"D:\xg\matches";
    private const string FolderB = @"D:\xg\other";
    private const string OutputFolder = @"D:\xg\positions";

    private readonly BrowserStoragePlan _storage;

    // An XGP job that wrote two positions under the default options, and says
    // so the way the job runner does: its commit, beside the row count.
    private readonly CapturingProcessHandler _processing = new(CapturingProcessHandler.Terminal(new ProcessingProgress
    {
        Complete = true,
        TotalRows = 2,
        XgpCommit = new XgpExportCommit(new XgpExportOptions(), 2),
    }));

    public HomeRunResultTests()
    {
        this.AddHomeServices("Local", _processing);
        _storage = BrowserStoragePlan.On(JSInterop);
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectHomeRestore(new()
        {
            [HomeStoragePlan.FolderPathKey] = FolderA,
            [HomeStoragePlan.OutputFormatKey] = nameof(OutputFormat.Xgp),
        });
        _storage.ExpectLocalPanelRestore(BrowserStorageReadAnswer.Stored(OutputFolder));
        _storage.ExpectFilterPanelMount();
    }

    /// <summary>
    /// The bookkeeping an export of two decisions under the default pattern
    /// makes, its first write held.
    /// </summary>
    private BrowserStorageHeldWrite ExpectTheExportBookkeepingHeld()
    {
        var pattern = _storage.ExpectHeldWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpPatternKey, "pos{n}");
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpLastNumberKey, "2",
            BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpSuffixLengthKey, "3",
            BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectRemove(BrowserStorageArea.Local, HomeStoragePlan.XgpPrefixKey,
            BrowserStorageWriteAnswer.Succeeded);
        return pattern;
    }

    /// <summary>
    /// Folder A, the empty filter ready, XGP: a run started, its status held.
    /// </summary>
    private async Task<(IRenderedComponent<Home> Page, Task Run)> StartARunOverFolderAAsync()
    {
        var cut = this.RenderHome();
        Assert.Equal(FilterSourceToken.FromPath(FolderA), Services.GetRequiredService<FilterSetup>().Current.Source);
        Assert.False(cut.RunButton().HasAttribute("disabled"));

        _processing.HoldStatus();
        var run = cut.RunButton().ClickAsync(new MouseEventArgs());
        await _processing.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);
        return (cut, run);
    }

    [Fact]
    public async Task ASupersededXgpRun_NeverShowsItsResult_WhileItsBookkeepingIsPendingOrAfter()
    {
        var (cut, run) = await StartARunOverFolderAAsync();

        // The folder changes under the running job; the empty filter stays
        // ready for the new one, so nothing about the gate falls.
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.FolderPathKey, FolderB,
            BrowserStorageWriteAnswer.Succeeded);
        await cut.CommitFolderAsync(FolderB);
        var bookkeeping = ExpectTheExportBookkeepingHeld();

        _processing.ReleaseStatus();
        cut.WaitForAssertion(() => Assert.True(bookkeeping.IsReached), CapturingProcessHandler.RunTimeout);

        // The job has done its accounting — the write is pending — and A's
        // result is not on screen beside folder B.
        Assert.DoesNotContain("Done", cut.Markup);

        bookkeeping.Release(BrowserStorageWriteAnswer.Succeeded);
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.DoesNotContain("Done", cut.Markup);
        // The counter advanced by what the job wrote, whatever is on screen.
        Assert.Equal(3, cut.FindComponent<LocalModePanel>().Instance.XgpOptions.StartNumber);
        _storage.Verify();
    }

    /// <summary>
    /// The control: the same run, its bookkeeping held the same way, with its
    /// inputs unchanged — its result is published before the bookkeeping, and
    /// stays. The absence above is the supersession's doing, not the hold's.
    /// </summary>
    [Fact]
    public async Task AnXgpRunOverUnchangedInputs_ShowsItsResult_WhileItsBookkeepingIsPending()
    {
        var (cut, run) = await StartARunOverFolderAAsync();
        var bookkeeping = ExpectTheExportBookkeepingHeld();

        _processing.ReleaseStatus();
        cut.WaitForAssertion(() => Assert.True(bookkeeping.IsReached), CapturingProcessHandler.RunTimeout);

        Assert.Contains("Done", cut.Markup);

        bookkeeping.Release(BrowserStorageWriteAnswer.Succeeded);
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("Done", cut.Markup);
        Assert.Equal(3, cut.FindComponent<LocalModePanel>().Instance.XgpOptions.StartNumber);
        _storage.Verify();
    }
}
