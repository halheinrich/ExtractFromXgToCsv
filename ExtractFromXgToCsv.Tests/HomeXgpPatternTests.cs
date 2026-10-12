using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Razor;
using XgFilter_Razor.TestSupport;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// bUnit tests for the Home-owned XGP name-pattern UI: the pattern textbox
/// binding, the registry-driven insert-token dropdown, the live preview /
/// inline error branch, the one-time migration of the keys the numbering
/// record replaced (the legacy prefix included) into that record, and the
/// post-export persistence (one checkpoint of the record — the umbrella's
/// <c>SPEC-notices.md</c> §7.5). Naming mechanics themselves are owned by the
/// engine suites — here only the wiring is pinned.
/// <para>
/// Storage is planned (<see cref="BrowserStoragePlan"/>): the migration reads
/// and the export's writes are the subject, and every other call the page
/// makes is declared beside them — switching to XGP persists the format, and
/// the Web panel's file name takes the <c>.zip</c> extension and persists it.
/// </para>
/// </summary>
public class HomeXgpPatternTests : BunitContext
{
    private readonly BrowserStoragePlan _storage;

    public HomeXgpPatternTests()
    {
        this.AddHomeServices("Web");
        _storage = BrowserStoragePlan.On(JSInterop);
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
    }

    private IRenderedComponent<Home> RenderHomeWithXgpSelected(
        Dictionary<string, string>? stored = null, string? migratedDocument = null)
    {
        _storage.ExpectHomeVisit("Web", stored, migratedDocument);
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputFormatKey, nameof(OutputFormat.Xgp),
            BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.CsvFileNameKey, "decisions.zip",
            BrowserStorageWriteAnswer.Succeeded);

