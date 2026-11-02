using System.Text;
using System.Text.Json.Nodes;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The inputs the omission tests need, synthesized through the producer's
/// public builder and written as the <c>.xg</c> bytes a run reads, so they
/// depend on nothing in <c>TestData</c>. XgFilter_Lib.Tests'
/// <c>SyntheticSources</c> is the worked form: a refused game and a corrupt
/// candidate have no public builder door, which is rightly so, and are made by
/// editing a built file's JSON document and reading it back.
/// </summary>
internal static class SyntheticXg
{
    /// <summary>
    /// Six legal plays from the standard opening, alternating player 1 and
    /// player 2, each in the mover's own numbering. Each leaves a real choice,
    /// so each is a decision, and any prefix of it is a game.
    /// </summary>
    private static readonly (DiceRoll Dice, Play Played)[] OpeningMoves =
    [
        (new DiceRoll(3, 1), [new(8, 5), new(6, 5)]),
        (new DiceRoll(3, 1), [new(8, 5), new(6, 5)]),
        (new DiceRoll(4, 2), [new(8, 4), new(6, 4)]),
        (new DiceRoll(4, 2), [new(8, 4), new(6, 4)]),
        (new DiceRoll(6, 1), [new(13, 7), new(8, 7)]),
        (new DiceRoll(6, 1), [new(13, 7), new(8, 7)]),
    ];

    // One checker of each side, player 1's on the 24-point: a 3-1 reaches one
    // position however it is played, so the play from here is forced.
    private static readonly int[] OneCheckerOn24 = OneCheckerEach();

    private static int[] OneCheckerEach()
    {
        var board = new int[26];
        board[24] = 1;
        board[1] = -1;
        return board;
    }

    private static readonly Play ForcedPlay = [new(24, 21), new(21, 20)];

    /// <summary>
    /// Plays the first <paramref name="moves"/> of the opening into
    /// <paramref name="game"/>, player 1 first, each an analysed decision
    /// numbered from 1.
    /// </summary>
    public static XgGameBuilder WithMoves(this XgGameBuilder game, int moves)
    {
        for (int i = 0; i < moves; i++)
        {
            var player = i % 2 == 0 ? XgPlayer.Player1 : XgPlayer.Player2;
            game.Play(player, OpeningMoves[i].Dice, OpeningMoves[i].Played);
        }
        return game;
    }

    /// <summary>
    /// Records <paramref name="plays"/> analysed forced plays in
    /// <paramref name="game"/>: the walk passes them by, with no decision and
    /// no omission.
    /// </summary>
    public static XgGameBuilder WithForcedPlays(this XgGameBuilder game, int plays)
    {
        for (int i = 0; i < plays; i++)
            game.AtPosition(OneCheckerOn24).Play(XgPlayer.Player1, new DiceRoll(3, 1), ForcedPlay);
        return game;
    }

    /// <summary>
    /// A 7-point match of <paramref name="games"/> games of
    /// <paramref name="moves"/> decisions each, game n at player 1 n-1, player
    /// 2 0, so a filter on the score can tell them apart.
    /// </summary>
    public static XgFile Match(int games, int moves = 2)
    {
        var builder = XgFileBuilder.ForMatch(7, "P1", "P2");
        for (int game = 0; game < games; game++)
            builder.AddGame(score1: game, score2: 0).WithMoves(moves);
        return builder.Build();
    }

    /// <summary>
    /// <paramref name="file"/> with the 1-based <paramref name="games"/>'
    /// headers saying player 1 has already won the match, a standing the
    /// session refuses, so the walk omits each of those games whole and keeps
    /// the numbers of the rest.
    /// </summary>
    public static XgFile WithRefusedGames(XgFile file, params int[] games) => Edited(file, records =>
    {
        var headers = records.Where(record => TypeOf(record) == "HeaderGame").ToList();
        int length = (int)records.Single(record => TypeOf(record) == "HeaderMatch")!["matchLength"]!;
        foreach (int game in games)
            headers[game - 1]!["score1"] = length;
    });

    /// <summary>
    /// <paramref name="file"/> with the first candidate of the 1-based
    /// <paramref name="move"/> of the 1-based <paramref name="game"/> opening
    /// from the 2-point, where the opening has no checker, so the walk omits
    /// that move and keeps the decisions around it.
    /// </summary>
    public static XgFile WithCorruptCandidate(XgFile file, int game, int move) => Edited(file, records =>
    {
        var header = records.Where(record => TypeOf(record) == "HeaderGame").ElementAt(game - 1);
        var candidate = records
            .SkipWhile(record => record != header)
            .Skip(1)
            .TakeWhile(record => TypeOf(record) != "HeaderGame")
            .Where(record => TypeOf(record) == "Move")
            .ElementAt(move - 1)!["analysis"]!["moves"]![0]!;
        candidate[0] = 1;
        candidate[1] = 0;
    });

    /// <summary>Writes <paramref name="file"/> as an <c>.xg</c> at <paramref name="path"/>, creating its folder if needed.</summary>
    public static void Write(string path, XgFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, XgFileWriter.ToBytes(file));
    }

    /// <summary><paramref name="file"/> with <paramref name="edit"/> applied to its JSON document's records, read back.</summary>
    private static XgFile Edited(XgFile file, Action<JsonArray> edit)
    {
        var document = JsonNode.Parse(XgFileReader.ToJson(file))!;
        edit(document["records"]!.AsArray());

        var edited = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()));
        return XgSourceReader.ReadStream("edited.json", edited, XgSourceFormat.Json)
            .Match(decoded => decoded.File, failed => throw failed.Exception);
    }

    private static string? TypeOf(JsonNode? record) => (string?)record!["$type"];
}
