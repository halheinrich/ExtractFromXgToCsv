using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// "The last download"'s label (Hal's ruling, 2026-10-12): it names the
/// attempt — the format it captured, by its short name, and how it ended
/// unless it completed — so it never reads as a verdict on the output
/// selected now. Pinned for every format and ending, including the ones the
/// panel never shows today: only an XGP attempt can skip a decision, and an
/// empty list renders nothing.
/// </summary>
public class WebDownloadTests
{
    // The ending by name: DownloadStatus is internal, and a test method xUnit
    // runs is public.
    [Theory]
    [InlineData(OutputFormat.Xgp, nameof(DownloadStatus.Completed), "The last download (XGP)")]
    [InlineData(OutputFormat.Xgp, nameof(DownloadStatus.Failed), "The last download (XGP, failed)")]
    [InlineData(OutputFormat.Xgp, nameof(DownloadStatus.Cancelled), "The last download (XGP, stopped)")]
    [InlineData(OutputFormat.Csv, nameof(DownloadStatus.Cancelled), "The last download (CSV, stopped)")]
    [InlineData(OutputFormat.Csv, nameof(DownloadStatus.Completed), "The last download (CSV)")]
    [InlineData(OutputFormat.DiagramJson, nameof(DownloadStatus.Completed), "The last download (Diagram JSON)")]
    [InlineData(OutputFormat.DiagramJson, nameof(DownloadStatus.Failed), "The last download (Diagram JSON, failed)")]
    public void Label_NamesTheAttemptsFormat_AndHowItEnded(OutputFormat format, string ending, string label)
    {
        var record = new WebDownload(format, "decisions.out", Enum.Parse<DownloadStatus>(ending), 0, null, SkipRecord.Empty);

        Assert.Equal(label, record.Label);
    }

    [Theory]
    [InlineData(OutputFormat.Csv, "CSV")]
    [InlineData(OutputFormat.DiagramJson, "Diagram JSON")]
    [InlineData(OutputFormat.Pptx, "PPTX")]
    [InlineData(OutputFormat.Pdf, "PDF")]
    [InlineData(OutputFormat.Xgp, "XGP")]
    public void ShortName_IsTheNameTheCopyUses(OutputFormat format, string name) =>
        Assert.Equal(name, format.ShortName());

    [Fact]
    public void ShortName_RefusesAnUndefinedFormat() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ((OutputFormat)99).ShortName());
}
