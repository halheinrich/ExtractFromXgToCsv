using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// A fixture file's decisions as <see cref="LocalFolderProcessor"/> reaches
/// them: the file read once through the converter's
/// <see cref="XgSourceReader"/>, then its rows or its records through
/// XgFilter_Lib's filtering operation under a ranking, the empty filter
/// admitting every decision.
/// </summary>
internal static class ProcessorDecisions
{
    /// <summary><paramref name="fixture"/>, read as the processor reads a source.</summary>
    public static XgFile Read(string fixture)
    {
        var read = XgSourceReader.ReadFile(
            Path.Combine(FixtureHelper.FixtureDir, fixture), XgSourceFormat.Xg, retainBytes: false);
        return Assert.IsType<XgSourceDecoded>(read).File;
    }

    /// <summary><paramref name="file"/>'s rows, by its name <paramref name="fixture"/>, built for <paramref name="ranking"/>.</summary>
    public static IReadOnlyList<DecisionRow> Rows(XgFile file, string fixture, PlayRanking ranking) =>
        Filtering(ranking).IterateFile(file, fixture).ToList();

    /// <summary><paramref name="file"/>'s records, by its name <paramref name="fixture"/>, the filtering configured for <paramref name="ranking"/>.</summary>
    public static IReadOnlyList<BgDecisionData> Records(XgFile file, string fixture, PlayRanking ranking) =>
        Filtering(ranking).IterateFileDiagrams(file, fixture).ToList();

    private static FilteredDecisionIterator Filtering(PlayRanking ranking) =>
        new(new DecisionFilterSet(), ranking, NullLogger<FilteredDecisionIterator>.Instance);
}
