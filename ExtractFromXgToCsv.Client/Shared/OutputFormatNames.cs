namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// The short name of each <see cref="OutputFormat"/>, for a sentence that
/// names an output, stated once.
/// </summary>
public static class OutputFormatNames
{
    /// <summary>The short name of <paramref name="format"/>: "CSV", "Diagram JSON", "PPTX", "PDF" or "XGP".</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined format.</exception>
    public static string ShortName(this OutputFormat format) => format switch
    {
        OutputFormat.Csv => "CSV",
        OutputFormat.DiagramJson => "Diagram JSON",
        OutputFormat.Pptx => "PPTX",
        OutputFormat.Pdf => "PDF",
        OutputFormat.Xgp => "XGP",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a defined output format."),
    };
}
