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
/// A run's commit reaching Home's next-number control (<c>SPEC-notices.md</c>
/// §7.1, §7.2 and §7.5): a Local XGP run's terminal result — completed,
/// cancelled or failed — carries what it committed, and the control follows
/// it to the number after the last the run used, and remembers it, unless the
/// user has changed the pattern or the next number since the run started. A
/// run that committed nothing moves nothing.
/// <para>
/// Storage is planned: what Home remembers is the subject. Each run goes
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
        return processing;
    }

    private static XgpExportOptions PanelOptions(IRenderedComponent<Home> cut) =>
        cut.FindComponent<LocalModePanel>().Instance.XgpOptions;

    /// <summary>
    /// The accounting case: a decision written, then a destination failure
    /// in the same file. The run failed — the panel shows its error — and the
    /// one file it wrote still advances the next number.
    /// </summary>
    [Fact]
    public async Task AFailedRun_AdvancesTheNextNumberPastTheFileItWroteBeforeTheFailure()
    {
        ServerAnswering(new ProcessingProgress
        {
            Complete = true,
            ErrorMessage = "Access to the path 'pos002.xgp' is denied.",
            TotalRows = 1,
            XgpCommit = new XgpExportCommit(DefaultOptions, 1),
        });
        var cut = this.RenderHome();
        Storage.ExpectNextNumberWrite(2);

        await cut.RunButton().ClickAsync(new MouseEventArgs()).WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("✗ Error", cut.Markup);
        Assert.Equal(2, PanelOptions(cut).StartNumber);
        Storage.Verify();
    }

    [Fact]
    public async Task ACancelledRun_AdvancesTheNextNumberPastTheFilesItWrote()
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
        Storage.ExpectNextNumberWrite(4);

        await cut.RunButton().ClickAsync(new MouseEventArgs()).WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("Stopped", cut.Markup);
        Assert.Equal(4, PanelOptions(cut).StartNumber);
        Storage.Verify();
    }

    /// <summary>
    /// A control changed while the run ran: the user types a new next number,
    /// which is remembered. The run's commit was made under the options it
    /// captured, which the control no longer holds, so the newer choice
    /// stands and nothing more is written. Before the commit path, Home
    /// computed the number from whatever the controls showed when the run
    /// ended, and overwrote them.
    /// </summary>
    [Fact]
    public async Task AControlChangedWhileTheRunRan_Stands()
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

        Storage.ExpectNextNumberWrite(50);
        await cut.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "50" });
        Assert.Equal(50, PanelOptions(cut).StartNumber);

        processing.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Equal(50, PanelOptions(cut).StartNumber);
        Storage.Verify();
    }

    /// <summary>
    /// "Changed since the run started" is the controls' value against the
    /// options the run captured: a next number changed away and back again
    /// while the run ran holds the run's start once more, so the control
    /// follows the run. (The edit clock this replaced kept such a control as
    /// the user's choice; the value rule is the simpler one, said once.)
    /// </summary>
    [Fact]
    public async Task AControlChangedAwayAndBackWhileTheRunRan_Follows()
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

        Storage.ExpectNextNumberWrite(50);
        await cut.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "50" });
        Storage.ExpectNextNumberWrite(1);
        await cut.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "1" });

        Storage.ExpectNextNumberWrite(3);
        processing.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Equal(3, PanelOptions(cut).StartNumber);
        Storage.Verify();
    }

    /// <summary>
    /// The user changes only the number length while the run runs. That says
    /// nothing about which number comes next, so the next number still
    /// follows the run. Before the correction Home advanced it only while all
    /// three controls equalled the run's options, so a length change left
    /// "next number" on 1 — a number the run had used.
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
        processing.HoldStatus();
        var run = cut.RunButton().ClickAsync(new MouseEventArgs());
        await processing.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        Storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpSuffixLengthKey, "4", BrowserStorageWriteAnswer.Succeeded);
        await cut.Find("#xgpSuffixLength").InputAsync(new ChangeEventArgs { Value = "4" });

        Storage.ExpectNextNumberWrite(3);
        processing.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Equal(new XgpExportOptions { StartNumber = 3, SuffixLength = 4 }, PanelOptions(cut));

        // Back to the run's length: the next export continues after the run.
        Storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpSuffixLengthKey, "3", BrowserStorageWriteAnswer.Succeeded);
        await cut.Find("#xgpSuffixLength").InputAsync(new ChangeEventArgs { Value = "3" });
        Assert.Equal(new XgpExportOptions { StartNumber = 3, SuffixLength = 3 }, PanelOptions(cut));
        Storage.Verify();
    }

    /// <summary>
    /// The zero-count guard: an export that committed nothing changes no
    /// number, and writes nothing.
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

        await cut.RunButton().ClickAsync(new MouseEventArgs()).WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("✗ Error", cut.Markup);
        Assert.Equal(DefaultOptions, PanelOptions(cut));
        Storage.Verify();
    }
}
