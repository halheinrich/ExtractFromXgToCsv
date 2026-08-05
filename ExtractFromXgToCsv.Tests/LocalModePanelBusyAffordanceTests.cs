using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the two Local-mode progress states that carry no fraction, both of
/// which the determinate bar misreports: the gap before the first status poll
/// lands (the block rendered empty — the click went unacknowledged), and the
/// deck pathway's atomic render (a solid 100% bar beside elapsed/throughput
/// figures frozen at their last per-file values — a finished-looking job for
/// however long the render takes, measured in minutes at corpus scale).
/// </summary>
public class LocalModePanelBusyAffordanceTests : BunitContext
{
    public LocalModePanelBusyAffordanceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient { BaseAddress = new Uri("http://localhost/") });
    }

    private IRenderedComponent<LocalModePanel> RenderPanel() =>
        Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Pptx)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, true)
            .Add(c => c.FilterDirty, false)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

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

    [Fact]
    public void BusyBeforeTheFirstPoll_RendersAnIndeterminateStartingState()
    {
        var cut = RenderPanel();
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_busy", true);
        cut.Render();

        var bar = cut.Find("div.progress-bar");
        Assert.Contains("progress-bar-animated", bar.ClassName);
        Assert.Contains("Starting…", cut.Find("#startingNotice").TextContent);
    }

    [Fact]
    public void RenderingPhase_RendersAnIndeterminateBarWithoutTheStaleFigures()
    {
        var cut = RenderPanel();
        bUnitTestHelpers.SetPrivateField(
            cut.Instance, "_progress", RenderingSnapshot(JobPhase.Rendering));
        cut.Render();

        var bar = cut.Find("div.progress-bar");
        Assert.Contains("progress-bar-animated", bar.ClassName);

        // No fraction is claimed for a phase that has none...
        Assert.DoesNotContain("100%", bar.TextContent);
        // ...and the frozen throughput/elapsed pair is suppressed rather than
        // presented as live.
        Assert.DoesNotContain("files/sec", cut.Markup);

        Assert.Contains("Rendering PPTX", cut.Find("#renderingNotice").TextContent);
    }

    /// <summary>
    /// The same snapshot, differing only in its phase, keeps the determinate
    /// bar and its figures. Pins that the branch keys on the typed
    /// discriminator and not on <see cref="ProcessingProgress.FileName"/>'s
    /// text — the string is presentation, and re-deriving the phase from it
    /// would put the fact in two places.
    /// </summary>
    [Fact]
    public void ProcessingPhase_KeepsTheDeterminateBarEvenWithARenderingFileName()
    {
        var cut = RenderPanel();
        bUnitTestHelpers.SetPrivateField(
            cut.Instance, "_progress", RenderingSnapshot(JobPhase.Processing));
        cut.Render();

        var bar = cut.Find("div.progress-bar");
        Assert.DoesNotContain("progress-bar-animated", bar.ClassName);
        Assert.Contains("100%", bar.TextContent);
        Assert.Contains("files/sec", cut.Markup);
    }

    /// <summary>
    /// A terminal snapshot keeps the Done line whatever phase it carries — the
    /// indeterminate branch must not swallow the completion state.
    /// </summary>
    [Fact]
    public void TerminalSnapshot_StillRendersDone()
    {
        var cut = RenderPanel();
        var done = RenderingSnapshot(JobPhase.Rendering);
        done.Complete = true;
        done.FileName = "Done";
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_progress", done);
        cut.Render();

        Assert.Contains("Done", cut.Find("span.text-success").TextContent);
        Assert.Empty(cut.FindAll("#renderingNotice"));
    }
}
