using System.Text.Json;
using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// What the walk leaves out of a file it read reaches the run's record, on all
/// four pathways (the umbrella's <c>SPEC-notices.md</c> §7.1,
/// halheinrich/backgammon#514): a refused game is a game entry and a corrupt
/// candidate a decision entry with no id, each named as every entry is, by the
/// path under the input folder, with the omission's own text as its reason.
/// The file stays read and contributes the rest, under the identifiers it
/// always had. Its omissions are recorded however its stage ended and before
/// its commit, so they precede its conversion skip and outlive a stop or a
/// failed commit. A forced play and an illegal play are neither skips nor
/// omissions. The record covers what the run examined, so a game a filter's
/// early exit skipped says nothing. The seam logs nothing: the producer's own
/// warning is the one line an omission gets.
/// </summary>
/// <remarks>
/// The inputs are synthesized (<see cref="SyntheticXg"/>), except for Hal's
/// three real files. A run's decisions are read from what it really wrote: a
/// CSV's columns, a Diagram JSON's records, each XGP payload as it was
/// prepared, each deck's Problem requests.
/// </remarks>
public class LocalFolderProcessorOmissionTests : IDisposable
{
    /// <summary>The name a synthesized file goes by, in a subfolder so its recorded name is a real path under the input folder.</summary>
    private const string Bare = "match.xg";

    private static readonly string Nested = Path.Combine("club", Bare);

    private readonly string _folder = Directory.CreateTempSubdirectory("xg-omission-").FullName;
    private readonly string _output = Directory.CreateTempSubdirectory("xg-omission-out-").FullName;
    private readonly RecordingLogger<LocalFolderProcessor> _logger = new();
    private readonly RecordingLogger<FilteredDecisionIterator> _iteratorLogger = new();

    public void Dispose()
    {
        foreach (var folder in new[] { _folder, _output })
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    public enum Pathway
    {
        Csv,
        DiagramJson,
        Xgp,
        Deck,
    }

    public static TheoryData<Pathway> Pathways => [Pathway.Csv, Pathway.DiagramJson, Pathway.Xgp, Pathway.Deck];

    public static TheoryData<Pathway> PathwaysThatCompleteAtZero => [Pathway.Csv, Pathway.DiagramJson, Pathway.Xgp];

    private static string Id(int game, int move, string file = Bare) =>
        new XgDecisionId(file, game, move, IsCube: false).ToString();

    /// <summary>
    /// Runs <paramref name="pathway"/> over the input folder and returns the
    /// decisions it wrote, by canonical id, in order.
    /// </summary>
    private async Task<List<string>> RunAsync(
        Pathway pathway,
        RunRecord? record = null,
        DecisionFilterSet? filter = null,
        RecordingProgress? progress = null,
        IOutputDestination? destination = null,
        CancellationToken cancellationToken = default)
    {
        record ??= new RunRecord();
        filter ??= new DecisionFilterSet();
        progress ??= new RecordingProgress();
        var prepared = new List<string>();
        var processor = new LocalFolderProcessor(
            _logger, _iteratorLogger, AppRanking.Ranking, bookProvider: null,
            (id, source, bytes, anonymize) =>
            {
                var payload = XgpPayload.Prepare(id, source, bytes, anonymize);
                prepared.Add(id.ToString());
                return payload;
            },
            destination ?? FileSystemDestination.Instance);

        switch (pathway)
        {
            case Pathway.Csv:
                var csv = Path.Combine(_output, "out.csv");
                await processor.ProcessAsync(_folder, csv, filter, progress, record, cancellationToken);
                return CsvDecisions(csv);

            case Pathway.DiagramJson:
                var json = Path.Combine(_output, "out.json");
                await processor.ProcessDiagramAsync(_folder, json, filter, progress, record, cancellationToken);
                return [.. JsonSerializer.Deserialize<List<BgDecisionData>>(await File.ReadAllTextAsync(json))!
                    .Select(item => item.Id.ToString())];

            case Pathway.Xgp:
                await processor.ProcessXgpAsync(
                    _folder, Path.Combine(_output, "positions"), filter, new XgpExportOptions(), new FilterConfig(),
                    progress, runRecord: record, cancellationToken: cancellationToken);
                return prepared;

            default:
                var renderer = new RecordingDeckRenderer();
                await processor.ProcessDeckAsync(
                    _folder, Path.Combine(_output, "deck.pdf"), filter, progress, renderer.Render, "PDF",
                    record, cancellationToken);
                // Each decision is a Problem and a Solution: the Problems name them.
                return [.. renderer.Requests.Where((_, index) => index % 2 == 0).Select(request => request.Decision!.Id.ToString())];
        }
    }

    private static List<string> CsvDecisions(string path)
    {
        var lines = File.ReadAllLines(path);
        var header = lines[0].Split(',');
        int source = Array.IndexOf(header, "SourceFile");
        int game = Array.IndexOf(header, "Game");
        int move = Array.IndexOf(header, "MoveNumber");
        return [.. lines.Skip(1)
            .Select(line => line.Split(','))
            .Select(cells => Id(int.Parse(cells[game]), int.Parse(cells[move]), cells[source]))];
    }

    private void Write(string relativePath, XgFile file) =>
        SyntheticXg.Write(Path.Combine(_folder, relativePath), file);

    /// <summary>What the producer says of <paramref name="file"/>, read directly: an entry's reason is this, whole.</summary>
    private static IReadOnlyList<string> OmissionsOf(XgFile file)
    {
        var state = new XgIteratorState();
        _ = new FilteredDecisionIterator(
                new DecisionFilterSet(), AppRanking.Ranking, NullLogger<FilteredDecisionIterator>.Instance)
            .IterateFile(file, Bare, state)
            .ToList();
        return [.. state.Omissions.Select(omission => omission.ToString())];
    }

    // ── A refused game ──────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pathways))]
    public async Task ARefusedGame_BetweenValidOnes_IsOneGameEntry_AndTheGamesAroundItKeepTheirIdentifiers(Pathway pathway)
    {
        var file = SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 3), 2);
        Write(Nested, file);
        var record = new RunRecord();
        var progress = new RecordingProgress();

