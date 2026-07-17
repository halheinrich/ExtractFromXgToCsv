using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Processes .xg / .xgp file bytes and exports decisions as CSV.
/// Runs entirely in WebAssembly on the user's machine — no data is transferred to the server.
/// </summary>
internal class XgProcessingService
{
    public IReadOnlyList<DecisionRow> ExtractDecisions(byte[] fileBytes, string fileName)
    {
        using var ms = new MemoryStream(fileBytes);
        var xgFile = XgFileReader.ReadStream(ms);

        string sourceFile = Path.GetFileName(fileName);
        return XgDecisionIterator.Iterate(xgFile, sourceFile).ToList();
    }

    public string BuildCsv(IEnumerable<DecisionRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(DecisionRow.CsvHeader);
        foreach (var row in rows)
            sb.AppendLine(row.ToCsvLine());
        return sb.ToString();
    }
    public IReadOnlyList<BgDecisionData> ExtractDiagramRequests(byte[] fileBytes, string fileName)
    {
        using var ms = new MemoryStream(fileBytes);
        var xgFile = XgFileReader.ReadStream(ms);
        string sourceFile = Path.GetFileName(fileName);
        return XgDecisionIterator.IterateDiagramRequests(xgFile, sourceFile).ToList();
    }

    public string BuildDiagramJson(IEnumerable<BgDecisionData> items)
    {
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Builds a zip archive holding one <c>.xgp</c> position file per decision,
    /// named by <paramref name="options"/>' pattern via an
    /// <see cref="XgpNameAllocator"/> in the order given (numbering order =
    /// filtered order; duplicate rendered names get Windows-style
    /// <c>" (2)"</c> suffixes). Batch-constant tokens draw from
    /// <paramref name="filters"/>; per-item tokens from each row. Parsing
    /// stays inside this service: callers hand over the retained raw file
    /// bytes keyed by bare filename plus the decision rows — never an
    /// <c>XgFile</c>.
    ///
    /// <para>
    /// A decision from an <c>.xg</c> source is sliced via
    /// <see cref="XgpExporter"/> (analysis panes carried through, XG-SaveAs
    /// equivalent). A decision from an <c>.xgp</c> source is the source file
    /// copied verbatim — it already is a single-position analyzed
    /// <c>.xgp</c>, so the copy is byte-identical and cheaper than a re-slice,
    /// and stays strictly more faithful: a re-slice would clear the
    /// match-level comments that the verbatim copy preserves.
    /// </para>
    ///
    /// <para>
    /// When <paramref name="anonymize"/> is <see langword="true"/>, every
    /// entry's player names are rewritten to the anonymize preset
    /// (<see cref="XgpSliceOptions.Anonymized"/> — the producer's SSOT for
    /// what "anonymized" means): an <c>.xg</c> slice takes the options-bearing
    /// overload, and an <c>.xgp</c> source takes the whole-file anonymize-copy
    /// (comments and rollouts still preserved; only the header names change).
    /// The toggle therefore covers every entry, not just the sliced ones.
    /// Every entry either way is a single decision, so the preset names by
    /// role — "On-roll" for the decision-maker (a cube decision's doubler),
    /// "Opponent" for the other — and the producer resolves which header slot
    /// each role lands in, per entry.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="options"/> fails validation.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a decision references a source file absent from
    /// <paramref name="sourceFiles"/>.
    /// </exception>
    public byte[] BuildXgpZip(
        IReadOnlyDictionary<string, byte[]> sourceFiles,
        IReadOnlyList<DecisionRow> decisions,
        XgpExportOptions options,
        FilterConfig filters,
        bool anonymize = false)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(decisions);

        // Create validates the options — the single ArgumentException throw
        // point shared with the Local-mode pathway.
        var allocator = XgpNameAllocator.Create(options, filters);

        // The service owns the bool -> producer-type mapping: the UI carries
        // only intent. null keeps the current byte-for-byte behaviour; the
        // preset is the producer's single source of what "anonymized" means.
        var nameOverrides = anonymize ? XgpSliceOptions.Anonymized : null;

        // Each source is parsed at most once per batch, however many of its
        // decisions are being exported.
        var parsed = new Dictionary<string, XgFile>();

        using var zipStream = new MemoryStream();
        // .xgp content is already zlib-compressed internally, so deflating
        // the entries again buys almost nothing — store fast.
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var row in decisions)
            {
                // Next (not Peek/Commit): any failure here aborts the whole
                // zip, so there's no partial-success state to keep the
                // counter honest for.
                var bytes = ExportDecision(row.Id, sourceFiles, parsed, nameOverrides);
                var entry = zip.CreateEntry(allocator.Next(row), CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(bytes);
            }
        }
        return zipStream.ToArray();
    }

    private static byte[] ExportDecision(
        DecisionId id,
        IReadOnlyDictionary<string, byte[]> sourceFiles,
        Dictionary<string, XgFile> parsed,
        XgpSliceOptions? nameOverrides)
    {
        if (!sourceFiles.TryGetValue(id.Filename, out var sourceBytes))
            throw new InvalidOperationException(
                $"Source file '{id.Filename}' for decision '{id}' is not among the selected files.");

        switch (id)
        {
            case XgpDecisionId:
                // Already a single-position analyzed .xgp. Not anonymizing:
                // copy verbatim (byte-for-byte with the source). Anonymizing:
                // whole-file re-emit with only the header names rewritten —
                // comments and rollout contexts still travel.
                return nameOverrides is null
                    ? sourceBytes
                    : XgpExporter.ToBytes(Parse(id.Filename, sourceBytes, parsed), nameOverrides);

            case XgDecisionId xgId:
                // Pass the typed Id straight to the producer's Id overload —
                // it destructures the coordinates internally and ignores
                // Filename (the source is already resolved). Without overrides
                // this is byte-identical to the coordinate overload.
                var xgFile = Parse(id.Filename, sourceBytes, parsed);
                return nameOverrides is null
                    ? XgpExporter.ToBytes(xgFile, xgId)
                    : XgpExporter.ToBytes(xgFile, xgId, nameOverrides);

            default:
                throw new NotSupportedException(
                    $"Unsupported DecisionId shape '{id.GetType().Name}' for .xgp export.");
        }
    }

    /// <summary>
    /// Parses <paramref name="sourceBytes"/> once per source filename,
    /// caching the result in <paramref name="parsed"/> so a batch re-parses
    /// no source however many of its decisions are exported.
    /// </summary>
    private static XgFile Parse(
        string filename, byte[] sourceBytes, Dictionary<string, XgFile> parsed)
    {
        if (!parsed.TryGetValue(filename, out var xgFile))
        {
            using var ms = new MemoryStream(sourceBytes);
            xgFile = XgFileReader.ReadStream(ms);
            parsed[filename] = xgFile;
        }
        return xgFile;
    }
}
