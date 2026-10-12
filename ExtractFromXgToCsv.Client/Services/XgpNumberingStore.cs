using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Home's numbering record in browser storage (the umbrella's
/// <c>SPEC-notices.md</c> §7.5): one document under one key, read and written
/// whole — a storage write stores the whole document or nothing — and its
/// one-time migration from the keys it replaces.
/// </summary>
/// <remarks>
/// <para>
/// <b>The document.</b> A JSON object: its <c>version</c>, the
/// <c>numbering</c> (pattern, last number, length), the <c>mark</c> (instance
/// and entry number) and the <c>owed</c> jobs, each with its identity,
/// instance, captured options and folder. What cannot be read as one — not
/// JSON, another version, a value out of range — reads as no record, and the
/// next write replaces it.
/// </para>
/// <para>
/// <b>Migration.</b> Before this document, the numbering was three keys
/// written separately — <c>xg_xgpPattern</c>, <c>xg_xgpLastNumber</c>,
/// <c>xg_xgpSuffixLength</c> — and, before the pattern, a prefix
/// (<c>xg_xgpPrefix</c>), migrated as the pattern <c>{prefix}{n}</c>. While
/// no document is stored they are read, once: what they hold is written as
/// the document, and once it is stored they are removed.
/// </para>
/// </remarks>
internal sealed class XgpNumberingStore(SettingsStorage settings)
{
    /// <summary>The document's key.</summary>
    internal const string Key = "xg_xgpNumbering";

    private const string LegacyPatternKey = "xg_xgpPattern";
    private const string LegacyPrefixKey = "xg_xgpPrefix";
    private const string LegacyLastNumberKey = "xg_xgpLastNumber";
    private const string LegacySuffixLengthKey = "xg_xgpSuffixLength";

    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The stored record; <see langword="null"/> when none is stored, the
    /// browser refused the read (reported), or what is stored cannot be read
    /// as one.
    /// </summary>
    public async Task<XgpNumberingRecord?> ReadAsync()
    {
        var text = await settings.ReadAsync(Key);
        return text is null ? null : Parse(text);
    }

    /// <summary>Writes <paramref name="record"/> whole, in one storage write.</summary>
    /// <returns>Whether the browser stored it; a refusal is reported.</returns>
    public Task<bool> WriteAsync(XgpNumberingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return settings.WriteAsync(Key, JsonSerializer.Serialize(
            new Document(CurrentVersion, record.Numbering, record.Mark, record.Owed), Json));
    }

    /// <summary>
    /// The record a page starts from: the stored document, or — while none is
    /// stored — the numbering migrated from the keys it replaces, written as
    /// the document; and once that is stored, the keys are removed.
    /// </summary>
    public async Task<XgpNumberingRecord> RestoreAsync()
    {
        if (await ReadAsync() is { } stored)
            return stored;

        var present = new List<string>(4);
        async Task<string?> ReadLegacyAsync(string key)
        {
            var value = await settings.ReadAsync(key);
            if (value is not null)
                present.Add(key);
            return value;
        }

        var pattern = await ReadLegacyAsync(LegacyPatternKey);
        if (pattern is null && await ReadLegacyAsync(LegacyPrefixKey) is { } prefix)
        {
            // The legacy prefix named files as "{prefix}{n}" does; a prefix
            // the pattern grammar rejects (braces) falls back to the default.
            var migrated = prefix + "{n}";
            pattern = XgpNameTemplate.TryParse(migrated, out _, out _) ? migrated : XgpExportOptions.DefaultNamePattern;
        }
        var lastNumber = await ReadLegacyAsync(LegacyLastNumberKey);
        var suffixLength = await ReadLegacyAsync(LegacySuffixLengthKey);
        if (present.Count == 0)
            return XgpNumberingRecord.Empty;

        var record = XgpNumberingRecord.Empty with
        {
            Numbering = new XgpNumbering(
                pattern ?? XgpExportOptions.DefaultNamePattern,
                int.TryParse(lastNumber, out var last) && last >= 0 ? last : 0,
                int.TryParse(suffixLength, out var length) && length is >= 1 and <= 9 ? length : 3),
        };
        if (await WriteAsync(record))
        {
            foreach (var key in present)
                await settings.RemoveAsync(key);
        }
        return record;
    }

    private static XgpNumberingRecord? Parse(string text)
    {
        try
        {
            var document = JsonSerializer.Deserialize<Document>(text, Json);
            if (document is null || document.Version != CurrentVersion)
                return null;
            var owed = document.Owed.IsDefault ? [] : document.Owed;
            if (!owed.All(IsWhole))
                return null;
            return new XgpNumberingRecord { Numbering = document.Numbering, Mark = document.Mark, Owed = owed };
        }
        // Anything that is not this document — malformed JSON, or a value its
        // types refuse — is no record. A refusal arrives wrapped when the
        // serializer builds the types by reflection rather than emitted code.
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException
                                       or System.Reflection.TargetInvocationException)
        {
            return null;
        }
    }

    // An owed job names everything a lost export is shown by.
    private static bool IsWhole(OwedXgpJob? job) =>
        job is not null
        && !string.IsNullOrWhiteSpace(job.JobId)
        && !string.IsNullOrWhiteSpace(job.Instance)
        && job.Options is not null
        && job.Folder is not null;

    /// <summary>The stored shape: the record, under its version.</summary>
    private sealed record Document(
        int Version, XgpNumbering? Numbering, LedgerMark? Mark, ImmutableArray<OwedXgpJob> Owed);
}
