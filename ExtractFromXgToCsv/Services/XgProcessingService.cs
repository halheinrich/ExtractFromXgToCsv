using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
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

    /// <summary>
    /// True when running on a developer machine (not Azure App Service).
    /// Azure App Service always sets WEBSITE_INSTANCE_ID.
    /// </summary>
    public bool IsLocalEnvironment =>
        !_env.IsProduction() ||
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));

    public XgProcessingService(IWebHostEnvironment env, ILogger<XgProcessingService> logger)
    {
        _env = env;
        _logger = logger;
    }

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

    public IReadOnlyList<DecisionRow> ExtractDecisions(byte[] fileBytes, string fileName)
    {
        _logger.LogInformation("Extracting decisions from {FileName} ({Bytes} bytes)", fileName, fileBytes.Length);

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