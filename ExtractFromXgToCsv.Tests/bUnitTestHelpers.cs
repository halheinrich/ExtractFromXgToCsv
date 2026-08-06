using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Shared helpers for bUnit-based component tests. Kept narrow on purpose:
/// reflection accessors for private state (used to pin the name allocator's
/// bookkeeping, and to seed panel fields normally hydrated from localStorage,
/// without exposing either as a permanent seam) and the minimal
/// HttpMessageHandlers the panels' own endpoints need.
/// </summary>
internal static class bUnitTestHelpers
{
    public static T GetPrivateField<T>(object instance, string fieldName) =>
        (T)PrivateField(instance, fieldName).GetValue(instance)!;

    public static void SetPrivateField(object instance, string fieldName, object? value) =>
        PrivateField(instance, fieldName).SetValue(instance, value);

    private static FieldInfo PrivateField(object instance, string fieldName) =>
        instance.GetType().GetField(
            fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"Field '{fieldName}' not found on {instance.GetType().Name}.");
}

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> stub. Returns the
/// <see cref="ExtractFromXgToCsv.Client.Shared.AppModeResponse"/> JSON
/// object shape for <c>/api/appmode</c>, matching the real server's
/// <see cref="ExtractFromXgToCsv.Controllers.AppModeController"/>, and an
/// in-memory edition of the saved-filters file relay
/// (<c>GET</c>/<c>PUT /api/filterdocument</c>, matching
/// <see cref="ExtractFromXgToCsv.Controllers.FilterDocumentController"/>'s
/// 200/204 contract) — Home hosts a <c>FilterSurface</c> whose store reads
/// the document at mount and on every source change, so a Local-mode Home
/// test without these routes would degrade to LoadFailed. Returns 404 for
/// everything else so a stray request fails loudly rather than hanging.
/// </summary>
internal sealed class StubAppModeHandler(string mode) : HttpMessageHandler
{
    /// <summary>
    /// The in-memory documents served and updated by the filterdocument
    /// routes, keyed by (folder, name) exactly as the query names them.
    /// Seed before rendering to give a folder a saved-filters document.
    /// </summary>
    public Dictionary<(string Folder, string Name), string> Documents { get; } = new();

    /// <summary>Every filterdocument read, in order — (folder, name).</summary>
    public List<(string Folder, string Name)> DocumentReads { get; } = new();

    /// <summary>Every filterdocument write, in order — (folder, name, content).</summary>
    public List<(string Folder, string Name, string Content)> DocumentWrites { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == "/api/appmode")
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"Mode\":\"{mode}\"}}",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
        }

        if (path == "/api/filterdocument")
        {
            var query = ParseQuery(request.RequestUri!);
            var key = (query["folder"], query["name"]);

            if (request.Method == HttpMethod.Get)
            {
                DocumentReads.Add(key);
                return Documents.TryGetValue(key, out var content)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(content, Encoding.UTF8, "text/plain"),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Put)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Documents[key] = body;
                DocumentWrites.Add((key.Item1, key.Item2, body));
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                kv => Uri.UnescapeDataString(kv[0]),
                kv => kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty);
}

/// <summary>
/// Fakes the Local-mode processing endpoints so <c>LocalModePanel.RunLocalAsync</c>
/// runs its real request-building and polling loop against an in-memory server:
/// captures the <see cref="ProcessRequest"/> POSTed to <c>/api/process/start</c>
/// and answers the first status poll with a terminal snapshot so the click
/// resolves in one iteration. Everything else 404s, so a stray request fails
/// loudly rather than hanging.
/// </summary>
/// <remarks>
/// Deserializes with the same case-insensitive options the panel serializes
/// with, standing in for ASP.NET Core's model binding on the real server —
/// so what <see cref="Captured"/> holds is what
/// <c>ProcessController.Start</c> would have bound.
/// </remarks>
internal sealed class CapturingProcessHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The last request POSTed to <c>/api/process/start</c>; null until one is.</summary>
    public ProcessRequest? Captured { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (request.Method == HttpMethod.Post && path == "/api/process/start")
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            Captured = JsonSerializer.Deserialize<ProcessRequest>(json, _opts);
            return Json("{\"jobId\":\"j1\"}");
        }

        if (request.Method == HttpMethod.Get && path == "/api/process/j1/status")
            return Json("{\"complete\":true,\"totalRows\":3}");

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
