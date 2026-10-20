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
/// Pins how <see cref="Home"/> reports its source to the app-scoped
/// <see cref="FilterSetup"/> (halheinrich/backgammon#374, Arc 2): the token
/// identifies the selection, and an unresolved source is not "no source".
/// <para>
/// <b>The hazard.</b> Home's folder arrives asynchronously, from storage, and
/// <c>ReportSource(null)</c> is a real answer that ends an existing setup. A
/// page that reported before its restore had finished would end the setup on
/// every return and then report the restored folder as a new one: the applied
/// baseline gone, Apply re-armed, Run dark, behind the user's back. So Home
/// reports nothing until its restore has resolved the source, and renders no
/// panel before then. The restore's last read, held below, makes that window
/// observable.
/// </para>
/// <para>
/// <b>What ends a setup, and what does not.</b> The same folder reported
/// again ends nothing — generation and baseline stand. A different folder and
/// a cleared one end it.
/// </para>
/// <para>
/// Storage is planned (<see cref="BrowserStoragePlan"/>): the restored folder
/// is the subject, and every call Home and the surface make is declared, the
/// surface's through <see cref="FilterSurfaceStorage"/>.
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

    // ── A Local return ──────────────────────────────────────────────────────

    /// <summary>
    /// The pin: a return with a non-empty committed filter, with the restore's
    /// last read held. While it is held nothing is reported, so the setup
    /// stands as the last visit left it; released with the same folder, the
    /// one report is the same token, which ends nothing. The generation and
    /// the baseline are preserved, and Run opens without a re-Apply.
    /// </summary>
    [Fact]
    public async Task LocalReturn_WithTheRestoreHeld_SameFolder_PreservesTheGenerationAndTheBaseline()
    {
        this.AddHomeServices();
        var (generation, _) = await FirstLocalVisitWithAnAppliedFilterAsync();
        await DisposeComponentsAsync();

        var lastRead = _storage.ExpectHomeVisitWithTheLastReadHeld(StoredFolder(FolderA));
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputPathKey, @"D:\xg\out.csv",
            BrowserStorageWriteAnswer.Succeeded);

        var cut = Render<Home>();
        cut.WaitForAssertion(() => Assert.True(lastRead.IsReached));

        // Held: nothing reported, nothing ended.
        Assert.Empty(cut.FindComponents<LocalModePanel>());
        Assert.Equal(generation, Setup.Current.Generation);
        Assert.Equal(Applied, Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));

        lastRead.Release(BrowserStorageReadAnswer.Absent);
        cut.WaitForLocalPanel();

        // Released with the same folder: the setup, its generation and its
        // baseline stand. The draft the user left is on screen, Apply has
        // nothing to do, and Run opens over the applied filter.
        Assert.Equal(FilterSourceToken.FromPath(FolderA), Setup.Current.Source);
        Assert.Equal(generation, Setup.Current.Generation);
        Assert.Equal(Applied, Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));
        Assert.Equal("0.01", cut.MinInputValue());
        Assert.True(cut.ApplyButton().HasAttribute("disabled"));
        Assert.Equal(Applied, cut.FindComponent<LocalModePanel>().Instance.FilterInEffect);
        await cut.SetOutputPathAsync(@"D:\xg\out.csv");
        Assert.False(cut.RunButton().HasAttribute("disabled"));
        _storage.Verify();
    }

    [Fact]
    public async Task LocalReturn_ADifferentFolderRestored_EndsTheSetup()
    {
        this.AddHomeServices();
        var (generation, _) = await FirstLocalVisitWithAnAppliedFilterAsync();
        await DisposeComponentsAsync();

        // Another tab committed a different folder meanwhile.
        var lastRead = _storage.ExpectHomeVisitWithTheLastReadHeld(StoredFolder(FolderB));
        var cut = Render<Home>();
        cut.WaitForAssertion(() => Assert.True(lastRead.IsReached));
        Assert.Equal(generation, Setup.Current.Generation);

        lastRead.Release(BrowserStorageReadAnswer.Absent);
        cut.WaitForLocalPanel();

        Assert.Equal(FilterSourceToken.FromPath(FolderB), Setup.Current.Source);
        Assert.Equal(generation + 1, Setup.Current.Generation);
        Assert.Null(Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderB)));
        Assert.Null(Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));
        Assert.True(cut.RunButton().HasAttribute("disabled"));
        _storage.Verify();
    }

    [Fact]
    public async Task LocalReturn_NoFolderRestored_ReportsNoSource_AndEndsTheSetup()
    {
        var server = this.AddHomeServices();
        var (generation, _) = await FirstLocalVisitWithAnAppliedFilterAsync();
        await DisposeComponentsAsync();

        _storage.ExpectHomeVisit();
        var cut = this.RenderHome();

        // A cleared folder is no source — a real answer, so it ends the setup.
        Assert.Null(Setup.Current.Source);
        Assert.Equal(generation + 1, Setup.Current.Generation);
        Assert.Null(Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));
        Assert.Single(cut.FindComponents<FilterSurface>());
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

    // ── A disposed page ─────────────────────────────────────────────────────

    /// <summary>
    /// A page's async work is tied to its lifetime. Home A reads folder A and
    /// waits on its restore's last read; the user navigates away and back; the
    /// new Home settles, and the user establishes folder B and applies its
    /// filter. Then A's read completes. A is gone, so nothing it read is
    /// reported: the owner's source, generation and baseline stay B's.
    /// </summary>
    [Fact]
    public async Task ADisposedPagesLateRestore_ReportsNothing_AndTheNewSetupStands()
    {
        this.AddHomeServices();
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        // Home A: its restore up to its last read, held, and the surface's
        // mount. Its restore never ends while it lives, so no panel renders
        // for it.
        var lastReadForA = _storage.ExpectHomeRestore(StoredFolder(FolderA), holdTheLastRead: true)!;
        _storage.ExpectFilterPanelMount();

        var pageA = Render<Home>();
        pageA.WaitForAssertion(() => Assert.True(lastReadForA.IsReached));
        await DisposeComponentsAsync();

        // Home B, over folder B, with a filter applied.
        _storage.ExpectHomeVisit(StoredFolder(FolderB));
        _storage.ExpectFilterCommit(Applied, BrowserStorageWriteAnswer.Succeeded);
        var cut = this.RenderHome();
        await cut.EditMinAsync("0.01");
        await cut.ApplyFiltersAsync();
        var folderB = FilterSourceToken.FromPath(FolderB);
        Assert.Equal(folderB, Setup.Current.Source);
        Assert.Equal(Applied, Setup.Current.ConfigInEffectFor(folderB));
        var generation = Setup.Current.Generation;

        // A's read completes now, after A has gone. Queued behind its
        // continuation on the renderer's context, so whatever that
        // continuation went on to do has been done.
        lastReadForA.Release(BrowserStorageReadAnswer.Absent);
        await cut.InvokeAsync(() => { });

        Assert.Equal(folderB, Setup.Current.Source);
        Assert.Equal(generation, Setup.Current.Generation);
        Assert.Equal(Applied, Setup.Current.ConfigInEffectFor(folderB));
        _storage.Verify();
    }

    /// <summary>
    /// A storage call already in flight when the page goes is not abandoned:
    /// its refusal still reaches the app-scoped condition. Only the page's
    /// use of the answer stops — the restore makes no further read, and reports
    /// nothing.
    /// </summary>
    [Fact]
    public async Task ADisposedPagesInFlightRead_StillReportsItsRefusal_AndTheRestoreStops()
    {
        this.AddHomeServices();
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.OutputFormatKey, BrowserStorageReadAnswer.Absent);
        var folderRead = _storage.ExpectHeldRead(BrowserStorageArea.Local, HomeStoragePlan.FolderPathKey);
        var condition = Services.GetRequiredService<BrowserStorageCondition>();
        var refusalReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = condition.Attach(() =>
        {
            if (condition.HasOccurred) refusalReported.TrySetResult();
        });

        var cut = Render<Home>();
        Assert.True(folderRead.IsReached);
        var generation = Setup.Current.Generation;
        await DisposeComponentsAsync();

        folderRead.Release(BrowserStorageReadAnswer.Refused);
        await refusalReported.Task.WaitAsync(DefaultWaitTimeout);
        // Queued behind the read's continuation on the renderer's context, so
        // whatever that continuation went on to do has been done.
        await cut.InvokeAsync(() => { });

        Assert.True(condition.HasOccurred);
        Assert.Null(Setup.Current.Source);
        Assert.Equal(generation, Setup.Current.Generation);
        // Verify names any read the restore made after the page had gone.
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
