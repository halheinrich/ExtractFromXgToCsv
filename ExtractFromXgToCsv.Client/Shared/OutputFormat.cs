using System.Text.Json.Serialization;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// The output a processing run produces. Crosses the client↔server wire as
/// <see cref="ProcessRequest.OutputFormat"/>, and the server switches on it.
/// </summary>
[JsonConverter(typeof(StrictJsonStringEnumConverter<OutputFormat>))]
public enum OutputFormat
{
    /// <summary>Decision rows as a single CSV file.</summary>
    Csv,

    /// <summary>Decisions as a single in-memory JSON array of diagram requests.</summary>
    DiagramJson,

    /// <summary>Problem/Solution slide deck (PowerPoint).</summary>
    Pptx,

    /// <summary>Problem/Solution page deck (PDF).</summary>
    Pdf,

    /// <summary>One <c>.xgp</c> position file per decision, written into a folder.</summary>
    Xgp
}
