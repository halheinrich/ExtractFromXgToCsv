using Bunit;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
using XgFilter_Razor.Components;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins Home's degradation when <c>localStorage</c> is unavailable
/// (halheinrich/backgammon#91) — a disabled-storage or hostile-privacy setting,
/// where every call raises a <c>SecurityError</c> that reaches Blazor as a
/// <see cref="JSException"/>.
/// <para>
/// What makes this worth a test class rather than a comment: since the mount
/// gate went in (#85) the failure was <em>structural</em>, not cosmetic. A
/// throwing read faulted <c>OnAfterRenderAsync</c> before
/// <c>_restoreComplete</c> was reached, so <c>FilterSurface</c> never mounted
/// and the page had no filtering at all — and silently, because this app
/// registers no <c>#blazor-error-ui</c>. The guard makes a dead store read as
/// "nothing is stored", a state every read site already answers with its own
/// documented default, so the flag is set over facts rather than over reads
/// that never landed. (Setting it in a <c>finally</c> instead was rejected for
/// #85 and stays rejected: that publishes a <c>Source</c> minted from
/// half-arrived facts.)
/// </para>
/// <para>
/// <b>Scope — the failure modelled here is per key, not per browser.</b> A real
/// storage-disabled browser throws for <em>every</em> caller, and Home's
/// siblings are still unguarded: <c>WebModePanel</c> and <c>LocalModePanel</c>
/// read their own keys raw, and the producer's <c>FilterPanel</c> restores its
/// selection the same way — a library-side change this repo may not make
/// locally. bUnit rethrows those lifecycle exceptions, so a whole-browser
/// model cannot pass until they are dealt with; that is booked as
/// halheinrich/backgammon#102. These tests therefore fail only the keys Home
/// itself owns, which is exactly the subject of #91.
/// </para>
/// </summary>
public class HomeStorageUnavailableTests : BunitContext
{
    /// <summary>
    /// The <c>localStorage</c> keys Home reads and writes itself. Everything
    /// else on a Home render belongs to a mode panel or to the producer's
    /// filter panel, and is left working — see the scope note above.
    /// </summary>
    private static readonly string[] HomeKeys =
    [
        "xg_folderPath", "xg_outputFormat", "xg_xgpAnonymize",
        "xg_xgpLastNumber", "xg_xgpPattern", "xg_xgpPrefix",
        "xg_xgpSuffixLength",
    ];

    private const string RestoredFolder = @"D:\xg\matches";

    /// <summary>
    /// What a browser with storage refused actually raises: the JS-side
    /// <c>SecurityError</c>, surfaced through the interop boundary.
    /// </summary>
    private static JSException StorageRefused() =>
        new("SecurityError: The operation is insecure.");

    public HomeStorageUnavailableTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
        Services.AddSingleton(new HttpClient(new StubAppModeHandler("Local"))
        {
            BaseAddress = new Uri("http://localhost/"),
        });
        Services.AddScoped<AppliedFilter>();
        Services.AddScoped<FilterRestoreNotice>();
    }

    private AppliedFilter Holder => Services.GetRequiredService<AppliedFilter>();

    private static bool IsHomeKey(JSRuntimeInvocation invocation) =>
        invocation.Arguments is [string key, ..] && HomeKeys.Contains(key);

    /// <summary>Every read of a Home-owned key throws, as a dead store does.</summary>
    private void WithReadsRefused() =>
        JSInterop.Setup<string?>("localStorage.getItem", IsHomeKey)
                 .SetException(StorageRefused());

    /// <summary>Every write of a Home-owned key throws.</summary>
    private void WithWritesRefused() =>
        JSInterop.SetupVoid("localStorage.setItem", IsHomeKey)
                 .SetException(StorageRefused());

    private IRenderedComponent<Home> RenderHome()
    {
        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<FilterSurface>().Any(), TimeSpan.FromSeconds(5));
        return cut;
    }

    // ── The structural failure this exists for ──────────────────────────────

    [Fact]
    public void StorageRefused_TheFilterSurfaceStillMounts()
    {
        WithReadsRefused();

        // Unguarded, the restore faults before _restoreComplete and this wait
        // never comes true — the page renders its shell forever, with no
        // filtering and nothing on screen to say why.
        var cut = RenderHome();

        Assert.Single(cut.FindComponents<FilterSurface>());
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Apply Filter");
    }

    [Fact]
    public void StorageRefused_AnnouncesItself()
    {
        WithReadsRefused();

        var cut = RenderHome();

        // The degradation is otherwise invisible: defaults look like a first
        // run, and every later boot forgets the folder again for no stated
        // reason.
        Assert.Single(cut.FindAll("#storageUnavailableNotice"));
    }

    [Fact]
    public void StorageWorking_ShowsNoNotice()
    {
        // The control for the two above: Loose mode answers every read with
        // null — nothing stored, which is not a failure — so the page must say
        // nothing about storage.
        var cut = RenderHome();

        Assert.Empty(cut.FindAll("#storageUnavailableNotice"));
    }

    // ── What the page restores instead ──────────────────────────────────────

    [Fact]
    public async Task StorageRefused_EveryOptionLandsOnItsDocumentedDefault()
    {
        WithReadsRefused();

        var cut = RenderHome();

        // The two facts Source is minted from, and the format the app opens on.
        Assert.True(cut.Find("#fmtCsv").HasAttribute("checked"));
        Assert.Equal(string.Empty, cut.Find("#folderPath").GetAttribute("value"));

        // The XGP options are behind the format switch — which also exercises
        // the write guard, since committing the format persists it.
        await cut.Find("#fmtXgp").ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.Equal(
            XgpExportOptions.DefaultNamePattern, cut.Find("#xgpPattern").GetAttribute("value"));
        Assert.Equal("1", cut.Find("#xgpNextNumber").GetAttribute("value"));
        Assert.Equal("3", cut.Find("#xgpSuffixLength").GetAttribute("value"));
        Assert.False(cut.Find("#xgpAnonymize").HasAttribute("checked"));
    }

    [Fact]
    public void StorageRefused_RestoresNoSource_SoTheSurvivingHolderIsDropped()
    {
        WithReadsRefused();
        // A previous visit's apply, on the DI-scoped holder that outlives the
        // page. No folder can be restored now, so this visit has no source —
        // and the restore's reconcile must retire it rather than leave it to be
        // re-adopted the moment that path is typed again.
        Holder.Set(
            new FilterConfig { ErrorMin = 0.75 }, FilterSourceToken.FromPath(RestoredFolder));

        var cut = RenderHome();

        Assert.Null(Holder.ConfigFor(FilterSourceToken.FromPath(RestoredFolder)));
        // And the gate the composite mounted with is the truthful one: nothing
        // applied, so Apply is armed rather than reporting a filter in effect.
        var apply = cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply Filter");
        Assert.False(apply.HasAttribute("disabled"));
    }

    // ── Writes fail the same way, and the notice has to keep up ─────────────

    [Fact]
    public async Task StorageLostAfterBoot_TheNoticeCatchesUp()
    {
        // Reads succeed, so the page boots clean and says nothing.
        WithWritesRefused();

        var cut = RenderHome();
        Assert.Empty(cut.FindAll("#storageUnavailableNotice"));

        // The first persisted gesture is where the loss shows up. Unguarded
        // this throws out of the event handler; guarded it degrades and the
        // notice starts telling the truth for the rest of the session.
        await cut.Find("#fmtXgp").ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.Single(cut.FindAll("#storageUnavailableNotice"));
        // The gesture itself still took effect — only its persistence was lost.
        Assert.Single(cut.FindAll("#xgpOptions"));
    }
}
