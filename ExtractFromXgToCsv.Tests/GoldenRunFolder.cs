using System.Diagnostics.CodeAnalysis;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Resolves a golden run's label to the folder its output goes into, or
/// refuses it (halheinrich/backgammon#424). The rule is checked on the
/// resolved path, not the label's spelling: on Windows <c>INPUT</c> and
/// <c>input.</c> resolve to the frozen input folder, and <c>..</c> to
/// <c>TestData</c> itself. An existing folder is refused, so a run never
/// writes over, or needs to delete, an earlier baseline.
/// </summary>
internal static class GoldenRunFolder
{
    // Windows resolves paths case-insensitively; elsewhere case is significant.
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Resolves <paramref name="label"/> to a new folder directly below
    /// <paramref name="goldenFolder"/>.
    /// </summary>
    /// <param name="goldenFolder">The folder every run folder lies below.</param>
    /// <param name="inputFolder">The frozen input folder, which no run may write into.</param>
    /// <param name="label">The run's label: one path segment.</param>
    /// <param name="runFolder">The resolved run folder, when accepted.</param>
    /// <param name="refusal">Why the label is refused, when it is.</param>
    /// <returns>Whether the label is accepted.</returns>
    internal static bool TryResolve(
        string goldenFolder,
        string inputFolder,
        string? label,
        [NotNullWhen(true)] out string? runFolder,
        [NotNullWhen(false)] out string? refusal)
    {
        runFolder = null;

        if (string.IsNullOrWhiteSpace(label))
        {
            refusal = "The label is empty.";
            return false;
        }
        if (label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || label.Contains('/') || label.Contains('\\')
            || label is "." or ".."
            || Path.GetFileName(label) != label)
        {
            refusal = $"The label '{label}' is not a single plain folder name.";
            return false;
        }

        var golden = Path.TrimEndingDirectorySeparator(Path.GetFullPath(goldenFolder));
        var input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputFolder));
        var resolved = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(golden, label)));

        if (!resolved.StartsWith(golden + Path.DirectorySeparatorChar, PathComparison))
        {
            refusal = $"The label '{label}' resolves to '{resolved}', which is not below '{golden}'.";
            return false;
        }
        if (string.Equals(resolved, input, PathComparison))
        {
            refusal = $"The label '{label}' resolves to the input folder '{input}'.";
            return false;
        }
        if (Directory.Exists(resolved) || File.Exists(resolved))
        {
            refusal = $"'{resolved}' already exists; choose a new label. Earlier runs are never overwritten.";
            return false;
        }

        runFolder = resolved;
        refusal = null;
        return true;
    }
}
