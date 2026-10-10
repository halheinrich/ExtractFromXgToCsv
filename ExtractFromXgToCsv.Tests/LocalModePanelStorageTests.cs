using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="LocalModePanel"/>'s own browser storage — the output-path
/// key — under a browser that refuses it (halheinrich/backgammon#102,
/// Extract's portion). The panel used to read and write the key unguarded,
/// so a refusing browser threw out of its first render and out of every
/// keystroke in the output box. Each refusal now reaches the app's storage
/// condition, and the panel goes on with its in-memory value.
/// </summary>
public class LocalModePanelStorageTests : BunitContext
{
    private const string OutputPath = @"D:\xg\out.csv";

    private readonly BrowserStoragePlan _storage;

    public LocalModePanelStorageTests()
    {
        Services.AddClientServices();
        // The book-status probe 404s, which the panel reads as "unknown".
        Services.AddSingleton(new HttpClient(new CapturingProcessHandler())
        {
            BaseAddress = new Uri("http://localhost/"),
        });
        _storage = BrowserStoragePlan.On(JSInterop);
    }

    private BrowserStorageCondition Condition => Services.GetRequiredService<BrowserStorageCondition>();

    private IRenderedComponent<LocalModePanel> RenderPanel() =>
        Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

    [Fact]
    public void RefusedRestore_LeavesTheOutputPathBlank_AndIsReported()
    {
        _storage.ExpectRead(BrowserStorageArea.Local, "xg_outputPath", BrowserStorageReadAnswer.Refused);

        var cut = RenderPanel();

        cut.WaitForAssertion(() => Assert.True(Condition.HasOccurred));
        Assert.Equal(string.Empty, cut.Find("#outputPath").GetAttribute("value"));
        _storage.Verify();
    }

    [Fact]
    public void RefusedWrite_KeepsTheTypedOutputPath_AndIsReported()
    {
        _storage.ExpectRead(BrowserStorageArea.Local, "xg_outputPath", BrowserStorageReadAnswer.Absent);
        _storage.ExpectWrite(BrowserStorageArea.Local, "xg_outputPath", OutputPath,
            BrowserStorageWriteAnswer.Refused);
        var cut = RenderPanel();
        Assert.False(Condition.HasOccurred);

        cut.Find("#outputPath").Input(new ChangeEventArgs { Value = OutputPath });

        Assert.Equal(OutputPath, cut.Find("#outputPath").GetAttribute("value"));
        Assert.True(Condition.HasOccurred);
        _storage.Verify();
    }
}
