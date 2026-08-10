using Bunit;
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
/// Pins this host's wiring of the restored-filter notice — the filtering
/// spec's §4 legibility rule, which says a reload must announce that the
/// filter on screen is restored and not yet in effect, because that state is
/// otherwise indistinguishable from a bug (the user's filter on screen with
/// Apply mysteriously armed).
/// <para>
/// The notice's <em>mechanics</em> — when it arms, when it dies, that a
/// remount re-arms it — belong to <c>XgFilter_Razor</c> and are pinned there.
/// What only this repo can prove is that its own two lines are live:
/// <see cref="FilterRestoreNotice"/> is registered at app scope beside
/// <see cref="AppliedFilter"/>, and it is bound to the hosted
/// <c>FilterSurface</c>. A missing registration throws at render; an unbound
/// parameter leaves the composite arming an instance nobody shows; and a
/// Transient registration gives every page instance a fresh notice, so a
/// dismissed one comes back on the next navigation — none of which any other
/// test in this project would catch.
/// </para>
/// </summary>
public class HomeRestoreNoticeTests : BunitContext
{
    private const string RestoredFolder = @"D:\xg\matches";

    public HomeRestoreNoticeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
        Services.AddSingleton(new HttpClient(new StubAppModeHandler("Local"))
        {
            BaseAddress = new Uri("http://localhost/"),
        });
        // Registered exactly as Program.cs registers them — the scope is half
        // of what this class is here to pin.
        Services.AddScoped<AppliedFilter>();
        Services.AddScoped<FilterRestoreNotice>();
    }

    /// <summary>
    /// A stored filter selection for the composite's first-render restore to
    /// find, answered <i>by exclusion</i>: the panel's <c>localStorage</c> key
    /// is a producer internal this host may not name (the producer keeps those
    /// constants <c>internal</c> precisely so no consumer depends on them), so
    /// this matches every <c>localStorage.getItem</c> for a key
    /// ExtractFromXgToCsv does not own. Everything left over on a Home render
    /// belongs to the composite, and the panel's other restore — its
    /// disclosure flag — reads tolerantly (anything but <c>"true"</c> keeps
    /// the collapsed default). Because the match is disjoint from the
    /// host-key stubs, registration order between them cannot matter.
    /// </summary>
    private void WithStoredFilterSelection(FilterConfig stored)
    {
        string[] hostKeys =
        [
            "xg_csvFileName", "xg_folderPath", "xg_lastFolderHint",
            "xg_outputFormat", "xg_outputPath", "xg_xgpAnonymize",
            "xg_xgpLastNumber", "xg_xgpPattern", "xg_xgpPrefix",
            "xg_xgpSuffixLength",
        ];
        JSInterop.Setup<string?>(
            "localStorage.getItem",
            invocation => invocation.Arguments is [string key] && !hostKeys.Contains(key))
            .SetResult(stored.ToJson());
    }

    private IRenderedComponent<Home> RenderHome()
    {
        var cut = Render<Home>();
        // Home holds the composite back until its own first-render restore
        // completes (#85), so the notice cannot exist before this.
        cut.WaitForState(
            () => cut.FindComponents<FilterSurface>().Any(), TimeSpan.FromSeconds(5));
        return cut;
    }

    private static string MinInputValue(IRenderedComponent<Home> cut) =>
        cut.Find("input[placeholder='Min']").GetAttribute("value") ?? string.Empty;

    [Fact]
    public async Task FreshBoot_RestoredSelection_AnnouncesItself_UntilAnEditSupersedesIt()
    {
        // One bUnit fixture is one scope, so this test is one app boot: the
        // stored selection below is a previous session's, restored by the
        // panel's first render once Home's mount gate lets the composite in.
        JSInterop.Setup<string?>("localStorage.getItem", "xg_folderPath")
                 .SetResult(RestoredFolder);
        WithStoredFilterSelection(new FilterConfig { ErrorMin = 0.5 });

        var cut = RenderHome();

        // The restore is interop-driven and lands after the composite's first
        // render, so wait on the selection actually reaching the buffers —
        // that is the positive precondition every assertion below leans on,
        // and without it the negative one further down would pass on a paint
        // that had simply not happened yet.
        cut.WaitForAssertion(() => Assert.Equal("0.5", MinInputValue(cut)));

        Assert.Single(cut.FindAll("#filterRestoredNotice"));

        // And the notice is telling the truth: nothing is in effect, so Apply
        // is armed over the restored selection. This is §4's whole point —
        // the armed Apply is correct, and the notice is why it doesn't read
        // as a defect.
        var apply = cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply Filter");
        Assert.False(apply.HasAttribute("disabled"));

        // The selection becomes the user's own — the notice's statement stops
        // holding, so it goes.
        await cut.Find("input[placeholder='Min']")
                 .InputAsync(new ChangeEventArgs { Value = "0.75" });

        Assert.Equal("0.75", MinInputValue(cut));
        Assert.Empty(cut.FindAll("#filterRestoredNotice"));

        // It stays gone across a navigate-back, which is the half that pins
        // the *lifetime* rather than the binding: a second Home mounts a
        // second composite that restores the same stored selection all over
        // again, and the only reason it does not announce it a second time is
        // that Home resolved the app-scoped instance the edit above spent.
        // Registered Transient this assertion fails while everything above
        // still passes — and §4 says navigation changes nothing, including no
        // notice appearing that wasn't already there.
        var back = RenderHome();

        // Wait on the restore itself before asserting the absence: the stored
        // 0.5 landing in the fresh panel's buffers (over the 0.75 typed into
        // the panel that just died) is the positive signal that the arming
        // path ran and declined.
        back.WaitForAssertion(() => Assert.Equal("0.5", MinInputValue(back)));
        Assert.Empty(back.FindAll("#filterRestoredNotice"));
    }
}
