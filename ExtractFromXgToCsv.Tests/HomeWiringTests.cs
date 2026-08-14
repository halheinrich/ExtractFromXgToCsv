using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
using XgFilter_Razor.Components;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the wire integration between the lib-owned <c>FilterSurface</c>
/// composite and the consumer-side <see cref="Home"/> — the applied-holder
/// derivation feeding both mode panels' gates, and the per-mode source-change
/// rule (the #78 re-gate: a folder commit or file re-selection ends the
/// setup — holder cleared, Apply re-armed, saved-filters context reloaded).
/// The original regression class shipped because a renamed panel event left a
/// consumer binding silently splatted; these tests fail closed if any of that
/// wiring is dropped again.
/// <para>
/// All filter gestures drive <c>FilterSurface</c>'s rendered DOM — the panels
/// are producer-internal now and <c>FindComponent</c> over them is banned,
/// host tests included (no carve-out). Two banked traps honored throughout: a
/// bare "Save" locator goes ambiguous once a saved-filter row exists (rows
/// carry their own Save buttons — use the ids), and the panel's own gate
/// refuses a DOM Apply for an unchanged selection, so a re-apply within one
/// committed mount needs a real edit first — <em>except</em> after a source
/// change, where the composite's forget-commit re-arms Apply; several tests
/// below prove exactly that by re-applying without an edit.
/// </para>
/// </summary>
public class HomeWiringTests : BunitContext
{
    private const string FixtureA = "MTCH4064.xg";
    private const string FixtureB = "ajhhBG0407.xg";

    public HomeWiringTests()
    {
        // Every JS interop call (localStorage gets/sets) returns default — the
        // tests don't depend on persisted state, so the folder latch starts
        // blank (no source) and the panel buffers start at defaults.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
        // The holder Home injects and FilterSurface mediates — registered like
        // the app registers it, and resolved by tests to assert the gate SSOT.
        Services.AddScoped<AppliedFilter>();
        Services.AddScoped<FilterRestoreNotice>();
    }

    private StubAppModeHandler RegisterHttpClient(string appMode)
    {
        var stub = new StubAppModeHandler(appMode);
        Services.AddSingleton(new HttpClient(stub)
        {
            BaseAddress = new Uri("http://localhost/"),
        });
        return stub;
    }

    private AppliedFilter Holder => Services.GetRequiredService<AppliedFilter>();

    /// <summary>
    /// What the holder reports as applied for <paramref name="folderPath"/> —
    /// the same source-relative question Home's <c>FilterInEffect</c> asks, and
    /// the only one the holder answers. Null covers both "nothing is applied"
    /// and "what is applied belongs elsewhere", which is precisely the
    /// distinction no gate is allowed to care about. The key is minted the way
    /// Home mints it — since halheinrich/backgammon#94 the bare factory call,
    /// because <see cref="FilterSourceToken.FromPath"/> owns path identity.
    /// </summary>
    private FilterConfig? AppliedForFolder(string folderPath) =>
        Holder.ConfigFor(FilterSourceToken.FromPath(folderPath));

    /// <summary>
    /// The Web-mode counterpart: what is applied for the
    /// <paramref name="generation"/>th selection gesture of a page instance —
    /// Home bumps the counter once per selection, so the first upload is
    /// generation 1 and 0 means no source at all.
    /// </summary>
    private FilterConfig? AppliedForSelection(int generation) =>
        Holder.ConfigFor(FilterSourceToken.FromGeneration(generation));

