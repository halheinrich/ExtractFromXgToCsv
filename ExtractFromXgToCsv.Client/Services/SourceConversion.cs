using System.Collections.Immutable;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// The two representations a decoded file converts to (the umbrella's
/// <c>SPEC-notices.md</c> §7.1): rows, for CSV and XGP, and records, for
/// Diagram JSON.
/// </summary>
internal enum Representation
{
    /// <summary><see cref="DecisionRow"/>s — what CSV writes and XGP exports.</summary>
    Rows,

    /// <summary><see cref="BgDecisionData"/> records — what Diagram JSON writes.</summary>
    Records,
}

/// <summary>A conversion's state for one selection and key (<c>SPEC-notices.md</c> §7.3).</summary>
internal enum ConversionState
{
    /// <summary>Nothing has asked for it.</summary>
    NotRequested,

    /// <summary>It has been asked for and has not published.</summary>
    Running,

    /// <summary>It has published: each file's representation, or its conversion skip.</summary>
    Completed,
}

/// <summary>
/// One decoded file's conversion to one representation, completed: its items,
/// or — when its conversion failed — no items and its conversion skip. A file
/// whose conversion fails midway contributes nothing: its items are staged
/// whole and published only on success.
/// </summary>
/// <typeparam name="T">The representation's item.</typeparam>
internal sealed record SourceConversion<T>
{
    private SourceConversion(DecodedSource source, ImmutableArray<T> items, SkippedItem? skip)
    {
        Source = source;
        Items = items;
        Skip = skip;
    }

    /// <summary>The file's whole representation.</summary>
    public static SourceConversion<T> Converted(DecodedSource source, IEnumerable<T> items) =>
        new(source ?? throw new ArgumentNullException(nameof(source)), [.. items], null);

    /// <summary>The file's conversion skip: named as picked, with the failure's message.</summary>
    public static SourceConversion<T> Failed(DecodedSource source, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(failure);
        return new(source, [], new SkippedItem(source.Name, null, failure.Message));
    }

    /// <summary>
    /// This result for <paramref name="source"/>, a retained instance of the
    /// same file — the items and skip unchanged.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="source"/> names another file.</exception>
    public SourceConversion<T> For(DecodedSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Name != Source.Name || !ReferenceEquals(source.File, Source.File))
            throw new ArgumentException("A result moves only to another instance of its own file.", nameof(source));
        return new(source, Items, Skip);
    }

    /// <summary>The decoded file converted.</summary>
    public DecodedSource Source { get; }

    /// <summary>Its items; empty when its conversion failed.</summary>
    public ImmutableArray<T> Items { get; }

    /// <summary>Its conversion skip; <see langword="null"/> when it converted.</summary>
    public SkippedItem? Skip { get; }
}

/// <summary>
/// Converts one decoded file to a representation, unfiltered, for the run's
/// ranking and the opening book in force — Web mode retains everything and
/// refilters with <c>DecisionFilterSet.Matches</c>, the producer's rule, when
/// the filter changes (XgFilter_Lib's "two strategies, one rule set").
/// </summary>
/// <remarks>
/// <para>
/// A conversion that cannot convert the file throws; the caller's per-file
/// boundary makes that the file's conversion skip. A cancellation propagates.
/// </para>
/// <para>
/// Asynchronous, though the producer's conversion is synchronous: the
/// conversion's completion is where its owner decides whether it may publish
/// (<c>SPEC-notices.md</c> §7.3, "Captured inputs, ownership, and late
/// completions"), and the seam is what lets a test hold a conversion there, or
/// count the conversions a gesture made. The application's implementation is
/// <see cref="XgSourceConverter"/>.
/// </para>
/// </remarks>
internal interface ISourceConverter
{
    /// <summary>The file's rows, each built for <paramref name="ranking"/>.</summary>
    Task<IReadOnlyList<DecisionRow>> RowsAsync(
        DecodedSource source, PlayRanking ranking, OpeningBook? book, CancellationToken cancellationToken);

    /// <summary>
    /// The file's records. A record depends on no ranking; the iterator is
    /// handed <paramref name="ranking"/> all the same, so the walk's
    /// configuration is the run's and not a producer default.
    /// </summary>
    Task<IReadOnlyList<BgDecisionData>> RecordsAsync(
        DecodedSource source, PlayRanking ranking, OpeningBook? book, CancellationToken cancellationToken);
}

/// <summary>
/// The application's <see cref="ISourceConverter"/>: ConvertXgToJson_Lib's
/// iterator over the decoded file, under options that always state the
/// ranking — so the iterator's own default never stands in — and carry the
/// book, when one is loaded.
/// </summary>
internal sealed class XgSourceConverter : ISourceConverter
{
    /// <inheritdoc />
    public Task<IReadOnlyList<DecisionRow>> RowsAsync(
        DecodedSource source, PlayRanking ranking, OpeningBook? book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DecisionRow> rows =
            [.. XgDecisionIterator.Iterate(source.File, source.Name, options: new XgIteratorOptions(book, ranking))];
        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BgDecisionData>> RecordsAsync(
        DecodedSource source, PlayRanking ranking, OpeningBook? book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<BgDecisionData> records =
            [.. XgDecisionIterator.IterateDiagramRequests(source.File, source.Name, options: new XgIteratorOptions(book, ranking))];
        return Task.FromResult(records);
    }
}
