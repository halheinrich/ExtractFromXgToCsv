using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// What the walk says of a file it left out, read straight off the producer:
/// the text a skip list's game or decision entry carries as its reason.
/// </summary>
internal static class WalkOmissions
{
    /// <summary>
    /// The omissions the walk finds in <paramref name="file"/>, read as
    /// <paramref name="sourceFile"/>, each as <see cref="XgOmission.ToString"/>
    /// words it, in walk order.
    /// </summary>
    public static IReadOnlyList<string> Of(XgFile file, string sourceFile)
    {
        var state = new XgIteratorState();
        _ = new FilteredDecisionIterator(
                new DecisionFilterSet(), AppRanking.Ranking, NullLogger<FilteredDecisionIterator>.Instance)
            .IterateFile(file, sourceFile, state)
            .ToList();
        return [.. state.Omissions.Select(omission => omission.ToString())];
    }
}
