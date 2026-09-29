using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using Xunit;
using Xunit.Abstractions;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// End-to-end smoke test for the Make20Pt filter path. Exercises the same
/// FilterConfig.Build + FilteredDecisionIterator.IterateXgDirectory call
/// sequence LocalFolderProcessor uses, against the real fixture .xg files.
///
/// Strategy: compute the Make20Pt reference set directly from the library's
/// documented XOR semantic over all unfiltered checker plays, then require
/// the filter-returned set to match it exactly. Survives fixture changes:
/// adding or removing Make20Pt-category decisions shifts both sides together.
/// </summary>
public class Make20PtSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public void FilterConfigWithMake20Pt_ReturnsExactlyRowsWhereXorHolds()
    {
        // Reference set: unfiltered iteration + manual XOR check.
        var allRows = new FilteredDecisionIterator(
                new DecisionFilterSet(),
                AppRanking.Ranking,
                NullLogger<FilteredDecisionIterator>.Instance)
            .IterateXgDirectory(FixtureHelper.FixtureDir)
            .ToList();

        static bool SatisfiesMake20Pt(DecisionRow r) =>
            r is { Kind: DecisionKind.CheckerPlay, AfterBestBoard: { } best, AfterPlayerBoard: { } player }
            && r.Board[20] < 2
            && ((best[5] <= -2) ^ (player[5] <= -2));

        var referenceXgids = allRows
            .Where(SatisfiesMake20Pt)
            .Select(r => r.Xgid)
            .ToHashSet();

        // Filter-returned set.
        var cfg = new FilterConfig { PlayTypes = { PlayType.Make20Pt } };
        var filteredRows = new FilteredDecisionIterator(
                cfg.Build(),
                AppRanking.Ranking,
                NullLogger<FilteredDecisionIterator>.Instance)
            .IterateXgDirectory(FixtureHelper.FixtureDir)
            .ToList();
        var filteredXgids = filteredRows.Select(r => r.Xgid).ToHashSet();

        output.WriteLine($"Reference XOR-satisfying rows: {referenceXgids.Count}");
        output.WriteLine($"Filter-returned rows:          {filteredXgids.Count}");

        // Non-empty: fixtures must actually exercise the filter. This is a
        // fixture-content guard — if TestData ever loses its Make20Pt match
        // the test yells rather than silently passing over an empty universe.
        Assert.NotEmpty(referenceXgids);

        // Exact match: no false positives, no false negatives, no drops.
        Assert.Equal(referenceXgids, filteredXgids);

        // Spot-check: one returned row's raw after-board values must
        // manifest the XOR. Logs the boards so a human reviewer can verify
        // the training signal is real.
        var sample = filteredRows[0];
        var sampleBest   = sample.AfterBestBoard!.Value;
        var samplePlayer = sample.AfterPlayerBoard!.Value;
        bool bestMakes   = sampleBest  [5] <= -2;
        bool playerMakes = samplePlayer[5] <= -2;
        output.WriteLine($"");
        output.WriteLine($"Spot-check: {sample.Xgid}");
        output.WriteLine($"  Board[20]           = {sample.Board[20]}  (must be < 2)");
        output.WriteLine($"  AfterBestBoard[5]   = {sampleBest[5]}  (<= -2? {bestMakes})");
        output.WriteLine($"  AfterPlayerBoard[5] = {samplePlayer[5]}  (<= -2? {playerMakes})");
        output.WriteLine($"  XOR holds: {bestMakes ^ playerMakes}");
        Assert.True(bestMakes ^ playerMakes);
    }
}
