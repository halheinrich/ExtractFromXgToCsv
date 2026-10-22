using System.Net;
using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Exit is retired (Hal, 2026-10-22, halheinrich/backgammon#433): the app
/// starts and stops from Visual Studio or the terminal, and a page that stops
/// its own host is not a shape this local tool keeps. Its endpoint,
/// <c>POST /api/shutdown</c>, is gone with the button — pinned over the real
/// host: it is answered exactly as a route that never existed is, and the
/// host goes on serving.
/// </summary>
/// <remarks>
/// Not 404: this host answers any POST to a path it does not map 405, since
/// its page endpoints match every path for GET (a GET of the same path is
/// 404). So the pin compares with a route that never existed rather than
/// naming a status.
/// </remarks>
public class ShutdownEndpointRetiredTests
{
    // JobStore is the entry-assembly marker; see FilterDocumentEndpointTests
    // for why. No opening book: these requests need none.
    private static WebApplicationFactory<JobStore> Host() =>
        new WebApplicationFactory<JobStore>().WithWebHostBuilder(builder => builder.UseSetting("OpeningBookPath", ""));

    [Fact]
    public async Task PostShutdown_IsAnsweredAsARouteThatNeverExisted_AndStopsNothing()
    {
        using var factory = Host();
        using var client = factory.CreateClient();

        using var shutdown = await client.PostAsync("/api/shutdown", content: null);
        using var neverExisted = await client.PostAsync("/api/no-such-endpoint", content: null);
        using var afterwards = await client.GetAsync("/api/openingbook/status");

        Assert.False(shutdown.IsSuccessStatusCode);
        Assert.Equal(neverExisted.StatusCode, shutdown.StatusCode);
        // The host was not stopped: it answers a route it has.
        Assert.Equal(HttpStatusCode.OK, afterwards.StatusCode);
    }
}
