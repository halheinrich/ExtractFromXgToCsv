using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// What one Local run does: its folder, destination, filters, format and
/// export options — the <see cref="StartRequest.Run"/> a start request
/// carries. The client owns this wire shape; the server deserializes against
/// it and dispatches on <see cref="OutputFormat"/>.
/// </summary>
/// <remarks>
/// A record, so two requests are equal exactly when they ask for the same run:
/// the server compares a repeated start with the one it admitted under the
/// same identity (the umbrella's <c>SPEC-notices.md</c> §7.5). Its members
/// compare by value — <see cref="FilterConfig"/> and
/// <see cref="XgpExportOptions"/> both do.
/// </remarks>
public sealed record ProcessRequest
{
    /// <summary>Folder to discover <c>.xg</c>/<c>.xgp</c> input files under, recursively.</summary>
    public string FolderPath    { get; init; } = string.Empty;

    /// <summary>
    /// Destination path. Names the output <b>file</b> for every format except
    /// <see cref="OutputFormat.Xgp"/>, where it names the output <b>folder</b>.
    /// </summary>
    public string OutputPath    { get; init; } = string.Empty;

    /// <summary>
    /// Filter configuration to apply, materialized server-side via
    /// <see cref="FilterConfig.Build"/>. Default-initialized so a partial
    /// payload deserializes cleanly.
    /// </summary>
    public FilterConfig Filters { get; init; } = new();

    /// <summary>Which output the run produces; the server switches on it.</summary>
    public OutputFormat OutputFormat { get; init; } = OutputFormat.Csv;

    /// <summary>
    /// Naming options for the Xgp pathway; ignored by every other
    /// <see cref="OutputFormat"/>. Default-initialized like
    /// <see cref="Filters"/> so older/partial payloads deserialize cleanly.
    /// </summary>
    public XgpExportOptions XgpOptions { get; init; } = new();

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
    public bool Anonymize { get; init; }
}
