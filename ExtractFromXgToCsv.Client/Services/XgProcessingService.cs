using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Processes .xg / .xgp file bytes and exports decisions as CSV.
/// Runs entirely in WebAssembly on the user's machine — no data is transferred to the server.
/// </summary>
public class XgProcessingService
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
    /// named per <paramref name="options"/> in the order given (numbering order
    /// = filtered order). Parsing stays inside this service: callers hand over
    /// the retained raw file bytes keyed by bare filename plus the decision
    /// identities — never an <c>XgFile</c>.
    ///
    /// <para>
    /// A decision from an <c>.xg</c> source is sliced via
    /// <see cref="XgpExporter"/> (analysis panes carried through, XG-SaveAs
    /// equivalent). A decision from an <c>.xgp</c> source is the source file
    /// copied verbatim — it already is a single-position analyzed
    /// <c>.xgp</c>; re-slicing would only strip its comments.
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
        IReadOnlyList<DecisionId> decisions,
        XgpExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.TryValidate(out var optionsError))
            throw new ArgumentException(optionsError, nameof(options));

        // Each source is parsed at most once per batch, however many of its
        // decisions are being exported.
        var parsed = new Dictionary<string, XgFile>();

        using var zipStream = new MemoryStream();
        // .xgp content is already zlib-compressed internally, so deflating
        // the entries again buys almost nothing — store fast.
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < decisions.Count; i++)
            {
                var bytes = ExportDecision(decisions[i], sourceFiles, parsed);
                var entry = zip.CreateEntry(options.EntryName(i), CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(bytes);
            }
        }
        return zipStream.ToArray();
    }

    private static byte[] ExportDecision(
        DecisionId id,
        IReadOnlyDictionary<string, byte[]> sourceFiles,
        Dictionary<string, XgFile> parsed)
    {
        if (!sourceFiles.TryGetValue(id.Filename, out var sourceBytes))
            throw new InvalidOperationException(
                $"Source file '{id.Filename}' for decision '{id}' is not among the selected files.");

        switch (id)
        {
            case XgpDecisionId:
                // Already a single-position analyzed .xgp — copy verbatim.
                return sourceBytes;

            case XgDecisionId(_, var game, var moveNumber, var isCube):
                if (!parsed.TryGetValue(id.Filename, out var xgFile))
                {
                    using var ms = new MemoryStream(sourceBytes);
                    xgFile = XgFileReader.ReadStream(ms);
                    parsed[id.Filename] = xgFile;
                }
                return XgpExporter.ToBytes(xgFile, game, moveNumber, isCube);

            default:
                throw new NotSupportedException(
                    $"Unsupported DecisionId shape '{id.GetType().Name}' for .xgp export.");
        }
    }
}
