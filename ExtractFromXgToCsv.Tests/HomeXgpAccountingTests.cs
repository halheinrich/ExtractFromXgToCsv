using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using XgFilter_Razor;
using XgFilter_Razor.TestSupport;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Local mode's committed accounting reaching Home (halheinrich/backgammon#374,
/// <c>SPEC-notices.md</c> §7.1–§7.2): the commit an XGP run's terminal
/// snapshot carries — completed, cancelled or failed — is handed to Home's
/// one accounting handler, which persists the numbering from the options the
/// run captured and never overwrites a newer choice in its controls.
/// <para>
/// Storage is planned: what Home persists is the subject. Each run goes
/// through the real page, the real panel and a stub server answering the
/// terminal snapshot the job runner would build.
/// </para>
/// </summary>
public class HomeXgpAccountingTests : BunitContext
{
    private const string Folder = @"D:\xg\matches";
    private const string OutputFolder = @"D:\xg\positions";

    private static readonly XgpExportOptions DefaultOptions = new();

    private BrowserStoragePlan Storage { get; set; } = default!;

    private CapturingProcessHandler ServerAnswering(ProcessingProgress terminal)
    {
        var processing = new CapturingProcessHandler(CapturingProcessHandler.Terminal(terminal));
        this.AddHomeServices("Local", processing);
        Storage = BrowserStoragePlan.On(JSInterop);
        Storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        Storage.ExpectHomeRestore(new()
        {
            [HomeStoragePlan.FolderPathKey] = Folder,
            [HomeStoragePlan.OutputFormatKey] = nameof(OutputFormat.Xgp),
        });
        Storage.ExpectLocalPanelRestore(BrowserStorageReadAnswer.Stored(OutputFolder));
        Storage.ExpectFilterPanelMount();
        return processing;
    }

    /// <summary>The writes Home's handler makes for an export ending at <paramref name="lastNumber"/>.</summary>
    private void ExpectTheNumberingPersisted(string pattern, int lastNumber, int suffixLength)
    {
        Storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpPatternKey, pattern,
            BrowserStorageWriteAnswer.Succeeded);
        Storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpLastNumberKey, lastNumber.ToString(),
            BrowserStorageWriteAnswer.Succeeded);
        Storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpSuffixLengthKey, suffixLength.ToString(),
            BrowserStorageWriteAnswer.Succeeded);
        Storage.ExpectRemove(BrowserStorageArea.Local, HomeStoragePlan.XgpPrefixKey,
            BrowserStorageWriteAnswer.Succeeded);
    }

    private static XgpExportOptions PanelOptions(IRenderedComponent<Home> cut) =>
        cut.FindComponent<LocalModePanel>().Instance.XgpOptions;

    /// <summary>
    /// The accounting case: a decision written, then a destination failure
    /// in the same file. The run failed — the panel shows its error — and the
    /// one file it wrote still advances the numbering. Before this leg the
    /// panel accounted only for a run without an error.
    /// </summary>
    [Fact]
    public async Task AFailedRun_AccountsForTheFileItWroteBeforeTheFailure()
    {
        ServerAnswering(new ProcessingProgress
        {
            Complete = true,
            ErrorMessage = "Access to the path 'pos002.xgp' is denied.",
            TotalRows = 1,
            XgpCommit = new XgpExportCommit(DefaultOptions, 1),
        });
        var cut = this.RenderHome();
        ExpectTheNumberingPersisted("pos{n}", lastNumber: 1, suffixLength: 3);

        await cut.RunButton().ClickAsync(new MouseEventArgs()).WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("✗ Error", cut.Markup);
        Assert.Equal(2, PanelOptions(cut).StartNumber);
        Storage.Verify();
    }

    [Fact]
    public async Task ACancelledRun_AccountsForTheFilesItWrote()
    {
        ServerAnswering(new ProcessingProgress
        {
            Current = 2,
            Total = 5,
            Complete = true,
            Cancelled = true,
            TotalRows = 3,
            XgpCommit = new XgpExportCommit(DefaultOptions, 3),
        });
        var cut = this.RenderHome();
        ExpectTheNumberingPersisted("pos{n}", lastNumber: 3, suffixLength: 3);

        await cut.RunButton().ClickAsync(new MouseEventArgs()).WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("Stopped", cut.Markup);
        Assert.Equal(4, PanelOptions(cut).StartNumber);
        Storage.Verify();
    }

    /// <summary>
    /// A control changed while the run ran: the user types a new next number.
    /// What the run committed is persisted from the options it captured — its
    /// pattern and its numbers — and the newer choice in the control stands.
    /// Before this leg Home computed the persisted number from whatever the
    /// controls showed when the run ended, and then overwrote them.
    /// </summary>
    [Fact]
    public async Task AControlChangedWhileTheRunRan_StandsAndTheRunsNumberingIsPersisted()
    {
        var processing = ServerAnswering(new ProcessingProgress
        {
            Complete = true,
            TotalRows = 2,
            XgpCommit = new XgpExportCommit(DefaultOptions, 2),
        });
        var cut = this.RenderHome();
        processing.HoldStatus();
        var run = cut.RunButton().ClickAsync(new MouseEventArgs());
        await processing.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        await cut.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "50" });
        Assert.Equal(50, PanelOptions(cut).StartNumber);

        ExpectTheNumberingPersisted("pos{n}", lastNumber: 2, suffixLength: 3);
        processing.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Equal(50, PanelOptions(cut).StartNumber);

        // And what was persisted is the run's: returning to its pattern
        // continues after the run's last number, not after the control's.
        await cut.Find("#xgpPattern").InputAsync(new ChangeEventArgs { Value = "other{n}" });
        await cut.Find("#xgpPattern").InputAsync(new ChangeEventArgs { Value = "pos{n}" });
        Assert.Equal(3, PanelOptions(cut).StartNumber);
        Storage.Verify();
    }
}
