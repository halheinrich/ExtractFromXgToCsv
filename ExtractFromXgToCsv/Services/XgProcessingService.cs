using ExtractFromXgToCsv.Models;
using Microsoft.AspNetCore.Hosting;
using System.Text;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Processes .xg / .xgp files and exports decisions as CSV.
/// Supports both local folder mode (server-side) and browser upload mode (Azure).
/// </summary>
public class XgProcessingService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<XgProcessingService> _logger;

    // True when running on a developer machine (not Azure App Service / Azure Functions)
    public bool IsLocalEnvironment =>
        !_env.IsProduction() ||
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));

    public XgProcessingService(IWebHostEnvironment env, ILogger<XgProcessingService> logger)
    {
        _env = env;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // LOCAL: scan a folder for .xg / .xgp files
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all .xg and .xgp file paths under <paramref name="folderPath"/>.
    /// Only available in local (non-Azure) mode.
    /// </summary>
    public IReadOnlyList<string> GetXgFilesInFolder(string folderPath)
    {
        if (!IsLocalEnvironment)
            throw new InvalidOperationException("Folder scanning is only available in local mode.");

        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        return Directory
            .EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xg", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".xgp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();
    }

    // -------------------------------------------------------------------------
    // CORE: process one file (bytes) → list of DecisionRow
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses a single .xg/.xgp file and returns all decision rows.
    /// Delegates to ConvertXgToJson_Lib.XgDecisionIterator once the lib is wired up.
    /// </summary>
    public IReadOnlyList<DecisionRow> ExtractDecisions(byte[] fileBytes, string fileName)
    {
        _logger.LogInformation("Extracting decisions from {FileName} ({Bytes} bytes)", fileName, fileBytes.Length);

        // TODO: replace stub with real call once ConvertXgToJson_Lib submodule is linked:
        //
        //   var iterator = new XgDecisionIterator(fileBytes);
        //   return iterator.ToList();

        // --- STUB: returns a single placeholder row so the UI is exercisable ---
        return new List<DecisionRow>
        {
            new DecisionRow(
                Xgid:          "XGID=-b----E-C---eE---c-e----B-:0:0:1:00:0:0:0:5:10",
                Error:         0.042,
                MatchScore:    "0-0",
                MatchLength:   5,
                Player:        "Player1",
                Match:         Path.GetFileNameWithoutExtension(fileName),
                Game:          1,
                MoveNum:       1,
                Roll:          "31",
                AnalysisDepth: 3,
                Equity:        0.153
            )
        };
    }

    // -------------------------------------------------------------------------
    // CSV builder
    // -------------------------------------------------------------------------

    /// <summary>Renders a collection of DecisionRows as a UTF-8 CSV string.</summary>
    public string BuildCsv(IEnumerable<DecisionRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(DecisionRow.CsvHeader);
        foreach (var row in rows)
            sb.AppendLine(row.ToCsvLine());
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // LOCAL: write CSV to disk
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="csvContent"/> to <paramref name="outputPath"/>.
    /// Only available in local mode.
    /// </summary>
    public async Task WriteLocalCsvAsync(string outputPath, string csvContent)
    {
        if (!IsLocalEnvironment)
            throw new InvalidOperationException("Local disk write is only available in local mode.");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(outputPath, csvContent, Encoding.UTF8);
        _logger.LogInformation("CSV written to {Path}", outputPath);
    }
}
