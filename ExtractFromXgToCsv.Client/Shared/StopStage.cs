using System.Text.Json.Serialization;
using BgDataTypes_Lib;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Where a stopped Local run stood among its input files, as its record holds
/// it — the opening of the stopped line (Hal's wording, chat, 2026-10-23,
/// halheinrich/backgammon#424). Derived from the record's two facts, the file
/// the run reached and the files it finished, never from
/// <see cref="ProcessingProgress.Current"/> against
/// <see cref="ProcessingProgress.Total"/>: a stop inside the last file has
/// reached it without finishing it.
/// </summary>
[JsonConverter(typeof(StrictJsonStringEnumConverter<StopStage>))]
public enum StopStage
{
    /// <summary>No input file had been taken up: the run reached none.</summary>
    BeforeFirstInputFile,

    /// <summary>An input file had been taken up and not every one finished: the run was in its input work.</summary>
    DuringInput,

    /// <summary>
    /// Every input file was processed — read and committed, or skipped as
    /// unreadable or unconvertible — and the run was past its input: at its
    /// write, or a deck's render.
    /// </summary>
    AfterInput,
}
