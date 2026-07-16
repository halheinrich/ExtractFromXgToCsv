using System.IO.Compression;
using Bunit;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wire test for the Web-mode .xgp export chain:
/// file selection → filter apply → Download click → <c>downloadFile</c>
/// JS interop invoked with a zip of per-decision .xgp entries →
/// <c>OnXgpExported</c> raised with the exported count. Real fixture bytes
/// travel the whole chain; only the JS boundary is faked (bUnit JSInterop).
/// </summary>
public class WebModePanelXgpExportTests : BunitContext
{
    private const string XgFixture = "MTCH4064.xg";

    public WebModePanelXgpExportTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
    }

    [Fact]
    public async Task SelectExportDownload_InvokesDownloadFileWithZip_AndRaisesOnXgpExported()
    {
        int exportedCount = -1;
        // The default pattern — pins that "pos{n}" still names entries
        // byte-identically to the pre-pattern prefix naming.
        var options = new XgpExportOptions
        {
            NamePattern = "pos{n}",
            StartNumber = 1,
            SuffixLength = 3,
        };

        // Established flow: select files first, then Apply — filtering runs
        // in OnParametersSet on the FilterApplied parameter change.
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Xgp)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, false)
            .Add(c => c.FilterDirty, false)
            .Add(c => c.XgpOptions, options)
            .Add(c => c.OnXgpExported, (int n) => exportedCount = n));

        var fixtureBytes = FixtureHelper.ReadFixture(XgFixture);
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(fixtureBytes, XgFixture));

        cut.Render(p => p.Add(c => c.FilterApplied, true));

        var expectedCount = bUnitTestHelpers
            .GetPrivateField<List<BgDataTypes_Lib.DecisionRow>>(cut.Instance, "_filteredRows")
            .Count;
        Assert.True(expectedCount > 0, "fixture should yield filtered decisions");

        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        var invocation = JSInterop.VerifyInvoke("downloadFile");
        Assert.Equal(3, invocation.Arguments.Count);
        Assert.EndsWith(".zip", (string)invocation.Arguments[0]!);
        Assert.Equal("application/zip", (string)invocation.Arguments[1]!);

        var zipBytes = (byte[])invocation.Arguments[2]!;
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        Assert.Equal(expectedCount, zip.Entries.Count);
        Assert.Equal("pos001.xgp", zip.Entries[0].FullName);

        Assert.Equal(expectedCount, exportedCount);
    }

    [Fact]
    public async Task SelectExportDownload_WithAnonymize_ProducesZipOfAnonymizedPositions()
    {
        // Same chain as above, but XgpAnonymize=true — the flag must ride the
        // panel -> BuildXgpZip seam and every zip entry re-reads with the
        // neutral names.
        var options = new XgpExportOptions
        {
            NamePattern = "pos{n}",
            StartNumber = 1,
            SuffixLength = 3,
        };

        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Xgp)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, false)
            .Add(c => c.FilterDirty, false)
            .Add(c => c.XgpOptions, options)
            .Add(c => c.XgpAnonymize, true));

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(XgFixture), XgFixture));
        cut.Render(p => p.Add(c => c.FilterApplied, true));

        var expectedCount = bUnitTestHelpers
            .GetPrivateField<List<BgDataTypes_Lib.DecisionRow>>(cut.Instance, "_filteredRows")
            .Count;
        Assert.True(expectedCount > 0, "fixture should yield filtered decisions");

        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        var zipBytes = (byte[])JSInterop.VerifyInvoke("downloadFile").Arguments[2]!;
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        Assert.Equal(expectedCount, zip.Entries.Count);
        foreach (var entry in zip.Entries)
        {
            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            ms.Position = 0;
            var info = XgDecisionIterator.ExtractMatchInfo(XgFileReader.ReadStream(ms))!;
            Assert.Equal("Player 1", info.Player1);
            Assert.Equal("Player 2", info.Player2);
        }
    }

    [Fact]
    public void DownloadButton_DisabledWhileXgpOptionsInvalid()
    {
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Xgp)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, false)
            .Add(c => c.FilterDirty, false)
            .Add(c => c.XgpOptions, new XgpExportOptions { NamePattern = "a/b" }));

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(XgFixture), XgFixture));
        cut.Render(p => p.Add(c => c.FilterApplied, true));

        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));

        // Valid options re-enable it — the gate is the options, not the data.
        cut.Render(p => p.Add(c => c.XgpOptions, new XgpExportOptions()));
        Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }
}
