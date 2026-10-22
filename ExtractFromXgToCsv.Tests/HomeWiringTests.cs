using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the wiring between XgFilter_Razor's filter setup and
/// <see cref="Home"/>: the source Home reports, the filter in effect it reads
/// back from the owner (<see cref="FilterSetupSnapshot.ConfigInEffectFor"/>)
/// and hands the panel, and the source-change rule — a folder commit ends the
/// setup, the baseline dropped and Apply re-armed — with readiness (halheinrich/backgammon#266): the empty selection
/// is in effect without Apply once there is a source, and stays so across a
/// source change.
/// <para>
/// All filter gestures drive <c>FilterSurface</c>'s rendered DOM — its panels
/// are producer-internal, and <c>FindComponent</c> over them is banned, host
/// tests included. Two traps honored throughout: a bare "Save" locator goes
/// ambiguous once a saved-filter row exists (use the ids), and the panel
/// refuses an Apply of a selection already in effect, so a re-apply needs a
/// real edit first — <em>except</em> after a source change, which drops the
/// baseline; several tests below prove exactly that by re-applying without an
/// edit.
/// </para>
/// <para>
/// Storage is incidental here, so the runtime is Loose: the real accessor
/// answers every read "nothing stored" and every write "landed". Storage is
/// planned where it is the subject (<see cref="HomeSourceReportTests"/>,
/// <see cref="HomeStorageUnavailableTests"/>).
/// </para>
/// </summary>
public class HomeWiringTests : BunitContext
{
    private const string Folder = @"D:\xg\matches";
    private const string OtherFolder = @"D:\xg\other";

    private static readonly FilterConfig NonEmpty = new() { ErrorMin = 0.01 };

    public HomeWiringTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    /// <summary>
    /// What is in effect for <paramref name="folderPath"/> — the same
    /// source-relative question Home asks, with the token minted the way Home
    /// mints it.
    /// </summary>
    private FilterConfig? InEffectForFolder(string folderPath) =>
        Setup.Current.ConfigInEffectFor(FilterSourceToken.FromPath(folderPath));

    private IRenderedComponent<Home> RenderPage(CapturingProcessHandler? processing = null)
    {
        this.AddHomeServices(processing);
        return this.RenderHome();
    }

    // ── Readiness: the empty selection needs no Apply ───────────────────────

    [Fact]
    public async Task EmptySelection_IsInEffectWithoutApply_OnceAFolderIsCommitted()
    {
        var cut = RenderPage();
        await cut.SetOutputPathAsync(@"D:\xg\out.csv");

        // No source yet: nothing can be in effect, and Apply has nothing to
        // apply against. No copy claims Apply is needed — the folder box is
        // what is missing.
        Assert.Null(cut.FindComponent<LocalModePanel>().Instance.FilterInEffect);
        Assert.True(cut.ApplyButton().HasAttribute("disabled"));
        Assert.True(cut.RunButton().HasAttribute("disabled"));
        Assert.DoesNotContain("Apply filters first", cut.Markup);

        await cut.CommitFolderAsync(Folder);

        // The empty selection is ready for the folder as it stands.
        Assert.Equal(new FilterConfig(), InEffectForFolder(Folder));
        Assert.Equal(new FilterConfig(), cut.FindComponent<LocalModePanel>().Instance.FilterInEffect);
        Assert.False(cut.RunButton().HasAttribute("disabled"));
        Assert.DoesNotContain("Apply filters first", cut.Markup);
    }

    // ── A non-empty selection needs Apply ───────────────────────────────────

    [Fact]
    public async Task Apply_ANonEmptySelection_PutsItInEffectAndOpensRun()
    {
        var cut = RenderPage();
        await cut.CommitFolderAsync(Folder);
        await cut.SetOutputPathAsync(@"D:\xg\out.csv");

        await cut.EditMinAsync("0.01");

        Assert.Null(InEffectForFolder(Folder));
        Assert.Null(cut.FindComponent<LocalModePanel>().Instance.FilterInEffect);
        Assert.True(cut.RunButton().HasAttribute("disabled"));
        Assert.Contains("Apply filters first", cut.Markup);

        await cut.ApplyFiltersAsync();

        // One filter, one truth: what the owner has in effect for this folder
        // is the value the panel would POST.
        Assert.Equal(NonEmpty, InEffectForFolder(Folder));
        Assert.Equal(NonEmpty, cut.FindComponent<LocalModePanel>().Instance.FilterInEffect);
        Assert.False(cut.RunButton().HasAttribute("disabled"));
    }

