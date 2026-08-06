using System.Net;
using ExtractFromXgToCsv.Client.Services;
using XgFilter_Razor;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Unit tests for <see cref="HttpFilterDocumentStorage"/> — the client half of
/// the saved-filters relay. The producer contract under test: absent is the
/// 204 → null mapping (a value, never an exception); everything that means
/// "the I/O failed" is wrapped in <see cref="FilterStorageException"/> so the
/// composite's store degrades instead of faulting; a call with no current
/// folder is an adapter-contract bug and propagates unwrapped.
/// </summary>
public class HttpFilterDocumentStorageTests
{
    private sealed class CannedHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpFilterDocumentStorage Create(
        CannedHandler handler, string? folder = @"D:\xg\matches") =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            () => folder);

    private static HttpResponseMessage Response(HttpStatusCode code, string? body = null) =>
        new(code)
        {
            Content = new StringContent(body ?? string.Empty),
        };

    [Fact]
    public async Task ReadAsync_Success_ReturnsBody_AndAddressesTheLiveFolder()
    {
        var handler = new CannedHandler(_ => Response(HttpStatusCode.OK, "{\"doc\":1}"));
        var storage = Create(handler, @"D:\xg files\matches");

        var content = await storage.ReadAsync("xg-filters.json");

        Assert.Equal("{\"doc\":1}", content);
        // Folder and name travel escaped in the query — the folder read from
        // the delegate at call time, the name verbatim from the caller.
        Assert.Equal(
            "/api/filterdocument?folder=D%3A%5Cxg%20files%5Cmatches&name=xg-filters.json",
            handler.LastRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task ReadAsync_NoContent_MapsAbsentToNull()
    {
        var storage = Create(new CannedHandler(_ => Response(HttpStatusCode.NoContent)));
        Assert.Null(await storage.ReadAsync("xg-filters.json"));
    }

    [Fact]
    public async Task ReadAsync_NonSuccess_WrapsInFilterStorageException()
    {
        var storage = Create(new CannedHandler(_ => Response(HttpStatusCode.InternalServerError)));
        await Assert.ThrowsAsync<FilterStorageException>(
            () => storage.ReadAsync("xg-filters.json"));
    }

    [Fact]
    public async Task ReadAsync_NetworkFailure_WrapsInFilterStorageException()
    {
        var storage = Create(new CannedHandler(_ => throw new HttpRequestException("down")));
        var ex = await Assert.ThrowsAsync<FilterStorageException>(
            () => storage.ReadAsync("xg-filters.json"));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task WriteAsync_Success_PutsBodyToTheLiveFolder()
    {
        string? sentBody = null;
        var handler = new CannedHandler(r =>
        {
            sentBody = r.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Response(HttpStatusCode.OK);
        });
        var storage = Create(handler);

        await storage.WriteAsync("xg-filters.json", "{\"doc\":2}");

        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Equal("{\"doc\":2}", sentBody);
    }

    [Fact]
    public async Task WriteAsync_NonSuccess_WrapsInFilterStorageException()
    {
        var storage = Create(new CannedHandler(_ => Response(HttpStatusCode.InternalServerError)));
        await Assert.ThrowsAsync<FilterStorageException>(
            () => storage.WriteAsync("xg-filters.json", "{}"));
    }

    [Fact]
    public async Task NoCurrentFolder_IsAnAdapterContractBug_AndPropagatesUnwrapped()
    {
        // The host passes Storage = null while the folder is blank, so the
        // composite never calls here — a call anyway is a bug, and must not
        // masquerade as an IO degrade.
        var storage = Create(new CannedHandler(_ => Response(HttpStatusCode.OK)), folder: null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.ReadAsync("xg-filters.json"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.WriteAsync("xg-filters.json", "{}"));
    }
}
