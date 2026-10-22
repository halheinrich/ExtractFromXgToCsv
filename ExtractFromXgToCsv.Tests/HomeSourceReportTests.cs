using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
using XgFilter_Razor.Components;
using XgFilter_Razor.TestSupport;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins how the app reports its source to the app-scoped
/// <see cref="FilterSetup"/> (halheinrich/backgammon#374, Arc 2), through
/// <see cref="Home"/>: the token identifies the selection, and an unresolved
/// source is not "no source".
/// <para>
/// <b>The hazard.</b> The folder arrives asynchronously, from storage, and
/// <c>ReportSource(null)</c> is a real answer that ends an existing setup. An
/// app that reported before its restore had finished would end the setup and
/// then report the restored folder as a new one: the applied baseline gone,
/// Apply re-armed, Run dark, behind the user's back. So the coordinator
/// reports nothing until its restore has resolved the source, and Home renders
/// no panel before then. The restore's last read, held below, makes that
/// window observable.
/// </para>
/// <para>
/// <b>Once per app</b> (halheinrich/backgammon#424). The restore runs once for
/// the app, whatever happens to the page that started it: a remount of Home
/// reads none of the coordinator's settings and reports nothing, so the setup,
/// its generation and its baseline stand; a restore in flight when its page
/// goes completes for the app. An explicit folder commit still changes the
/// source.
/// </para>
/// <para>
/// <b>What ends a setup, and what does not.</b> The same folder reported
/// again ends nothing — generation and baseline stand. A different folder and
/// a cleared one end it.
/// </para>
/// <para>
/// Storage is planned (<see cref="BrowserStoragePlan"/>): the restored folder
/// is the subject, and every call the coordinator and the surface make is
/// declared, the surface's through <see cref="FilterSurfaceStorage"/>.
/// </para>
/// </summary>
public class HomeSourceReportTests : BunitContext
{
    private const string FolderA = @"D:\xg\matches";
    private const string FolderB = @"D:\xg\other";

    private static readonly FilterConfig Applied = new() { ErrorMin = 0.01 };

    private readonly BrowserStoragePlan _storage;

    public HomeSourceReportTests() => _storage = BrowserStoragePlan.On(JSInterop);

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    private static Dictionary<string, string> StoredFolder(string folder) =>
        new() { [HomeStoragePlan.FolderPathKey] = folder };

    // ── Before the source is known ──────────────────────────────────────────

    [Fact]
    public void SourceUnresolved_TheSurfaceIsUp_AndNothingIsReportedUntilTheRestoreEnds()
    {
        this.AddHomeServices();
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        var lastRead = _storage.ExpectHomeVisitWithTheLastReadHeld(StoredFolder(FolderA));

        var cut = Render<Home>();
        cut.WaitForAssertion(() => Assert.True(lastRead.IsReached));

        // The surface is mounted from the first render — there is no mount
        // gate — and shows the setup as it stands. No panel exists to latch a
        // source, and none has been reported, though the folder has been read.
        Assert.Single(cut.FindComponents<FilterSurface>());
        Assert.Empty(cut.FindComponents<LocalModePanel>());
        Assert.Null(Setup.Current.Source);
        var generation = Setup.Current.Generation;

        lastRead.Release(BrowserStorageReadAnswer.Absent);
        cut.WaitForLocalPanel();

        // One report, of the resolved folder.
        Assert.Equal(FilterSourceToken.FromPath(FolderA), Setup.Current.Source);
        Assert.Equal(generation + 1, Setup.Current.Generation);
        _storage.Verify();
    }

    // ── A remount ───────────────────────────────────────────────────────────

    /// <summary>
    /// The pin: a remount over a non-empty committed filter. The restore has
    /// run for the app, so the new page reads none of the coordinator's
    /// settings and reports nothing: the setup, its generation and its
    /// baseline stand, the folder on screen is the app's, and Run opens
    /// without a re-Apply. (Re-based, halheinrich/backgammon#424: before, a
    /// return re-ran the restore and reported the same token, which ended
    /// nothing.)
    /// </summary>
    [Fact]
    public async Task ARemount_ReadsNoSetting_ReportsNothing_AndKeepsTheGenerationAndTheBaseline()
    {
        this.AddHomeServices();
        var (generation, _) = await FirstLocalVisitWithAnAppliedFilterAsync();
        await DisposeComponentsAsync();

        _storage.ExpectHomeRemount();
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputPathKey, @"D:\xg\out.csv",
            BrowserStorageWriteAnswer.Succeeded);

        var cut = this.RenderHome();

