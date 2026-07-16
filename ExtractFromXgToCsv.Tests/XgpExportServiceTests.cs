using System.Globalization;
using System.IO.Compression;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="XgProcessingService.BuildXgpZip"/> — the Web-mode .xgp
/// export seam. The semantic oracle is the in-app round-trip: exported bytes
/// re-read through the same service yield exactly one decision whose XGID
/// equals the source decision's (the XGID digests position, cube, dice, and
/// match state). Byte-level .xgp correctness is owned by the producer's
/// XgpExporter tests; these tests own the app-side batching, naming, and
/// source-routing rules. Name-pattern grammar and uniquifier mechanics are
/// owned by <see cref="XgpNameTemplateTests"/> /
/// <see cref="XgpNameAllocatorTests"/> — here they're exercised through the
/// zip seam.
///
/// Fixtures are pinned by name — TestData/FixtureFiles is append-only.
/// </summary>
public class XgpExportServiceTests
{
    private const string XgFixture = "MTCH4064.xg";
    private const string XgpFixture = "MTCH4064_1_22.xgp";

    private readonly XgProcessingService _service = new();

    private static Dictionary<string, byte[]> Sources(params string[] names) =>
        names.ToDictionary(n => n, FixtureHelper.ReadFixture);

    private static List<ZipArchiveEntry> ReadEntries(byte[] zipBytes, out ZipArchive zip)
    {
        zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        return zip.Entries.ToList();
    }

    private static byte[] EntryBytes(ZipArchiveEntry entry)
    {
        using var ms = new MemoryStream();
        using var es = entry.Open();
        es.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void EntryNames_FollowPatternStartNumberAndSuffixLength()
    {
        // Counter unification: "quiz{n}" must produce byte-identical names
        // to what the removed Prefix="quiz" option produced.
        var sources = Sources(XgFixture);
        var rows = _service.ExtractDecisions(sources[XgFixture], XgFixture)
            .Take(3).ToList();
        Assert.Equal(3, rows.Count);

        var options = new XgpExportOptions
        {
            NamePattern = "quiz{n}",
            StartNumber = 12,
            SuffixLength = 4,
        };
        var zipBytes = _service.BuildXgpZip(sources, rows, options, new FilterConfig());

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal(
                new[] { "quiz0012.xgp", "quiz0013.xgp", "quiz0014.xgp" },
                entries.Select(e => e.FullName));
        }
    }

