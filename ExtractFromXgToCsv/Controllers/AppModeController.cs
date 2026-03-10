using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

[ApiController]
[Route("api/appmode")]
public class AppModeController : ControllerBase
{
    private readonly AppModeService _appMode;
    public AppModeController(AppModeService appMode) => _appMode = appMode;

    [HttpGet]
    public IActionResult Get() => Ok(_appMode.Mode);
}
