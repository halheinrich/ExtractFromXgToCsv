using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BgDataTypes_Lib;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// The token registry — the single source of truth for which
/// <c>{token}</c> placeholders an .xgp name pattern may use. Everything
/// downstream derives from <see cref="All"/>: <see cref="XgpNameTemplate"/>
/// validates and renders against it, and the UI builds its insert-token
/// dropdown from it. Adding a token is one added element here; declaration
/// order is dropdown order.
/// </summary>
public static class XgpNameTokens
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
            Render = static ctx => ctx.Row.Roll.ToString("D2", CultureInfo.InvariantCulture),
        },
        new()
        {
            Name = "score",
            Description = "Match score (\"3a5a\", \"3a5aC\", \"money\")",
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

    /// <summary>
    /// Synthetic decision used for the UI's live name preview. Field values
    /// are chosen so every current — and anticipated — per-item token renders
    /// something non-trivial (a real roll, a real match score, a mid-game
    /// move number).
    /// </summary>
    public static DecisionRow SampleRow { get; } = new()
    {
        Id = new XgDecisionId("sample.xg", Game: 2, MoveNumber: 5, IsCube: false),
        Xgid = "XGID=-b----E-C---eE---c-e----B-:0:0:1:31:2:4:0:7:10",
        Player = "Player 1",
        Game = 2,
        MoveNumber = 5,
        Roll = 31,
        MatchLength = 7,
        OnRollNeeds = 3,
        OpponentNeeds = 5,
        Error = 0.062,
        Equity = 0.184,
        AnalysisDepth = "3-ply",
    };
}