    [Fact]
    public async Task EditAfterApply_ClosesTheGate_UndoReopensIt()
    {
        var cut = RenderPage();
        await cut.CommitFolderAsync(Folder);
        await cut.SetOutputPathAsync(@"D:\xg\out.csv");
        await cut.EditMinAsync("0.01");
        await cut.ApplyFiltersAsync();
        Assert.False(cut.RunButton().HasAttribute("disabled"));

        // A keystroke moves the selection off what is in effect: the gate
        // closes, and the panel is handed nothing.
        await cut.EditMinAsync("0.02");
        Assert.Null(InEffectForFolder(Folder));
        Assert.Null(cut.FindComponent<LocalModePanel>().Instance.FilterInEffect);
        Assert.True(cut.RunButton().HasAttribute("disabled"));

        // Undo back to the applied values: the baseline is in effect again,
        // without a re-Apply.
        await cut.EditMinAsync("0.01");
        Assert.Equal(NonEmpty, InEffectForFolder(Folder));
        Assert.False(cut.RunButton().HasAttribute("disabled"));
    }

    // ── Source changes end the setup ────────────────────────────────────────

    [Fact]
    public async Task FolderChange_EndsTheSetup_TheAppliedFilterIsDroppedAndApplyRearmed()
    {
        var cut = RenderPage();
        await cut.CommitFolderAsync(Folder);
        await cut.SetOutputPathAsync(@"D:\xg\out.csv");
        await cut.EditMinAsync("0.01");
        await cut.ApplyFiltersAsync();
        var generation = Setup.Current.Generation;

        await cut.CommitFolderAsync(OtherFolder);

        Assert.Equal(generation + 1, Setup.Current.Generation);
        Assert.Null(InEffectForFolder(OtherFolder));
        Assert.True(cut.RunButton().HasAttribute("disabled"));

        // Returning to the first folder does not bring its consent back:
        // that setup ended (A → B → A is two endings).
        await cut.CommitFolderAsync(Folder);
        Assert.Null(InEffectForFolder(Folder));

        // Apply is re-armed: this click commits the unchanged selection,
        // which the panel would refuse were it still in effect.
        await cut.ApplyFiltersAsync();
        Assert.Equal(NonEmpty, InEffectForFolder(Folder));
        Assert.False(cut.RunButton().HasAttribute("disabled"));
    }

    /// <summary>
    /// Changing folders while the empty filter stays ready: the gate never
    /// closes — the empty selection is in effect for the new folder at once —
    /// but the run on screen was computed from the old folder, so its result
    /// goes. Through the real page: Home's report, the owner's readiness, and
    /// the panel's result keyed by its inputs.
    /// </summary>
    [Fact]
    public async Task FolderChange_WhileTheEmptyFilterStaysReady_DropsTheEarlierResult()
    {
        var cut = RenderPage(new CapturingProcessHandler());
        await cut.CommitFolderAsync(Folder);
        await cut.SetOutputPathAsync(@"D:\xg\out.csv");
        await cut.RunButton().ClickAsync(new MouseEventArgs()).WaitAsync(CapturingProcessHandler.RunTimeout);
        Assert.Contains("Done", cut.Markup);

        await cut.CommitFolderAsync(OtherFolder);

        Assert.Equal(new FilterConfig(), InEffectForFolder(OtherFolder));
        Assert.False(cut.RunButton().HasAttribute("disabled"));
        Assert.DoesNotContain("Done", cut.Markup);
    }

