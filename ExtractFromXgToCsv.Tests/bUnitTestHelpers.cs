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
/// <see cref="ExtractFromXgToCsv.Controllers.AppModeController"/>. Returns
/// 404 for everything else so a stray request fails loudly rather than
/// hanging.
/// </summary>
internal sealed class StubAppModeHandler(string mode) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath == "/api/appmode")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"Mode\":\"{mode}\"}}",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
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
