using System.Text.Json.Serialization;
using BgDataTypes_Lib;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// What a <see cref="SkippedItem"/> left out of a run's output (the umbrella's
/// <c>SPEC-notices.md</c> §7.1). Closed: an entry states exactly one, and a
/// <see cref="SkipRecord"/> counts by it.
/// </summary>
/// <remarks>
/// No member is zero, so a body that names no unit binds to no unit at all and
/// <see cref="SkippedItem"/> refuses it, rather than reading it as a file.
/// </remarks>
[JsonConverter(typeof(StrictJsonStringEnumConverter<SkipUnit>))]
public enum SkipUnit
{
    /// <summary>A whole source file: one that could not be read, or whose conversion to an output's representation failed.</summary>
    File = 1,

    /// <summary>A game of a file the run read: the walk omitted it, and the rest of the file contributes.</summary>
    Game = 2,

    /// <summary>
    /// One decision of a file the run read: an export skip, whose decision was
    /// built, or a decision the walk omitted, which never was.
    /// </summary>
    Decision = 3,
}