    // Waits for the composite as well as the mode panel: Home holds
    // FilterSurface back until its first-render restore completes (#85), and in
    // Web mode the panel alone is up from the very first render — so the panel
    // is not a restore-completed signal there. Every gesture below drives the
    // composite's DOM, so this is the render the tests actually need.
    private IRenderedComponent<Home> RenderHome(string appMode)
    {
        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<FilterSurface>().Any()
                && (appMode == "Local"
                    ? cut.FindComponents<LocalModePanel>().Any()
                    : cut.FindComponents<WebModePanel>().Any()),
            TimeSpan.FromSeconds(5));
        return cut;
    }

    // ── DOM gesture helpers ─────────────────────────────────────────────────

    /// <summary>Commit the panel's current selection via its own Apply button.</summary>
    private static Task ApplyFiltersAsync(IRenderedComponent<Home> cut) =>
        cut.FindAll("button")
           .First(b => b.TextContent.Trim() == "Apply Filter")
           .ClickAsync(new());

    /// <summary>
    /// A real edit on the always-visible error-range Min input, moving the
    /// buffers off any committed config. Undo with <see cref="UndoFilterEditAsync"/>.
    /// </summary>
    private static Task EditFilterControlAsync(IRenderedComponent<Home> cut) =>
        cut.Find("input[placeholder='Min']")
           .InputAsync(new ChangeEventArgs { Value = "0.75" });

    private static Task UndoFilterEditAsync(IRenderedComponent<Home> cut) =>
        cut.Find("input[placeholder='Min']")
           .InputAsync(new ChangeEventArgs { Value = "" });

    /// <summary>
    /// Type <paramref name="path"/> into the Local folder input and commit it
    /// at the change boundary — the gesture that latches the source path and
    /// (on an actual change) re-gates.
    /// </summary>
    private static async Task CommitFolderAsync(IRenderedComponent<Home> cut, string path)
    {
        await cut.Find("#folderPath").InputAsync(new ChangeEventArgs { Value = path });
        await cut.Find("#folderPath").ChangeAsync(new ChangeEventArgs { Value = path });
    }

    private static Task SetOutputPathAsync(IRenderedComponent<Home> cut, string path) =>
        cut.Find("#outputPath").InputAsync(new ChangeEventArgs { Value = path });

    private static AngleSharp.Dom.IElement RunButton(IRenderedComponent<Home> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Run");

    private static AngleSharp.Dom.IElement DownloadButton(IRenderedComponent<Home> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Download"));

    private static void UploadFixture(IRenderedComponent<Home> cut, string fixture) =>
        // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(fixture), fixture));

    // ── Applied-holder derivation → mode panels ─────────────────────────────

    [Fact]
    public async Task Apply_WithFolderSource_RecordsOnHolderAndEnablesRun()
    {
        RegisterHttpClient("Local");
        var cut = RenderHome("Local");
        await CommitFolderAsync(cut, @"D:\xg\matches");
        await SetOutputPathAsync(cut, @"D:\xg\out.csv");

        var localPanel = cut.FindComponent<LocalModePanel>();
        Assert.False(localPanel.Instance.FilterApplied);
        Assert.True(RunButton(cut).HasAttribute("disabled"));

        await ApplyFiltersAsync(cut);

        var recorded = AppliedForFolder(@"D:\xg\matches");
        Assert.NotNull(recorded);
        Assert.True(localPanel.Instance.FilterApplied);
        // One config, one truth: the config applied for this folder is the
        // same instance the panel would POST.
        Assert.Same(recorded, localPanel.Instance.FilterConfig);
        Assert.False(RunButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Apply_WithSelectionSource_RecordsOnHolderAndPropagatesToWebPanel()
    {
        RegisterHttpClient("Web");
        var cut = RenderHome("Web");
        UploadFixture(cut, FixtureA);

        var webPanel = cut.FindComponent<WebModePanel>();
        Assert.False(webPanel.Instance.FilterApplied);

        await ApplyFiltersAsync(cut);

        // The upload was this page's first selection gesture, so the commit is
        // keyed to generation 1.
        var recorded = AppliedForSelection(1);
        Assert.NotNull(recorded);
        Assert.True(webPanel.Instance.FilterApplied);
        Assert.Same(recorded, webPanel.Instance.FilterConfig);
    }

    [Fact]
    public async Task EditAfterApply_ClearsHolderAndRegatesRun_UndoRestoresBoth()
    {
        RegisterHttpClient("Local");
        var cut = RenderHome("Local");
        await CommitFolderAsync(cut, @"D:\xg\matches");
        await SetOutputPathAsync(cut, @"D:\xg\out.csv");
        await ApplyFiltersAsync(cut);
        Assert.False(RunButton(cut).HasAttribute("disabled"));

        // A keystroke moves the buffers off the committed config: the composite
        // clears the holder, and that *is* the gate closing. Since the §6.1
        // collapse (halheinrich/backgammon#101) there is no second flag to
        // check — the panel's FilterApplied reads the holder through
        // Home.FilterInEffect, so an edit and a source change close it by the
        // same mechanism.
        await EditFilterControlAsync(cut);
        var localPanel = cut.FindComponent<LocalModePanel>();
        Assert.Null(AppliedForFolder(@"D:\xg\matches"));
        Assert.False(localPanel.Instance.FilterApplied);
        Assert.True(RunButton(cut).HasAttribute("disabled"));

        // Undo back to the applied values: the panel reports clean, the
        // composite re-affirms the holder, and the gate re-opens without a
        // re-Apply (which the panel refuses for an unchanged selection).
        await UndoFilterEditAsync(cut);
        Assert.NotNull(AppliedForFolder(@"D:\xg\matches"));
        Assert.True(localPanel.Instance.FilterApplied);
        Assert.False(RunButton(cut).HasAttribute("disabled"));
    }

    // ── The #78 re-gate: source changes end the setup ───────────────────────

    [Fact]
    public async Task FolderChange_EndsSetup_RunRegatedAndApplyRearmed()
    {
        RegisterHttpClient("Local");
        var cut = RenderHome("Local");
        await CommitFolderAsync(cut, @"D:\xg\matches");
        await SetOutputPathAsync(cut, @"D:\xg\out.csv");
        await ApplyFiltersAsync(cut);
        Assert.NotNull(AppliedForFolder(@"D:\xg\matches"));

        // Committing a different folder is a source change: the setup ends —
        // holder cleared, Run re-gated.
        await CommitFolderAsync(cut, @"D:\xg\other");
        Assert.Null(AppliedForFolder(@"D:\xg\other"));
        Assert.True(RunButton(cut).HasAttribute("disabled"));

        // Nothing survives for the *old* folder either, and in this host that
        // is the load-bearing half. Local tokens are minted from the path, so
        // re-entering "matches" mints an equal token again — a config merely
        // left unmatched would be silently re-adopted the moment the user
        // typed the old path back. #78's clear is what retires it; the keying
        // alone expires nothing here.
        Assert.Null(AppliedForFolder(@"D:\xg\matches"));

        // And Apply is re-armed: this click commits the *unchanged* selection,
        // which the panel would refuse had the composite not forget-committed
        // it — the re-open below is the proof.
        await ApplyFiltersAsync(cut);
        Assert.NotNull(AppliedForFolder(@"D:\xg\other"));
        Assert.False(RunButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task FolderRecommitSameValue_IsNotASourceChange()
    {
        RegisterHttpClient("Local");
        var cut = RenderHome("Local");
        await CommitFolderAsync(cut, @"D:\xg\matches");
        await SetOutputPathAsync(cut, @"D:\xg\out.csv");
        await ApplyFiltersAsync(cut);

        // A blur that commits the same value latches the same path → same
        // token → the setup survives.
        await CommitFolderAsync(cut, @"D:\xg\matches");
        Assert.NotNull(AppliedForFolder(@"D:\xg\matches"));
        Assert.False(RunButton(cut).HasAttribute("disabled"));
    }

    /// <summary>
    /// The same folder respelled with a trailing separator is the same source
    /// (halheinrich/backgammon#94). Home no longer folds paths itself; it hands
    /// the spelling it holds to <c>FilterSourceToken.FromPath</c>, whose rule
    /// makes trailing separators insignificant.
    /// <para>
    /// Pinned at the wire layer, not left to the producer's unit test, because
    /// the two answer different questions: that one says <c>FromPath</c> folds
    /// the spelling, this one says nothing between the folder input and the
    /// holder re-introduces a distinction the token dissolved.
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
    /// caught it. That is the argument for the rule living in the token rather
    /// than in a host: the failure was invisible at the mint site *and*
    /// unreachable from the host's own suite. What this pins is the contract
    /// going forward — a host-side fold reintroduced here, or a token that
    /// stops folding, fails it on any platform.
    /// </para>
    /// </summary>
    [Fact]
    public async Task FolderRecommitWithTrailingSeparator_IsNotASourceChange()
    {
        RegisterHttpClient("Local");
        var cut = RenderHome("Local");
        await CommitFolderAsync(cut, @"D:\xg\matches");
        await SetOutputPathAsync(cut, @"D:\xg\out.csv");
        await ApplyFiltersAsync(cut);
        Assert.NotNull(AppliedForFolder(@"D:\xg\matches"));

        await CommitFolderAsync(cut, @"D:\xg\matches\");

        // Same source, so nothing ends: the applied config is still there and
        // Run is still live. Asked under *both* spellings — one token answers
        // to each, which is the property under test.
        Assert.NotNull(AppliedForFolder(@"D:\xg\matches"));
        Assert.NotNull(AppliedForFolder(@"D:\xg\matches\"));
        Assert.False(RunButton(cut).HasAttribute("disabled"));

        // And the panel was never re-armed — the composite's forget-commit is
        // what a spurious source change would show up as here.
        var apply = cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply Filter");
        Assert.True(apply.HasAttribute("disabled"));
    }

    [Fact]
    public async Task OutputPathChange_IsNotASourceChange()
    {
        RegisterHttpClient("Local");
        var cut = RenderHome("Local");
        await CommitFolderAsync(cut, @"D:\xg\matches");
        await SetOutputPathAsync(cut, @"D:\xg\out.csv");
        await ApplyFiltersAsync(cut);

        // The output path is not a source (umbrella-ratified): changing it
        // must never end the setup.
        await SetOutputPathAsync(cut, @"D:\elsewhere\out.csv");
        Assert.NotNull(AppliedForFolder(@"D:\xg\matches"));
        Assert.False(RunButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task WebReselection_EndsSetup_PreviewBlanksUntilReApply()
    {
        RegisterHttpClient("Web");
        var cut = RenderHome("Web");
        UploadFixture(cut, FixtureA);
        await ApplyFiltersAsync(cut);
        Assert.Contains("rows match current filters", cut.Markup);

        // Re-selecting files bumps the selection generation: one source-change
        // rule everywhere, so the preview blanks and Download re-gates until
        // the user re-applies (ruled UX delta — supersedes the old "files
        // selected after an Apply are filtered immediately" contract).
        UploadFixture(cut, FixtureB);
        Assert.Null(AppliedForSelection(2));
        Assert.Contains("set filters and click Apply Filter", cut.Markup);
        Assert.True(DownloadButton(cut).HasAttribute("disabled"));

        // Apply is re-armed by the same rule — no edit needed.
        await ApplyFiltersAsync(cut);
        Assert.NotNull(AppliedForSelection(2));
        Assert.Contains("rows match current filters", cut.Markup);
        Assert.False(DownloadButton(cut).HasAttribute("disabled"));
    }

    // ── First-latch transitions (null → first source) ───────────────────────

    [Fact]
    public async Task ApplyBeforeAnyFolder_IsNotRecorded_FirstCommitRearmsApply()
    {
        RegisterHttpClient("Local");
        var cut = RenderHome("Local");
        await SetOutputPathAsync(cut, @"D:\xg\out.csv");

        // No source yet: the apply is deliberately unrecorded — there is
        // nothing to key it to, and Run stays gated.
        await ApplyFiltersAsync(cut);
        Assert.False(cut.FindComponent<LocalModePanel>().Instance.FilterApplied);
        Assert.True(RunButton(cut).HasAttribute("disabled"));

        // The first folder commit is a source change (null → token): the
        // end-setup runs, re-arming Apply. The unrecorded click above does not
        // attach to the new folder retroactively —
        await CommitFolderAsync(cut, @"D:\xg\matches");
        Assert.Null(AppliedForFolder(@"D:\xg\matches"));

        // — so this second click of an unchanged selection is the one that
        // commits, and it is recorded against the folder.
        await ApplyFiltersAsync(cut);
        Assert.NotNull(AppliedForFolder(@"D:\xg\matches"));
        Assert.False(RunButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task ApplyBeforeAnySelection_IsNotRecorded_FirstSelectionRearmsApply()
    {
        RegisterHttpClient("Web");
        var cut = RenderHome("Web");

        // No selection yet: no source, so the apply is unrecorded.
        await ApplyFiltersAsync(cut);
        Assert.False(cut.FindComponent<WebModePanel>().Instance.FilterApplied);

        // The first selection mints generation 1 (null → token): end-setup,
        // Apply re-armed, and the re-apply is recorded — the preview follows.
        UploadFixture(cut, FixtureA);
        Assert.Null(AppliedForSelection(1));
        await ApplyFiltersAsync(cut);
        Assert.NotNull(AppliedForSelection(1));
        Assert.Contains("rows match current filters", cut.Markup);
    }

    // ── Saved filters over the file relay ───────────────────────────────────

    [Fact]
    public async Task SavedFilters_DocumentInSourceFolder_RendersItsRows()
    {
        var stub = RegisterHttpClient("Local");
        stub.Documents[(@"D:\xg\matches", SavedFiltersDocument.FileName)] =
            NamedFilterCollection.Empty.With("Blitz", new FilterConfig()).ToJson();

        var cut = RenderHome("Local");
        await CommitFolderAsync(cut, @"D:\xg\matches");

        // The commit reloads the saved-filters context from the latched
        // folder through the relay — the document's rows appear.
        cut.WaitForAssertion(() =>
            Assert.Contains(cut.FindAll("li"), li => li.TextContent.Contains("Blitz")));
    }

    [Fact]
    public async Task SavedFilters_SaveAs_WritesDocumentIntoLatchedFolder()
    {
        var stub = RegisterHttpClient("Local");
        var cut = RenderHome("Local");
        await CommitFolderAsync(cut, @"D:\xg\matches");
        cut.WaitForState(() => cut.FindAll("#saveFilterName").Any(), TimeSpan.FromSeconds(5));

        // Save-as by id — a bare "Save" locator goes ambiguous once a
        // saved-filter row (with its own Save button) exists.
        await cut.Find("#saveFilterName").InputAsync(new ChangeEventArgs { Value = "Blitz" });
        await cut.Find("#saveFilterButton").ClickAsync(new());

        cut.WaitForAssertion(() =>
        {
            var write = Assert.Single(stub.DocumentWrites);
            Assert.Equal(@"D:\xg\matches", write.Folder);
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
        var stub = RegisterHttpClient("Local");
        stub.Documents[(@"D:\xg\matches", SavedFiltersDocument.FileName)] =
            NamedFilterCollection.Empty.With("Blitz", new FilterConfig()).ToJson();

        var cut = RenderHome("Local");
        await CommitFolderAsync(cut, @"D:\xg\matches");
        cut.WaitForAssertion(() =>
            Assert.Contains(cut.FindAll("li"), li => li.TextContent.Contains("Blitz")));

        // The source folder and the filter store are the same folder: one
        // change event drives both the re-gate and the store reload.
        await CommitFolderAsync(cut, @"D:\xg\other");
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(stub.DocumentReads,
                r => r.Folder == @"D:\xg\other" && r.Name == SavedFiltersDocument.FileName);
            Assert.DoesNotContain(cut.FindAll("li"), li => li.TextContent.Contains("Blitz"));
        });
    }

    [Fact]
    public async Task SavedFilters_WebMode_RendersNoSection()
    {
        var stub = RegisterHttpClient("Web");
        var cut = RenderHome("Web");

        // Web mode binds Storage = null (ruled: no second store — no
        // localStorage fallback), so no saved-filters section exists in any
        // Web state, selection or not — and nothing ever calls the relay.
        UploadFixture(cut, FixtureA);
        await ApplyFiltersAsync(cut);

        Assert.Empty(cut.FindAll("#saveFilterName"));
        Assert.DoesNotContain("Saved Filters", cut.Markup);
        Assert.Empty(stub.DocumentReads);
    }

    // ── XGP option wiring (unchanged contracts, mode-shell level) ───────────

    [Fact]
    public void XgpRadio_EnabledInWebMode()
    {
        RegisterHttpClient("Web");
        var cut = RenderHome("Web");
        Assert.False(cut.Find("#fmtXgp").HasAttribute("disabled"));
    }

    [Fact]
    public void XgpRadio_EnabledInLocalMode_AndOptionsPropagateToLocalPanel()
    {
        RegisterHttpClient("Local");
        var cut = RenderHome("Local");
        Assert.False(cut.Find("#fmtXgp").HasAttribute("disabled"));

        var localPanel = cut.FindComponent<LocalModePanel>();
        Assert.NotNull(localPanel.Instance.XgpOptions);
        Assert.True(localPanel.Instance.OnXgpExported.HasDelegate);
    }

    [Fact]
    public void XgpOptionsBlock_RendersOnlyWhileXgpSelected_AndPropagatesToWebPanel()
    {
        RegisterHttpClient("Web");
        var cut = RenderHome("Web");

        Assert.Empty(cut.FindAll("#xgpOptions"));

        cut.Find("#fmtXgp").Change(true);
        Assert.Single(cut.FindAll("#xgpOptions"));

        var webPanel = cut.FindComponent<WebModePanel>();
        Assert.NotNull(webPanel.Instance.XgpOptions);
        Assert.True(webPanel.Instance.XgpOptions.TryValidate(out _));

        cut.Find("#fmtCsv").Change(true);
        Assert.Empty(cut.FindAll("#xgpOptions"));
    }

    [Fact]
    public void OnXgpExported_AdvancesAndPersistsTheCounter()
    {
        RegisterHttpClient("Web");
        var cut = RenderHome("Web");
        cut.Find("#fmtXgp").Change(true);

        var webPanel = cut.FindComponent<WebModePanel>();
        Assert.Equal(1, webPanel.Instance.XgpOptions.StartNumber);

        // Panel reports an export of 5 decisions → counter advances to 6
        // and the last used number is persisted.
        cut.InvokeAsync(() => webPanel.Instance.OnXgpExported.InvokeAsync(5));

        Assert.Equal(6, webPanel.Instance.XgpOptions.StartNumber);
        Assert.Contains(JSInterop.Invocations, i =>
            i.Identifier == "localStorage.setItem"
            && (string?)i.Arguments[0] == "xg_xgpLastNumber"
            && (string?)i.Arguments[1] == "5");
    }
}
