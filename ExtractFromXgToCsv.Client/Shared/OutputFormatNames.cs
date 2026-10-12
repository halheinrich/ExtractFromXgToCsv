namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// The short name this app's copy gives each <see cref="OutputFormat"/> where
/// a sentence names an output — "The last download (XGP)" — stated once.
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
