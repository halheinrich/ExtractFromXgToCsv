using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

/// <summary>
/// Local-mode-only endpoint that gracefully stops the host (the app's Exit
/// button). Not intended for Azure deployment.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ShutdownController : ControllerBase
{
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Creates the controller with the host <paramref name="lifetime"/> it stops.</summary>
    public ShutdownController(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    /// <summary>
    /// POST /api/shutdown
    /// Gracefully stops the application (equivalent to Shift-F5 in Visual Studio).
    /// Local mode only — not intended for Azure deployment.
    /// </summary>
    [HttpPost]
    public IActionResult Shutdown()
    {
        _lifetime.StopApplication();
        return Ok();
    }
}
