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
/// The two counts are derived from the entries — a file skip is an entry with
/// no <see cref="SkippedItem.DecisionId"/>, a decision skip one with — so they
/// cannot disagree with the list, and this is the one place that rule is
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
        FileCount = items.Count(item => item.DecisionId is null);
        DecisionCount = items.Length - FileCount;
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

    /// <summary>Whole files skipped: entries with no decision.</summary>
    public int FileCount { get; }

    /// <summary>Single decisions skipped: entries naming a decision.</summary>
    public int DecisionCount { get; }

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
