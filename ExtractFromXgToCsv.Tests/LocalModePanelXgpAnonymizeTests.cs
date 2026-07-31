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
/// is faked (<see cref="CapturingProcessHandler"/>); the panel's real
/// request-building runs. <see cref="LocalModePanelFilterWireTests"/> is the
/// sibling covering the same POST's <see cref="ProcessRequest.Filters"/> half.
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
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_folderPath", "D:\\xg");
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_outputPath", "D:\\xg\\positions");
        cut.Render();

        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        Assert.NotNull(handler.Captured);
        Assert.Equal(OutputFormat.Xgp, handler.Captured!.OutputFormat);
        Assert.Equal(anonymize, handler.Captured.Anonymize);
    }
}
