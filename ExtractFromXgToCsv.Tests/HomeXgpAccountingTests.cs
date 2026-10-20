using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Razor;
using XgFilter_Razor.TestSupport;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Committed accounting reaching Home's numbering record
/// (halheinrich/backgammon#374 and halheinrich/backgammon#397, <c>SPEC-notices.md</c> §7.1, §7.2
/// and §7.5): a Local XGP run's ended ledger entry — completed, cancelled or
/// failed — is applied in one checkpoint, from the options the run captured,
/// and the controls follow it control by control, so a newer choice in them
/// stands.
/// <para>
/// Storage is planned: what Home records is the subject. Each run goes
/// through the real page, the real panel and a stub server answering the
/// terminal snapshot the job runner would build; the job's identity is the
/// test's (<see cref="SequentialJobIdentities"/>), so the owed job's place in
/// the record can be stated.
/// </para>
/// </summary>
public class HomeXgpAccountingTests : BunitContext
{
    private const string Folder = @"D:\xg\matches";
    private const string OutputFolder = @"D:\xg\positions";
    private const string Instance = CapturingProcessHandler.Instance;

    private static readonly XgpExportOptions DefaultOptions = new();

    private BrowserStoragePlan Storage { get; set; } = default!;

    private CapturingProcessHandler ServerAnswering(ProcessingProgress terminal)
    {
        var processing = new CapturingProcessHandler(CapturingProcessHandler.Terminal(terminal));
        this.AddHomeServices(processing);
        Storage = BrowserStoragePlan.On(JSInterop);
        Storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        Storage.ExpectHomeRestore(new()
        {
            [HomeStoragePlan.FolderPathKey] = Folder,
            [HomeStoragePlan.OutputFormatKey] = nameof(OutputFormat.Xgp),
        });
        Storage.ExpectLocalPanelRestore(BrowserStorageReadAnswer.Stored(OutputFolder));
        Storage.ExpectFilterPanelMount();
        Storage.ExpectLocalReconcile();
        return processing;
    }

    /// <summary>
    /// The record's part in the page's first Local XGP run, <c>job-1</c>,
    /// started under the default options: owed before its start, then its
    /// entry applied — the numbering ending at <paramref name="lastNumber"/>,
    /// or none for an export that committed nothing, and the mark at the
    /// instance's first entry.
    /// </summary>
    private void ExpectTheRunsBookkeeping(int? lastNumber)
    {
        var owing = "{\"version\":1,\"owed\":["
            + HomeStoragePlan.OwedJob("job-1", Instance, "pos{n}", 1, 3, OutputFolder) + "]}";
        var applied = lastNumber is { } last
            ? HomeStoragePlan.NumberingDocument("pos{n}", last, 3, (Instance, 1))
            : $"{{\"version\":1,\"mark\":{{\"instance\":\"{Instance}\",\"number\":1}},\"owed\":[]}}";
        Storage.ExpectXgpRunBookkeeping(before: null, owing, applied);
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
        ExpectTheRunsBookkeeping(lastNumber: 1);

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
        ExpectTheRunsBookkeeping(lastNumber: 3);

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
        ExpectTheRunsBookkeeping(lastNumber: 2);
        processing.HoldStatus();
        var run = cut.RunButton().ClickAsync(new MouseEventArgs());
        await processing.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        await cut.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "50" });
        Assert.Equal(50, PanelOptions(cut).StartNumber);

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

    /// <summary>
    /// The control-by-control rule's other half: the user changes only the
    /// number length while the run runs. That says nothing about which number
    /// comes next, so the next number still follows the run. Before the
    /// correction Home advanced it only while all three controls equalled the
    /// run's options, so a length change left "next number" on 1 — a number
    /// the run had used — and setting the length back made the next export
    /// reuse pos001.
    /// </summary>
    [Fact]
    public async Task ALengthChangeWhileTheRunRan_StillAdvancesTheNextNumber()
    {
        var processing = ServerAnswering(new ProcessingProgress
        {
            Complete = true,
            TotalRows = 2,
            XgpCommit = new XgpExportCommit(DefaultOptions, 2),
        });
        var cut = this.RenderHome();
        ExpectTheRunsBookkeeping(lastNumber: 2);
        processing.HoldStatus();
        var run = cut.RunButton().ClickAsync(new MouseEventArgs());
        await processing.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        await cut.Find("#xgpSuffixLength").InputAsync(new ChangeEventArgs { Value = "4" });

        processing.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Equal(new XgpExportOptions { StartNumber = 3, SuffixLength = 4 }, PanelOptions(cut));

        // Back to the run's length: the next export continues after the run.
        await cut.Find("#xgpSuffixLength").InputAsync(new ChangeEventArgs { Value = "3" });
        Assert.Equal(new XgpExportOptions { StartNumber = 3, SuffixLength = 3 }, PanelOptions(cut));
        Storage.Verify();
    }

    /// <summary>
    /// The zero-count guard: an export that committed nothing changes no
    /// number. Its entry is still applied — the mark moves to it and the job
    /// is no longer owed, in the one checkpoint — but the record holds no
    /// numbering, and the controls stand.
    /// </summary>
    [Fact]
    public async Task ARunThatCommittedNothing_ChangesNoNumber()
    {
        ServerAnswering(new ProcessingProgress
        {
            Complete = true,
            ErrorMessage = "pos001.xgp could not be written.",
            XgpCommit = new XgpExportCommit(DefaultOptions, 0),
        });
        var cut = this.RenderHome();
        ExpectTheRunsBookkeeping(lastNumber: null);

        await cut.RunButton().ClickAsync(new MouseEventArgs()).WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("✗ Error", cut.Markup);
        Assert.Equal(DefaultOptions, PanelOptions(cut));
        Storage.Verify();
    }
}
