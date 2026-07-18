using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

/// <summary>
/// Exposes the server's opening-book load state via
/// <c>GET /api/openingbook/status</c>, so the Local-mode WASM front end can show
/// whether book-analysed decisions will be enriched or degrade to level Unknown.
/// <para>
/// Local mode only, like <see cref="ProcessController"/>:
/// <see cref="OpeningBookProvider"/> is registered only inside the Local-mode
/// guard in <c>Program.cs</c>, so this controller is only constructible — and
/// only reached by the client — in Local mode.
/// </para>
/// </summary>
/// <param name="provider">The singleton that resolved and loaded the book.</param>
[ApiController]
[Route("api/openingbook")]
public class OpeningBookController(OpeningBookProvider provider) : ControllerBase
{
    /// <summary>
    /// GET /api/openingbook/status — the loaded book's presence and entry count
    /// as an <see cref="OpeningBookStatus"/>.
    /// </summary>
    [HttpGet("status")]
    public IActionResult Status()
        => Ok(new OpeningBookStatus(provider.Book is not null, provider.Book?.EntryCount ?? 0));
}
