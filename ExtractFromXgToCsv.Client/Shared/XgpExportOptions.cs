using System.Globalization;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Naming options for a batch of per-decision <c>.xgp</c> exports. Single
/// source of the <c>{Prefix}{number:D{SuffixLength}}.xgp</c> rule — the WASM
/// zip builder and the server-side Local-mode processor both name entries
/// through <see cref="EntryName"/>.
///
/// <para>
/// Permissive wire DTO like <see cref="ProcessRequest"/> — it crosses the
/// Local-mode POST boundary, so validation is a separate
/// <see cref="TryValidate"/> call (the UI gates on it; server paths throw
/// <see cref="ArgumentException"/>) rather than throwing property
/// initializers that would break deserialization.
/// </para>
/// </summary>
public sealed record XgpExportOptions
{
    /// <summary>Filename prefix, e.g. <c>"pos"</c> for <c>pos001.xgp</c>.</summary>
    public string Prefix { get; init; } = "pos";

    /// <summary>Number assigned to the first exported decision (1-based counter).</summary>
    public int StartNumber { get; init; } = 1;

    /// <summary>
    /// Zero-padded width of the numeric suffix (1–9). Numbers wider than
    /// this grow naturally rather than truncating (e.g. width 3, number
    /// 1000 → <c>pos1000.xgp</c>).
    /// </summary>
    public int SuffixLength { get; init; } = 3;

    /// <summary>
    /// Characters rejected in <see cref="Prefix"/>: the Windows-invalid
    /// filename set, checked explicitly so Web mode (browser sandbox) and
    /// Local mode (server filesystem) enforce the same rule regardless of
    /// what OS the runtime reports.
    /// </summary>
    private static readonly char[] _invalidPrefixChars =
        ['"', '<', '>', '|', ':', '*', '?', '\\', '/'];

    /// <summary>
    /// Filename for the <paramref name="index"/>-th decision of the batch
    /// (0-based): <c>{Prefix}{StartNumber + index:D{SuffixLength}}.xgp</c>.
    /// </summary>
    public string EntryName(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        int number = StartNumber + index;
        return Prefix + number.ToString($"D{SuffixLength}", CultureInfo.InvariantCulture) + ".xgp";
    }

    /// <summary>
    /// Validates the option ranges and the prefix's filename safety.
    /// Returns <see langword="false"/> with a user-displayable
    /// <paramref name="error"/> on the first violation found.
    /// </summary>
    public bool TryValidate(out string error)
    {
        if (Prefix is null)
        {
            error = "Prefix must not be null.";
            return false;
        }
        if (Prefix.IndexOfAny(_invalidPrefixChars) >= 0
            || Prefix.Any(char.IsControl))
        {
            error = "Prefix contains characters not allowed in filenames.";
            return false;
        }
        if (StartNumber < 1)
        {
            error = "Next number must be at least 1.";
            return false;
        }
        if (SuffixLength is < 1 or > 9)
        {
            error = "Suffix length must be between 1 and 9.";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
