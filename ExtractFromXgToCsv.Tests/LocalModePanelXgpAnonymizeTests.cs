using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wire test for the Local-mode anonymize propagation: the coordinator's
/// anonymize choice must ride into the <c>/api/process/start</c> POST body as
/// <see cref="ProcessRequest.Anonymize"/>. Only the HTTP boundary is faked
/// (<see cref="CapturingProcessHandler"/>); the coordinator's real
/// request-building runs. <see cref="LocalModePanelFilterWireTests"/> is the
/// sibling covering the same POST's <see cref="ProcessRequest.Filters"/> half.
/// </summary>
public class LocalModePanelXgpAnonymizeTests : BunitContext
{
    public LocalModePanelXgpAnonymizeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunLocal_PostsXgpAnonymizeFlagInRequestBody(bool anonymize)
    {
        var handler = new CapturingProcessHandler();
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        var host = await LocalPanelHost.RenderReadyAsync(this, OutputFormat.Xgp);
        await host.SetAnonymizeAsync(anonymize);

        await host.ClickRunAsync();

        Assert.NotNull(handler.Captured);
        Assert.Equal(OutputFormat.Xgp, handler.Captured!.OutputFormat);
        Assert.Equal(anonymize, handler.Captured.Anonymize);
    }
}
