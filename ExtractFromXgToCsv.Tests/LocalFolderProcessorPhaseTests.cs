using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Infrastructure;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the server half of the busy affordance: the deck pathway announces its
/// atomic render as <see cref="JobPhase.Rendering"/>, and the streaming
/// pathways never do. The client picks its bar from this discriminator, so a
/// processor that stopped stamping it would silently revert the render window
/// to a solid 100% bar beside frozen figures — no error, just a job that looks
/// finished for minutes (issue #53).
/// </summary>
public class LocalFolderProcessorPhaseTests
{
    /// <summary>
    /// A single small fixture in a folder of its own, filtered down hard: the
    /// pin is on the reported phases, and a full-corpus deck render would cost
    /// minutes to learn the same thing.
    /// </summary>
    private static string OneFileFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"xg-phase-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.Copy(
            Path.Combine(FixtureHelper.FixtureDir, "MTCH4064.xg"),
            Path.Combine(dir, "MTCH4064.xg"));
        return dir;
    }

    /// <summary>
    /// Loose enough that the fixture yields decisions to render (an empty deck
    /// throws instead of reaching the render), tight enough to keep the deck to
    /// a handful of slides.
    /// </summary>
    private static FilterConfig NarrowFilter() => new() { ErrorMin = 0.05 };

    [Fact]
    public async Task DeckPathway_ReportsRenderingForItsAtomicRender()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var folder = OneFileFolder();
        var outputPath = Path.Combine(folder, "deck.pptx");
        try
        {
            var reports = new List<ProcessingProgress>();
            var progress = new Progress<ProcessingProgress>(reports.Add);

            await new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance)
                .ProcessPptxAsync(folder, outputPath, NarrowFilter().Build(), progress);

            var rendering = Assert.Single(reports, r => r.Phase == JobPhase.Rendering);

            // It is the last thing the client hears before the run finishes —
            // that is exactly why it has to describe itself honestly.
            Assert.False(rendering.Complete);
            Assert.Same(reports[^2], rendering);
            Assert.True(reports[^1].Complete);

            // Every other report is the per-file pass.
            Assert.All(reports.Where(r => !ReferenceEquals(r, rendering)),
                r => Assert.Equal(JobPhase.Processing, r.Phase));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task StreamingPathway_NeverReportsRendering()
    {
        var folder = OneFileFolder();
        try
        {
            var reports = new List<ProcessingProgress>();
            var progress = new Progress<ProcessingProgress>(reports.Add);

            await new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance)
                .ProcessAsync(
                    folder, Path.Combine(folder, "out.csv"), NarrowFilter().Build(), progress);

            Assert.NotEmpty(reports);
            Assert.All(reports, r => Assert.Equal(JobPhase.Processing, r.Phase));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        }
    }
}
