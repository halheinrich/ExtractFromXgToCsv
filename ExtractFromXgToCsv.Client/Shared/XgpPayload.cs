using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// One decision's <c>.xgp</c> payload, prepared from its decoded source: the
/// one statement, for both modes, of how a decision becomes a position file.
/// Local mode writes each payload to the output folder; Web mode writes each
/// into the download's archive. Preparing a payload is the per-decision unit
/// of the XGP export: a failure here skips that decision
/// (<c>SPEC-notices.md</c> §7.1), while writing the payload is the commit and
/// is not this type's.
/// </summary>
/// <remarks>
/// The routing is by the decision's id, as the producer stamped it:
/// <list type="bullet">
/// <item>an <see cref="XgDecisionId"/> (from an <c>.xg</c> source) is sliced
/// out of its source through <see cref="XgpExporter"/>'s slice surface, the
/// analysis carried through — XG-SaveAs-equivalent;</item>
/// <item>an <see cref="XgpDecisionId"/> (from an <c>.xgp</c> source) is its
/// source file copied <b>verbatim</b>: the source already is a
/// single-position analysed <c>.xgp</c>, so the copy is byte-identical,
/// cheaper than a re-slice, and strictly more faithful — a re-slice would
/// clear the match-level comments the copy keeps.</item>
/// </list>
/// Anonymizing rewrites every payload's player names to the producer's
/// preset, <see cref="XgpSliceOptions.Anonymized"/>, which names by role
/// ("On-roll" for the decision-maker, "Opponent" for the other): a slice takes
/// the options-bearing overload, and an <c>.xgp</c> source takes the
/// whole-file anonymize-copy (comments and rollouts kept, only the header
/// names change), so the toggle covers every payload. The bool-to-preset
/// mapping is stated here once; a request carries only the intent.
/// </remarks>
public static class XgpPayload
{
    /// <summary>
    /// Whether <paramref name="id"/>'s payload, with names kept, is its source
    /// file's exact bytes — so whoever prepares it must have kept them. True
    /// for a position from an <c>.xgp</c> source.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is null.</exception>
    public static bool CopiesSourceVerbatim(DecisionId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id is XgpDecisionId;
    }

    /// <summary>
    /// Prepares the payload of the decision <paramref name="id"/> names, from
    /// <paramref name="source"/>, the decoded file it came from.
    /// </summary>
    /// <param name="id">The decision, as the producer stamped it.</param>
    /// <param name="source">The decoded source file the decision came from.</param>
    /// <param name="sourceBytes">
    /// The source's exact bytes, as read; required when
    /// <see cref="CopiesSourceVerbatim"/> holds and names are kept, and unused
    /// otherwise.
    /// </param>
    /// <param name="anonymize">Whether to rewrite the player names to the producer's role preset.</param>
    /// <returns>The complete <c>.xgp</c> file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> or <paramref name="source"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The payload is the source's exact bytes and <paramref name="sourceBytes"/> was not supplied.
    /// </exception>
    /// <exception cref="NotSupportedException"><paramref name="id"/> is an id shape no export routes.</exception>
    /// <remarks>
    /// Whatever the producer raises while slicing or re-emitting — a decision
    /// it cannot find, a comment the format cannot carry — propagates: it is
    /// this decision's failure, for the caller to skip.
    /// </remarks>
    public static ReadOnlyMemory<byte> Prepare(
        DecisionId id, XgFile source, ReadOnlyMemory<byte>? sourceBytes, bool anonymize)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(source);

        var names = anonymize ? XgpSliceOptions.Anonymized : null;
        return id switch
        {
            XgpDecisionId => names is null
                ? sourceBytes ?? throw new InvalidOperationException(
                    $"The source of '{id}' is copied verbatim, but its bytes were not kept.")
                : XgpExporter.ToBytes(source, names),
            // The typed id goes straight to the producer's id overload: it
            // destructures the coordinates and ignores the filename, the
            // source being resolved already.
            XgDecisionId xg => names is null
                ? XgpExporter.ToBytes(source, xg)
                : XgpExporter.ToBytes(source, xg, names),
            _ => throw new NotSupportedException(
                $"Unsupported DecisionId shape '{id.GetType().Name}' for .xgp export."),
        };
    }
}
