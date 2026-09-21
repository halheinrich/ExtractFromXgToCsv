using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the delivery chain for the shared <c>Notice</c>'s scoped CSS
/// (halheinrich/backgammon#248) through the real hosted pipeline: the root
/// document links this host's scoped-CSS bundle, the bundle is served and
/// imports BgUiPrimitives_Razor's, and that one is served and carries the
/// rules that make a dismissible notice's text a click target.
/// <para>
/// This host had never linked its bundle — nothing in the repository has
/// scoped CSS of its own — and nothing fails when it is missing: the notice
/// still renders and its close button still dismisses; only the large target
/// is silently gone. bUnit cannot see any of it, as component tests load no
/// stylesheet at all.
/// </para>
/// <para>
/// <b>What this does not pin.</b> It follows the links a browser would follow
/// and reads what is served; it does not execute CSS. That the rules
/// <em>apply</em> — a dismissible box computing <c>cursor: pointer</c>, its
/// content wrapper <c>pointer-events: none</c> — needs a real page load in a
/// real browser, and this repository has no browser-level suite to hold that.
/// </para>
/// </summary>
public class ScopedCssBundleLinkTests
{
    // AppModeService is the entry-assembly marker, for the reason
    // FilterDocumentEndpointTests gives.
    private sealed class WebFactory
        : WebApplicationFactory<ExtractFromXgToCsv.Services.AppModeService>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("AppMode", "Web");
    }

    [Fact]
    public async Task TheRootDocument_DeliversTheNoticesScopedCss()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient();

        // 1 — the root document links this host's bundle.
        var page = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var document = new HtmlParser().ParseDocument(await page.Content.ReadAsStringAsync());
        var bundleHref = Assert.Single(
            document.QuerySelectorAll("head > link[rel=stylesheet]")
                .Select(link => link.GetAttribute("href")!),
            href => Regex.IsMatch(href, @"^ExtractFromXgToCsv(\.[^./]+)?\.styles\.css$"));

        // 2 — the bundle is served, and imports the library's.
        var bundle = await client.GetAsync("/" + bundleHref);
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
        var import = Regex.Match(
            await bundle.Content.ReadAsStringAsync(),
            @"@import\s+'(?<url>_content/BgUiPrimitives_Razor/[^']+\.bundle\.scp\.css)'");
        Assert.True(import.Success, "the host bundle must @import BgUiPrimitives_Razor's scoped CSS");

        // 3 — the library's bundle is served, and carries the Notice's rules.
        var library = await client.GetAsync("/" + import.Groups["url"].Value);
        Assert.Equal(HttpStatusCode.OK, library.StatusCode);
        var css = await library.Content.ReadAsStringAsync();
        Assert.Contains(".alert-dismissible", css);
        Assert.Contains("cursor: pointer", css);
        Assert.Contains("pointer-events: none", css);
    }
}
