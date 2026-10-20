using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

/// <summary>
/// Exposes the server's opening-book load state via
/// <c>GET /api/openingbook/status</c>, so the WASM front end can show whether
/// book-analysed decisions will be enriched or degrade to level Unknown.
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
