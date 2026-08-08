using System.Net;
using System.Text;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
using XgFilter_Razor.Components;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the <c>FilterSurface</c> mount gate (#85): <see cref="Home"/> holds the
/// composite back until its first-render restore has run, so the composite's
/// first parameters-set sees the real <c>Source</c> token instead of a
/// placeholder null that would later be corrected.
/// <para>
/// The regression this class exists for: mode and folder-path restore happen in
/// Home's own <c>OnAfterRenderAsync(firstRender)</c>, which Blazor runs
/// <em>after</em> the child's, so an ungated composite mounted against a null
/// <c>Source</c> and then saw the correction as a genuine source change — #78's
/// end-setup fired on every return to the page, clearing the applied holder and
/// re-arming Apply behind the user's back. With the gate, the restored source is
/// the token the composite mounts with, and #82's first-mount reconcile — which
/// could never fire in this host — seeds the panel's committed config back from
/// the holder.
/// </para>
/// <para>
/// The gate is on the restore, not on <c>Source</c> being non-null: a null
/// <c>Source</c> is a legitimate steady state here (blank folder path, no Web
/// selection), handled in place by the source-change rule. The last test below
/// is the guard on that distinction.
/// </para>
/// </summary>
public class HomeMountGateTests : BunitContext
{
    private const string RestoredFolder = @"D:\xg\matches";

    public HomeMountGateTests()
    {
        // Loose by default (the panel's own restore reads keys these tests
        // don't care about); the source-of-truth keys are stubbed per test.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
        Services.AddScoped<AppliedFilter>();
    }

    private AppliedFilter Holder => Services.GetRequiredService<AppliedFilter>();

    private StubAppModeHandler RegisterHttpClient(string appMode)
    {
        var stub = new StubAppModeHandler(appMode);
        RegisterHttpClient(stub);
        return stub;
    }

    private void RegisterHttpClient(HttpMessageHandler handler) =>
        Services.AddSingleton(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/"),
        });

    /// <summary>
    /// Stand in for the localStorage the previous visit left behind: the
    /// committed folder path, and the filter selection the panel re-hydrates
    /// its buffers from.
    /// </summary>
    private void SeedRestoredState(string folderPath, FilterConfig persistedSelection)
    {
        JSInterop.Setup<string?>("localStorage.getItem", "xg_folderPath")
                 .SetResult(folderPath);
        // The panel's own persisted selection — literal key because
        // FilterPanel.ConfigKey is producer-internal.
        JSInterop.Setup<string?>("localStorage.getItem", "xg_filter_config")
                 .SetResult(persistedSelection.ToJson());
    }

    /// <summary>
    /// The holder as a previous visit left it: <paramref name="config"/> applied
    /// against <paramref name="folderPath"/>, stamped exactly as Home mints the
    /// token (normalized for Windows' case-insensitive path identity).
    /// </summary>
    private void SeedAppliedHolder(FilterConfig config, string folderPath) =>
        Holder.Set(config, FilterSourceToken.FromPath(
            Path.TrimEndingDirectorySeparator(folderPath).ToUpperInvariant()));

    private IRenderedComponent<Home> RenderHome()
    {
        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<FilterSurface>().Any(), TimeSpan.FromSeconds(5));
        return cut;
    }

    private static Task ApplyFiltersAsync(IRenderedComponent<Home> cut) =>
        cut.FindAll("button")
           .First(b => b.TextContent.Trim() == "Apply Filter")
           .ClickAsync(new());

    private static async Task CommitFolderAsync(IRenderedComponent<Home> cut, string path)
    {
        await cut.Find("#folderPath").InputAsync(new ChangeEventArgs { Value = path });
        await cut.Find("#folderPath").ChangeAsync(new ChangeEventArgs { Value = path });
    }

    private static Task SetOutputPathAsync(IRenderedComponent<Home> cut, string path) =>
        cut.Find("#outputPath").InputAsync(new ChangeEventArgs { Value = path });

    private static AngleSharp.Dom.IElement RunButton(IRenderedComponent<Home> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Run");

    // ── The gate itself ─────────────────────────────────────────────────────

    [Fact]
    public void Composite_DoesNotMount_UntilTheRestoreCompletes()
    {
        // Suspend the restore mid-flight on its slowest step — the app-mode
        // probe — so the pre-restore render is observable rather than a race.
        var handler = new GatedAppModeHandler("Local");
        RegisterHttpClient(handler);

        var cut = Render<Home>();

        // The page shell is up, but nothing filter-shaped exists yet: mounting
        // now would publish a Source this page cannot yet know.
        Assert.Contains("Extract XG Decisions to CSV", cut.Markup);
        Assert.Empty(cut.FindComponents<FilterSurface>());
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Apply Filter");

        handler.ReleaseAppMode();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindComponents<FilterSurface>());
            Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Apply Filter");
        });
    }

    // ── The reported bug: returning to Home un-applied the filter ───────────

    [Fact]
    public void ReturnToHome_RestoredFolderAlreadyApplied_LeavesTheSetupStanding()
    {
        RegisterHttpClient("Local");
        var applied = new FilterConfig { ErrorMin = 0.75 };
        SeedRestoredState(RestoredFolder, applied);
        SeedAppliedHolder(applied, RestoredFolder);

        // A fresh mount of Home over state a previous visit left behind — the
        // holder is DI-scoped and outlives the page, the folder and the panel
        // selection come back from localStorage.
        var cut = RenderHome();

        // The applied filter survives the remount: the restored folder is the
        // token the composite mounted with, so nothing reads as a source
        // change and #78's end-setup never fires.
        cut.WaitForAssertion(() =>
        {
            Assert.True(Holder.IsApplied);
            Assert.Equal(applied, Holder.Config);

            var localPanel = cut.FindComponent<LocalModePanel>();
            Assert.True(localPanel.Instance.FilterApplied);
            Assert.False(localPanel.Instance.FilterDirty);
            // And the gate is open over the *applied* config, not a default
            // one: the config the panel would POST is the holder's.
            Assert.Equal(applied, localPanel.Instance.FilterConfig);
        });
    }

    [Fact]
    public async Task ReturnToHome_RestoredFolderAlreadyApplied_KeepsTheRunGateOpen()
    {
        RegisterHttpClient("Local");
        var applied = new FilterConfig { ErrorMin = 0.75 };
        SeedRestoredState(RestoredFolder, applied);
        SeedAppliedHolder(applied, RestoredFolder);

        var cut = RenderHome();
        // The output path is panel-owned and not restored, so it is supplied
        // here — the filter half of the gate is what is under test.
        await SetOutputPathAsync(cut, @"D:\xg\out.csv");

        cut.WaitForAssertion(() => Assert.False(RunButton(cut).HasAttribute("disabled")));
    }

    [Fact]
    public void ReturnToHome_RestoredFolderAlreadyApplied_ReconcilesApplyDisabled()
    {
        RegisterHttpClient("Local");
        var applied = new FilterConfig { ErrorMin = 0.75 };
        SeedRestoredState(RestoredFolder, applied);
        SeedAppliedHolder(applied, RestoredFolder);

        var cut = RenderHome();

        // #82's first-mount reconcile, reaching this host for the first time:
        // the holder's stamp matches the mounted token, so the panel's
        // committed config is seeded from it and Apply is offered only when
        // there is something to do — which here there is not.
        cut.WaitForAssertion(() =>
        {
            var apply = cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply Filter");
            Assert.True(apply.HasAttribute("disabled"));
            Assert.Contains("already applied", cut.Find("#applyDisabledReason").TextContent);
        });
    }

    [Fact]
    public void ReturnToHome_HolderStampedForAnotherFolder_IsNotAdopted()
    {
        RegisterHttpClient("Local");
        var applied = new FilterConfig { ErrorMin = 0.75 };
        SeedRestoredState(RestoredFolder, applied);
        // Applied against a different folder than the one restored: the
        // reconcile is guarded on the stamp, so this config must not be
        // adopted as the restored folder's.
        SeedAppliedHolder(applied, @"D:\xg\elsewhere");

        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            Assert.False(Holder.IsApplied);
            var apply = cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply Filter");
            Assert.False(apply.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void ReturnToHome_WebModeWithNoSelectionRestored_DropsTheAppliedState()
    {
        RegisterHttpClient("Web");
        var applied = new FilterConfig { ErrorMin = 0.75 };
        JSInterop.Setup<string?>("localStorage.getItem", "xg_filter_config")
                 .SetResult(applied.ToJson());
        SeedAppliedHolder(applied, RestoredFolder);

        // Web mode's selection lives in the panel's own row cache and dies
        // with the page, so a fresh visit restores no source at all. The
        // holder outlived it; an apply against a source this visit does not
        // have must not hold the gate open.
        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            Assert.False(Holder.IsApplied);
            Assert.False(cut.FindComponent<WebModePanel>().Instance.FilterApplied);
        });
    }

    // ── The gate must not disturb #78's source-change rule ──────────────────

    [Fact]
    public async Task AfterRestore_CommittingADifferentFolder_StillEndsTheSetup()
    {
        RegisterHttpClient("Local");
        var applied = new FilterConfig { ErrorMin = 0.75 };
        SeedRestoredState(RestoredFolder, applied);
        SeedAppliedHolder(applied, RestoredFolder);

        var cut = RenderHome();
        await SetOutputPathAsync(cut, @"D:\xg\out.csv");
        cut.WaitForAssertion(() => Assert.False(RunButton(cut).HasAttribute("disabled")));

        // A real source change from a restored setup — the newly reachable
        // path, since before the gate every visit started from a spurious one.
        await CommitFolderAsync(cut, @"D:\xg\other");

        Assert.False(Holder.IsApplied);
        Assert.True(RunButton(cut).HasAttribute("disabled"));

        // And Apply is re-armed: this commits the unchanged selection, which
        // the panel would refuse had the composite not forget-committed it.
        await ApplyFiltersAsync(cut);
        Assert.True(Holder.IsApplied);
        Assert.False(RunButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task AfterRestore_BlankingTheFolder_RegatesInPlaceWithoutUnmounting()
    {
        RegisterHttpClient("Local");
        var applied = new FilterConfig { ErrorMin = 0.75 };
        SeedRestoredState(RestoredFolder, applied);
        SeedAppliedHolder(applied, RestoredFolder);

        var cut = RenderHome();
        await SetOutputPathAsync(cut, @"D:\xg\out.csv");
        cut.WaitForAssertion(() => Assert.False(RunButton(cut).HasAttribute("disabled")));

        // Source goes back to null. This is the case a source-existence gate
        // would have got wrong: the composite must stay mounted and let the
        // in-place source-change rule re-gate, not be destroyed and rebuilt.
        await CommitFolderAsync(cut, string.Empty);

        Assert.Single(cut.FindComponents<FilterSurface>());
        Assert.False(Holder.IsApplied);
        Assert.True(RunButton(cut).HasAttribute("disabled"));
        // A blank path renders no saved-filters section — and no load failure.
        Assert.Empty(cut.FindAll("#saveFilterName"));
        Assert.Empty(cut.FindAll("#savedFiltersLoadFailed"));
    }

    /// <summary>
    /// <see cref="StubAppModeHandler"/>'s shape with the app-mode probe held
    /// open until <see cref="ReleaseAppMode"/> — the seam that makes Home's
    /// pre-restore render observable instead of a race. The saved-filters
    /// routes answer "no document" so a Local-mode store loads clean.
    /// </summary>
    private sealed class GatedAppModeHandler(string mode) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Let the pending <c>/api/appmode</c> response through.</summary>
        public void ReleaseAppMode() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;

            if (path == "/api/appmode")
            {
                await _gate.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"Mode\":\"{mode}\"}}", Encoding.UTF8, "application/json"),
                };
            }

            if (path == "/api/filterdocument")
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
