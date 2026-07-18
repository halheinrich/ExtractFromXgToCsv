using ConvertXgToJson_Lib;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Resolves, loads, and holds XG's opening-book database
/// (<c>OpeningBookV2.ob</c>) once for the Local-mode processing pipeline, then
/// offers it as ready-to-pass <see cref="XgIteratorOptions"/>. Registered as a
/// singleton (Local mode only) so the ~13.6&#160;MB database is read and
/// decoded a single time per process — the book is immutable after load and
/// safe for the concurrent reads the iterators perform.
///
/// <para>Path resolution (<see cref="ResolvePath"/>), in order:</para>
/// <list type="number">
///   <item><description>the <see cref="ConfigKey"/> configuration key when it
///     is present and non-empty — an explicit path;</description></item>
///   <item><description>when the key is <em>absent</em>, the default eXtreme
///     Gammon 2 install location
///     (<see cref="DefaultInstallPath"/>) — auto-detect;</description></item>
///   <item><description>when the key is present but <em>empty</em>, enrichment
///     is disabled and no path is probed.</description></item>
/// </list>
///
/// <para>
/// A disabled key, a missing file, or an unreadable/invalid book is logged and
/// degrades gracefully: <see cref="Book"/> and <see cref="IteratorOptions"/>
/// are <see langword="null"/>, book-analysed decisions stamp their unenriched
/// "Book V2" / level Unknown form, and extraction proceeds. Loading the book is
/// never allowed to fail a run.
/// </para>
/// </summary>
public sealed class OpeningBookProvider
{
    /// <summary>Configuration key naming the opening-book database file.</summary>
    public const string ConfigKey = "OpeningBookPath";

    /// <summary>
    /// Default <c>OpeningBookV2.ob</c> location for a standard eXtreme Gammon 2
    /// install — probed when <see cref="ConfigKey"/> is absent.
    /// </summary>
    public const string DefaultInstallPath =
        @"C:\Program Files (x86)\eXtreme Gammon 2\OpeningBookV2.ob";

    /// <summary>
    /// Resolves the configured path and loads the book, degrading to no book on
    /// any failure. Never throws for a missing or invalid book.
    /// </summary>
    public OpeningBookProvider(IConfiguration configuration, ILogger<OpeningBookProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        Book = Load(ResolvePath(configuration), logger);
        IteratorOptions = Book is null ? null : new XgIteratorOptions(Book);
    }

    /// <summary>
    /// The loaded book, or <see langword="null"/> when enrichment is disabled,
    /// the file is absent, or it could not be read as a valid opening book.
    /// </summary>
    public OpeningBook? Book { get; }

    /// <summary>
    /// Iterator options carrying the loaded book, or <see langword="null"/> when
    /// no book is available — pass straight to <see cref="XgDecisionIterator"/>'s
    /// <c>options</c> parameter, where a <see langword="null"/> is the "default
    /// behaviour" (unenriched) signal the iterator already honours.
    /// </summary>
    public XgIteratorOptions? IteratorOptions { get; }

    /// <summary>How <see cref="ResolvePath"/> arrived at (or declined) a path.</summary>
    internal enum PathSource
    {
        /// <summary><see cref="ConfigKey"/> was absent; <see cref="DefaultInstallPath"/> is used.</summary>
        AutoDetect,

        /// <summary><see cref="ConfigKey"/> supplied a non-empty path.</summary>
        Explicit,

        /// <summary><see cref="ConfigKey"/> was present but empty; enrichment is off.</summary>
        Disabled,
    }

    /// <summary>The outcome of path resolution: the source, and the path to probe (null when disabled).</summary>
    internal readonly record struct BookPath(PathSource Source, string? Path);

    /// <summary>
    /// Pure resolution of <see cref="ConfigKey"/> to a <see cref="BookPath"/>,
    /// separated from the file IO in <see cref="Load"/> so the three branches
    /// are testable without a filesystem. A <see langword="null"/> config value
    /// means the key is absent (auto-detect); an empty/whitespace value means it
    /// is present-but-empty (disabled).
    /// </summary>
    internal static BookPath ResolvePath(IConfiguration configuration)
    {
        var configured = configuration[ConfigKey];
        if (configured is null)
            return new BookPath(PathSource.AutoDetect, DefaultInstallPath);
        if (string.IsNullOrWhiteSpace(configured))
            return new BookPath(PathSource.Disabled, Path: null);
        return new BookPath(PathSource.Explicit, configured);
    }

    /// <summary>
    /// Loads the resolved book, logging and returning <see langword="null"/> for
    /// every degradation path (disabled, missing file, unreadable/invalid book).
    /// </summary>
    private static OpeningBook? Load(BookPath resolved, ILogger logger)
    {
        if (resolved.Source == PathSource.Disabled)
        {
            logger.LogInformation(
                "Opening-book enrichment disabled ({Key} is empty). Book-analysed " +
                "decisions will report analysis depth as Unknown.", ConfigKey);
            return null;
        }

        string path = resolved.Path!;
        if (!File.Exists(path))
        {
            logger.LogWarning(
                "Opening book not found at '{Path}' (source: {Source}). Book-analysed " +
                "decisions will report analysis depth as Unknown. Set the {Key} " +
                "configuration key to the OpeningBookV2.ob location to enable enrichment.",
                path, resolved.Source, ConfigKey);
            return null;
        }

        if (!OpeningBook.TryLoad(path, out var book))
        {
            logger.LogWarning(
                "Opening book at '{Path}' could not be read as a valid opening book. " +
                "Proceeding without book enrichment.", path);
            return null;
        }

        logger.LogInformation(
            "Opening book loaded from '{Path}' ({EntryCount} entries).", path, book.EntryCount);
        return book;
    }
}