        var cut = this.RenderHome();
        cut.Find("#fmtXgp").Change(true);
        return cut;
    }

    [Fact]
    public void PatternInput_BindsToXgpOptions()
    {
        var cut = RenderHomeWithXgpSelected();

        cut.Find("#xgpPattern").Input("Move{dice}");

        var webPanel = cut.FindComponent<WebModePanel>();
        Assert.Equal("Move{dice}", webPanel.Instance.XgpOptions.NamePattern);
        _storage.Verify();
    }

    [Fact]
    public void InsertTokenDropdown_ListsEveryRegistryToken_AndAppendsOnSelect()
    {
        var cut = RenderHomeWithXgpSelected();

        // Placeholder option + one option per registry token, in registry
        // (declaration) order.
        var optionValues = cut.FindAll("#xgpInsertToken option")
            .Select(o => o.GetAttribute("value"))
            .ToList();
        Assert.Equal(
            new[] { "" }.Concat(XgpNameTokens.All.Select(t => t.Name)),
            optionValues);

        cut.Find("#xgpInsertToken").Change("dice");

        Assert.Equal("pos{n}{dice}",
            cut.FindComponent<WebModePanel>().Instance.XgpOptions.NamePattern);
        Assert.Equal("pos{n}{dice}", cut.Find("#xgpPattern").GetAttribute("value"));
        _storage.Verify();
    }

    [Fact]
    public void ValidPattern_RendersLivePreview_ShowingCounterAdvance()
    {
        var cut = RenderHomeWithXgpSelected();

        // Default "pos{n}": the two-name preview shows the {n} advance.
        var preview = cut.Find("#xgpPreview").TextContent;
        Assert.Contains("pos001.xgp", preview);
        Assert.Contains("pos002.xgp", preview);
        Assert.Empty(cut.FindAll("#xgpOptions .text-danger"));
        _storage.Verify();
    }

    [Fact]
    public void CounterlessPattern_PreviewShowsTheUniquifier()
    {
        var cut = RenderHomeWithXgpSelected();

        cut.Find("#xgpPattern").Input("quiz");

        var preview = cut.Find("#xgpPreview").TextContent;
        Assert.Contains("quiz.xgp", preview);
        Assert.Contains("quiz (2).xgp", preview);
        _storage.Verify();
    }

    [Fact]
    public void InvalidPattern_RendersInlineError_InsteadOfPreview()
    {
        var cut = RenderHomeWithXgpSelected();

        cut.Find("#xgpPattern").Input("pos{bogus}");

        Assert.Empty(cut.FindAll("#xgpPreview"));
        Assert.Contains("{bogus}", cut.Find("#xgpOptions .text-danger").TextContent);
        _storage.Verify();
    }

    [Fact]
    public void LegacyPrefix_MigratesToPattern_AndContinuesTheCounter()
    {
        // Pre-pattern persistence: prefix "quiz", last exported number 42,
        // no xg_xgpPattern key. The migrated pattern must be "quiz{n}" AND
        // count as the recorded pattern, so the counter continues at 43. The
        // migration writes the numbering record and removes the keys it read.
        var cut = RenderHomeWithXgpSelected(new()
        {
            [HomeStoragePlan.XgpPrefixKey] = "quiz",
            [HomeStoragePlan.XgpLastNumberKey] = "42",
        }, HomeStoragePlan.NumberingDocument("quiz{n}", 42, 3));

        Assert.Equal("quiz{n}", cut.Find("#xgpPattern").GetAttribute("value"));
        var options = cut.FindComponent<WebModePanel>().Instance.XgpOptions;
        Assert.Equal("quiz{n}", options.NamePattern);
        Assert.Equal(43, options.StartNumber);
        _storage.Verify();
    }

    [Fact]
    public void LegacyPrefixTheGrammarRejects_FallsBackToTheDefaultPattern()
    {
        // Legacy prefixes could contain braces, which the pattern grammar
        // reserves — the migration must fall back rather than produce a
        // permanently invalid pattern.
        var cut = RenderHomeWithXgpSelected(new()
        {
            [HomeStoragePlan.XgpPrefixKey] = "qu{iz",
            [HomeStoragePlan.XgpLastNumberKey] = "42",
        }, HomeStoragePlan.NumberingDocument("pos{n}", 42, 3));

        var options = cut.FindComponent<WebModePanel>().Instance.XgpOptions;
        Assert.Equal(XgpExportOptions.DefaultNamePattern, options.NamePattern);
        // The fallback counts as the recorded pattern too — the counter
        // still continues rather than resetting underneath the user.
        Assert.Equal(43, options.StartNumber);
        _storage.Verify();
    }

    [Fact]
    public void TheThreeKeys_MigrateToTheRecord_AndAreRemovedOnceItIsStored()
    {
        var cut = RenderHomeWithXgpSelected(new()
        {
            [HomeStoragePlan.XgpPatternKey] = "m{n}",
            [HomeStoragePlan.XgpLastNumberKey] = "7",
            [HomeStoragePlan.XgpSuffixLengthKey] = "2",
        }, HomeStoragePlan.NumberingDocument("m{n}", 7, 2));

        var options = cut.FindComponent<WebModePanel>().Instance.XgpOptions;
        Assert.Equal(new XgpExportOptions { NamePattern = "m{n}", StartNumber = 8, SuffixLength = 2 }, options);
        _storage.Verify();
    }

    /// <summary>
    /// A stored record is what the page starts from: the keys it replaced are
    /// not read again, whatever they still hold.
    /// </summary>
    [Fact]
    public void AStoredRecord_IsReadAlone()
    {
        var cut = RenderHomeWithXgpSelected(new()
        {
            [HomeStoragePlan.NumberingKey] = HomeStoragePlan.NumberingDocument("r{n}", 12, 4),
        });

        var options = cut.FindComponent<WebModePanel>().Instance.XgpOptions;
        Assert.Equal(new XgpExportOptions { NamePattern = "r{n}", StartNumber = 13, SuffixLength = 4 }, options);
        _storage.Verify();
    }

    [Fact]
    public async Task Export_RecordsTheNumbering_AsOneDocument_InOneWrite()
    {
        var cut = RenderHomeWithXgpSelected();
        var webPanel = cut.FindComponent<WebModePanel>();
        _storage.ExpectNumberingCheckpoint(
            BrowserStorageReadAnswer.Absent, HomeStoragePlan.NumberingDocument("pos{n}", 5, 3));

        await cut.InvokeAsync(() => webPanel.Instance.OnXgpExported.InvokeAsync(
            new XgpExportCommit(webPanel.Instance.XgpOptions, 5)));

        Assert.Equal(6, webPanel.Instance.XgpOptions.StartNumber);
        _storage.Verify();
    }
}
