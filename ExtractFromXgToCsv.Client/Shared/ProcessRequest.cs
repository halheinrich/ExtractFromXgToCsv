using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// POST body for <c>/api/process/start</c>. The client owns this wire shape;
/// the server deserializes against it and dispatches on <see cref="OutputFormat"/>.
/// </summary>
public class ProcessRequest
{
    /// <summary>Folder to discover <c>.xg</c>/<c>.xgp</c> input files under, recursively.</summary>
    public string FolderPath    { get; set; } = string.Empty;

    /// <summary>
    /// Destination path. Names the output <b>file</b> for every format except
    /// <see cref="OutputFormat.Xgp"/>, where it names the output <b>folder</b>.
    /// </summary>
    public string OutputPath    { get; set; } = string.Empty;

    /// <summary>
    /// Filter configuration to apply, materialized server-side via
    /// <see cref="FilterConfig.Build"/>. Default-initialized so a partial
    /// payload deserializes cleanly.
    /// </summary>
    public FilterConfig Filters { get; set; } = new();

    /// <summary>Which output the run produces; the server switches on it.</summary>
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
