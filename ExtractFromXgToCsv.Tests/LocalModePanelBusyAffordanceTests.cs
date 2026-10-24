using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the two Local-mode progress states that carry no fraction, both of
/// which the determinate bar misreports: the gap before the first status poll
/// lands (the block rendered empty — the click went unacknowledged), and the
/// deck pathway's atomic render (a solid 100% bar beside elapsed/throughput
/// figures frozen at their last per-file values — a finished-looking job for
/// however long the render takes, measured in minutes at corpus scale). Each
/// state is a real run's: a status read held, or a snapshot the stub server
/// answers.
/// </summary>
public class LocalModePanelBusyAffordanceTests : BunitContext
{
    public LocalModePanelBusyAffordanceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    private Task<LocalPanelHost> RenderPanelAsync(CapturingProcessHandler server)
    {
        Services.AddSingleton(new HttpClient(server) { BaseAddress = new Uri("http://localhost/") });
        return LocalPanelHost.RenderReadyAsync(this, OutputFormat.Pptx);
    }

    /// <summary>
    /// A snapshot mid-render: every file read (so the counter reads 100%) and
    /// the timings stopped a moment after the run began.
    /// </summary>
    private static ProcessingProgress RenderingSnapshot(JobPhase phase) => new()
    {
        Current = 40,
        Total = 40,
        Phase = phase,
        FileName = "Rendering PPTX (120 decisions, 240 slides)…",
        TotalRows = 120,
        ElapsedSec = 1.2,
        FilesPerSec = 33,
    };

    /// <summary>
    /// A panel showing <paramref name="snapshot"/> as its running job's
    /// progress: the server answers it to the first status read and holds the
    /// second. Release the server to end the run.
    /// </summary>
    private async Task<(IRenderedComponent<LocalModePanel> Panel, Task Run, CapturingProcessHandler Server)> ShowingAsync(
        ProcessingProgress snapshot)
    {
        var server = new CapturingProcessHandler(snapshot, snapshot with { Complete = true });
        server.HoldPoll(2);
        var host = await RenderPanelAsync(server);
        var run = host.ClickRunAsync();
        await server.HeldPollReached.WaitAsync(CapturingProcessHandler.RunTimeout);
        host.Panel.WaitForAssertion(() => Assert.Empty(host.Panel.FindAll("#startingNotice")));
        return (host.Panel, run, server);
    }

    [Fact]
    public async Task BusyBeforeTheFirstPoll_RendersAnIndeterminateStartingState()
    {
        var server = new CapturingProcessHandler();
        server.HoldStatus();
        var host = await RenderPanelAsync(server);
        var cut = host.Panel;
        var run = host.ClickRunAsync();
        await server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        var bar = cut.Find("div.progress-bar");
        Assert.Contains("progress-bar-animated", bar.ClassName);
        Assert.Contains("Starting…", cut.Find("#startingNotice").TextContent);

        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);
    }

    [Fact]
    public async Task RenderingPhase_RendersAnIndeterminateBarWithoutTheStaleFigures()
    {
        var (cut, run, server) = await ShowingAsync(RenderingSnapshot(JobPhase.Rendering));

        var bar = cut.Find("div.progress-bar");
        Assert.Contains("progress-bar-animated", bar.ClassName);

        // No fraction is claimed for a phase that has none...
        Assert.DoesNotContain("100%", bar.TextContent);
        // ...and the frozen throughput/elapsed pair is suppressed rather than
        // presented as live.
        Assert.DoesNotContain("files/sec", cut.Markup);

        // The render takes the run's token, and the notice says Stop still
        // works (Hal's wording, chat, 2026-10-24).
        var notice = System.Text.RegularExpressions.Regex.Replace(cut.Find("#renderingNotice").TextContent, @"\s+", " ").Trim();
        Assert.Equal(
            "Rendering PPTX (120 decisions, 240 slides)… — 120 rows collected. This step can't report progress, but Stop still works.",
            notice);

        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);
    }

    /// <summary>
    /// The same snapshot, differing only in its phase, keeps the determinate
    /// bar and its figures. Pins that the branch keys on the typed
    /// discriminator and not on <see cref="ProcessingProgress.FileName"/>'s
    /// text — the string is presentation, and re-deriving the phase from it
    /// would put the fact in two places.
    /// </summary>
    [Fact]
    public async Task ProcessingPhase_KeepsTheDeterminateBarEvenWithARenderingFileName()
    {
        var (cut, run, server) = await ShowingAsync(RenderingSnapshot(JobPhase.Processing));

        var bar = cut.Find("div.progress-bar");
        Assert.DoesNotContain("progress-bar-animated", bar.ClassName);
        Assert.Contains("100%", bar.TextContent);
        Assert.Contains("files/sec", cut.Markup);

        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);
    }

    /// <summary>
    /// A terminal snapshot keeps the Done line whatever phase it carries — the
    /// indeterminate branch must not swallow the completion state.
    /// </summary>
    [Fact]
    public async Task TerminalSnapshot_StillRendersDone()
    {
        var done = RenderingSnapshot(JobPhase.Rendering) with { Complete = true, FileName = "Done" };
        var host = await RenderPanelAsync(new CapturingProcessHandler(done));
        var cut = host.Panel;

        await host.RunToCompletionAsync();

        Assert.Contains("Done", cut.Find("span.text-success").TextContent);
        Assert.Empty(cut.FindAll("#renderingNotice"));
    }
}
