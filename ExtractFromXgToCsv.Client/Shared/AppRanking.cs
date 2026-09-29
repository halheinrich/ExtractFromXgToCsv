using BgDataTypes_Lib;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// The one ranking this application runs under: the application's choice,
/// stated once, of which <see cref="PlayRanking"/> a run's rows, its filtering
/// and its diagrams are interpreted under (SPEC-scoring §2a;
/// halheinrich/backgammon#282).
/// </summary>
/// <remarks>
/// <para>
/// This application has no ranking setting, so it selects the specified
/// default, <see cref="PlayRanking.Equity"/> — "Apps without the setting use
/// the default." It states that choice here and passes this same value to
/// every producer operation whose meaning depends on a ranking: the iterator's
/// options (<c>XgIteratorOptions</c>, so every row is built for it), each
/// record's filter view (<see cref="BgDecisionData.ViewFor"/>), a row built by
/// hand (<see cref="DecisionRow.From"/>, the name preview's sample) and each
/// diagram (<c>DiagramRequest.ForDecision</c>). No producer's own default
/// stands in for it, so one run is one ranking whatever those defaults are.
/// </para>
/// <para>
/// What a ranking <em>means</em> — the order, the best play, each play's error
/// and whether it is scored — is BgDataTypes_Lib's, and nothing here restates
/// it. Public because the server reads it too: its composition root hands it
/// to the Local-mode processor.
/// </para>
/// </remarks>
public static class AppRanking
{
    /// <summary>The ranking every run is interpreted under: <see cref="PlayRanking.Equity"/>.</summary>
    public static PlayRanking Ranking => PlayRanking.Equity;
}
