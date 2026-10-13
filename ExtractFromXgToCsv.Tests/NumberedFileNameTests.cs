using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="NumberedFileName"/>, the one statement of the Windows-style
/// <c>" (n)"</c> numbering an XGP run's within-run uniquifier and a Local
/// run's next free name share (the umbrella's <c>SPEC-notices.md</c> §7.5).
/// </summary>
public class NumberedFileNameTests
{
    [Fact]
    public void TheFirstName_IsTheStemAlone_AndEachNextIsNumberedOneHigher()
    {
        var first = NumberedFileName.Create("quiz", 1, ".xgp");

        Assert.Equal("quiz.xgp", first.Name);
        Assert.Equal("quiz (2).xgp", first.Next().Name);
        Assert.Equal("quiz (3).xgp", first.Next().Next().Name);
        Assert.Equal("quiz (7).xgp", NumberedFileName.Create("quiz", 7, ".xgp").Name);
    }

    [Fact]
    public void FirstOf_SplitsAFileName_AndANumberItCarriesIsPartOfItsStem()
    {
        var report = NumberedFileName.FirstOf("report.csv");
        Assert.Equal(("report", 1, ".csv"), (report.Stem, report.Ordinal, report.Extension));
        Assert.Equal("report (2).csv", report.Next().Name);

        Assert.Equal("report (2) (2).csv", NumberedFileName.FirstOf("report (2).csv").Next().Name);
        Assert.Equal("notes (2)", NumberedFileName.FirstOf("notes").Next().Name);
    }

    [Fact]
    public void Names_AreEqualByTheirParts_AndPrintAsTheirName()
    {
        Assert.Equal(NumberedFileName.Create("a", 2, ".pdf"), NumberedFileName.FirstOf("a.pdf").Next());
        Assert.NotEqual(NumberedFileName.Create("a", 2, ".pdf"), NumberedFileName.Create("a", 3, ".pdf"));
        Assert.Equal("a (2).pdf", NumberedFileName.Create("a", 2, ".pdf").ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RefusesAnOrdinalBelowOne(int ordinal) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NumberedFileName.Create("a", ordinal, ".csv"));

    [Fact]
    public void Next_RefusesToWrapPastTheLastOrdinal() =>
        Assert.Throws<OverflowException>(() => NumberedFileName.Create("a", int.MaxValue, ".csv").Next());
}
