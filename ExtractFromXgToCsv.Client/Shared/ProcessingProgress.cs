namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Progress update sent from LocalFolderProcessor to the UI via SSE.
/// Lives in the Client project so it can be used in both server and client code.
/// </summary>
public class ProcessingProgress
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public bool Complete { get; set; }
    public bool Cancelled { get; set; }
    public double ElapsedSec { get; set; }
    public int FilesPerSec { get; set; }

    public int PercentComplete => Total == 0 ? 0 : (int)((Current / (double)Total) * 100);
}
