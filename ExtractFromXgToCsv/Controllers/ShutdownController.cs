using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ShutdownController : ControllerBase
{
    private readonly IHostApplicationLifetime _lifetime;

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
