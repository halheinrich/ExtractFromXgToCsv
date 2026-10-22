using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the output-path key's browser storage, as the Local panel shows it,
/// under a browser that refuses it (halheinrich/backgammon#102, Extract's
/// portion). The panel used to read and write the key unguarded, so a
/// refusing browser threw out of its first render and out of every keystroke
/// in the output box. The coordinator reads and writes it now; each refusal
/// reaches the app's storage condition, and the panel goes on showing the
/// in-memory value. The filter surface is not rendered, so the plan holds
/// only the coordinator's calls.
/// </summary>
public class LocalModePanelStorageTests : BunitContext
{
    private const string OutputPath = @"D:\xg\out.csv";

    private readonly BrowserStoragePlan _storage;

    public LocalModePanelStorageTests()
    {
        Services.AddClientServices();
        // The book-status probe 404s, which the coordinator reads as "unknown".
        Services.AddSingleton(new HttpClient(new CapturingProcessHandler())
        {
            BaseAddress = new Uri("http://localhost/"),
        });
        _storage = BrowserStoragePlan.On(JSInterop);
    }

    private BrowserStorageCondition Condition => Services.GetRequiredService<BrowserStorageCondition>();

    private async Task<IRenderedComponent<LocalModePanel>> RenderPanelAsync() =>
        (await LocalPanelHost.RenderAsync(this, withFilterSurface: false)).Panel;

    /// <summary>The restore, its output-path read answered <paramref name="outputPath"/> and every other read nothing stored.</summary>
    private void ExpectRestore(BrowserStorageReadAnswer outputPath)
    {
        foreach (var key in new[]
                 {
                     HomeStoragePlan.OutputFormatKey, HomeStoragePlan.FolderPathKey, HomeStoragePlan.XgpPatternKey,
                     HomeStoragePlan.XgpNextNumberKey, HomeStoragePlan.XgpSuffixLengthKey, HomeStoragePlan.XgpAnonymizeKey,
                 })
            _storage.ExpectRead(BrowserStorageArea.Local, key, BrowserStorageReadAnswer.Absent);
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.OutputPathKey, outputPath);
    }

    [Fact]
    public async Task RefusedRestore_LeavesTheOutputPathBlank_AndIsReported()
    {
        ExpectRestore(BrowserStorageReadAnswer.Refused);

        var cut = await RenderPanelAsync();

        cut.WaitForAssertion(() => Assert.True(Condition.HasOccurred));
        Assert.Equal(string.Empty, cut.Find("#outputPath").GetAttribute("value"));
        _storage.Verify();
    }

    [Fact]
    public async Task RefusedWrite_KeepsTheTypedOutputPath_AndIsReported()
    {
        ExpectRestore(BrowserStorageReadAnswer.Absent);
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputPathKey, OutputPath,
            BrowserStorageWriteAnswer.Refused);
        var cut = await RenderPanelAsync();
        Assert.False(Condition.HasOccurred);

        await cut.Find("#outputPath").InputAsync(new ChangeEventArgs { Value = OutputPath });

        Assert.Equal(OutputPath, cut.Find("#outputPath").GetAttribute("value"));
        Assert.True(Condition.HasOccurred);
        _storage.Verify();
    }
}
