namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Singleton that exposes the configured app mode (Local or Web).
/// Injected into controllers and components to switch behavior.
/// </summary>
public class AppModeService
{
    /// <summary>The configured mode string, <c>"Local"</c> or <c>"Web"</c>.</summary>
    public string Mode { get; }

    /// <summary>Whether the app runs in Local mode — the single source of the mode rule.</summary>
    public bool IsLocal => Mode == "Local";

    /// <summary>Creates the service for the configured <paramref name="mode"/> string.</summary>
    public AppModeService(string mode)
    {
        Mode = mode;
    }
}
