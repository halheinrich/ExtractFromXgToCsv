using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wire test for the Local-mode anonymize propagation: the Home-owned
/// <c>XgpAnonymize</c> parameter must ride into the <c>/api/process/start</c>
/// POST body as <see cref="ProcessRequest.Anonymize"/>. Only the HTTP boundary
/// is faked (a capturing handler); the panel's real request-building runs.
/// </summary>
public class LocalModePanelXgpAnonymizeTests : BunitContext
{
    public LocalModePanelXgpAnonymizeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunLocal_PostsXgpAnonymizeFlagInRequestBody(bool anonymize)
    {
        var handler = new CapturingProcessHandler();
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });

        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Xgp)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, true)
            .Add(c => c.FilterDirty, false)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, anonymize));

        // Folder/output paths are normally loaded from localStorage; set them
        // directly so the Run button's path gate is satisfied.
        SetPrivateField(cut.Instance, "_folderPath", "D:\\xg");
        SetPrivateField(cut.Instance, "_outputPath", "D:\\xg\\positions");
        cut.Render();

        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        Assert.NotNull(handler.Captured);
        Assert.Equal(OutputFormat.Xgp, handler.Captured!.OutputFormat);
        Assert.Equal(anonymize, handler.Captured.Anonymize);
    }

    private static void SetPrivateField(object instance, string fieldName, object value) =>
        instance.GetType()
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(instance, value);

    /// <summary>
    /// Captures the <see cref="ProcessRequest"/> POSTed to
    /// <c>/api/process/start</c> and drives the panel's polling loop to a
    /// single Complete status so the click resolves cleanly.
    /// </summary>
    private sealed class CapturingProcessHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions _opts = new()
        {
            PropertyNameCaseInsensitive = true,
        };

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
}
