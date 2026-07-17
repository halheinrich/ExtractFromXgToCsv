using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

/// <summary>
/// Exposes the configured app mode via <c>GET /api/appmode</c>, so the WASM
/// front end can branch its UI without hardcoding the mode.
/// </summary>
[ApiController]
[Route("api/appmode")]
public class AppModeController : ControllerBase
{
    private readonly AppModeService _appMode;

    /// <summary>Creates the controller for the given <paramref name="appMode"/> service.</summary>
    public AppModeController(AppModeService appMode) => _appMode = appMode;

    /// <summary>GET /api/appmode — the configured mode as an <see cref="AppModeResponse"/>.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new AppModeResponse(_appMode.Mode));
}
