using System.Net;
using System.Reflection;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Shared helpers for bUnit-based component tests. Kept narrow on purpose:
/// reflection accessors for private state (used to pin cache invariants
/// without modifying source for a test seam) and a minimal HttpMessageHandler
/// for the one endpoint Home.razor calls during OnAfterRenderAsync.
/// </summary>
internal static class bUnitTestHelpers
{
    public static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(
            fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null)
            throw new InvalidOperationException(
                $"Field '{fieldName}' not found on {instance.GetType().Name}.");
        return (T)field.GetValue(instance)!;
    }
}

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> stub. Returns a JSON-quoted
/// app-mode string for <c>/api/appmode</c> (matching the real server's
/// <see cref="ExtractFromXgToCsv.Controllers.AppModeController"/> shape:
/// <c>Ok(string)</c> serializes as a JSON string with quotes). Returns
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
                Content = new StringContent($"\"{mode}\""),
            });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
