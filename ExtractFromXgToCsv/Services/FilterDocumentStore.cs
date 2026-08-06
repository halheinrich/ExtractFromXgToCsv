namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Text-file relay for the client's saved-filters documents: reads and writes
/// one named file inside a caller-supplied folder — the Local-mode source
/// folder the request names. The server owns <b>safety, not policy</b>: it
/// enforces the simple-filename rule (<see cref="IsValidFileName"/>) so a
/// request can never escape its folder, but it knows nothing about which file
/// names the filter store uses — those come from the client, whose
/// <c>XgFilter_Razor</c> reference this project deliberately does not share.
/// </summary>
/// <remarks>
/// Dependency-free IO glue, registered unconditionally (unlike the
/// Local-guarded processing services): the Local-mode gate lives in
/// <c>FilterDocumentController</c> as an explicit action guard, so Web mode
/// answers with an observable 404 rather than a container resolution failure.
/// <c>public</c> by the same framework constraint as the other server services
/// — controller constructor injection.
/// </remarks>
public class FilterDocumentStore
{
    /// <summary>
    /// Whether <paramref name="name"/> is a plain file name — non-blank, no
    /// directory separators, not rooted, no <c>.</c>/<c>..</c> traversal, no
    /// characters Windows forbids in file names. The one rule that keeps a
    /// relayed read or write inside the requested folder.
    /// </summary>
    /// <param name="name">The candidate file name.</param>
    /// <returns><see langword="true"/> when the name is a safe simple file name.</returns>
    public static bool IsValidFileName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name == Path.GetFileName(name)
        && name != "."
        && name != ".."
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>
    /// Read the named file from <paramref name="folder"/>, or
    /// <see langword="null"/> when the file (or the folder itself) does not
    /// exist — absence is a value, not an error, matching the client seam's
    /// null-for-absent contract.
    /// </summary>
    /// <param name="folder">The folder to read from.</param>
    /// <param name="fileName">The simple file name inside it.</param>
    /// <returns>The file's text, or <see langword="null"/> when absent.</returns>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is blank or <paramref name="fileName"/> fails <see cref="IsValidFileName"/>.</exception>
    public async Task<string?> ReadAsync(string folder, string fileName)
    {
        var path = ResolvePath(folder, fileName);
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path);
    }

    /// <summary>
    /// Write <paramref name="content"/> to the named file in
    /// <paramref name="folder"/>, overwriting any existing file. The folder is
    /// never created: a write into a folder that doesn't exist is the caller's
    /// mistake and surfaces as the IO exception it is.
    /// </summary>
    /// <param name="folder">The folder to write into.</param>
    /// <param name="fileName">The simple file name inside it.</param>
    /// <param name="content">The full text to write.</param>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is blank or <paramref name="fileName"/> fails <see cref="IsValidFileName"/>.</exception>
    public Task WriteAsync(string folder, string fileName, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return File.WriteAllTextAsync(ResolvePath(folder, fileName), content);
    }

    // Shared argument guard + path resolution. The name check is defense in
    // depth behind the controller's 400 — a caller that bypasses the
    // controller still cannot escape the folder.
    private static string ResolvePath(string folder, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!IsValidFileName(fileName))
            throw new ArgumentException(
                $"'{fileName}' is not a simple file name.", nameof(fileName));
        return Path.Combine(folder, fileName);
    }
}
