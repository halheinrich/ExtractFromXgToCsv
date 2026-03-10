using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ExtractFromXgToCsv.Client.Shared;
using System.Diagnostics;
using System.Text;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Server-side processor for local mode.
/// Streams through .xg/.xgp files in a folder one at a time,
/// applies a DecisionFilterSet, and writes CSV rows as it goes.
/// Never holds more than one file's rows in memory at a time.
/// </summary>
public class LocalFolderProcessor
{
    private readonly ILogger<LocalFolderProcessor> _logger;

    public LocalFolderProcessor(ILogger<LocalFolderProcessor> logger)
    {
        _logger = logger;
    }

    public async Task ProcessAsync(
        string folderPath,
        string outputPath,
        DecisionFilterSet filterSet,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xg", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".xgp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException("No .xg or .xgp files found in folder.");

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        await using var writer = new StreamWriter(outputPath, append: false, Encoding.UTF8);
        await writer.WriteLineAsync(DecisionRow.CsvHeader);

        int totalRows = 0;
        var stopwatch = Stopwatch.StartNew();
        const int reportEvery = 10; // client polls every second; no need to update on every file

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            var fileName = Path.GetFileName(file);

            if (i % reportEvery == 0)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var filesPerSec = elapsed > 0 ? (int)(i / elapsed) : 0;

                progress.Report(new ProcessingProgress
                {
                    Current = i + 1,
                    Total = files.Count,
                    FileName = fileName,
                    TotalRows = totalRows,
                    ElapsedSec = elapsed,
                    FilesPerSec = filesPerSec
                });
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                using var ms = new MemoryStream(bytes);
                var xgFile = XgFileReader.ReadStream(ms);
                var matchId = Path.GetFileNameWithoutExtension(file);
                var rows = XgDecisionIterator.Iterate(xgFile, matchId);

                foreach (var row in filterSet.Apply(rows))
                {
                    await writer.WriteLineAsync(row.ToCsvLine());
                    totalRows++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Skipping {File}: {Error}", fileName, ex.Message);
            }
        }

        var totalElapsed = stopwatch.Elapsed.TotalSeconds;
        var finalFilesPerSec = totalElapsed > 0 ? (int)(files.Count / totalElapsed) : 0;

        progress.Report(new ProcessingProgress
        {
            Current = files.Count,
            Total = files.Count,
            FileName = "Done",
            TotalRows = totalRows,
            Complete = true,
            ElapsedSec = totalElapsed,
            FilesPerSec = finalFilesPerSec
        });
    }
}