        var decisions = await RunAsync(pathway, record, progress: progress);

        Assert.Equal([Id(1, 1), Id(1, 2), Id(3, 1), Id(3, 2)], decisions);

        var entry = Assert.Single(record.Skips);
        Assert.Equal(SkipUnit.Game, entry.Unit);
        Assert.Equal(Nested, entry.FileName);
        Assert.Null(entry.DecisionId);
        Assert.Equal(Assert.Single(OmissionsOf(file)), entry.Reason);

        Assert.Equal(0, record.Skips.FileCount);
        Assert.Equal(1, record.Skips.GameCount);

        // It reached the terminal snapshot, and the file counts as read.
        Assert.True(progress.Last!.Complete);
        Assert.Equal(record.Skips, progress.Last.Skipped);
        Assert.Equal(4, progress.Last.TotalRows);
        Assert.Equal(1, record.FilesDone);
    }

    // ── A corrupt candidate ─────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pathways))]
    public async Task ACorruptCandidate_IsOneDecisionEntryWithNoId_AndTheDecisionsAroundItStay(Pathway pathway)
    {
        var file = SyntheticXg.WithCorruptCandidate(SyntheticXg.Match(games: 2, moves: 3), game: 1, move: 2);
        Write(Nested, file);
        var record = new RunRecord();

        var decisions = await RunAsync(pathway, record);

        Assert.Equal([Id(1, 1), Id(1, 3), Id(2, 1), Id(2, 2), Id(2, 3)], decisions);

        var entry = Assert.Single(record.Skips);
        Assert.Equal(SkipUnit.Decision, entry.Unit);
        Assert.Equal(Nested, entry.FileName);
        Assert.Null(entry.DecisionId);
        Assert.Equal(Assert.Single(OmissionsOf(file)), entry.Reason);

        Assert.Equal(0, record.Skips.FileCount);
        Assert.Equal(0, record.Skips.GameCount);
        Assert.Equal(1, record.Skips.DecisionCount);
    }

    // ── Neither a skip nor an omission ──────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pathways))]
    public async Task AnIllegalPlay_IsNoEntry_AndNoSkip_AndTheDecisionsAroundItStay(Pathway pathway)
    {
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        builder.AddGame()
            .WithMoves(1)
            .IllegalPlay(XgPlayer.Player2, new DiceRoll(5, 2))
            .Play(XgPlayer.Player2, new DiceRoll(3, 1), [new(8, 5), new(6, 5)]);
        Write(Bare, builder.Build());
        var record = new RunRecord();

        var decisions = await RunAsync(pathway, record);

        Assert.Equal([Id(1, 1), Id(1, 3)], decisions);
        Assert.Empty(record.Skips);
        Assert.Empty(_logger.Entries);
        Assert.Empty(_iteratorLogger.Entries);
    }

    [Theory]
    [MemberData(nameof(Pathways))]
    public async Task AForcedOnlyFile_IsNoEntry_AndNoSkip(Pathway pathway)
    {
        // Beside a file with decisions, so every pathway ends in a success:
        // what this pins is that the forced file left no mark.
        var forced = XgFileBuilder.ForMatch(7, "P1", "P2");
        forced.AddGame().WithForcedPlays(2);
        Write("a-forced.xg", forced.Build());
        Write("b-good.xg", SyntheticXg.Match(games: 1));
        var record = new RunRecord();

        var decisions = await RunAsync(pathway, record);

        Assert.Equal([Id(1, 1, "b-good.xg"), Id(1, 2, "b-good.xg")], decisions);
        Assert.Empty(record.Skips);
        Assert.Equal(2, record.FilesDone);
    }

    // ── Every game omitted ──────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(PathwaysThatCompleteAtZero))]
    public async Task EveryGameOmitted_CompletesAtZero_AndTheEntriesSurvive(Pathway pathway)
    {
        Write(Nested, SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 1, 2));
        var record = new RunRecord();
        var progress = new RecordingProgress();

        var decisions = await RunAsync(pathway, record, progress: progress);

        Assert.Empty(decisions);
        Assert.True(progress.Last!.Complete);
        Assert.Null(progress.Last.ErrorMessage);
        Assert.Equal(0, progress.Last.TotalRows);
        Assert.Equal(new[] { SkipUnit.Game, SkipUnit.Game }, progress.Last.Skipped.Select(entry => entry.Unit));
        Assert.Equal(0, progress.Last.Skipped.FileCount);
    }

    [Fact]
    public async Task EveryGameOmitted_FailsTheDeck_SayingSo_WithTheEntriesOnItsRecord()
    {
        Write(Nested, SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 1, 2));
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(Pathway.Deck, record));

        Assert.Equal("No decisions to render — 2 games were skipped; see the list below.", failure.Message);
        Assert.Equal(2, record.Skips.GameCount);
        Assert.Equal(record.Skips, record.FailedSnapshot(failure.Message).Skipped);
    }

    /// <summary>
    /// The deck's message counts every unit, and says "was" of one entry
    /// alone. Today's two file messages are <c>ProcessEndpointTests</c>'.
    /// </summary>
    [Theory]
    [InlineData(1, 0, "No decisions to render — 1 game was skipped; see the list below.")]
    [InlineData(0, 1, "No decisions to render — 1 decision was skipped; see the list below.")]
    [InlineData(1, 1, "No decisions to render — 1 game and 1 decision were skipped; see the list below.")]
    public async Task TheDecksMessage_NamesWhatWasSkipped(int refusedGames, int corruptDecisions, string message)
    {
        // Game 1 is refused, or a lone move of it is corrupt; a further
        // one-move game holds the corrupt decision when both are asked for.
        var file = SyntheticXg.Match(games: refusedGames + corruptDecisions, moves: 1);
        if (corruptDecisions > 0)
            file = SyntheticXg.WithCorruptCandidate(file, game: refusedGames + 1, move: 1);
        if (refusedGames > 0)
            file = SyntheticXg.WithRefusedGames(file, 1);
        Write(Bare, file);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(Pathway.Deck));

        Assert.Equal(message, failure.Message);
    }

    // ── The record covers what the run examined ─────────────────────────────

    [Theory]
    [MemberData(nameof(Pathways))]
    public async Task AGameAFiltersEarlyExitSkipped_SaysNothing_AndARefusedGameUnderThatFilterStillDoes(Pathway pathway)
    {
        // Game 1 at 0-0 passes the filter. Game 2, at 1-0 with a corrupt
        // candidate, is skipped at its header, so that candidate is never
        // examined. Game 3's refused header reaches no filter.
        var builder = XgFileBuilder.ForMatch(5, "P1", "P2");
        builder.AddGame().WithMoves(2);
        builder.AddGame(score1: 1).WithMoves(2);
        builder.AddGame().WithMoves(2);
        var file = SyntheticXg.WithRefusedGames(
            SyntheticXg.WithCorruptCandidate(builder.Build(), game: 2, move: 1), 3);
        Write(Nested, file);
        var filter = new FilterConfig { MatchScores = { "5a5a" } }.Build();

        var unfiltered = new RunRecord();
        await RunAsync(pathway, unfiltered);
        var filtered = new RunRecord();
        Directory.Delete(_output, recursive: true);
        Directory.CreateDirectory(_output);
        var decisions = await RunAsync(pathway, filtered, filter);

        // The control: over no filter the run examines game 2.
        Assert.Equal(new[] { SkipUnit.Decision, SkipUnit.Game }, unfiltered.Skips.Select(entry => entry.Unit));

        Assert.Equal([Id(1, 1), Id(1, 2)], decisions);
        var entry = Assert.Single(filtered.Skips);
        Assert.Equal(SkipUnit.Game, entry.Unit);
        Assert.Contains("game 3", entry.Reason, StringComparison.Ordinal);
    }

    // ── Several files ───────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pathways))]
    public async Task TwoFilesWithOmissions_AreEntriesInWalkOrder_AndNothingLeaksBetweenFiles(Pathway pathway)
    {
        // Each file's entries come in walk order, and the clean file between
        // them adds none.
        var first = SyntheticXg.WithCorruptCandidate(
            SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 1), game: 2, move: 1);
        var last = SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 2);
        Write("a.xg", first);
        Write("b.xg", SyntheticXg.Match(games: 1));
        Write(Path.Combine("c", "z.xg"), last);
        var record = new RunRecord();

        await RunAsync(pathway, record);

        Assert.Equal(
            ["a.xg", "a.xg", Path.Combine("c", "z.xg")],
            record.Skips.Select(entry => entry.FileName));
        Assert.Equal(
            [SkipUnit.Game, SkipUnit.Decision, SkipUnit.Game],
            record.Skips.Select(entry => entry.Unit));
        Assert.Contains("game 1", record.Skips[0].Reason, StringComparison.Ordinal);
        Assert.Contains("game 2, move 1", record.Skips[1].Reason, StringComparison.Ordinal);
        Assert.Contains("z.xg, game 2", record.Skips[2].Reason, StringComparison.Ordinal);
    }

    // ── However the stage ended ─────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pathways))]
    public async Task AFileWhoseStageFailsAfterAnOmission_KeepsItsOmission_ThenItsConversionSkip_AndTheNextFileGoesOn(Pathway pathway)
    {
        // The filter's first call is the first decision after a's refused
        // game, so a's stage fails having omitted it.
        Write("a.xg", SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 1));
        Write("b.xg", SyntheticXg.Match(games: 1));
        var record = new RunRecord();

        var decisions = await RunAsync(pathway, record, new DecisionFilterSet().Add(new ThrowAtCall(1, new InvalidOperationException("Failed midway."))));

        Assert.Equal([SkipUnit.Game, SkipUnit.File], record.Skips.Select(entry => entry.Unit));
        Assert.All(record.Skips, entry => Assert.Equal("a.xg", entry.FileName));
        Assert.Equal("Failed midway.", record.Skips[1].Reason);
        Assert.Equal([Id(1, 1, "b.xg"), Id(1, 2, "b.xg")], decisions);
    }

    [Theory]
    [MemberData(nameof(Pathways))]
    public async Task ACancellationOutOfAStage_LeavesTheFilesOmissions_AndIsNoSkip(Pathway pathway)
    {
        Write("a.xg", SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 1));
        var record = new RunRecord();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(
            pathway, record, new DecisionFilterSet().Add(new ThrowAtCall(1, new OperationCanceledException()))));

        Assert.Equal(SkipUnit.Game, Assert.Single(record.Skips).Unit);
    }

    [Theory]
    [MemberData(nameof(Pathways))]
    public async Task AStopAfterAFileWithOmissions_LeavesThemOnTheStoppedSnapshot(Pathway pathway)
    {
        // The stop lands inside a's conversion, so a is committed and the walk
        // sees the stop before b, whose omission is never found.
        Write("a.xg", SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 1));
        Write("b.xg", SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 2));
        var record = new RunRecord();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(
            pathway, record, CancelAtCall.Set(1, cts), cancellationToken: cts.Token));

        var stopped = record.CancelledSnapshot();
        var entry = Assert.Single(stopped.Skipped);
        Assert.Equal(SkipUnit.Game, entry.Unit);
        Assert.Equal("a.xg", entry.FileName);
    }

    [Fact]
    public async Task AFailedCommit_OfTheFileWithOmissions_LeavesThemOnTheFailedSnapshot()
    {
        // XGP commits inside the walk, so this fails while that file's commit
        // is under way: its omissions reached the record before it began.
        Write(Nested, SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 1));
        var record = new RunRecord();
        var destination = new ScriptedDestination { FailWriteOf = "pos001.xgp" };

        var failure = await Assert.ThrowsAsync<UnfinishedFileException>(
            () => RunAsync(Pathway.Xgp, record, destination: destination));

        var failed = record.FailedSnapshot(failure.Message);
        Assert.Equal(SkipUnit.Game, Assert.Single(failed.Skipped).Unit);
        Assert.Equal(Nested, failed.Skipped[0].FileName);
    }

    [Fact]
    public async Task AFailedWriteAfterTheWalk_LeavesTheOmissionsOnTheFailedSnapshot()
    {
        Write(Nested, SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 1));
        var record = new RunRecord();
        var destination = new ScriptedDestination { FailWriteOf = "out.json" };

        var failure = await Assert.ThrowsAsync<IOException>(
            () => RunAsync(Pathway.DiagramJson, record, destination: destination));

        Assert.Equal(SkipUnit.Game, Assert.Single(record.FailedSnapshot(failure.Message).Skipped).Unit);
    }

    // ── One log line ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pathways))]
    public async Task EachOmission_IsOneWarning_FromTheIteratorsLogger_AndNoneFromTheProcessors(Pathway pathway)
    {
        Write(Bare, SyntheticXg.WithCorruptCandidate(
            SyntheticXg.WithRefusedGames(SyntheticXg.Match(games: 2), 1), game: 2, move: 1));
        var record = new RunRecord();

        await RunAsync(pathway, record);

        Assert.Equal(2, record.Skips.Count);
        Assert.Equal(2, _iteratorLogger.Entries.Count(entry => entry.Level == LogLevel.Warning));
        Assert.Empty(_logger.Entries);
    }

    // ── Hal's three files ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "RequiresFixtureFiles")]
    public async Task HalsThreeFiles_AreFiveGameEntries_AndNoFileSkip()
    {
        const string Hardigree = "2024-07-03_Jonathan_Hardigree_vs_Michael_Mesich_29046.xg";
        const string Norberg = "2024-11-30_Johan_Norberg_vs_Konrad_Fröschl_34083.xg";
        const string Mtch = "MTCH2987.xg";
        foreach (var fixture in new[] { Hardigree, Norberg, Mtch })
            File.Copy(Path.Combine(FixtureHelper.FixtureDir, fixture), Path.Combine(_folder, fixture));
        var record = new RunRecord();

        var decisions = await RunAsync(Pathway.Csv, record);

        Assert.NotEmpty(decisions);
        Assert.Equal(0, record.Skips.FileCount);
        Assert.Equal(0, record.Skips.DecisionCount);
        Assert.Equal(5, record.Skips.GameCount);
        Assert.Equal(
            [(Hardigree, 7), (Norberg, 9), (Mtch, 1), (Mtch, 2), (Mtch, 3)],
            record.Skips.Select(entry => (entry.FileName, GameNumberIn(entry.Reason, entry.FileName))));
    }

    /// <summary>The game an omission's reason names: <c>"Invalid standing in {file}, game {n}: …"</c>.</summary>
    private static int GameNumberIn(string reason, string file)
    {
        var opening = $"Invalid standing in {file}, game ";
        Assert.StartsWith(opening, reason, StringComparison.Ordinal);
        return int.Parse(reason[opening.Length..reason.IndexOf(':', opening.Length)]);
    }

    /// <summary>
    /// A filter that passes every decision until call <paramref name="throwAt"/>,
    /// where it throws <paramref name="failure"/> — the walk of the file that
    /// decision belongs to ends there. It counts, so each run takes a fresh one.
    /// </summary>
    private sealed class ThrowAtCall(int throwAt, Exception failure) : IDecisionFilter
    {
        private int _calls;

        public bool Matches(IDecisionFilterData data) => ++_calls == throwAt ? throw failure : true;
    }
}