    [Fact]
    public void TokenPattern_DrawsBatchTokensFromFiltersAndItemTokensFromEachRow()
    {
        var sources = Sources(XgFixture);
        var all = _service.ExtractDecisions(sources[XgFixture], XgFixture).ToList();

        // Two rows guaranteed to render distinct names, so no uniquifier
        // muddies the expected strings.
        var first = all[0];
        var second = all.First(r =>
            r.Roll != first.Roll || r.MatchScore != first.MatchScore);
        var rows = new List<DecisionRow> { first, second };

        var filters = new FilterConfig { MoveNumberMin = 5 };
        var options = new XgpExportOptions
        {
            NamePattern = "Move{min-move}_{dice}_{score}",
        };

        var zipBytes = _service.BuildXgpZip(sources, rows, options, filters);

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal(
                rows.Select(r =>
                    $"Move5_{r.Roll.ToString("D2", CultureInfo.InvariantCulture)}_{r.MatchScore}.xgp"),
                entries.Select(e => e.FullName));
        }
    }

    [Fact]
    public void CounterlessPattern_UniquifiesDuplicateEntryNames()
    {
        // Zip archives happily hold same-named entries, so without the
        // uniquifier a {n}-less pattern would produce silently colliding
        // members.
        var sources = Sources(XgFixture);
        var rows = _service.ExtractDecisions(sources[XgFixture], XgFixture)
            .Take(3).ToList();

        var options = new XgpExportOptions { NamePattern = "pos" };
        var zipBytes = _service.BuildXgpZip(sources, rows, options, new FilterConfig());

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal(
                new[] { "pos.xgp", "pos (2).xgp", "pos (3).xgp" },
                entries.Select(e => e.FullName));
        }
    }

    [Fact]
    public void SlicedXgDecision_RoundTripsToExactlyOneDecision_WithTheSourceXgid()
    {
        // One play and one cube decision — both slice shapes round-trip.
        // MTCH4064.xg carries no analyzed cube decisions, so the cube comes
        // from match35253054.xg (its rolled-out cube is pinned fixture
        // ground truth) — which also exercises multi-source batching.
        const string cubeFixture = "match35253054.xg";
        var sources = Sources(XgFixture, cubeFixture);

        var picked = new List<DecisionRow>
        {
            _service.ExtractDecisions(sources[XgFixture], XgFixture)
                .First(r => !r.IsCube),
            _service.ExtractDecisions(sources[cubeFixture], cubeFixture)
                .First(r => r.IsCube),
        };

        var zipBytes = _service.BuildXgpZip(
            sources, picked, new XgpExportOptions(), new FilterConfig());

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal(picked.Count, entries.Count);
            for (int i = 0; i < picked.Count; i++)
            {
                var reRead = _service
                    .ExtractDiagramRequests(EntryBytes(entries[i]), entries[i].FullName)
                    .Single();
                Assert.Equal(picked[i].Xgid, reRead.Xgid);
                Assert.Equal(picked[i].IsCube, reRead.IsCube);
            }
        }
    }

    [Fact]
    public void XgpSourcedDecision_IsCopiedVerbatim()
    {
        // An .xgp source already is a single-position analyzed file — the
        // export copies it byte-for-byte rather than re-slicing.
        var sources = Sources(XgpFixture);
        var row = _service.ExtractDecisions(sources[XgpFixture], XgpFixture).Single();
        Assert.IsType<XgpDecisionId>(row.Id);

        var zipBytes = _service.BuildXgpZip(
            sources, [row], new XgpExportOptions(), new FilterConfig());

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal("pos001.xgp", entries.Single().FullName);
            Assert.Equal(sources[XgpFixture], EntryBytes(entries.Single()));
        }
    }

    [Fact]
    public void MixedSources_ParseEachXgSourceOnceAndNumberInGivenOrder()
    {
        var sources = Sources(XgFixture, XgpFixture);
        var xgRows = _service.ExtractDecisions(sources[XgFixture], XgFixture)
            .Take(2).ToList();
        var xgpRow = _service.ExtractDecisions(sources[XgpFixture], XgpFixture).Single();

        // Interleave: xg, xgp, xg — numbering must follow list order.
        var rows = new List<DecisionRow> { xgRows.First(), xgpRow, xgRows.Last() };

        var zipBytes = _service.BuildXgpZip(
            sources, rows, new XgpExportOptions(), new FilterConfig());

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal(new[] { "pos001.xgp", "pos002.xgp", "pos003.xgp" },
                entries.Select(e => e.FullName));
            Assert.Equal(sources[XgpFixture], EntryBytes(entries[1]));
        }
    }

    private static (string Player1, string Player2) ReadPlayerNames(byte[] xgpBytes)
    {
        using var ms = new MemoryStream(xgpBytes);
        var info = XgDecisionIterator.ExtractMatchInfo(XgFileReader.ReadStream(ms))!;
        return (info.Player1, info.Player2);
    }

    [Fact]
    public void Anonymize_RewritesBothXgSlicedAndXgpCopiedEntries_ToTheRoleNames()
    {
        // A mixed batch: one .xg decision (sliced) and the whole .xgp (copied).
        // Both are single-decision surfaces, so BOTH must come back named by
        // role — the toggle closes the mixed-batch privacy gap or it lies.
        var sources = Sources(XgFixture, XgpFixture);
        var xgRow = _service.ExtractDecisions(sources[XgFixture], XgFixture)
            .First(r => !r.IsCube);
        var xgpRow = _service.ExtractDecisions(sources[XgpFixture], XgpFixture).Single();
        Assert.IsType<XgpDecisionId>(xgpRow.Id);

        var zipBytes = _service.BuildXgpZip(
            sources, new List<DecisionRow> { xgRow, xgpRow },
            new XgpExportOptions(), new FilterConfig(),
            anonymize: true);

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal(2, entries.Count);
            foreach (var entry in entries)
                XgpAnonymizeAssert.IsRoleAnonymized(EntryBytes(entry), entry.FullName);
            // The .xgp entry is NOT the verbatim source when anonymizing —
            // it's a whole-file re-emit with the names rewritten.
            Assert.NotEqual(sources[XgpFixture], EntryBytes(entries[1]));
        }
    }

    [Fact]
    public void AnonymizeOff_PreservesEachSourcesOwnPlayerNames()
    {
        // The OFF path must not touch names — each entry re-reads to the
        // player names of its own source (and the .xgp stays byte-verbatim).
        var sources = Sources(XgFixture, XgpFixture);
        var xgNames = ReadPlayerNames(sources[XgFixture]);
        var xgpNames = ReadPlayerNames(sources[XgpFixture]);

        var xgRow = _service.ExtractDecisions(sources[XgFixture], XgFixture)
            .First(r => !r.IsCube);
        var xgpRow = _service.ExtractDecisions(sources[XgpFixture], XgpFixture).Single();

        var zipBytes = _service.BuildXgpZip(
            sources, new List<DecisionRow> { xgRow, xgpRow },
            new XgpExportOptions(), new FilterConfig(),
            anonymize: false);

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal(xgNames, ReadPlayerNames(EntryBytes(entries[0])));
            Assert.Equal(xgpNames, ReadPlayerNames(EntryBytes(entries[1])));
            Assert.Equal(sources[XgpFixture], EntryBytes(entries[1]));
        }
    }

    [Fact]
    public void EmptyDecisionList_YieldsEmptyZip()
    {
        var zipBytes = _service.BuildXgpZip(
            Sources(XgFixture), [], new XgpExportOptions(), new FilterConfig());

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Empty(entries);
        }
    }

    [Fact]
    public void MissingSourceFile_Throws()
    {
        var rows = new List<DecisionRow>
        {
            new() { Id = new XgDecisionId("absent.xg", 1, 1, false) },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.BuildXgpZip(
                Sources(XgFixture), rows, new XgpExportOptions(), new FilterConfig()));
        Assert.Contains("absent.xg", ex.Message);
    }

    [Fact]
    public void InvalidOptions_Throw()
    {
        var rows = new List<DecisionRow>
        {
            new() { Id = new XgpDecisionId(XgpFixture) },
        };

        Assert.Throws<ArgumentException>(() => _service.BuildXgpZip(
            Sources(XgpFixture), rows,
            new XgpExportOptions { SuffixLength = 0 }, new FilterConfig()));
        Assert.Throws<ArgumentException>(() => _service.BuildXgpZip(
            Sources(XgpFixture), rows,
            new XgpExportOptions { NamePattern = "a/b{n}" }, new FilterConfig()));
        Assert.Throws<ArgumentException>(() => _service.BuildXgpZip(
            Sources(XgpFixture), rows,
            new XgpExportOptions { StartNumber = 0 }, new FilterConfig()));
    }
}
