namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// The output a processing run produces. Crosses the client↔server wire as
/// <see cref="ProcessRequest.OutputFormat"/>, and the server switches on it.
/// <c>Pptx</c> and <c>Pdf</c> are Local-mode only (they need server-side
/// rasterization); <c>Xgp</c> works in both modes (see the XGP export section).
/// </summary>
public enum OutputFormat
{
    /// <summary>Decision rows as a single CSV file.</summary>
    Csv,

    /// <summary>Decisions as a single in-memory JSON array of diagram requests.</summary>
    DiagramJson,

    /// <summary>Problem/Solution slide deck (PowerPoint). Local mode only.</summary>
    Pptx,

    /// <summary>Problem/Solution page deck (PDF). Local mode only.</summary>
    Pdf,

    /// <summary>
    /// One <c>.xgp</c> position file per decision — a single <c>.zip</c> in Web
    /// mode, files written into a folder in Local mode.
    /// </summary>
    Xgp
}
