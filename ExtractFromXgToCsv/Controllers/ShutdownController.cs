using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

/// <summary>
/// Endpoint that gracefully stops the host (the app's Exit button).
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
    /// </summary>
    [HttpPost]
    public IActionResult Shutdown()
    {
        _lifetime.StopApplication();
        return Ok();
    }
}
