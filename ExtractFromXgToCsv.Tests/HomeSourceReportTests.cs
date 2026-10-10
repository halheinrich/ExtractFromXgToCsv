using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
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
/// <b>The hazard.</b> Home's mode and Local folder arrive asynchronously — the
/// folder from storage, the mode from <c>/api/appmode</c> — and
/// <c>ReportSource(null)</c> is a real answer that ends an existing setup. A
/// page that reported before both had arrived would end the setup on every
/// return and then report the restored folder as a new one: the applied
/// baseline gone, Apply re-armed, Run dark, behind the user's back. So Home
/// reports nothing until its restore has resolved the source, and renders no
/// mode panel before then. The held app-mode response below makes that window
/// observable.
/// </para>
/// <para>
/// <b>What ends a setup, and what does not.</b> The same folder reported
/// again ends nothing — generation and baseline stand. A different folder, a
/// cleared one, and every new Web pick end it, and a pick never inherits an
/// earlier pick's consent: its token is new even when the files are the same.
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
    private const string Fixture = "MTCH4064.xg";
    private const string HintAfterUpload = "1 file(s) — first: " + Fixture;

    private static readonly FilterConfig Applied = new() { ErrorMin = 0.01 };

    private readonly BrowserStoragePlan _storage;

    public HomeSourceReportTests() => _storage = BrowserStoragePlan.On(JSInterop);

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    private static Dictionary<string, string> StoredFolder(string folder) =>
        new() { [HomeStoragePlan.FolderPathKey] = folder };

    // ── Before the source is known ──────────────────────────────────────────

    [Fact]
    public void ModeUnresolved_TheSurfaceIsUp_AndNothingIsReportedUntilTheModeArrives()
    {
        var server = this.AddHomeServices("Local");
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectHomeVisit("Local", StoredFolder(FolderA));
        server.HoldAppMode();

        var cut = Render<Home>();

        // The surface is mounted from the first render — there is no mount
        // gate — and shows the setup as it stands. No mode panel exists to
        // latch a source, and none has been reported.
        Assert.Single(cut.FindComponents<FilterSurface>());
        Assert.Empty(cut.FindComponents<LocalModePanel>());
        Assert.Empty(cut.FindComponents<WebModePanel>());
        Assert.Null(Setup.Current.Source);
        var generation = Setup.Current.Generation;

        server.ReleaseAppMode();
        cut.WaitForModePanel();

        // One report, of the resolved folder.
        Assert.Equal(FilterSourceToken.FromPath(FolderA), Setup.Current.Source);
        Assert.Equal(generation + 1, Setup.Current.Generation);
        _storage.Verify();
    }

    // ── A Local return ──────────────────────────────────────────────────────

    /// <summary>
    /// The pin: a Local return with a non-empty committed filter, with the
    /// app-mode response held. While it is held nothing is reported, so the
    /// setup stands as the last visit left it; released with the same folder,
    /// the one report is the same token, which ends nothing. The generation
    /// and the baseline are preserved, and Run opens without a re-Apply.
    /// </summary>
    [Fact]
    public async Task LocalReturn_WithTheAppModeHeld_SameFolder_PreservesTheGenerationAndTheBaseline()
    {
        var server = this.AddHomeServices("Local");
        var (generation, _) = await FirstLocalVisitWithAnAppliedFilterAsync();
        await DisposeComponentsAsync();

        _storage.ExpectHomeVisit("Local", StoredFolder(FolderA));
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputPathKey, @"D:\xg\out.csv",
            BrowserStorageWriteAnswer.Succeeded);
        server.HoldAppMode();

        var cut = Render<Home>();

        // Held: nothing reported, nothing ended.
        Assert.Empty(cut.FindComponents<LocalModePanel>());
        Assert.Equal(generation, Setup.Current.Generation);
        Assert.Equal(Applied, Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));

        server.ReleaseAppMode();
        cut.WaitForModePanel();

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
        var server = this.AddHomeServices("Local");
        var (generation, _) = await FirstLocalVisitWithAnAppliedFilterAsync();
        await DisposeComponentsAsync();

        // Another tab committed a different folder meanwhile.
        _storage.ExpectHomeVisit("Local", StoredFolder(FolderB));
        server.HoldAppMode();
        var cut = Render<Home>();
        Assert.Equal(generation, Setup.Current.Generation);

        server.ReleaseAppMode();
        cut.WaitForModePanel();

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
        var server = this.AddHomeServices("Local");
        var (generation, _) = await FirstLocalVisitWithAnAppliedFilterAsync();
        await DisposeComponentsAsync();

        _storage.ExpectHomeVisit("Local");
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
        this.AddHomeServices("Local");
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

    // ── Web selections ──────────────────────────────────────────────────────

    /// <summary>
    /// A Web return: the selection's bytes belonged to the panel that died
    /// with the page, so the return reports no source and ends the setup. A
    /// pick after it — even of the same file — is a new selection with a new
    /// token, and the earlier pick's applied filter does not carry over.
    /// </summary>
    [Fact]
    public async Task WebReturn_ReportsNoSource_AndANewPickDoesNotInheritTheEarlierPicksConsent()
    {
        this.AddHomeServices("Web");
        var (generation, firstPick, _) = await FirstWebVisitWithAnAppliedFilterAsync();
        await DisposeComponentsAsync();

        _storage.ExpectHomeVisit("Web");
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.LastFolderHintKey, HintAfterUpload,
            BrowserStorageWriteAnswer.Succeeded);
        var cut = this.RenderHome();

        Assert.Null(Setup.Current.Source);
        Assert.Equal(generation + 1, Setup.Current.Generation);

        cut.UploadFixture(Fixture);

        AssertANewPickWithoutConsent(cut, firstPick);
        _storage.Verify();
    }

    /// <summary>
    /// A Web clear in the same visit — a dismissed picker — reports no source;
    /// the next pick is new and inherits nothing.
    /// </summary>
    [Fact]
    public async Task WebClear_ReportsNoSource_AndTheNextPickDoesNotInheritConsent()
    {
        this.AddHomeServices("Web");
        var (generation, firstPick, cut) = await FirstWebVisitWithAnAppliedFilterAsync();
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.LastFolderHintKey, HintAfterUpload,
            BrowserStorageWriteAnswer.Succeeded);

        await cut.UploadNothingAsync();

        Assert.Null(Setup.Current.Source);
        Assert.Equal(generation + 1, Setup.Current.Generation);
        Assert.Null(Setup.Current.ConfigInEffectFor(firstPick));

        cut.UploadFixture(Fixture);

        AssertANewPickWithoutConsent(cut, firstPick);
        _storage.Verify();
    }

    private void AssertANewPickWithoutConsent(IRenderedComponent<Home> cut, FilterSourceToken firstPick)
    {
        var newPick = Assert.IsType<FilterSourceToken>(Setup.Current.Source);
        Assert.NotEqual(firstPick, newPick);
        Assert.Null(Setup.Current.ConfigInEffectFor(newPick));
        Assert.Null(Setup.Current.ConfigInEffectFor(firstPick));
        Assert.Contains("set filters and click Apply Filter", cut.Markup);
        Assert.True(cut.DownloadButton().HasAttribute("disabled"));
    }

    // ── First visits ────────────────────────────────────────────────────────

    /// <summary>
    /// This boot's first Local visit over folder A: a non-empty filter edited
    /// and applied. Returns the generation it leaves, and the page.
    /// </summary>
    private async Task<(int Generation, IRenderedComponent<Home> Page)> FirstLocalVisitWithAnAppliedFilterAsync()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectHomeVisit("Local", StoredFolder(FolderA));
        _storage.ExpectFilterCommit(Applied, BrowserStorageWriteAnswer.Succeeded);

        var cut = this.RenderHome();
        Assert.Equal(FilterSourceToken.FromPath(FolderA), Setup.Current.Source);

        await cut.EditMinAsync("0.01");
        await cut.ApplyFiltersAsync();
        Assert.Equal(Applied, Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(FolderA)));
        return (Setup.Current.Generation, cut);
    }

    /// <summary>
    /// This boot's first Web visit: a fixture picked, and a non-empty filter
    /// edited and applied to it. Returns the generation, the pick's token, and
    /// the page.
    /// </summary>
    private async Task<(int Generation, FilterSourceToken Pick, IRenderedComponent<Home> Page)>
        FirstWebVisitWithAnAppliedFilterAsync()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectHomeVisit("Web");
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.LastFolderHintKey, HintAfterUpload,
            BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectFilterCommit(Applied, BrowserStorageWriteAnswer.Succeeded);

        var cut = this.RenderHome();
        Assert.Null(Setup.Current.Source);
        cut.UploadFixture(Fixture);
        var pick = Assert.IsType<FilterSourceToken>(Setup.Current.Source);

        await cut.EditMinAsync("0.01");
        await cut.ApplyFiltersAsync();
        Assert.Equal(Applied, Setup.Current.ConfigInEffectFor(pick));
        Assert.Contains("rows match current filters", cut.Markup);
        return (Setup.Current.Generation, pick, cut);
    }
}
