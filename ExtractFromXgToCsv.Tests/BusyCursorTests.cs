using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the busy-cursor discipline: the panel puts the <c>is-busy</c> marker
/// on its root for exactly as long as a run is in hand, and app.css turns
/// that one class into the cursor.
/// Because the marker rides the fact rather than any particular busy window,
/// a window added later inherits the cursor for free — and these tests are the
/// only place a panel that quietly stopped marking its root would be caught.
/// Each state is reached by a real run against a stub server: a run whose
/// status reads are held is in hand, and a snapshot the server answers is the
/// one on screen.
/// </summary>
/// <remarks>
/// The cursor itself is the OS's to draw and is not observable from a
/// component test; the binding that asks for it is, and it is the half that
/// can regress. The other half — that app.css still defines a rule for this
/// class — is deliberately unpinned: it is one rule in one file, and asserting
/// on stylesheet text would buy a brittle string match rather than a contract.
/// Another panel's pin belongs here too, because the contract is one contract
/// (issue halheinrich/backgammon#77).
/// </remarks>
public class BusyCursorTests : BunitContext
{
    /// <summary>
    /// The marker app.css dresses, as a selector. One constant so a rename has
    /// one place to fail rather than eight.
    /// </summary>
    private const string BusyRoot = "div.is-busy";

    public BusyCursorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    // PPTX because the deck pathway is where the missing cursor was found: a
    // multi-minute atomic render with nothing at the pointer to say so.
    private Task<LocalPanelHost> RenderLocalAsync(CapturingProcessHandler server)
    {
        Services.AddSingleton(new HttpClient(server) { BaseAddress = new Uri("http://localhost/") });
        return LocalPanelHost.RenderReadyAsync(this, OutputFormat.Pptx);
    }

    /// <summary>A snapshot mid-render: every file read, timings stopped early.</summary>
    private static ProcessingProgress RenderingSnapshot() => new()
    {
        Current = 40,
        Total = 40,
        Phase = JobPhase.Rendering,
        FileName = "Rendering PPTX (120 decisions, 240 slides)…",
        TotalRows = 120,
        ElapsedSec = 1.2,
        FilesPerSec = 33,
    };

    [Fact]
    public async Task LocalPanel_MarksItsRoot_BeforeTheFirstStatusPoll()
    {
        var server = new CapturingProcessHandler();
        server.HoldStatus();
        var host = await RenderLocalAsync(server);
        var cut = host.Panel;
        var run = host.ClickRunAsync();
        await server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        // One marker, and it encloses the panel rather than decorating a
        // corner of it — the Run button the pointer is still resting on has to
        // be inside, which is where a per-spot class would have failed.
        Assert.Single(cut.FindAll(BusyRoot));
        Assert.NotEmpty(cut.FindAll($"{BusyRoot} #startingNotice"));
        Assert.NotEmpty(cut.FindAll($"{BusyRoot} button.btn-primary"));

        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);
    }

    [Fact]
    public async Task LocalPanel_KeepsItsRootMarked_ThroughTheAtomicRenderPhase()
    {
        var server = new CapturingProcessHandler(RenderingSnapshot(), RenderingSnapshot() with { Complete = true });
        server.HoldPoll(2);
        var host = await RenderLocalAsync(server);
        var cut = host.Panel;
        var run = host.ClickRunAsync();
        await server.HeldPollReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"{BusyRoot} #renderingNotice")));
        Assert.Single(cut.FindAll(BusyRoot));

        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);
    }

    [Fact]
    public async Task LocalPanel_IsUnmarked_WhenIdle()
    {
        var cut = (await RenderLocalAsync(new CapturingProcessHandler())).Panel;

        Assert.Empty(cut.FindAll(BusyRoot));
    }

    /// <summary>
    /// A finished run leaves its terminal snapshot on screen — the Done line and
    /// the progress block around it — with the run no longer in hand. The
    /// marker rides that fact, not the presence of that block, so it must
    /// already be gone: the failure mode here is a busy cursor stranded over an
    /// idle app, which is worse than the one being fixed.
    /// </summary>
    [Fact]
    public async Task LocalPanel_DropsTheMarkerWithTheFlag_NotWithTheProgressBlock()
    {
        var done = RenderingSnapshot() with { Complete = true, FileName = "Done" };
        var host = await RenderLocalAsync(new CapturingProcessHandler(done));
        var cut = host.Panel;

        await host.RunToCompletionAsync();

        Assert.NotEmpty(cut.FindAll("span.text-success"));
        Assert.Empty(cut.FindAll(BusyRoot));
    }
}
