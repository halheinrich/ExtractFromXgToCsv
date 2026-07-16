using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Shared;

public class ProcessRequest
{
    public string FolderPath    { get; set; } = string.Empty;
    public string OutputPath    { get; set; } = string.Empty;
    public FilterConfig Filters { get; set; } = new();
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Csv;

    /// <summary>
    /// Naming options for the Xgp pathway; ignored by every other
    /// <see cref="OutputFormat"/>. Default-initialized like
    /// <see cref="Filters"/> so older/partial payloads deserialize cleanly.
    /// </summary>
    public XgpExportOptions XgpOptions { get; set; } = new();

    /// <summary>
    /// When <see langword="true"/>, the Xgp pathway rewrites the player names
    /// of every exported position to the decision's roles — "On-roll" for the
    /// player who made it, "Opponent" for the other (comments and rollouts
    /// preserved). The producer resolves which header slot is which; every
    /// position this pathway exports is a single decision, so the roles are
    /// always defined. Ignored by every other
    /// <see cref="OutputFormat"/>. Defaults to <see langword="false"/> —
    /// the conservative behaviour default, so an older/partial payload never
    /// rewrites anyone's file by omission.
    /// </summary>
    public bool Anonymize { get; set; }
}
