using System.IO.Compression;
using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="XgProcessingService.BuildXgpZip"/> — the Web-mode .xgp
/// export seam. The semantic oracle is the in-app round-trip: exported bytes
/// re-read through the same service yield exactly one decision whose XGID
/// equals the source decision's (the XGID digests position, cube, dice, and
/// match state). Byte-level .xgp correctness is owned by the producer's
/// XgpExporter tests; these tests own the app-side batching, naming, and
/// source-routing rules.
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
    public void EntryNames_FollowPrefixStartNumberAndSuffixLength()
    {
        var sources = Sources(XgFixture);
        var ids = _service.ExtractDecisions(sources[XgFixture], XgFixture)
            .Take(3).Select(r => r.Id).ToList();
        Assert.Equal(3, ids.Count);

        var options = new XgpExportOptions { Prefix = "quiz", StartNumber = 12, SuffixLength = 4 };
        var zipBytes = _service.BuildXgpZip(sources, ids, options);

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal(
                new[] { "quiz0012.xgp", "quiz0013.xgp", "quiz0014.xgp" },
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

        var picked = new[]
        {
            _service.ExtractDecisions(sources[XgFixture], XgFixture)
                .First(r => !r.IsCube),
            _service.ExtractDecisions(sources[cubeFixture], cubeFixture)
                .First(r => r.IsCube),
        };

        var zipBytes = _service.BuildXgpZip(
            sources, picked.Select(r => r.Id).ToList(), new XgpExportOptions());

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal(picked.Length, entries.Count);
            for (int i = 0; i < picked.Length; i++)
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
        var id = _service.ExtractDecisions(sources[XgpFixture], XgpFixture).Single().Id;
        Assert.IsType<XgpDecisionId>(id);

        var zipBytes = _service.BuildXgpZip(sources, [id], new XgpExportOptions());

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
        var xgIds = _service.ExtractDecisions(sources[XgFixture], XgFixture)
            .Take(2).Select(r => r.Id);
        var xgpId = _service.ExtractDecisions(sources[XgpFixture], XgpFixture).Single().Id;

        // Interleave: xg, xgp, xg — numbering must follow list order.
        var ids = new List<DecisionId> { xgIds.First(), xgpId, xgIds.Last() };

        var zipBytes = _service.BuildXgpZip(sources, ids, new XgpExportOptions());

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Equal(new[] { "pos001.xgp", "pos002.xgp", "pos003.xgp" },
                entries.Select(e => e.FullName));
            Assert.Equal(sources[XgpFixture], EntryBytes(entries[1]));
        }
    }

    [Fact]
    public void EmptyDecisionList_YieldsEmptyZip()
    {
        var zipBytes = _service.BuildXgpZip(
            Sources(XgFixture), [], new XgpExportOptions());

        var entries = ReadEntries(zipBytes, out var zip);
        using (zip)
        {
            Assert.Empty(entries);
        }
    }

    [Fact]
    public void MissingSourceFile_Throws()
    {
        var ids = new List<DecisionId> { new XgDecisionId("absent.xg", 1, 1, false) };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.BuildXgpZip(Sources(XgFixture), ids, new XgpExportOptions()));
        Assert.Contains("absent.xg", ex.Message);
    }

    [Fact]
    public void InvalidOptions_Throw()
    {
        var ids = new List<DecisionId> { new XgpDecisionId(XgpFixture) };

        Assert.Throws<ArgumentException>(() => _service.BuildXgpZip(
            Sources(XgpFixture), ids, new XgpExportOptions { SuffixLength = 0 }));
        Assert.Throws<ArgumentException>(() => _service.BuildXgpZip(
            Sources(XgpFixture), ids, new XgpExportOptions { Prefix = "a/b" }));
        Assert.Throws<ArgumentException>(() => _service.BuildXgpZip(
            Sources(XgpFixture), ids, new XgpExportOptions { StartNumber = 0 }));
    }
}
