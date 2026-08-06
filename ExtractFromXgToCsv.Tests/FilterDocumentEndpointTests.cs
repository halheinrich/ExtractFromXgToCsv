using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Hosted wire tests for <c>GET</c>/<c>PUT /api/filterdocument</c> through the
/// real pipeline (<see cref="WebApplicationFactory{TEntryPoint}"/> over the
/// server's <c>Program</c>): the parts only hosting can observe — the
/// Local-mode action guard answering an observable 404 under Web config, the
/// shape rejection at the real route, the 204-absent contract, and the write →
/// read round-trip landing on disk. The store's own IO/validation unit
/// contracts live in <see cref="FilterDocumentStoreTests"/>.
/// </summary>
public class FilterDocumentEndpointTests : IDisposable
{
    // AppModeService is the entry-assembly marker (WebApplicationFactory
    // accepts any type from the entry assembly): naming `Program` here would
    // be ambiguous — the Client's top-level entry point is equally visible to
    // this project through its InternalsVisibleTo grant.
    private sealed class ModeFactory(string mode)
        : WebApplicationFactory<ExtractFromXgToCsv.Services.AppModeService>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("AppMode", mode);
    }

    private readonly string _folder =
        Directory.CreateTempSubdirectory("xg-filterdoc-wire-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static string Url(string folder, string name) =>
        $"/api/filterdocument?folder={Uri.EscapeDataString(folder)}&name={Uri.EscapeDataString(name)}";

    [Fact]
    public async Task LocalMode_WriteThenRead_RoundTripsThroughTheRealPipeline()
    {
        using var factory = new ModeFactory("Local");
        using var client = factory.CreateClient();

        var put = await client.PutAsync(
            Url(_folder, "xg-filters.json"),
            new StringContent("{\"doc\":true}"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // The relay wrote the real file — the round-trip is disk-backed, not
        // an in-memory echo.
        Assert.Equal("{\"doc\":true}",
            File.ReadAllText(Path.Combine(_folder, "xg-filters.json")));

        var get = await client.GetAsync(Url(_folder, "xg-filters.json"));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("{\"doc\":true}", await get.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LocalMode_AbsentFile_Returns204()
    {
        using var factory = new ModeFactory("Local");
        using var client = factory.CreateClient();

        var get = await client.GetAsync(Url(_folder, "absent.json"));
        Assert.Equal(HttpStatusCode.NoContent, get.StatusCode);
    }

    [Theory]
    [InlineData("a/b.json")]
    [InlineData(@"..\up.json")]
    [InlineData(@"C:\rooted.json")]
    public async Task LocalMode_NonSimpleFileName_Returns400(string name)
    {
        using var factory = new ModeFactory("Local");
        using var client = factory.CreateClient();

        var get = await client.GetAsync(Url(_folder, name));
        Assert.Equal(HttpStatusCode.BadRequest, get.StatusCode);

        var put = await client.PutAsync(Url(_folder, name), new StringContent("x"));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task WebMode_Endpoints_Answer404()
    {
        // The Local guard is an explicit action guard precisely so this is
        // observable: in Web mode the file relay does not exist — 404, not a
        // container failure — and nothing touches the disk.
        using var factory = new ModeFactory("Web");
        using var client = factory.CreateClient();

        var get = await client.GetAsync(Url(_folder, "xg-filters.json"));
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var put = await client.PutAsync(
            Url(_folder, "xg-filters.json"), new StringContent("x"));
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.False(File.Exists(Path.Combine(_folder, "xg-filters.json")));
    }
}
