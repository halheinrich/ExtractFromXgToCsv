using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
using XgFilter_Razor.Components;
using XgFilter_Razor.TestSupport;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins this app's one storage condition as Home shows it
/// (halheinrich/backgammon#374, Arc 2; SPEC-notices.md §1–§2): every refusal
/// — Home's own settings, either mode panel's, or the filter surface's — is
/// one occurrence, held by the app-scoped <see cref="BrowserStorageCondition"/>
/// with its dismissal, and Home shows it however and whenever it arose.
/// <para>
/// <b>Observed, not polled.</b> A panel's refusal re-renders the panel, not
/// Home, and a commit's refusal can land after the click that made it has
/// rendered — or after Home has gone. Home attaches to the condition, so the
/// notice appears on the refusal itself; the cases below that would pass
/// without the attachment are said so.
/// </para>
/// <para>
/// <b>No latch.</b> A refusal records that something happened; it never stops
/// a call. Every visit makes its reads, refused or not, and a write after a
/// refusal is attempted (and may succeed) — which changes nothing about the
/// occurrence or its dismissal.
/// </para>
/// <para>
/// Storage is the subject, so it is planned (<see cref="BrowserStoragePlan"/>),
/// the surface's calls through <see cref="FilterSurfaceStorage"/>. Every
/// caller is guarded now — Home, both panels, the surface — so a browser that
/// refuses <em>everything</em> can be modelled whole.
/// </para>
/// </summary>
public class HomeStorageUnavailableTests : BunitContext
{
    private const string StorageNotice = "storageUnavailableNotice";

    /// <summary>
    /// Hal's ruled wording for the notice (halheinrich/backgammon#374, comment
    /// 6093027780) — an independent literal, because the ruling is what the
    /// copy is held to. The earlier text said settings start from their
    /// defaults and changes will not be saved, which is untrue once reads can
    /// succeed while writes are refused.
    /// </summary>
    private const string RuledWording =
        "This page had trouble using your browser's storage. You can keep using it, "
        + "but some settings may not be restored or saved.";

    private readonly BrowserStoragePlan _storage;

    public HomeStorageUnavailableTests()
    {
        this.AddHomeServices("Local");
        _storage = BrowserStoragePlan.On(JSInterop);
    }

    private BrowserStorageCondition Condition => Services.GetRequiredService<BrowserStorageCondition>();

    public static TheoryData<NoticeDismissGesture> BothGestures =>
        new(Enum.GetValues<NoticeDismissGesture>());

    private void ExpectAllRefusedBoot()
    {
        _storage.ExpectFilterRestore(FilterRestoration.Refused);
        _storage.ExpectHomeVisitRefused("Local");
    }

