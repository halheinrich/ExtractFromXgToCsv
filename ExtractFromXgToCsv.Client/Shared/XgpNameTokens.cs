using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// The token registry — the single source of truth for which
/// <c>{token}</c> placeholders an .xgp name pattern may use. Everything
/// downstream derives from <see cref="All"/>: <see cref="XgpNameTemplate"/>
/// validates and renders against it, and the UI builds its insert-token
/// dropdown from it. Adding a token is one added element here; declaration
/// order is dropdown order.
/// </summary>
internal static class XgpNameTokens
{
    /// <summary>Every available token, in dropdown display order.</summary>
    public static IReadOnlyList<XgpNameToken> All { get; } =
    [
        new()
        {
            Name = "n",
            Description = "Counter (next number, zero-padded to the number length)",
            Source = XgpTokenSource.Batch,
            Render = static ctx => (ctx.StartNumber + ctx.Index)
                .ToString($"D{ctx.SuffixLength}", CultureInfo.InvariantCulture),
        },
        new()
        {
            Name = "min-move",
            Description = "Move number minimum from the active filters (empty when unset)",
            Source = XgpTokenSource.Batch,
            Render = static ctx => ctx.Filters.MoveNumberMin
                ?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        },
        new()
        {
            Name = "dice",
            Description = "Dice roll, two digits (\"31\"; \"00\" for a cube decision)",
            Source = XgpTokenSource.PerItem,
            // A cube decision has no roll: the row states none (Roll is null
            // exactly for a cube decision). "00" is this token's established
            // file-name spelling of that absence — presentation only, never a
            // roll, and never handed to anything as one.
            Render = static ctx => ctx.Row.Roll is int roll
                ? roll.ToString("D2", CultureInfo.InvariantCulture)
                : "00",
        },
        new()
        {
            Name = "score",
            Description = "Match score (\"3a5a\", \"1a5aC\", \"moneyJ\", \"moneyNJ\")",
            Source = XgpTokenSource.PerItem,
            Render = static ctx => ctx.Row.MatchScore,
        },
    ];

    private static readonly FrozenDictionary<string, XgpNameToken> _byName =
        All.ToFrozenDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>
    /// Looks up a token by its placeholder name (ordinal match — token names
    /// are case-sensitive by contract).
    /// </summary>
    public static bool TryGet(string name, [NotNullWhen(true)] out XgpNameToken? token) =>
        _byName.TryGetValue(name, out token);
}
