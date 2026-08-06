using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// bUnit tests for the Home-owned XGP name-pattern UI: the pattern textbox
/// binding, the registry-driven insert-token dropdown, the live preview /
/// inline error branch, the one-time <c>xg_xgpPrefix</c> →
/// <c>xg_xgpPattern</c> localStorage migration, and the post-export
/// persistence (write the pattern key, remove the legacy key). Naming
/// mechanics themselves are owned by the engine suites — here only the
/// wiring is pinned.
/// </summary>
public class HomeXgpPatternTests : BunitContext
{
    public HomeXgpPatternTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
        // Home injects the applied-filter holder since the FilterSurface
        // adoption; these tests never touch it, but the page can't render
        // without it registered.
        Services.AddScoped<XgFilter_Razor.AppliedFilter>();
        Services.AddSingleton(new HttpClient(new StubAppModeHandler("Web"))
        {
            BaseAddress = new Uri("http://localhost/"),
        });
    }

    private IRenderedComponent<Home> RenderHomeWithXgpSelected()
    {
        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<WebModePanel>().Any(),
            TimeSpan.FromSeconds(5));
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
    }

    [Fact]
    public void CounterlessPattern_PreviewShowsTheUniquifier()
    {
        var cut = RenderHomeWithXgpSelected();

        cut.Find("#xgpPattern").Input("quiz");

        var preview = cut.Find("#xgpPreview").TextContent;
        Assert.Contains("quiz.xgp", preview);
        Assert.Contains("quiz (2).xgp", preview);
    }

    [Fact]
    public void InvalidPattern_RendersInlineError_InsteadOfPreview()
    {
        var cut = RenderHomeWithXgpSelected();

        cut.Find("#xgpPattern").Input("pos{bogus}");

        Assert.Empty(cut.FindAll("#xgpPreview"));
        Assert.Contains("{bogus}", cut.Find("#xgpOptions .text-danger").TextContent);
    }

    [Fact]
    public void LegacyPrefix_MigratesToPattern_AndContinuesTheCounter()
    {
        // Pre-pattern persistence: prefix "quiz", last exported number 42,
        // no xg_xgpPattern key. The migrated pattern must be "quiz{n}" AND
        // count as the persisted pattern, so the counter continues at 43.
        JSInterop.Setup<string?>("localStorage.getItem", "xg_xgpPrefix").SetResult("quiz");
        JSInterop.Setup<string?>("localStorage.getItem", "xg_xgpLastNumber").SetResult("42");

        var cut = RenderHomeWithXgpSelected();

        Assert.Equal("quiz{n}", cut.Find("#xgpPattern").GetAttribute("value"));
        var options = cut.FindComponent<WebModePanel>().Instance.XgpOptions;
        Assert.Equal("quiz{n}", options.NamePattern);
        Assert.Equal(43, options.StartNumber);
    }

    [Fact]
    public void LegacyPrefixTheGrammarRejects_FallsBackToTheDefaultPattern()
    {
        // Legacy prefixes could contain braces, which the pattern grammar
        // reserves — the migration must fall back rather than produce a
        // permanently invalid pattern.
        JSInterop.Setup<string?>("localStorage.getItem", "xg_xgpPrefix").SetResult("qu{iz");
        JSInterop.Setup<string?>("localStorage.getItem", "xg_xgpLastNumber").SetResult("42");

        var cut = RenderHomeWithXgpSelected();

        var options = cut.FindComponent<WebModePanel>().Instance.XgpOptions;
        Assert.Equal(XgpExportOptions.DefaultNamePattern, options.NamePattern);
        // The fallback counts as the persisted pattern too — the counter
        // still continues rather than resetting underneath the user.
        Assert.Equal(43, options.StartNumber);
    }

    [Fact]
    public async Task Export_PersistsThePattern_AndRemovesTheLegacyPrefixKey()
    {
        var cut = RenderHomeWithXgpSelected();
        var webPanel = cut.FindComponent<WebModePanel>();

        await cut.InvokeAsync(() => webPanel.Instance.OnXgpExported.InvokeAsync(5));

        Assert.Contains(JSInterop.Invocations, i =>
            i.Identifier == "localStorage.setItem"
            && (string?)i.Arguments[0] == "xg_xgpPattern"
            && (string?)i.Arguments[1] == "pos{n}");
        Assert.Contains(JSInterop.Invocations, i =>
            i.Identifier == "localStorage.setItem"
            && (string?)i.Arguments[0] == "xg_xgpLastNumber"
            && (string?)i.Arguments[1] == "5");
        Assert.Contains(JSInterop.Invocations, i =>
            i.Identifier == "localStorage.removeItem"
            && (string?)i.Arguments[0] == "xg_xgpPrefix");
    }
}
