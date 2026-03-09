using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using System.Text;

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

        string matchId = Path.GetFileNameWithoutExtension(fileName);
        return XgDecisionIterator.Iterate(xgFile, matchId).ToList();
    }

    public string BuildCsv(IEnumerable<DecisionRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(DecisionRow.CsvHeader);
        foreach (var row in rows)
            sb.AppendLine(row.ToCsvLine());
        return sb.ToString();
    }
}
