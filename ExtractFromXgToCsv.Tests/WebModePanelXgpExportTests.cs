using System.IO.Compression;
using Bunit;
using ExtractFromXgToCsv.Client;
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
/// <c>OnXgpExported</c> raised with the download's commit. Real fixture bytes
/// travel the whole chain; only the JS boundary is faked (bUnit JSInterop).
/// </summary>
public class WebModePanelXgpExportTests : BunitContext
{
    private const string XgFixture = "MTCH4064.xg";

    public WebModePanelXgpExportTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    /// <summary>
    /// The panel raises the export's start to its host as an XGP download's
    /// attempt begins — once, before anything is built or handed over — and
    /// for no other format: from it the host's controls know which of the
    /// user's edits came after the export started. A click that begins no
    /// attempt raises nothing (<see cref="WebModePanelDownloadTests"/>).
    /// </summary>
    [Theory]
    [InlineData(OutputFormat.Xgp)]
    [InlineData(OutputFormat.Csv)]
    [InlineData(OutputFormat.DiagramJson)]
    public async Task AnAttemptBeginning_RaisesTheExportsStart_ForXgpAlone_BeforeAnythingIsHandedOver(OutputFormat format)
    {
        var handedOverWhenRaised = new List<int>();
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, format)
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.OnXgpExportStarting, () => handedOverWhenRaised.Add(JSInterop.Invocations["downloadFile"].Count))
            .Add(c => c.OnXgpExported, (XgpExportCommit _) => { }));
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(XgFixture), XgFixture));
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled")));

        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(JSInterop.Invocations["downloadFile"]));

        int[] expected = format == OutputFormat.Xgp ? [0] : [];
        Assert.Equal(expected, handedOverWhenRaised);
    }

    [Fact]
    public async Task SelectExportDownload_InvokesDownloadFileWithZip_AndRaisesOnXgpExported()
    {
        XgpExportCommit? commit = null;
        // The default pattern — pins that "pos{n}" still names entries
        // byte-identically to the pre-pattern prefix naming.
        var options = new XgpExportOptions
        {
            NamePattern = "pos{n}",
            StartNumber = 1,
            SuffixLength = 3,
        };

        // Established flow: select files first, then Apply — filtering runs
        // in OnParametersSet when FilterInEffect changes.
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Xgp)
            .Add(c => c.FilterInEffect, (FilterConfig?)null)
            .Add(c => c.XgpOptions, options)
            .Add(c => c.OnXgpExported, (XgpExportCommit c) => commit = c));

        var fixtureBytes = FixtureHelper.ReadFixture(XgFixture);
        // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(fixtureBytes, XgFixture));

        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig()));

        var expectedCount = cut.Instance.RowCache.FilteredRows.Count;
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

        Assert.Equal(new XgpExportCommit(options, expectedCount), commit);
    }

    [Fact]
    public async Task SelectExportDownload_WithAnonymize_ProducesZipOfAnonymizedPositions()
    {
        // Same chain as above, but XgpAnonymize=true — the flag must ride the
        // panel -> BuildXgpArchive seam and every zip entry re-reads named by
        // decision role.
        var options = new XgpExportOptions
        {
            NamePattern = "pos{n}",
            StartNumber = 1,
            SuffixLength = 3,
        };

        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Xgp)
            .Add(c => c.FilterInEffect, (FilterConfig?)null)
            .Add(c => c.XgpOptions, options)
            .Add(c => c.XgpAnonymize, true));

        // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(XgFixture), XgFixture));
        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig()));

        var expectedCount = cut.Instance.RowCache.FilteredRows.Count;
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
            XgpAnonymizeAssert.IsRoleAnonymized(ms.ToArray(), entry.FullName);
        }
    }

    [Fact]
    public void DownloadButton_DisabledWhileXgpOptionsInvalid()
    {
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Xgp)
            .Add(c => c.FilterInEffect, (FilterConfig?)null)
            .Add(c => c.XgpOptions, new XgpExportOptions { NamePattern = "a/b" }));

        // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(XgFixture), XgFixture));
        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig()));

        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));

        // Valid options re-enable it — the gate is the options, not the data.
        cut.Render(p => p.Add(c => c.XgpOptions, new XgpExportOptions()));
        Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }
}
