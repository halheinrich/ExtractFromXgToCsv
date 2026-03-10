namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Singleton that exposes the configured app mode (Local or Web).
/// Injected into controllers and components to switch behavior.
/// </summary>
public class AppModeService
{
    public string Mode { get; }
    public bool IsLocal => Mode == "Local";

    public AppModeService(string mode)
    {
        Mode = mode;
    }
}
