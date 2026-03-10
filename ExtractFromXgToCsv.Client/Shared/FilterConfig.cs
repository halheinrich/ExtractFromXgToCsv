namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Serializable DTO mirroring DecisionFilterSet fields.
/// Sent as JSON from browser to server in local mode.
/// Server reconstructs a DecisionFilterSet from this.
/// </summary>
public class FilterConfig
{
    public List<string> Players       { get; set; } = new();
    public string DecisionType        { get; set; } = "Both";
    public List<string> MatchScores   { get; set; } = new();
    public double? ErrorMin           { get; set; }
    public double? ErrorMax           { get; set; }
    public List<string> PositionTypes { get; set; } = new();
    public List<string> PlayTypes     { get; set; } = new();
}

public class ProcessRequest
{
    public string FolderPath    { get; set; } = string.Empty;
    public string OutputPath    { get; set; } = string.Empty;
    public FilterConfig Filters { get; set; } = new();
}
