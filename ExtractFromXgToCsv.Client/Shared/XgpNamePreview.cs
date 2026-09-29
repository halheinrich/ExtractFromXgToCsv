using BgDataTypes_Lib;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// The UI's live <c>.xgp</c> name preview: the sample decision the preview
/// names. App-level, beside the naming engine rather than inside it, because
/// the sample is built for the application's ranking (<see cref="AppRanking"/>)
/// and the engine takes only primitives and library types (see
/// <see cref="XgpNameContext"/>).
/// </summary>
internal static class XgpNamePreview
{
    /// <summary>
    /// Synthetic decision used for the UI's live name preview: a 3-1 from the
    /// standard start, 3-away/5-away in a 7-point match, game 2, move 5. The
    /// values are chosen so every current — and anticipated — per-item token
    /// renders something non-trivial (a real roll, a real match score, a
    /// mid-game move number).
    /// </summary>
    /// <remarks>
    /// Built as the producers build a row, through their public surface: a
    /// record projected by <see cref="DecisionRow.From"/> for the
    /// application's one ranking. So the sample holds every rule the producer
    /// enforces on a record and its row. It is not a position from a real
    /// game: those rules do not relate a board to its move number, and this
    /// one puts the standard start at move 5.
    /// </remarks>
    public static DecisionRow SampleRow { get; } = DecisionRow.From(
        new CheckerPlayDecision
        {
            Id = new XgDecisionId("sample.xg", Game: 2, MoveNumber: 5, IsCube: false),
            Position = new PositionData
            {
                Mop = BoardPosition.Standard,
                CubeSize = 1,
                CubeOwner = CubeOwner.Centered,
                Session = Session.Create(
                    new MatchTerms { Length = 7 },
                    new MatchStanding { Away1 = 3, Away2 = 5, IsCrawford = false },
                    Seat.Player1),
            },
            Decision = new CheckerPlayDecisionData
            {
                Dice = [3, 1],
                Plays =
                [
                    new PlayCandidate
                    {
                        Play = [new(8, 5), new(6, 5)],
                        AnalysisMode = AnalysisMode.Evaluation,
                        AnalysisLevel = AnalysisLevel.Ply3,
                        Equity = 0.184,
                    },
                ],
                UserPlayIndex = 0,
            },
            Descriptive = new DescriptiveData
            {
                OnRollName = "Player 1",
                OpponentName = "Player 2",
                IsStandardStart = true,
                Flagged = false,
            },
        },
        AppRanking.Ranking);
}
