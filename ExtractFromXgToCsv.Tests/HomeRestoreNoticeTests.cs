using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Components.Pages;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.TestSupport;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins this host's side of the restored-filter notice — the filtering spec's
/// §4 legibility rule: a reload announces that the selection on screen was
/// restored and is not yet in effect, because that state is otherwise
/// indistinguishable from a bug (the user's filter on screen with Apply
/// armed).
/// <para>
/// The notice's mechanics belong to XgFilter_Razor's app-scoped
/// <c>FilterSetup</c>, which holds the restoration, the draft and the notice,
/// and are pinned there. What only this repo can prove is that its
/// registration gives the owner the app's lifetime: the notice shows through
/// Home on a boot that restores, an edit ends it, and a navigation back
/// neither announces it again nor restores over the user's edit — the draft
/// the user left is the one on screen.
/// </para>
/// </summary>
public class HomeRestoreNoticeTests : BunitContext
{
    private const string Folder = @"D:\xg\matches";

    private readonly BrowserStoragePlan _storage;

    public HomeRestoreNoticeTests()
    {
        this.AddHomeServices();
        _storage = BrowserStoragePlan.On(JSInterop);
    }

    /// <summary>
    /// (Re-based, halheinrich/backgammon#424: the navigation back is a remount,
    /// which reads none of the coordinator's settings — only the filter
    /// panel's mount reads.)
    /// </summary>
    [Fact]
    public async Task FreshBoot_RestoredSelection_AnnouncesItself_UntilAnEditSupersedesIt_AndARemountRestoresNothing()
    {
        // One bUnit context is one app boot: the stored selection below is a
        // previous session's, restored once, at the panel's first render.
        _storage.ExpectFilterRestore(new FilterConfig { ErrorMin = 0.5 });
        _storage.ExpectHomeVisit(new() { [HomeStoragePlan.FolderPathKey] = Folder });

        var cut = this.RenderHome();

        // The restore is interop-driven and lands after the panel's first
        // render, so wait on the selection reaching the screen — the positive
        // precondition every assertion below leans on.
        cut.WaitForAssertion(() => Assert.Equal("0.5", cut.MinInputValue()));
        Assert.Single(cut.FindAll("#filterRestoredNotice"));

        // And the notice is telling the truth: nothing is in effect, so Apply
        // is armed over the restored selection.
        Assert.False(cut.ApplyButton().HasAttribute("disabled"));

        // The selection becomes the user's own — the notice's statement stops
        // holding, so it goes.
        await cut.EditMinAsync("0.75");
        Assert.Empty(cut.FindAll("#filterRestoredNotice"));

        // A navigation back: a new page, the same owners. Nothing is restored
        // again — the boot's one restoration has settled, and so has the
        // app's — so the user's draft is on screen, and nothing is announced.
        await DisposeComponentsAsync();
        _storage.ExpectHomeRemount();
        var back = this.RenderHome();

        Assert.Equal("0.75", back.MinInputValue());
        Assert.Empty(back.FindAll("#filterRestoredNotice"));
        _storage.Verify();
    }
}
