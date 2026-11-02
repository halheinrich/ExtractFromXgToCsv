using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// A record of skipped units, in the order they were skipped: the one record
/// type (the umbrella's <c>SPEC-notices.md</c> §7.1). A Local run's record is
/// one of these, and one rendering shows any of them (<c>SkipListNotice</c>).
/// A shared type does not merge the owners: each record belongs to whoever
/// keeps it.
/// </summary>
/// <remarks>
/// <para>
/// Immutable: <see cref="With"/> returns a new record, so a record handed to
/// a snapshot, a published projection or a component can never change under
/// its reader (the collection rider of halheinrich/backgammon#273). A record
/// holds no null entry.
/// </para>
/// <para>
/// The three counts are derived from the entries, each by the unit it states
/// (<see cref="SkippedItem.Unit"/>), so they cannot disagree with the list.
/// <see cref="CountPhrase"/> words them, and this is the one place that is
/// stated.
/// </para>
/// <para>
/// On the wire it is the JSON array of its entries, through its own bundled
/// converter, so every reader and writer of a snapshot spells it the same way.
/// </para>
/// </remarks>
[JsonConverter(typeof(SkipRecordJsonConverter))]
[CollectionBuilder(typeof(SkipRecord), nameof(Create))]
public sealed class SkipRecord : IReadOnlyList<SkippedItem>, IEquatable<SkipRecord>
{
    private readonly ImmutableArray<SkippedItem> _items;

    private SkipRecord(ImmutableArray<SkippedItem> items)
    {
        _items = items;
        FileCount = items.Count(item => item.Unit == SkipUnit.File);
        GameCount = items.Count(item => item.Unit == SkipUnit.Game);
        DecisionCount = items.Count(item => item.Unit == SkipUnit.Decision);
    }

    /// <summary>The record with no entries.</summary>
    public static SkipRecord Empty { get; } = new([]);

    /// <summary>A record of <paramref name="items"/>, in the order given.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null, or holds a null entry.</exception>
    public static SkipRecord Of(IEnumerable<SkippedItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var array = items.ToImmutableArray();
        foreach (var item in array)
            ArgumentNullException.ThrowIfNull(item, nameof(items));
        return array.IsEmpty ? Empty : new(array);
    }

    /// <summary>
    /// A record of <paramref name="items"/>, in the order given — the builder
    /// a collection expression (<c>[item, …]</c>) of this type calls.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> holds a null entry.</exception>
    public static SkipRecord Create(ReadOnlySpan<SkippedItem> items) => Of(items.ToArray());

    /// <summary>This record with <paramref name="item"/> appended; this record is unchanged.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public SkipRecord With(SkippedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(_items.Add(item));
    }

    /// <summary>Whether nothing was skipped.</summary>
    public bool IsEmpty => _items.IsEmpty;

    /// <summary>The number of entries.</summary>
    public int Count => _items.Length;

    /// <summary>The entry at <paramref name="index"/>.</summary>
    public SkippedItem this[int index] => _items[index];

    /// <summary>Whole files skipped.</summary>
    public int FileCount { get; }

    /// <summary>Games skipped: each one a game the walk omitted from a file it read.</summary>
    public int GameCount { get; }

    /// <summary>Single decisions skipped: an export skip and an omitted play alike.</summary>
    public int DecisionCount { get; }

    /// <summary>
    /// The non-zero counts for a reader, in the unit order — <c>"2 files and 1
    /// decision"</c>, <c>"1 file, 2 games and 3 decisions"</c> — and empty for
    /// an empty record. The one wording of them: every sentence that counts
    /// what a run skipped takes it from here.
    /// </summary>
    public string CountPhrase
    {
        get
        {
            var parts = new List<string>(3);
            if (FileCount > 0) parts.Add(Counted(FileCount, "file"));
            if (GameCount > 0) parts.Add(Counted(GameCount, "game"));
            if (DecisionCount > 0) parts.Add(Counted(DecisionCount, "decision"));
            return parts.Count < 2
                ? string.Concat(parts)
                : $"{string.Join(", ", parts.SkipLast(1))} and {parts[^1]}";

            static string Counted(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
        }
    }

    /// <inheritdoc />
    public IEnumerator<SkippedItem> GetEnumerator() => ((IEnumerable<SkippedItem>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Whether <paramref name="other"/> holds the same entries in the same order.</summary>
    public bool Equals(SkipRecord? other) =>
        other is not null && (ReferenceEquals(this, other) || _items.SequenceEqual(other._items));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SkipRecord);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    /// <summary>Value equality: the same entries in the same order.</summary>
    public static bool operator ==(SkipRecord? left, SkipRecord? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(SkipRecord? left, SkipRecord? right) => !(left == right);
}

/// <summary>
/// <see cref="SkipRecord"/>'s wire form: the JSON array of its entries, each
/// written and read as <see cref="SkippedItem"/> is under the options in use.
/// </summary>
internal sealed class SkipRecordJsonConverter : JsonConverter<SkipRecord>
{
    public override SkipRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var items = JsonSerializer.Deserialize<List<SkippedItem?>>(ref reader, options)
            ?? throw new JsonException("A skip record is an array of entries.");
        if (items.Contains(null))
            throw new JsonException("A skip record holds no null entry.");
        return SkipRecord.Of(items!);
    }

    public override void Write(Utf8JsonWriter writer, SkipRecord value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, (IEnumerable<SkippedItem>)value, options);
}
