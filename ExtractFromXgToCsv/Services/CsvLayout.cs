using BgDataTypes_Lib;
using System.Globalization;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Extract's CSV, stated once: the header, the column order and every cell's
/// formatting, derived from one ordered table of columns so the header and
/// the line cannot drift. The layout is Extract's own, not the shared
/// <see cref="DecisionRow"/>'s, so another app's need for a column never
/// reaches this file (halheinrich/backgammon#424). It formats;
/// <see cref="LocalFolderProcessor"/> writes, and owns the file, its encoding
/// and its line endings.
/// </summary>
/// <remarks>
/// A cube decision's <c>Roll</c> is <c>0</c>: Hal ruled on 2026-10-19/20
/// (halheinrich/backgammon#424) that the CSV carries no <c>Kind</c> column
/// and a cube decision is identified by a roll of 0 — nor a <c>Ranking</c> or
/// <c>Result</c> column, since an error here is the decision taken in the
/// source game, under the app's one ranking. The row keeps its
/// <see langword="null"/> roll; the <c>0</c> is this file's convention.
/// Every other absent fact is an empty cell, never a <c>0</c>, and every
/// number is written in the invariant culture, so a decimal comma cannot
/// split a cell.
/// </remarks>
internal static class CsvLayout
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly (string Name, Func<DecisionRow, string?> Cell)[] Columns =
    [
        ("Xgid", row => Text(row.Xgid)),
        ("Error", row => row.Error?.ToString("G6", Invariant)),
        ("MatchScore", row => Text(row.MatchScore)),
        ("MatchLength", row => row.MatchLength?.ToString(Invariant)),
        ("Player", row => Text(row.Player)),
        ("SourceFile", row => Text(row.SourceFile)),
        ("Game", row => row.Game?.ToString(Invariant)),
        ("MoveNumber", row => row.MoveNumber?.ToString(Invariant)),
        // The cube's 0 stands for the kind the ruled-out column named, so it
        // is keyed on the kind, not on the row's null roll.
        ("Roll", row => row.Kind == DecisionKind.Cube ? "0" : row.Roll?.ToString(Invariant)),
        ("AnalysisDepth", row => Text(row.AnalysisDepth)),
        ("Equity", row => row.Equity.ToString("G6", Invariant)),
    ];

    /// <summary>The header line: the column names in order (no trailing newline).</summary>
    public static string Header { get; } = string.Join(",", Columns.Select(column => column.Name));

    /// <summary>
    /// <paramref name="row"/> as a CSV line under <see cref="Header"/> (no
    /// trailing newline); a <see langword="null"/> fact is an empty cell.
    /// </summary>
    public static string Line(DecisionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Join(",", Columns.Select(column => column.Cell(row)));
    }

    /// <summary>
    /// <paramref name="value"/> as a text cell: quoted, with its quotes
    /// doubled, when it holds a comma, a double quote, CR or LF;
    /// <see langword="null"/> is an empty cell.
    /// </summary>
    private static string? Text(string? value) =>
        value is not null && value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
