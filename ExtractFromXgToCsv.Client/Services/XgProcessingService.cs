using ConvertXgToJson_Lib;
using BgDataTypes_Lib;
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
}
