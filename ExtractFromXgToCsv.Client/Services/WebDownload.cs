using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>One decision for the XGP export, with the decoded file it came from.</summary>
/// <param name="Row">The decision, as its row.</param>
/// <param name="Source">The decoded file the decision came from, which its payload is prepared from.</param>
internal sealed record XgpDecision(DecisionRow Row, DecodedSource Source);

/// <summary>How a download attempt ended.</summary>
internal enum DownloadStatus
{
    /// <summary>Its file was handed to the browser.</summary>
    Completed,

    /// <summary>Building or handing over its file failed; nothing was exported.</summary>
    Failed,

    /// <summary>It was cancelled before the hand-off; nothing was exported.</summary>
    Cancelled,
}

/// <summary>
/// One download attempt's record — Web mode's "last download" (the umbrella's
/// <c>SPEC-notices.md</c> §7.3, projection B): what it skipped, what it
/// committed, and how it ended. Immutable, labelled as that attempt, and never
/// reinterpreted after a filter, format, option or book change.
/// </summary>
/// <remarks>
/// A download commits once, at the finalized file's hand-off to the browser;
/// a successful hand-off is the observable boundary, not proof the user saved
/// the file. So an attempt that did not complete committed nothing, whatever
/// it had prepared, and keeps only its diagnostics.
/// </remarks>
internal sealed record WebDownload
{
    /// <summary>The record of an attempt.</summary>
    /// <param name="format">The format the attempt captured.</param>
    /// <param name="fileName">The file name the attempt captured.</param>
    /// <param name="status">How it ended.</param>
    /// <param name="committed">The decisions its handed-off file holds; zero unless it completed.</param>
    /// <param name="numbering">For a completed XGP attempt, the numbering it committed; otherwise <see langword="null"/>.</param>
    /// <param name="exportSkips">The decisions whose payload preparation failed, in order.</param>
    /// <exception cref="ArgumentException">The facts disagree: a count or numbering on an attempt that did not complete, or a numbering whose count is not the committed count.</exception>
    public WebDownload(
        OutputFormat format,
        string fileName,
        DownloadStatus status,
        int committed,
        XgpExportCommit? numbering,
        SkipRecord exportSkips)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(exportSkips);
        ArgumentOutOfRangeException.ThrowIfNegative(committed);
        if (status != DownloadStatus.Completed && (committed != 0 || numbering is not null))
            throw new ArgumentException("An attempt that did not complete committed nothing.", nameof(committed));
        if (numbering is not null && numbering.Count != committed)
            throw new ArgumentException("The numbering's count is the committed count.", nameof(numbering));

        Format = format;
        FileName = fileName;
        Status = status;
        Committed = committed;
        Numbering = numbering;
        ExportSkips = exportSkips;
    }

    /// <summary>The format the attempt captured.</summary>
    public OutputFormat Format { get; }

    /// <summary>The file name the attempt captured.</summary>
    public string FileName { get; }

    /// <summary>How the attempt ended.</summary>
    public DownloadStatus Status { get; }

    /// <summary>The decisions actually committed — handed to the browser — by this attempt.</summary>
    public int Committed { get; }

    /// <summary>For a completed XGP attempt, the numbering it committed; otherwise <see langword="null"/>.</summary>
    public XgpExportCommit? Numbering { get; }

    /// <summary>The decisions this attempt skipped because their payload preparation failed.</summary>
    public SkipRecord ExportSkips { get; }

    /// <summary>
    /// The label of the list this record is shown as: the attempt it is the
    /// record of — its captured format, by its short name, and how it ended
    /// unless it completed — so it never reads as a verdict on the output
    /// selected now. "The last download (XGP)", "The last download (XGP,
    /// failed)", "The last download (CSV, stopped)" (Hal's ruling,
    /// 2026-10-12).
    /// </summary>
    public string Label => Status switch
    {
        DownloadStatus.Failed => $"The last download ({Format.ShortName()}, failed)",
        DownloadStatus.Cancelled => $"The last download ({Format.ShortName()}, stopped)",
        _ => $"The last download ({Format.ShortName()})",
    };
}
