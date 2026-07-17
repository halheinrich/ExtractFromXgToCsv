using BgDataTypes_Lib;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Everything an <see cref="XgpNameToken"/> may draw on when rendering its
/// value for one exported decision.
///
/// <para>
/// Deliberately built from primitives and library types
/// (<see cref="FilterConfig"/>, <see cref="DecisionRow"/>) — never the app's
/// <see cref="XgpExportOptions"/> DTO. That keeps the naming engine free of
/// app-type coupling: <see cref="XgpNameAllocator.Create"/> is where the DTO
/// stops, so a future move of the engine into a library is relocation-only.
/// </para>
/// </summary>
internal sealed record XgpNameContext
{
    /// <summary>Number the <c>{n}</c> token assigns to the batch's first decision.</summary>
    public required int StartNumber { get; init; }

    /// <summary>Zero-padded width of the <c>{n}</c> token's value.</summary>
    public required int SuffixLength { get; init; }

    /// <summary>The batch's active filters — the source for batch-constant tokens like <c>{min-move}</c>.</summary>
    public required FilterConfig Filters { get; init; }

    /// <summary>The decision being named — the source for per-item tokens like <c>{dice}</c>.</summary>
    public required DecisionRow Row { get; init; }

    /// <summary>0-based slot of this decision within the export run.</summary>
    public required int Index { get; init; }
}
