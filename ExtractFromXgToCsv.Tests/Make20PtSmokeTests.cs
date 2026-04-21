using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using XgFilter_Lib;
using Xunit;
using Xunit.Abstractions;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// End-to-end smoke test for the Make20Pt filter path. Exercises the same
/// FilterSetBuilder.Build + FilteredDecisionIterator.IterateXgDirectory call
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
        var allRows = FilteredDecisionIterator
            .IterateXgDirectory(FixtureHelper.FixtureDir, new())
            .ToList();

        static bool SatisfiesMake20Pt(DecisionRow r) =>
            !r.IsCube
            && r.Board.Count            > 20
            && r.AfterBestBoard.Count   >  5
            && r.AfterPlayerBoard.Count >  5
            && r.Board[20] < 2
            && ((r.AfterBestBoard[5] <= -2) ^ (r.AfterPlayerBoard[5] <= -2));

        var referenceXgids = allRows
            .Where(SatisfiesMake20Pt)
            .Select(r => r.Xgid)
            .ToHashSet();

        // Filter-returned set.
        var cfg = new FilterConfig { PlayTypes = ["Make20Pt"] };
        var set = FilterSetBuilder.Build(cfg);
        var filteredRows = FilteredDecisionIterator
            .IterateXgDirectory(FixtureHelper.FixtureDir, set)
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
        bool bestMakes   = sample.AfterBestBoard  [5] <= -2;
        bool playerMakes = sample.AfterPlayerBoard[5] <= -2;
        output.WriteLine($"");
        output.WriteLine($"Spot-check: {sample.Xgid}");
        output.WriteLine($"  Board[20]           = {sample.Board[20]}  (must be < 2)");
        output.WriteLine($"  AfterBestBoard[5]   = {sample.AfterBestBoard[5]}  (<= -2? {bestMakes})");
        output.WriteLine($"  AfterPlayerBoard[5] = {sample.AfterPlayerBoard[5]}  (<= -2? {playerMakes})");
        output.WriteLine($"  XOR holds: {bestMakes ^ playerMakes}");
        Assert.True(bestMakes ^ playerMakes);
    }
}
