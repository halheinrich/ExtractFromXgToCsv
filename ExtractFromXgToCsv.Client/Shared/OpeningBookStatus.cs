namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Response body for <c>GET /api/openingbook/status</c> (Local mode only):
/// whether the server loaded XG's opening book, and how many entries it holds.
/// Lives in Client/Shared so the server controller and the WASM consumer
/// reference the same wire shape — matching <see cref="AppModeResponse"/>.
/// Lets the Local-mode UI tell the user which analysis-depth stamps to expect
/// (enriched book labels vs. level Unknown) without the client parsing any
/// <c>.ob</c> file itself — that is the server's concern in Local mode.
/// </summary>
/// <param name="Loaded">Whether a book is loaded and enriching decisions.</param>
/// <param name="EntryCount">Entry count of the loaded book; <c>0</c> when none is loaded.</param>
public record OpeningBookStatus(bool Loaded, int EntryCount);
