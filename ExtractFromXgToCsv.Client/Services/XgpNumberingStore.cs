using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using BgUiPrimitives_Razor;
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
/// instance, captured options and folder.
/// </para>
/// <para>
/// <b>Four read outcomes</b> (<see cref="NumberingRead"/>): nothing stored, a
/// refused read, a record this version reads, and a document it cannot — not
/// JSON, another version, a value out of range. An unreadable document may hold
/// owed exports, so it is never read as nothing stored, and nothing here
/// replaces it but a write its owner was authorized to make.
/// </para>
/// <para>
/// <b>Migration.</b> Before this document, the numbering was three keys
/// written separately — <c>xg_xgpPattern</c>, <c>xg_xgpLastNumber</c>,
/// <c>xg_xgpSuffixLength</c> — and, before the pattern, a prefix
/// (<c>xg_xgpPrefix</c>), migrated as the pattern <c>{prefix}{n}</c>. Only
/// while no document is stored are they read, once: what they hold is
/// written as the document, and once it is stored every one of them is
/// removed, the prefix too where a pattern superseded it.
/// </para>
/// </remarks>
internal sealed class XgpNumberingStore(ReportingBrowserStorage storage)
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

    /// <summary>Reads the stored document: one of its four outcomes; a refusal is reported.</summary>
    public async Task<NumberingRead> ReadAsync()
    {
        var read = await storage.ReadAsync(BrowserStorageArea.Local, Key,
            "the XGP numbering starts from memory for this visit");
        if (read.IsRefused)
            return NumberingRead.Refused.Value;
        if (read.Value is not { } text)
            return NumberingRead.Missing.Value;
        return Parse(text) is { } record ? new NumberingRead.Valid(record) : new NumberingRead.Unreadable(text);
    }

    /// <summary>Writes <paramref name="record"/> whole, in one storage write.</summary>
    /// <returns>Whether the browser stored it; a refusal is reported.</returns>
    public Task<bool> WriteAsync(XgpNumberingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return SucceededAsync(storage.WriteAsync(BrowserStorageArea.Local, Key, JsonSerializer.Serialize(
                new Document(CurrentVersion, record.Numbering, record.Mark, record.Owed), Json),
            "the XGP numbering and any owed exports are kept for this visit only"));

        // The argument check above throws at the call; only the awaiting is deferred.
        static async Task<bool> SucceededAsync(Task<BrowserStorageWriteResult> write) => (await write).Succeeded;
    }

    /// <summary>
    /// Migrates the keys the document replaced: the numbering they hold,
    /// written as the document, and once that is stored, every key that held
    /// anything removed. For a record read as missing, and nothing else: never
    /// over a refused read, nor over a document that cannot be read.
    /// </summary>
    /// <returns>
    /// The migrated record, and whether storage stored it;
    /// <see langword="null"/> when no key holds anything.
    /// </returns>
    public async Task<(XgpNumberingRecord Record, bool Stored)?> MigrateAsync()
    {
        var present = new List<string>(4);
        async Task<string?> ReadLegacyAsync(string key)
        {
            var read = await storage.ReadAsync(BrowserStorageArea.Local, key,
                "that older numbering setting is left out of the migration");
            var value = read.IsStored ? read.Value : null;
            if (value is not null)
                present.Add(key);
            return value;
        }

        var pattern = await ReadLegacyAsync(LegacyPatternKey);
        var prefix = await ReadLegacyAsync(LegacyPrefixKey);
        var lastNumber = await ReadLegacyAsync(LegacyLastNumberKey);
        var suffixLength = await ReadLegacyAsync(LegacySuffixLengthKey);
        if (present.Count == 0)
            return null;

        // The legacy prefix named files as "{prefix}{n}" does, and counts only
        // where no pattern superseded it; a prefix the pattern grammar rejects
        // (braces) falls back to the default.
        if (pattern is null && prefix is not null)
        {
            var migrated = prefix + "{n}";
            pattern = XgpNameTemplate.TryParse(migrated, out _, out _) ? migrated : XgpExportOptions.DefaultNamePattern;
        }

        var record = XgpNumberingRecord.Empty with
        {
            Numbering = new XgpNumbering(
                pattern ?? XgpExportOptions.DefaultNamePattern,
                int.TryParse(lastNumber, out var last) && last >= 0 ? last : 0,
                int.TryParse(suffixLength, out var length) && length is >= 1 and <= 9 ? length : 3),
        };
        var stored = await WriteAsync(record);
        if (stored)
        {
            foreach (var key in present)
                await storage.RemoveAsync(BrowserStorageArea.Local, key,
                    "the superseded numbering key stays stored, unused");
        }
        return (record, stored);
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
        // types refuse — is unreadable. A refusal arrives wrapped when the
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

/// <summary>
/// What reading Home's numbering record found: one of the four outcomes of the
/// umbrella's <c>SPEC-notices.md</c> §7.5.
/// </summary>
internal abstract record NumberingRead
{
    private NumberingRead()
    {
    }

    /// <summary>Nothing is stored: the keys the document replaced are migrated.</summary>
    public sealed record Missing : NumberingRead
    {
        /// <summary>The one value.</summary>
        public static Missing Value { get; } = new();
    }

    /// <summary>The browser refused the read (reported). It establishes nothing: the page works from memory.</summary>
    public sealed record Refused : NumberingRead
    {
        /// <summary>The one value.</summary>
        public static Refused Value { get; } = new();
    }

    /// <summary>A record this version reads.</summary>
    /// <param name="Record">The stored record.</param>
    public sealed record Valid(XgpNumberingRecord Record) : NumberingRead;

    /// <summary>
    /// A document this version cannot read: malformed, or written by a version
    /// it does not know. It may hold owed exports, so it is never read as
    /// nothing outstanding, and it is kept until the user resolves it.
    /// </summary>
    /// <param name="Document">The stored text, as it is.</param>
    public sealed record Unreadable(string Document) : NumberingRead;
}
