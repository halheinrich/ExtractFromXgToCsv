using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExtractFromXgToCsv.Controllers;

/// <summary>
/// Local-mode pass-through for the client's saved-filters documents: reads and
/// writes one named text file in the request's source folder via
/// <see cref="FilterDocumentStore"/>. The server relays bytes and owns safety
/// (the simple-filename rule), never policy — which files exist and what they
/// contain is entirely the client's business.
/// <para>
/// The Local-mode gate is an <b>explicit action guard</b> (<c>404</c> when
/// <see cref="AppModeService.IsLocal"/> is false), deviating from the
/// <see cref="OpeningBookController"/> DI-constructibility precedent
/// deliberately: an observable 404 — "this endpoint does not exist in Web
/// mode" — is testable end to end, where an unresolvable constructor
/// dependency is only a 500.
/// </para>
/// </summary>
/// <param name="store">The file relay doing the actual IO.</param>
/// <param name="appMode">The configured mode; non-Local requests get 404.</param>
[ApiController]
[Route("api/filterdocument")]
public class FilterDocumentController(
    FilterDocumentStore store,
    AppModeService appMode) : ControllerBase
{
    /// <summary>
    /// GET /api/filterdocument?folder=…&amp;name=… — the named file's text as
    /// <c>text/plain</c>, or <c>204 No Content</c> when the file (or folder)
    /// is absent. Absence is a value: the client seam maps 204 to its
    /// null-for-absent contract, so a fresh folder is an empty context, never
    /// an error.
    /// </summary>
    /// <param name="folder">The source folder to read from.</param>
    /// <param name="name">The simple file name inside it.</param>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string folder, [FromQuery] string name)
    {
        if (Validate(folder, name) is { } problem) return problem;

        var content = await store.ReadAsync(folder, name);
        return content is null ? NoContent() : Content(content, "text/plain");
    }

    /// <summary>
    /// PUT /api/filterdocument?folder=…&amp;name=… — writes the raw request
    /// body as the named file's full text, overwriting any existing file. IO
    /// failures (e.g. the folder doesn't exist) surface as 500; the client
    /// adapter degrades them to its write-failed state.
    /// </summary>
    /// <param name="folder">The source folder to write into.</param>
    /// <param name="name">The simple file name inside it.</param>
    [HttpPut]
    public async Task<IActionResult> Put([FromQuery] string folder, [FromQuery] string name)
    {
        if (Validate(folder, name) is { } problem) return problem;

        using var reader = new StreamReader(Request.Body);
        var content = await reader.ReadToEndAsync();
        await store.WriteAsync(folder, name, content);
        return Ok();
    }

    // The shared request gate: the mode guard first (a Web deployment serves
    // no file relay at all), then the shape rules the relay's safety rides on.
    private IActionResult? Validate(string folder, string name)
    {
        if (!appMode.IsLocal) return NotFound();
        if (string.IsNullOrWhiteSpace(folder))
            return BadRequest("A source folder is required.");
        if (!FilterDocumentStore.IsValidFileName(name))
            return BadRequest("The name must be a simple file name — no paths.");
        return null;
    }
}