    private void ExpectWorkingBoot()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectHomeVisit("Local");
    }

    private static NoticeBox TheNotice(IRenderedComponent<Home> cut) =>
        NoticeBox.ById(cut, StorageNotice).ShouldBe(
            NoticeKind.Warning, NoticeAnnouncement.Assertive, dismissible: true, "id", "style");

    // ── A browser that refuses everything ───────────────────────────────────

    [Fact]
    public void StorageRefused_ThePageWorks_AndTheNoticeSaysSo()
    {
        ExpectAllRefusedBoot();

        var cut = this.RenderHome();

        // Every caller degraded instead of throwing: the surface is up and
        // working, and the condition is announced.
        Assert.Single(cut.FindComponents<FilterSurface>());
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Apply Filter");
        var box = TheNotice(cut);
        Assert.Equal("max-width:800px", box.Element.GetAttribute("style"));
        Assert.True(Condition.HasOccurred);
        _storage.Verify();
    }

    [Fact]
    public void TheNotice_SaysTheRuledWording()
    {
        ExpectAllRefusedBoot();

        var cut = this.RenderHome();

        Assert.Equal(RuledWording, Normalize(TheNotice(cut).Content.TextContent));
        _storage.Verify();
    }

    [Fact]
    public async Task StorageRefused_EveryOptionLandsOnItsDocumentedDefault()
    {
        ExpectAllRefusedBoot();
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputFormatKey, nameof(OutputFormat.Xgp),
            BrowserStorageWriteAnswer.Refused);

        var cut = this.RenderHome();

        // The two facts the source is minted from, and the format the app
        // opens on.
        Assert.True(cut.Find("#fmtCsv").HasAttribute("checked"));
        Assert.Equal(string.Empty, cut.Find("#folderPath").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find("#outputPath").GetAttribute("value"));

        // The XGP options are behind the format switch — whose write is
        // refused too, and the switch still takes effect.
        await cut.Find("#fmtXgp").ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.Single(cut.FindAll("#xgpOptions"));
        Assert.Equal(XgpExportOptions.DefaultNamePattern, cut.Find("#xgpPattern").GetAttribute("value"));
        Assert.Equal("1", cut.Find("#xgpNextNumber").GetAttribute("value"));
        Assert.Equal("3", cut.Find("#xgpSuffixLength").GetAttribute("value"));
        Assert.False(cut.Find("#xgpAnonymize").HasAttribute("checked"));
        _storage.Verify();
    }

    [Fact]
    public void StorageWorking_ShowsNoNotice()
    {
        // The control: answered reads — nothing stored, which is not a
        // failure — say nothing about storage.
        ExpectWorkingBoot();

        var cut = this.RenderHome();

        Assert.Empty(cut.FindAll("#" + StorageNotice));
        Assert.False(Condition.HasOccurred);
        _storage.Verify();
    }

    // ── The three reporters ─────────────────────────────────────────────────

    /// <summary>
    /// A panel-only refusal after Home has settled: Home's own reads were
    /// answered, and the Local panel's output-path write is refused. The
    /// keystroke re-renders the panel, not Home; the notice appears because
    /// Home observes the condition.
    /// </summary>
    [Fact]
    public async Task APanelOnlyRefusal_AfterHomeHasSettled_ShowsTheNotice()
    {
        ExpectWorkingBoot();
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputPathKey, @"D:\xg\out.csv",
            BrowserStorageWriteAnswer.Refused);
        var cut = this.RenderHome();
        Assert.Empty(cut.FindAll("#" + StorageNotice));

        await cut.SetOutputPathAsync(@"D:\xg\out.csv");

        TheNotice(cut);
        // The typed value stands: only its saving failed.
        Assert.Equal(@"D:\xg\out.csv", cut.Find("#outputPath").GetAttribute("value"));
        _storage.Verify();
    }

    /// <summary>
    /// A producer refusal: the filter surface's commit write is refused. The
    /// write is held, so the click's own render has happened before the
    /// browser answers; the notice appears on the refusal, through the
    /// registered sink.
    /// </summary>
    [Fact]
    public async Task AProducerRefusal_ShowsTheNotice()
    {
        ExpectWorkingBoot();
        var (cut, apply, commit) = await ApplyWithTheCommitHeldAsync();
        Assert.Empty(cut.FindAll("#" + StorageNotice));

        commit.Release(BrowserStorageWriteAnswer.Refused);
        await apply.WaitAsync(DefaultWaitTimeout);

        TheNotice(cut);
        _storage.Verify();
    }

    /// <summary>
    /// A producer refusal while Home is absent: the commit's write is answered
    /// after the page has gone. The refusal still reaches the condition, and
    /// the next visit shows it with no further failure.
    /// </summary>
    [Fact]
    public async Task ARefusalWhileHomeIsAbsent_IsThereOnReturn()
    {
        ExpectWorkingBoot();
        var (_, apply, commit) = await ApplyWithTheCommitHeldAsync();

        await DisposeComponentsAsync();
        commit.Release(BrowserStorageWriteAnswer.Refused);
        await apply.WaitAsync(DefaultWaitTimeout);
        Assert.True(Condition.HasOccurred);

        _storage.ExpectHomeVisit("Local", new() { [HomeStoragePlan.FolderPathKey] = Folder });
        var back = this.RenderHome();

        TheNotice(back);
        _storage.Verify();
    }

    private const string Folder = @"D:\xg\matches";

    /// <summary>
    /// A folder committed, a filter edited, and Apply clicked with the
    /// commit's write held: the owner has published the applied state — and
    /// Home re-rendered for it — before the browser answers.
    /// </summary>
    private async Task<(IRenderedComponent<Home> Page, Task Apply, BrowserStorageHeldWrite Commit)>
        ApplyWithTheCommitHeldAsync()
    {
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.FolderPathKey, Folder,
            BrowserStorageWriteAnswer.Succeeded);
        var commit = _storage.ExpectHeldFilterCommit(new FilterConfig { ErrorMin = 0.5 });

        var cut = this.RenderHome();
        await cut.CommitFolderAsync(Folder);
        await cut.EditMinAsync("0.5");
        var apply = cut.ApplyFiltersAsync();
        Assert.True(commit.IsReached);
        return (cut, apply, commit);
    }

    /// <summary>
    /// Readable storage with refused writes — the full-quota shape — followed
    /// by a successful operation. The reads restore; the refused write begins
    /// the occurrence; a later write that lands begins nothing and ends
    /// nothing: the notice stands, and once dismissed, a further refusal does
    /// not bring it back.
    /// </summary>
    [Fact]
    public async Task RefusedWrites_ThenASuccess_IsStillOneOccurrence()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectHomeVisit("Local", new() { [HomeStoragePlan.OutputFormatKey] = nameof(OutputFormat.DiagramJson) });
        var refusedXgp = _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputFormatKey,
            nameof(OutputFormat.Xgp), BrowserStorageWriteAnswer.Refused);
        var landedCsv = _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputFormatKey,
            nameof(OutputFormat.Csv), BrowserStorageWriteAnswer.Succeeded);
        var refusedPdf = _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputFormatKey,
            nameof(OutputFormat.Pdf), BrowserStorageWriteAnswer.Refused);
        _storage.RequireOrder(refusedXgp, landedCsv, refusedPdf);

        var cut = this.RenderHome();
        // The read was served: the stored format is restored, and nothing is
        // said.
        Assert.True(cut.Find("#fmtDiagram").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("#" + StorageNotice));

        await cut.Find("#fmtXgp").ChangeAsync(new ChangeEventArgs { Value = true });
        TheNotice(cut);

        await cut.Find("#fmtCsv").ChangeAsync(new ChangeEventArgs { Value = true });
        // A success begins nothing and ends nothing.
        TheNotice(cut);

        await TheNotice(cut).DismissAndSettleAsync(cut, NoticeDismissGesture.CloseButton);
        Assert.Empty(cut.FindAll("#" + StorageNotice));

        // A further refusal in the same visit is the same occurrence: the
        // dismissal stands.
        await cut.Find("#fmtPdf").ChangeAsync(new ChangeEventArgs { Value = true });
        Assert.Empty(cut.FindAll("#" + StorageNotice));
        Assert.True(Condition.IsNoticeDismissed);
        _storage.Verify();
    }

    // ── The dismissal ───────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BothGestures))]
    public async Task TheNotice_DismissesByEitherGesture_AndTheConditionStands(NoticeDismissGesture gesture)
    {
        ExpectAllRefusedBoot();
        var cut = this.RenderHome();

        await TheNotice(cut).DismissAndSettleAsync(cut, gesture);

        Assert.Empty(cut.FindAll("#" + StorageNotice));
        Assert.True(Condition.HasOccurred);
        _storage.Verify();
    }

    /// <summary>
    /// Dismissed stays dismissed across a navigate-back — the page is new, the
    /// app-scoped holder is not, and the same occurrence is not shown again.
    /// And storage is still attempted: the second visit makes every read the
    /// first made, refused again. Nothing remembers to stop calling.
    /// </summary>
    [Fact]
    public async Task Dismissed_StaysDismissedAcrossANavigateBack_AndStorageIsStillAttempted()
    {
        ExpectAllRefusedBoot();
        var firstVisit = this.RenderHome();
        await TheNotice(firstVisit).DismissAndSettleAsync(firstVisit, NoticeDismissGesture.CloseButton);

        await DisposeComponentsAsync();
        _storage.ExpectHomeVisitRefused("Local");
        var secondVisit = this.RenderHome();

        Assert.Empty(secondVisit.FindAll("#" + StorageNotice));
        // Every read of the second visit was made — and refused — or Verify
        // names the one that was not.
        _storage.Verify();
    }

    [Fact]
    public async Task NotDismissed_IsStillShownAfterANavigateBack()
    {
        // The control for the one above: the second visit's silence there is
        // the dismissal's doing, not the remount's.
        ExpectAllRefusedBoot();
        this.RenderHome();

        await DisposeComponentsAsync();
        _storage.ExpectHomeVisitRefused("Local");
        var secondVisit = this.RenderHome();

        TheNotice(secondVisit);
        _storage.Verify();
    }

    // Collapses the markup's line breaks and indentation into the single
    // spaces a reader sees.
    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
