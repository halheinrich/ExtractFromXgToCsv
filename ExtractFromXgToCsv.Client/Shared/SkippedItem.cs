using BgDataTypes_Lib;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// One input a Local-mode run skipped instead of letting it end the batch: a
/// whole source file the run could not process, or — on the Xgp pathway — one
/// decision whose position file could not be written. Carried by
/// <see cref="ProcessingProgress.Skipped"/>. Lives in Client/Shared so the
/// server processor and the WASM panel share the one wire shape.
/// </summary>
/// <param name="FileName">
/// The source file's path relative to the run's input folder. The search is
/// recursive, so a bare name could name two files; rows, decision ids and
/// <see cref="ProcessingProgress.FileName"/> keep the bare name, which is the
/// producer's convention.
/// </param>
/// <param name="DecisionId">
/// The skipped decision's identity, or <see langword="null"/> when the whole
/// file was skipped. Crosses the wire in its canonical string form through the
/// type's own bundled converter.
/// </param>
/// <param name="Reason">
/// Why it was skipped: the message of the exception that skipped it.
/// </param>
public sealed record SkippedItem(string FileName, DecisionId? DecisionId, string Reason);
