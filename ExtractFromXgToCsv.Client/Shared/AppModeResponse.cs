namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Response body for <c>GET /api/appmode</c>. Lives in Client/Shared so the
/// server controller and the WASM consumer reference the same wire shape —
/// matching the symmetric pattern used by <see cref="ProcessRequest"/> and
/// <see cref="ProcessingProgress"/>.
/// </summary>
public record AppModeResponse(string Mode);
