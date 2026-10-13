namespace ExtractFromXgToCsv.Services;

/// <summary>
/// An XGP position file the run created could not be finished: writing it or
/// closing it failed. It was the run's — its create-new had succeeded, under
/// the name in <see cref="FileName"/>, numbered if it had to be — so the run
/// removed it, and no unfinished position file is left beside the run's
/// counted ones (the umbrella's <c>SPEC-notices.md</c> §7.5). A file that
/// existed before the run is never touched. A destination failure (§7.1),
/// never a skip. The message is the primary failure's, with any failure to
/// close or remove the file reported beside it, never in its place.
/// </summary>
internal sealed class UnfinishedFileException : IOException
{
    /// <summary>Finishing <paramref name="fileName"/> failed with <paramref name="primary"/>.</summary>
    /// <param name="fileName">The unfinished file's name.</param>
    /// <param name="primary">The write's or the close's failure: the run's error.</param>
    /// <param name="closeFailure">A failure closing the file after its write failed; <see langword="null"/> for none.</param>
    /// <param name="removalFailure">A failure removing the file; <see langword="null"/> when it was removed.</param>
    public UnfinishedFileException(string fileName, Exception primary, Exception? closeFailure, Exception? removalFailure)
        : base(MessageFor(fileName, primary, closeFailure, removalFailure), primary)
    {
        FileName = fileName;
        CloseFailure = closeFailure;
        RemovalFailure = removalFailure;
    }

    /// <summary>The unfinished file's name, as the run created it.</summary>
    public string FileName { get; }

    /// <summary>A failure closing the file after its write failed; <see langword="null"/> for none.</summary>
    public Exception? CloseFailure { get; }

    /// <summary>A failure removing the file; <see langword="null"/> when it was removed.</summary>
    public Exception? RemovalFailure { get; }

    private static string MessageFor(string fileName, Exception primary, Exception? closeFailure, Exception? removalFailure)
    {
        var message = DestinationFailureText.Beside(primary, fileName, closeFailure);
        if (removalFailure is not null)
            message += $" The unfinished {fileName} could not be removed: {removalFailure.Message}";
        return message;
    }
}

/// <summary>
/// The one output file a CSV, Diagram JSON or deck run writes could not be
/// finished: its write failed, and closing it failed too. The run created the
/// file new, under the name the user gave or the next free one, so nothing
/// that existed before it is touched; and, unlike an XGP file, the unfinished
/// file is not removed — removal is an XGP run's rule alone (the umbrella's
/// <c>SPEC-notices.md</c> §7.5). A destination failure (§7.1), never a
/// skip. The write's failure is the run's error, with the close failure
/// reported beside it, never in its place, as an XGP file's are
/// (<see cref="UnfinishedFileException"/>).
/// </summary>
internal sealed class UnfinishedOutputException : IOException
{
    /// <summary>Writing <paramref name="fileName"/> failed with <paramref name="primary"/>, and closing it with <paramref name="closeFailure"/>.</summary>
    public UnfinishedOutputException(string fileName, Exception primary, Exception closeFailure)
        : base(DestinationFailureText.Beside(primary, fileName, closeFailure), primary)
    {
        FileName = fileName;
        CloseFailure = closeFailure;
    }

    /// <summary>The unfinished file's name.</summary>
    public string FileName { get; }

    /// <summary>The failure closing the file after its write failed.</summary>
    public Exception CloseFailure { get; }
}

/// <summary>
/// A run was cancelled while it wrote its one output file, and closing the
/// file failed. The cancellation stands, so the run is stopped, never failed
/// (the umbrella's <c>SPEC-notices.md</c> §7.1: a cancellation's record stands,
/// marked cancelled). The close failure is reported beside it
/// (<see cref="CloseFailureMessage"/>), never in its place.
/// </summary>
internal sealed class CancelledWithUnfinishedOutputException : OperationCanceledException
{
    /// <summary><paramref name="cancellation"/> ended the run, and closing <paramref name="fileName"/> then failed with <paramref name="closeFailure"/>.</summary>
    public CancelledWithUnfinishedOutputException(string fileName, OperationCanceledException cancellation, Exception closeFailure)
        : base(cancellation.Message, closeFailure, cancellation.CancellationToken)
    {
        FileName = fileName;
        CloseFailureMessage = $"Closing the unfinished {fileName} failed: {closeFailure.Message}";
    }

    /// <summary>The unfinished file's name.</summary>
    public string FileName { get; }

    /// <summary>What the stopped run reports beside its cancellation.</summary>
    public string CloseFailureMessage { get; }
}

/// <summary>The wording a destination failure shares, stated once.</summary>
internal static class DestinationFailureText
{
    /// <summary>
    /// <paramref name="primary"/>'s message, with a failure closing the
    /// unfinished <paramref name="fileName"/> after it reported beside it.
    /// </summary>
    public static string Beside(Exception primary, string fileName, Exception? closeFailure) =>
        closeFailure is null
            ? primary.Message
            : $"{primary.Message} Closing the unfinished {fileName} failed too: {closeFailure.Message}";
}