    [Fact]
    public async Task FolderRecommitSameValue_IsNotASourceChange()
    {
        var cut = RenderPage();
        await cut.CommitFolderAsync(Folder);
        await cut.SetOutputPathAsync(@"D:\xg\out.csv");
        await cut.EditMinAsync("0.01");
        await cut.ApplyFiltersAsync();
        var generation = Setup.Current.Generation;

        // A blur that commits the same value reports the same token: the
        // setup survives.
        await cut.CommitFolderAsync(Folder);
        Assert.Equal(generation, Setup.Current.Generation);
        Assert.Equal(NonEmpty, InEffectForFolder(Folder));
        Assert.False(cut.RunButton().HasAttribute("disabled"));
    }

    /// <summary>
    /// The same folder respelled with a trailing separator is the same source
    /// (halheinrich/backgammon#94). Home does not fold paths itself; it hands
    /// the spelling it holds to <c>FilterSourceToken.FromPath</c>, whose rule
    /// makes trailing separators insignificant.
    /// <para>
    /// Pinned at the wire layer, not left to the producer's unit test, because
    /// the two answer different questions: that one says <c>FromPath</c> folds
    /// the spelling, this one says nothing between the folder input and the
    /// owner re-introduces a distinction the token dissolved.
    /// </para>
    /// <para>
    /// <b>What it does not do is reproduce the bug that motivated the move</b>,
    /// and it must not be read as if it did. Home's old rule trimmed with
    /// <c>Path.TrimEndingDirectorySeparator</c>, which is platform-sensitive:
    /// under WebAssembly's Unix path semantics it recognizes only <c>/</c> and
    /// left the trailing <c>\</c> in place, so in the browser
    /// <c>…\matches\</c> minted a token that failed to match <c>…\matches</c>
    /// and ended the setup behind the user's back. bUnit runs on Windows .NET,
    /// where that same call trims <c>\</c> correctly — so this test passed
    /// against the old code too, and no test that runs on this host could have
    /// caught it. What this pins is the contract going forward — a host-side
    /// fold reintroduced here, or a token that stops folding, fails it on any
    /// platform.
    /// </para>
    /// </summary>
    [Fact]
    public async Task FolderRecommitWithTrailingSeparator_IsNotASourceChange()
    {
        var cut = RenderPage();
        await cut.CommitFolderAsync(Folder);
        await cut.SetOutputPathAsync(@"D:\xg\out.csv");
        await cut.EditMinAsync("0.01");
        await cut.ApplyFiltersAsync();
        var generation = Setup.Current.Generation;

        await cut.CommitFolderAsync(Folder + @"\");

        // Same source, so nothing ends — asked under both spellings, since
        // one token answers to each.
        Assert.Equal(generation, Setup.Current.Generation);
        Assert.Equal(NonEmpty, InEffectForFolder(Folder));
        Assert.Equal(NonEmpty, InEffectForFolder(Folder + @"\"));
        Assert.False(cut.RunButton().HasAttribute("disabled"));
        Assert.True(cut.ApplyButton().HasAttribute("disabled"));
    }

    [Fact]
    public async Task OutputPathChange_IsNotASourceChange()
    {
        var cut = RenderPage();
        await cut.CommitFolderAsync(Folder);
        await cut.SetOutputPathAsync(@"D:\xg\out.csv");
        await cut.EditMinAsync("0.01");
        await cut.ApplyFiltersAsync();
        var generation = Setup.Current.Generation;

        // The output path is not a source: changing it never ends the setup.
        await cut.SetOutputPathAsync(@"D:\elsewhere\out.csv");
        Assert.Equal(generation, Setup.Current.Generation);
        Assert.Equal(NonEmpty, InEffectForFolder(Folder));
        Assert.False(cut.RunButton().HasAttribute("disabled"));
    }

    // ── Saved filters over the file relay ───────────────────────────────────

    [Fact]
    public async Task SavedFilters_DocumentInSourceFolder_RendersItsRows()
    {
        var stub = this.AddHomeServices();
        stub.Documents[(Folder, SavedFiltersDocument.FileName)] =
            NamedFilterCollection.Empty.With("Blitz", new FilterConfig()).ToJson();

        var cut = this.RenderHome();
        await cut.CommitFolderAsync(Folder);

        // The commit reloads the saved-filters context from the latched
        // folder through the relay — the document's rows appear.
        cut.WaitForAssertion(() =>
            Assert.Contains(cut.FindAll("li"), li => li.TextContent.Contains("Blitz")));
    }

    [Fact]
    public async Task SavedFilters_SaveAs_WritesDocumentIntoLatchedFolder()
    {
        var stub = this.AddHomeServices();
        var cut = this.RenderHome();
        await cut.CommitFolderAsync(Folder);
        cut.WaitForState(() => cut.FindAll("#saveFilterName").Any(), TimeSpan.FromSeconds(5));

        // Save-as by id — a bare "Save" locator goes ambiguous once a
        // saved-filter row (with its own Save button) exists.
        await cut.Find("#saveFilterName").InputAsync(new ChangeEventArgs { Value = "Blitz" });
        await cut.Find("#saveFilterButton").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            var write = Assert.Single(stub.DocumentWrites);
            Assert.Equal(Folder, write.Folder);
            // The name comes from the producer's document identity — the
            // server never knows it, the client supplies it.
            Assert.Equal(SavedFiltersDocument.FileName, write.Name);
            Assert.True(NamedFilterCollection.TryFromJson(write.Content, out var saved));
            Assert.True(saved.Contains("Blitz"));
        });
    }

    [Fact]
    public async Task SavedFilters_FolderChange_ReloadsContextFromNewFolder()
    {
        var stub = this.AddHomeServices();
        stub.Documents[(Folder, SavedFiltersDocument.FileName)] =
            NamedFilterCollection.Empty.With("Blitz", new FilterConfig()).ToJson();

        var cut = this.RenderHome();
        await cut.CommitFolderAsync(Folder);
        cut.WaitForAssertion(() =>
            Assert.Contains(cut.FindAll("li"), li => li.TextContent.Contains("Blitz")));

        // The source folder and the filter store are the same folder: one
        // change event drives both the source report and the store reload.
        await cut.CommitFolderAsync(OtherFolder);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(stub.DocumentReads,
                r => r.Folder == OtherFolder && r.Name == SavedFiltersDocument.FileName);
            Assert.DoesNotContain(cut.FindAll("li"), li => li.TextContent.Contains("Blitz"));
        });
    }

    // ── Format and XGP option wiring ────────────────────────────────────────

    [Fact]
    public void EveryFormatRadio_IsEnabled()
    {
        var cut = RenderPage();

        foreach (var id in new[] { "#fmtCsv", "#fmtDiagram", "#fmtPptx", "#fmtPdf", "#fmtXgp" })
            Assert.False(cut.Find(id).HasAttribute("disabled"), id);
    }

    [Fact]
    public void XgpRadio_Enabled_AndOptionsPropagateToThePanel()
    {
        var cut = RenderPage();
        Assert.False(cut.Find("#fmtXgp").HasAttribute("disabled"));

        // The commit's hand-off (SPEC-notices.md §7.5) is bound: a silently
        // splatted one would leave the next number behind every export.
        var localPanel = cut.FindComponent<LocalModePanel>();
        Assert.NotNull(localPanel.Instance.XgpOptions);
        Assert.True(localPanel.Instance.OnXgpJobEnded.HasDelegate);
    }

    [Fact]
    public void XgpOptionsBlock_RendersOnlyWhileXgpSelected_AndPropagatesToThePanel()
    {
        var cut = RenderPage();

        Assert.Empty(cut.FindAll("#xgpOptions"));

        cut.Find("#fmtXgp").Change(true);
        Assert.Single(cut.FindAll("#xgpOptions"));

        var panel = cut.FindComponent<LocalModePanel>();
        Assert.NotNull(panel.Instance.XgpOptions);
        Assert.True(panel.Instance.XgpOptions.TryValidate(out _));

        cut.Find("#fmtCsv").Change(true);
        Assert.Empty(cut.FindAll("#xgpOptions"));
    }

}
