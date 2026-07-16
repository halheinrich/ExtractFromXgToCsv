using ConvertXgToJson_Lib;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The consumer-side expectation of what an anonymized export looks like,
/// shared by every suite that exercises the anonymize toggle.
/// <see cref="XgpSliceOptions.Anonymized"/> stays the producer's SSOT for the
/// names themselves; this is the single place the tests assert them.
///
/// <para>Every surface the toggle reaches from this app is single-decision —
/// an <c>.xg</c> slice, or a copy of an already-single-position <c>.xgp</c> —
/// so the producer names by decision <i>role</i> rather than by header slot.
/// Which slot the decision-maker occupies varies from decision to decision,
/// so the mapping is re-derived from the exported bytes' own decision record
/// rather than assumed to be slot 1.</para>
/// </summary>
internal static class XgpAnonymizeAssert
{
    /// <summary>
    /// Asserts <paramref name="xgpBytes"/> is anonymized by role: the match
    /// header carries exactly "On-roll" and "Opponent" (so no source name
    /// survives in either slot), and the "On-roll" one is the decision-maker's
    /// — the player every re-read decision is anchored to (for a cube
    /// decision, the doubler).
    /// </summary>
    /// <param name="xgpBytes">The exported single-position <c>.xgp</c> bytes.</param>
    /// <param name="sourceFile">
    /// The export's own file name. Must end in <c>.xgp</c> — that is what
    /// makes the iterator narrow to the single exported position.
    /// </param>
    internal static void IsRoleAnonymized(byte[] xgpBytes, string sourceFile)
    {
        using var ms = new MemoryStream(xgpBytes);
        var file = XgFileReader.ReadStream(ms);

        var info = XgDecisionIterator.ExtractMatchInfo(file)!;
        Assert.Equal(
            ["On-roll", "Opponent"],
            new[] { info.Player1, info.Player2 }.OrderBy(n => n, StringComparer.Ordinal));

        var decisions = XgDecisionIterator.Iterate(file, sourceFile).ToList();
        Assert.NotEmpty(decisions);
        Assert.All(decisions, d => Assert.Equal("On-roll", d.Player));
    }
}
