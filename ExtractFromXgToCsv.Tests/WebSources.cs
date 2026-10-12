using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// A fixture file as Web mode holds it — read through the converter's
/// <see cref="XgSourceReader"/> into a <see cref="DecodedSource"/> — and
/// converted through the application's <see cref="XgSourceConverter"/>, for
/// tests below the panel that need a selection's files without a pick.
/// </summary>
internal static class WebSources
{
    private static readonly XgSourceConverter Converter = new();

    /// <summary>
    /// <paramref name="fixture"/> decoded under its own name, with its exact
    /// bytes kept when <paramref name="keepBytes"/>.
    /// </summary>
    public static DecodedSource Decode(string fixture, bool keepBytes = true)
    {
        var read = XgSourceReader.ReadFile(
            Path.Combine(FixtureHelper.FixtureDir, fixture), XgSourceFormat.Xg, retainBytes: keepBytes);
        var decoded = Assert.IsType<XgSourceDecoded>(read);
        return new DecodedSource(fixture, decoded.File, decoded.Bytes);
    }

    /// <summary><paramref name="source"/>'s rows, built for <paramref name="ranking"/> (the application's, by default).</summary>
    public static IReadOnlyList<DecisionRow> Rows(
        DecodedSource source, PlayRanking? ranking = null, OpeningBook? book = null) =>
        Converter.RowsAsync(source, ranking ?? AppRanking.Ranking, book, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary><paramref name="source"/>'s records, the iterator configured for <paramref name="ranking"/>.</summary>
    public static IReadOnlyList<BgDecisionData> Records(
        DecodedSource source, PlayRanking? ranking = null, OpeningBook? book = null) =>
        Converter.RecordsAsync(source, ranking ?? AppRanking.Ranking, book, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Each of <paramref name="rows"/> paired with <paramref name="source"/>, as the XGP export takes them.</summary>
    public static List<XgpDecision> With(this IEnumerable<DecisionRow> rows, DecodedSource source) =>
        [.. rows.Select(row => new XgpDecision(row, source))];
}
