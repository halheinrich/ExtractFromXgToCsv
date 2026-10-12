namespace ExtractFromXgToCsv.Services;

/// <summary>
/// An XGP position file the run was to create exists already. Local XGP files
/// are never overwritten (the umbrella's <c>SPEC-notices.md</c> §7.5), so the
/// run stops there: a destination failure (§7.1), never a skip. The file is
/// not the run's, so nothing is removed, and the files written before it stay
/// committed and counted. Its message is the run error, and names the file.
/// </summary>
internal sealed class XgpFileExistsException : IOException
{
    /// <summary>The run stopped at <paramref name="fileName"/>, having written <paramref name="written"/> files before it.</summary>
    /// <param name="fileName">The existing file's name, as the run would have named its own.</param>
    /// <param name="written">The files the run had written before it.</param>
    /// <param name="refusal">The create's own refusal.</param>
    public XgpFileExistsException(string fileName, int written, Exception refusal)
        : base(MessageFor(fileName, written), refusal)
    {
        FileName = fileName;
    }

    /// <summary>The existing file's name.</summary>
    public string FileName { get; }

    private static string MessageFor(string fileName, int written) =>
        $"{fileName} already exists in the output folder, and XGP export never overwrites a file, so the run stopped there. "
        + (written == 0 ? "Nothing was written." : $"The {written} file(s) before it were written.");
}

/// <summary>
/// An XGP position file the run created could not be finished: writing it or
/// closing it failed. It was the run's — its create-new had succeeded — so the
/// run removed it, and it can never block a later export as an existing file
/// (the umbrella's <c>SPEC-notices.md</c> §7.5). A destination failure (§7.1),
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

    /// <summary>The unfinished file's name.</summary>
    public string FileName { get; }

    /// <summary>A failure closing the file after its write failed; <see langword="null"/> for none.</summary>
    public Exception? CloseFailure { get; }

    /// <summary>A failure removing the file; <see langword="null"/> when it was removed.</summary>
    public Exception? RemovalFailure { get; }

    private static string MessageFor(string fileName, Exception primary, Exception? closeFailure, Exception? removalFailure)
    {
        var message = primary.Message;
        if (closeFailure is not null)
            message += $" Closing the unfinished {fileName} failed too: {closeFailure.Message}";
        if (removalFailure is not null)
            message += $" The unfinished {fileName} could not be removed: {removalFailure.Message}";
        return message;
    }
}
