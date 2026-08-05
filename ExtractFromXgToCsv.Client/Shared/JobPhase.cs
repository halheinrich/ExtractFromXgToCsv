namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Which stage of a Local-mode run a <see cref="ProcessingProgress"/> snapshot
/// describes. The client branches its busy affordance on this discriminator;
/// <see cref="ProcessingProgress.FileName"/> stays a human-readable line and is
/// never parsed for the same fact.
/// </summary>
/// <remarks>
/// Deliberately has no terminal member: <see cref="ProcessingProgress.Complete"/>,
/// <see cref="ProcessingProgress.Cancelled"/> and
/// <see cref="ProcessingProgress.ErrorMessage"/> already are the terminal-state
/// SSOT, and a second one would let the two disagree.
/// </remarks>
public enum JobPhase
{
    /// <summary>
    /// The per-file pass: reading, iterating and filtering the input files.
    /// <see cref="ProcessingProgress.Current"/> over
    /// <see cref="ProcessingProgress.Total"/> is a meaningful fraction here, so
    /// the client shows a determinate bar. Default, so every pathway that only
    /// ever has this one stage needs no assignment.
    /// </summary>
    Processing = 0,

    /// <summary>
    /// The deck pathway's atomic render (PPTX/PDF). Every input file has been
    /// read and the renderer is building the document in one uninterruptible
    /// call, so there is no fraction to show: the file counter has stopped at
    /// 100% and <see cref="ProcessingProgress.ElapsedSec"/> /
    /// <see cref="ProcessingProgress.FilesPerSec"/> are frozen at their last
    /// per-file values. The client shows an indeterminate bar and suppresses
    /// the stale figures.
    /// </summary>
    Rendering = 1,
}
