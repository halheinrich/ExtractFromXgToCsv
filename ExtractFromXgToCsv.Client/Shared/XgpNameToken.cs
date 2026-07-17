namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// One <c>{token}</c> placeholder usable in an .xgp name pattern: its name
/// (as written between the braces), user-facing description, source
/// classification, and render function. Instances live in the
/// <see cref="XgpNameTokens"/> registry — the single source of truth for
/// which tokens exist; adding a token is one added registry element.
/// </summary>
internal sealed record XgpNameToken
{
    /// <summary>Placeholder name as written between braces, e.g. <c>"min-move"</c>. Matched ordinally.</summary>
    public required string Name { get; init; }

    /// <summary>User-facing description shown in the insert-token dropdown.</summary>
    public required string Description { get; init; }

    /// <summary>Whether the value is batch-constant or varies per decision.</summary>
    public required XgpTokenSource Source { get; init; }

    /// <summary>
    /// Produces the token's value for one decision. Must not fail — a value
    /// that isn't available renders as the empty string. Output is sanitized
    /// by <see cref="XgpNameTemplate"/> before it reaches a filename.
    /// </summary>
    public required Func<XgpNameContext, string> Render { get; init; }
}