        // The setup, its generation and its baseline stand. The draft the
        // user left is on screen, Apply has nothing to do, and Run opens over
        // the applied filter.
        Assert.Equal(FilterSourceToken.FromPath(FolderA), Setup.Current.Source);
        Assert.Equal(generation, Setup.Current.Generation);
        Assert.Equal(Applied, Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));
        Assert.Equal(FolderA, cut.Find("#folderPath").GetAttribute("value"));
        Assert.Equal("0.01", cut.MinInputValue());
        Assert.True(cut.ApplyButton().HasAttribute("disabled"));
        Assert.Equal(Applied, this.App().FilterInEffect);
        await cut.SetOutputPathAsync(@"D:\xg\out.csv");
        Assert.False(cut.RunButton().HasAttribute("disabled"));
        _storage.Verify();
    }

    /// <summary>
    /// A remount keeps this app's folder — storage another tab may have
    /// written is not read — and an explicit commit of a different folder
    /// still ends the setup. (Re-based, halheinrich/backgammon#424: before, a
    /// return read the folder another tab had committed and ended the setup
    /// with it; a remount reads none of the coordinator's settings now.)
    /// </summary>
    [Fact]
    public async Task ARemount_KeepsThisAppsFolder_AndACommitOfAnotherStillEndsTheSetup()
    {
        this.AddHomeServices();
        var (generation, _) = await FirstLocalVisitWithAnAppliedFilterAsync();
        await DisposeComponentsAsync();

        _storage.ExpectHomeRemount();
        var cut = this.RenderHome();
        Assert.Equal(FolderA, cut.Find("#folderPath").GetAttribute("value"));
        Assert.Equal(generation, Setup.Current.Generation);

        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.FolderPathKey, FolderB,
            BrowserStorageWriteAnswer.Succeeded);
        await cut.CommitFolderAsync(FolderB);

        Assert.Equal(FilterSourceToken.FromPath(FolderB), Setup.Current.Source);
        Assert.Equal(generation + 1, Setup.Current.Generation);
        Assert.Null(Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderB)));
        Assert.Null(Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));
        Assert.True(cut.RunButton().HasAttribute("disabled"));
        _storage.Verify();
    }

    /// <summary>
    /// A cleared folder is no source — a real answer, so it ends the setup —
    /// and a remount after it reports nothing: the source stays none, and no
    /// folder comes back from anywhere. (Re-based,
    /// halheinrich/backgammon#424: before, a return that restored no folder
    /// reported no source and so ended the setup.)
    /// </summary>
    [Fact]
    public async Task ARemount_AfterTheFolderWasCleared_ReportsNothing_AndTheSourceStaysNone()
    {
        this.AddHomeServices();
        var (generation, page) = await FirstLocalVisitWithAnAppliedFilterAsync();
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.FolderPathKey, string.Empty,
            BrowserStorageWriteAnswer.Succeeded);
        await page.CommitFolderAsync(string.Empty);
        await DisposeComponentsAsync();

        _storage.ExpectHomeRemount();
        var cut = this.RenderHome();

        Assert.Null(Setup.Current.Source);
        Assert.Equal(generation + 1, Setup.Current.Generation);
        Assert.Null(Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));
        Assert.Single(cut.FindComponents<FilterSurface>());
        Assert.Equal(string.Empty, cut.Find("#folderPath").GetAttribute("value"));
        _storage.Verify();
    }

    [Fact]
    public async Task BlankingTheFolder_ReportsNoSource_WithoutUnmountingTheSurface()
    {
        this.AddHomeServices();
        var (generation, cut) = await FirstLocalVisitWithAnAppliedFilterAsync();
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.FolderPathKey, string.Empty,
            BrowserStorageWriteAnswer.Succeeded);

        await cut.CommitFolderAsync(string.Empty);

        Assert.Null(Setup.Current.Source);
        Assert.Equal(generation + 1, Setup.Current.Generation);
        Assert.Null(Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));
        Assert.Single(cut.FindComponents<FilterSurface>());
        // A blank path renders no saved-filters section — and no load failure.
        Assert.Empty(cut.FindAll("#saveFilterName"));
        Assert.Empty(cut.FindAll("#savedFiltersLoadFailed"));
        _storage.Verify();
    }

    // ── A page gone while the restore is in flight ──────────────────────────

    /// <summary>
    /// The restore is the app's, not its page's. Page A starts it, reads folder
    /// A and waits on its last read; the user navigates away and back. Page B
    /// starts no second restore — it reads no setting — and shows no controls
    /// while the restore is unresolved. Then A's read completes: the restore
    /// completes for the app, reports folder A once, and page B shows it.
    /// (Re-based, halheinrich/backgammon#424: before, a disposed page's late
    /// restore reported nothing, and the new page ran a restore of its own.)
    /// </summary>
    [Fact]
    public async Task ARestoreInFlight_CompletesForTheApp_WhenItsPageHasGone()
    {
        this.AddHomeServices();
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        // Page A: the restore up to its last read, held, and the surface's
        // mount. The restore does not end while A lives, so no panel renders
        // for it.
        var lastRead = _storage.ExpectRestore(StoredFolder(FolderA), holdTheLastRead: true)!;
        _storage.ExpectFilterPanelMount();

        var pageA = Render<Home>();
        pageA.WaitForAssertion(() => Assert.True(lastRead.IsReached));
        await DisposeComponentsAsync();

        // Page B: a remount — the surface's mount, and nothing of the
        // coordinator's.
        _storage.ExpectHomeRemount();
        var cut = Render<Home>();
        Assert.Empty(cut.FindComponents<LocalModePanel>());
        Assert.Null(Setup.Current.Source);
        var generation = Setup.Current.Generation;

        lastRead.Release(BrowserStorageReadAnswer.Absent);
        cut.WaitForLocalPanel();

        Assert.Equal(FilterSourceToken.FromPath(FolderA), Setup.Current.Source);
        Assert.Equal(generation + 1, Setup.Current.Generation);
        Assert.Equal(FolderA, cut.Find("#folderPath").GetAttribute("value"));
        _storage.Verify();
    }

    /// <summary>
    /// A storage call already in flight when the page goes is not abandoned:
    /// its refusal still reaches the app-scoped condition, and the restore
    /// runs to its end for the app — every read after it made, and the app
    /// idle. (Re-based, halheinrich/backgammon#424: before, the page's restore
    /// stopped after the read its page no longer owned.)
    /// </summary>
    [Fact]
    public async Task ARestoreInFlight_WhenItsPageHasGone_StillReportsItsRefusal_AndRunsToItsEnd()
    {
        this.AddHomeServices();
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.OutputFormatKey, BrowserStorageReadAnswer.Absent);
        var folderRead = _storage.ExpectHeldRead(BrowserStorageArea.Local, HomeStoragePlan.FolderPathKey);
        foreach (var key in new[]
                 {
                     HomeStoragePlan.OutputPathKey, HomeStoragePlan.XgpPatternKey, HomeStoragePlan.XgpNextNumberKey,
                     HomeStoragePlan.XgpSuffixLengthKey, HomeStoragePlan.XgpAnonymizeKey,
                 })
            _storage.ExpectRead(BrowserStorageArea.Local, key, BrowserStorageReadAnswer.Absent);
        var condition = Services.GetRequiredService<BrowserStorageCondition>();
        var refusalReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = condition.Attach(() =>
        {
            if (condition.HasOccurred) refusalReported.TrySetResult();
        });

        Render<Home>();
        Assert.True(folderRead.IsReached);
        var generation = Setup.Current.Generation;
        await DisposeComponentsAsync();

        folderRead.Release(BrowserStorageReadAnswer.Refused);
        await refusalReported.Task.WaitAsync(DefaultWaitTimeout);
        await this.App().EnsureRestoredAsync().WaitAsync(DefaultWaitTimeout);

        Assert.True(condition.HasOccurred);
        Assert.Equal(ExtractPhase.Idle, this.App().Phase);
        // No folder was restored: no source, which the owner already had.
        Assert.Null(Setup.Current.Source);
        Assert.Equal(generation, Setup.Current.Generation);
        // Verify names any read the restore did not make.
        _storage.Verify();
    }

    // ── First visits ────────────────────────────────────────────────────────

    /// <summary>
    /// This boot's first Local visit over folder A: a non-empty filter edited
    /// and applied. Returns the generation it leaves, and the page.
    /// </summary>
    private async Task<(int Generation, IRenderedComponent<Home> Page)> FirstLocalVisitWithAnAppliedFilterAsync()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectHomeVisit(StoredFolder(FolderA));
        _storage.ExpectFilterCommit(Applied, BrowserStorageWriteAnswer.Succeeded);

        var cut = this.RenderHome();
        Assert.Equal(FilterSourceToken.FromPath(FolderA), Setup.Current.Source);

        await cut.EditMinAsync("0.01");
        await cut.ApplyFiltersAsync();
        Assert.Equal(Applied, Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));
        return (Setup.Current.Generation, cut);
    }
}
