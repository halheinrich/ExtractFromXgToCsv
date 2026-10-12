using System.Collections.Immutable;
using ConvertXgToJson_Lib.Models;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Razor;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// One picked file, as the browser offers it: its name, and how to open it.
/// The pick's read acquires the stream once, through
/// <c>XgSourceReader.ReadAsync</c>, which owns and disposes it.
/// </summary>
/// <param name="Name">The file's name as picked — the decisions' source name, extension included.</param>
/// <param name="Open">Opens the file's stream, observing the read's token.</param>
internal sealed record PickedFile(string Name, Func<CancellationToken, Task<Stream>> Open);

/// <summary>
/// A picked file that decoded: its name, its parsed file, and — only when an
/// output needs them — its exact bytes. Retained by the selection so that no
/// output reads or parses the file again.
/// </summary>
/// <remarks>
/// Compared by reference: each is one decoded file of one selection, and a
/// conversion's per-file results are keyed to the instance.
/// </remarks>
internal sealed class DecodedSource
{
    /// <summary>A decoded file, with its exact bytes when an export copies it verbatim.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    public DecodedSource(string name, XgFile file, ReadOnlyMemory<byte>? verbatimBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(file);
        Name = name;
        File = file;
        VerbatimBytes = verbatimBytes;
    }

    /// <summary>The file's name as picked.</summary>
    public string Name { get; }

    /// <summary>The parsed file, complete in memory.</summary>
    public XgFile File { get; }

    /// <summary>
    /// The file's exact bytes as read, kept only where an export copies the
    /// file verbatim (<see cref="XgpPayload.CopiesSourceVerbatim"/>);
    /// <see langword="null"/> otherwise.
    /// </summary>
    public ReadOnlyMemory<byte>? VerbatimBytes { get; }
}

/// <summary>
/// One picked entry and its read outcome: decoded, or a read skip naming the
/// file with the reader's message. Exactly one of the two.
/// </summary>
internal sealed record SelectionEntry
{
    private SelectionEntry(DecodedSource? decoded, SkippedItem? readSkip)
    {
        Decoded = decoded;
        ReadSkip = readSkip;
    }

    /// <summary>The entry of a file that decoded.</summary>
    public static SelectionEntry Of(DecodedSource decoded) =>
        new(decoded ?? throw new ArgumentNullException(nameof(decoded)), null);

    /// <summary>The entry of a file that could not be read.</summary>
    public static SelectionEntry Skipped(SkippedItem readSkip) =>
        new(null, readSkip ?? throw new ArgumentNullException(nameof(readSkip)));

    /// <summary>The decoded file; <see langword="null"/> for a read skip.</summary>
    public DecodedSource? Decoded { get; }

    /// <summary>The read skip; <see langword="null"/> for a decoded file.</summary>
    public SkippedItem? ReadSkip { get; }
}

/// <summary>
/// Web mode's selection (the umbrella's <c>SPEC-notices.md</c> §7.3): every
/// entry one pick chose, in pick order, each with its read outcome, under the
/// selection's own token. A pick whose every file failed still has a
/// selection, which owns those failures. Immutable; a new pick is a new
/// selection.
/// </summary>
internal sealed class WebSelection
{
    /// <summary>The selection a pick made under <paramref name="token"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    public WebSelection(FilterSourceToken token, IEnumerable<SelectionEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Token = token;
        Entries = [.. entries];
        Decoded = [.. Entries.Select(e => e.Decoded).OfType<DecodedSource>()];
        ReadSkips = SkipRecord.Of(Entries.Select(e => e.ReadSkip).OfType<SkippedItem>());
    }

    /// <summary>The selection's token — the filter source it is.</summary>
    public FilterSourceToken Token { get; }

    /// <summary>Every picked entry, in pick order.</summary>
    public ImmutableArray<SelectionEntry> Entries { get; }

    /// <summary>The entries that decoded, in pick order.</summary>
    public ImmutableArray<DecodedSource> Decoded { get; }

    /// <summary>The entries that could not be read, in pick order.</summary>
    public SkipRecord ReadSkips { get; }
}
