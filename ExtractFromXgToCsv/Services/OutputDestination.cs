namespace ExtractFromXgToCsv.Services;

/// <summary>
/// The files a Local run writes its output to — the destination of the
/// umbrella's <c>SPEC-notices.md</c> §7.1, where a failure fails the run and is
/// never a skip. <see cref="LocalFolderProcessor"/> opens every output file
/// through this, and nothing else.
/// </summary>
/// <remarks>
/// The application's is <see cref="FileSystemDestination"/>. A test supplies
/// another to make a write or a close fail: no real destination is known to
/// fail partway, and the run's handling of such a failure is pinned through
/// this.
/// </remarks>
internal interface IOutputDestination
{
    /// <summary>
    /// Opens <paramref name="path"/> for writing, creating the file or
    /// replacing one of that name — the one file a CSV, Diagram JSON or deck
    /// run writes, which the user named.
    /// </summary>
    Stream Replace(string path);
}

/// <summary>The application's <see cref="IOutputDestination"/>: the file system.</summary>
internal sealed class FileSystemDestination : IOutputDestination
{
    private FileSystemDestination()
    {
    }

    /// <summary>The one instance; it holds no state.</summary>
    public static FileSystemDestination Instance { get; } = new();

    /// <inheritdoc />
    public Stream Replace(string path) =>
        new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
}
