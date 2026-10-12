namespace ExtractFromXgToCsv.Services;

/// <summary>
/// The files a Local run writes its output to — the destination of the
/// umbrella's <c>SPEC-notices.md</c> §7.1, where a failure fails the run and is
/// never a skip. <see cref="LocalFolderProcessor"/> opens every output file
/// through this, and nothing else.
/// </summary>
/// <remarks>
/// The application's is <see cref="FileSystemDestination"/>. A test supplies
/// another to make a write, a close or a removal fail: no real destination is
/// known to fail partway, and the run's handling of such a failure is pinned
/// through this.
/// </remarks>
internal interface IOutputDestination
{
    /// <summary>
    /// Opens <paramref name="path"/> for writing, creating the file or
    /// replacing one of that name — the one file a CSV, Diagram JSON or deck
    /// run writes, which the user named.
    /// </summary>
    Stream Replace(string path);

    /// <summary>
    /// Creates <paramref name="path"/> for writing only if no file of that name
    /// exists, in one operation — an XGP position file, which is never
    /// overwritten (§7.5). There is no existence check before it: the create
    /// itself refuses.
    /// </summary>
    /// <exception cref="IOException">A file of that name exists, or the file could not be created.</exception>
    Stream CreateNew(string path);

    /// <summary>Removes the file at <paramref name="path"/> — an unfinished file the run itself created.</summary>
    void Delete(string path);
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
    public Stream Replace(string path) => Open(path, FileMode.Create);

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="FileMode.CreateNew"/>: the operating system creates the file
    /// only if none of its name exists, and throws an
    /// <see cref="IOException"/> if one does.
    /// </remarks>
    public Stream CreateNew(string path) => Open(path, FileMode.CreateNew);

    /// <inheritdoc />
    public void Delete(string path) => File.Delete(path);

    private static FileStream Open(string path, FileMode mode) =>
        new(path, mode, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
}
