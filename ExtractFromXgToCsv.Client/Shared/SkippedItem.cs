using System.Text.Json.Serialization;
using BgDataTypes_Lib;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// One unit a run skipped instead of letting it end the work, the
/// <see cref="Unit"/> it states: a whole source file — one that could not be
/// read, or whose conversion to an output's representation failed — or a game
/// or a decision the walk left out of a file it read, or, on the XGP export,
/// one decision whose payload could not be prepared (the umbrella's
/// <c>SPEC-notices.md</c> §7.1). The one entry type, held in a
/// <see cref="SkipRecord"/>. Lives in Client/Shared so the server processor
/// and the WASM panel share the one shape, which is also the wire shape.
/// </summary>
/// <remarks>
/// An entry claims one unit: <see cref="ForFile"/>, <see cref="ForGame"/> and
/// <see cref="ForDecision"/> are the only ways to build one, and the
/// constructor they share refuses a contradiction, which is what a body read
/// off the wire reaches. An entry with no <see cref="DecisionId"/> is therefore
/// not a file: a decision the walk omitted has none.
/// </remarks>
public sealed record SkippedItem
{
    // Private, so nothing outside can state a unit and a decision that
    // disagree; the serializer is its only other caller, and a body that
    // disagrees is refused here.
    [JsonConstructor]
    private SkippedItem(SkipUnit unit, string fileName, DecisionId? decisionId, string reason)
    {
        if (!Enum.IsDefined(unit))
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "A skipped unit is a file, a game or a decision.");
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(reason);
        if (decisionId is not null && unit != SkipUnit.Decision)
            throw new ArgumentException("Only a decision carries a decision id.", nameof(decisionId));

        Unit = unit;
        FileName = fileName;
        DecisionId = decisionId;
        Reason = reason;
    }

    /// <summary>A whole file skipped.</summary>
    /// <param name="fileName">See <see cref="FileName"/>.</param>
    /// <param name="reason">See <see cref="Reason"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> or <paramref name="reason"/> is null.</exception>
    public static SkippedItem ForFile(string fileName, string reason) =>
        new(SkipUnit.File, fileName, decisionId: null, reason);

    /// <summary>A game of <paramref name="fileName"/> skipped.</summary>
    /// <inheritdoc cref="ForFile"/>
    public static SkippedItem ForGame(string fileName, string reason) =>
        new(SkipUnit.Game, fileName, decisionId: null, reason);

    /// <summary>A decision of <paramref name="fileName"/> skipped.</summary>
    /// <param name="fileName">See <see cref="FileName"/>.</param>
    /// <param name="decisionId">See <see cref="DecisionId"/>.</param>
    /// <param name="reason">See <see cref="Reason"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> or <paramref name="reason"/> is null.</exception>
    public static SkippedItem ForDecision(string fileName, DecisionId? decisionId, string reason) =>
        new(SkipUnit.Decision, fileName, decisionId, reason);

    /// <summary>What was skipped. A body that names no unit is refused, never read as a file.</summary>
    public SkipUnit Unit { get; }

    /// <summary>
    /// The source file, named as the user chose it: its path relative to the
    /// run's input folder (the search is recursive, so a bare name could name
    /// two files). Rows, decision ids, a <see cref="Reason"/> the walk wrote
    /// and <see cref="ProcessingProgress.FileName"/> keep the bare name, which
    /// is the producer's convention.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// The skipped decision's identity, where one was built — file and decision
    /// identity stay distinct. <see langword="null"/> on a file or a game, and
    /// on a decision the walk omitted. Crosses the wire in its canonical string
    /// form through the type's own bundled converter.
    /// </summary>
    public DecisionId? DecisionId { get; }

    /// <summary>
    /// Why it was skipped: the message of the failure that skipped it, or what
    /// the walk says of its omission, whole. Text for a reader, which can hold
    /// a line break; nothing parses it.
    /// </summary>
    public string Reason { get; }
}
