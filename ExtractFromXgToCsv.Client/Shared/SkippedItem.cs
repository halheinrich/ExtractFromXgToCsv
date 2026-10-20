using BgDataTypes_Lib;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// One unit a run skipped instead of letting it end the work: a whole
/// source file — one that could not be read, or whose conversion to an
/// output's representation failed — or, on the XGP export, one decision whose
/// payload could not be prepared (the umbrella's <c>SPEC-notices.md</c> §7.1).
/// The one entry type, held in a <see cref="SkipRecord"/>. Lives in
/// Client/Shared so the server processor and the WASM panel share the one
/// shape, which is also the wire shape.
/// </summary>
/// <param name="FileName">
/// The source file, named as the user chose it: its path relative to the
/// run's input folder (the search is recursive, so a bare name could name two
/// files). Rows,
/// decision ids and <see cref="ProcessingProgress.FileName"/> keep the bare
/// name, which is the producer's convention.
/// </param>
/// <param name="DecisionId">
/// The skipped decision's identity, or <see langword="null"/> when the whole
/// file was skipped — file and decision identity stay distinct. Crosses the
/// wire in its canonical string form through the type's own bundled converter.
/// </param>
/// <param name="Reason">
/// Why it was skipped: the message of the failure that skipped it.
/// </param>
public sealed record SkippedItem(string FileName, DecisionId? DecisionId, string Reason);
