using System.Globalization;
using System.IO.Compression;
using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="XgProcessingService.BuildXgpArchive"/> — the Web-mode
/// .xgp export, built from the selection's retained decoded files. The
/// semantic oracle is the in-app round-trip: an exported entry re-read through
/// the same reader and converter yields exactly one decision whose XGID equals
/// the source decision's (the XGID digests position, cube, dice, and match
/// state). Byte-level .xgp correctness is owned by the producer's XgpExporter
/// tests; these tests own the app-side batching, naming, source routing and
/// the export skip (the umbrella's <c>SPEC-notices.md</c> §7.1, F1 of §7.4).
/// Name-pattern grammar and uniquifier mechanics are owned by
/// <see cref="XgpNameTemplateTests"/> / <see cref="XgpNameAllocatorTests"/> —
/// here they're exercised through the archive.
///
/// Fixtures are pinned by name — TestData/FixtureFiles is append-only.
/// </summary>
public class XgpExportServiceTests
{
    private const string XgFixture = "MTCH4064.xg";
    private const string XgpFixture = "MTCH4064_1_22.xgp";

    private readonly XgProcessingService _service = new();

    /// <summary>Builds an archive, collecting what it skipped.</summary>
    private XgpArchive Build(
        IReadOnlyList<XgpDecision> decisions, XgpExportOptions options, FilterConfig? filters,
        out List<SkippedItem> skipped, bool anonymize = false)
    {
        var collected = new List<SkippedItem>();
        var archive = _service.BuildXgpArchive(decisions, options, filters ?? new FilterConfig(), anonymize, collected.Add);
        skipped = collected;
        return archive;
    }

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

    /// <summary>An exported entry, re-read as Web mode reads a picked file.</summary>
    private static DecodedSource Reread(ZipArchiveEntry entry)
    {
        var bytes = EntryBytes(entry);
        var read = XgSourceReader.ReadStream(entry.FullName, new MemoryStream(bytes), XgSourceFormat.Xg);
        return new DecodedSource(entry.FullName, Assert.IsType<XgSourceDecoded>(read).File, bytes);
    }

    private static byte[] FixtureBytes(string name) => FixtureHelper.ReadFixture(name);

    [Fact]
    public void EntryNames_FollowPatternStartNumberAndSuffixLength()
    {
        // Counter unification: "quiz{n}" must produce byte-identical names
        // to what the removed Prefix="quiz" option produced.
        var source = WebSources.Decode(XgFixture);
        var decisions = WebSources.Rows(source).Take(3).With(source);
        Assert.Equal(3, decisions.Count);

        var options = new XgpExportOptions
        {
            NamePattern = "quiz{n}",
            StartNumber = 12,
            SuffixLength = 4,
        };
        var archive = Build(decisions, options, null, out var skipped);

        var entries = ReadEntries(archive.Bytes, out var zip);
        using (zip)
        {
            Assert.Equal(
                new[] { "quiz0012.xgp", "quiz0013.xgp", "quiz0014.xgp" },
                entries.Select(e => e.FullName));
        }
        Assert.Empty(skipped);
        Assert.Equal(new XgpExportCommit(options, 3), archive.Commit);
    }

    [Fact]
    public void TokenPattern_DrawsBatchTokensFromFiltersAndItemTokensFromEachRow()
    {
        var source = WebSources.Decode(XgFixture);
        var all = WebSources.Rows(source)
            .Where(r => r.Kind == DecisionKind.CheckerPlay)
            .ToList();

        // Two checker plays guaranteed to render distinct names, so no
        // uniquifier muddies the expected strings. (The cube decision's "00"
        // is pinned in XgpNameTemplateTests.)
        var first = all[0];
        var second = all.First(r =>
            r.Roll != first.Roll || r.MatchScore != first.MatchScore);
        var rows = new List<DecisionRow> { first, second };

        var filters = new FilterConfig { MoveNumberMin = 5 };
        var options = new XgpExportOptions
        {
            NamePattern = "Move{min-move}_{dice}_{score}",
        };

        var archive = Build(rows.With(source), options, filters, out _);

        var entries = ReadEntries(archive.Bytes, out var zip);
        using (zip)
        {
            Assert.Equal(
                rows.Select(r =>
                    $"Move5_{r.Roll!.Value.ToString("D2", CultureInfo.InvariantCulture)}_{r.MatchScore}.xgp"),
                entries.Select(e => e.FullName));
        }
    }

    [Fact]
    public void CounterlessPattern_UniquifiesDuplicateEntryNames()
    {
        // Zip archives happily hold same-named entries, so without the
        // uniquifier a {n}-less pattern would produce silently colliding
        // members.
        var source = WebSources.Decode(XgFixture);
        var decisions = WebSources.Rows(source).Take(3).With(source);

        var archive = Build(decisions, new XgpExportOptions { NamePattern = "pos" }, null, out _);

        var entries = ReadEntries(archive.Bytes, out var zip);
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
        var xg = WebSources.Decode(XgFixture);
        var cube = WebSources.Decode(cubeFixture);

        var picked = new List<XgpDecision>
        {
            new(WebSources.Rows(xg).First(r => r.Kind == DecisionKind.CheckerPlay), xg),
            new(WebSources.Rows(cube).First(r => r.Kind == DecisionKind.Cube), cube),
        };

        var archive = Build(picked, new XgpExportOptions(), null, out _);

        var entries = ReadEntries(archive.Bytes, out var zip);
        using (zip)
        {
            Assert.Equal(picked.Count, entries.Count);
            for (int i = 0; i < picked.Count; i++)
            {
                var reRead = WebSources.Records(Reread(entries[i])).Single();
                Assert.Equal(picked[i].Row.Xgid, reRead.Xgid);
                Assert.Equal(picked[i].Row.Kind, reRead.Kind);
            }
        }
    }

    [Fact]
    public void XgpSourcedDecision_IsCopiedVerbatim()
    {
        // An .xgp source already is a single-position analyzed file — the
        // export copies its retained bytes byte-for-byte rather than
        // re-slicing.
        var source = WebSources.Decode(XgpFixture);
        var row = WebSources.Rows(source).Single();
        Assert.IsType<XgpDecisionId>(row.Id);

        var archive = Build([new XgpDecision(row, source)], new XgpExportOptions(), null, out _);

        var entries = ReadEntries(archive.Bytes, out var zip);
        using (zip)
        {
            Assert.Equal("pos001.xgp", entries.Single().FullName);
            Assert.Equal(FixtureBytes(XgpFixture), EntryBytes(entries.Single()));
        }
    }

    [Fact]
    public void MixedSources_NumberInGivenOrder()
    {
        var xg = WebSources.Decode(XgFixture);
        var xgp = WebSources.Decode(XgpFixture);
        var xgRows = WebSources.Rows(xg).Take(2).ToList();
        var xgpRow = WebSources.Rows(xgp).Single();

        // Interleave: xg, xgp, xg — numbering must follow list order.
        var decisions = new List<XgpDecision> { new(xgRows[0], xg), new(xgpRow, xgp), new(xgRows[1], xg) };

        var archive = Build(decisions, new XgpExportOptions(), null, out _);

        var entries = ReadEntries(archive.Bytes, out var zip);
        using (zip)
        {
            Assert.Equal(new[] { "pos001.xgp", "pos002.xgp", "pos003.xgp" },
                entries.Select(e => e.FullName));
            Assert.Equal(FixtureBytes(XgpFixture), EntryBytes(entries[1]));
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
        var xg = WebSources.Decode(XgFixture);
        var xgp = WebSources.Decode(XgpFixture);
        var xgRow = WebSources.Rows(xg).First(r => r.Kind == DecisionKind.CheckerPlay);
        var xgpRow = WebSources.Rows(xgp).Single();
        Assert.IsType<XgpDecisionId>(xgpRow.Id);

        var archive = Build(
            [new XgpDecision(xgRow, xg), new XgpDecision(xgpRow, xgp)],
            new XgpExportOptions(), null, out _, anonymize: true);

        var entries = ReadEntries(archive.Bytes, out var zip);
        using (zip)
        {
            Assert.Equal(2, entries.Count);
            foreach (var entry in entries)
                XgpAnonymizeAssert.IsRoleAnonymized(EntryBytes(entry), entry.FullName);
            // The .xgp entry is NOT the verbatim source when anonymizing —
            // it's a whole-file re-emit with the names rewritten.
            Assert.NotEqual(FixtureBytes(XgpFixture), EntryBytes(entries[1]));
        }
    }

    [Fact]
    public void AnonymizeOff_PreservesEachSourcesOwnPlayerNames()
    {
        // The OFF path must not touch names — each entry re-reads to the
        // player names of its own source (and the .xgp stays byte-verbatim).
        var xgNames = ReadPlayerNames(FixtureBytes(XgFixture));
        var xgpNames = ReadPlayerNames(FixtureBytes(XgpFixture));
        var xg = WebSources.Decode(XgFixture);
        var xgp = WebSources.Decode(XgpFixture);

        var archive = Build(
            [
                new XgpDecision(WebSources.Rows(xg).First(r => r.Kind == DecisionKind.CheckerPlay), xg),
                new XgpDecision(WebSources.Rows(xgp).Single(), xgp),
            ],
            new XgpExportOptions(), null, out _, anonymize: false);

        var entries = ReadEntries(archive.Bytes, out var zip);
        using (zip)
        {
            Assert.Equal(xgNames, ReadPlayerNames(EntryBytes(entries[0])));
            Assert.Equal(xgpNames, ReadPlayerNames(EntryBytes(entries[1])));
            Assert.Equal(FixtureBytes(XgpFixture), EntryBytes(entries[1]));
        }
    }

    [Fact]
    public void EmptyDecisionList_YieldsEmptyZip_CommittingNothing()
    {
        var archive = Build([], new XgpExportOptions(), null, out _);

        var entries = ReadEntries(archive.Bytes, out var zip);
        using (zip)
        {
            Assert.Empty(entries);
        }
        Assert.Equal(0, archive.Commit.Count);
    }

    // ── The export skip ─────────────────────────────────────────────────────

    /// <summary>
    /// A decision whose payload cannot be prepared — here one the source holds
    /// no decision for, which the producer's slice refuses — is skipped,
    /// reported with its file and id, and consumes no number: the decisions
    /// around it are numbered without a gap. Before this the first failure
    /// failed the whole download.
    /// </summary>
    [Fact]
    public void APayloadWhosePreparationFails_IsAnExportSkip_AndConsumesNoNumber()
    {
        var source = WebSources.Decode(XgFixture);
        var rows = WebSources.Rows(source).Take(2).ToList();
        var nowhere = TestRecords.Row(TestRecords.CheckerPlay(
            id: new XgDecisionId(XgFixture, Game: 99, MoveNumber: 1, IsCube: false)));
        var decisions = new List<XgpDecision> { new(rows[0], source), new(nowhere, source), new(rows[1], source) };
        var options = new XgpExportOptions();

        var archive = Build(decisions, options, null, out var skipped);

        var skip = Assert.Single(skipped);
        Assert.Equal(XgFixture, skip.FileName);
        Assert.Equal(nowhere.Id, skip.DecisionId);
        Assert.False(string.IsNullOrWhiteSpace(skip.Reason));

        var entries = ReadEntries(archive.Bytes, out var zip);
        using (zip)
        {
            Assert.Equal(new[] { "pos001.xgp", "pos002.xgp" }, entries.Select(e => e.FullName));
            Assert.Equal(rows[1].Xgid, WebSources.Records(Reread(entries[1])).Single().Xgid);
        }
        Assert.Equal(new XgpExportCommit(options, 2), archive.Commit);
    }

    /// <summary>
    /// A verbatim copy needs the exact bytes, which the selection keeps only
    /// for a file an export copies verbatim: a source decoded without them
    /// fails that decision's preparation — a skip, not a silent re-slice —
    /// while an anonymized copy, re-emitted from the decoded file, needs none.
    /// </summary>
    [Fact]
    public void AVerbatimCopyWithoutItsBytes_IsAnExportSkip_WhileAnAnonymizedCopyNeedsNone()
    {
        var withoutBytes = WebSources.Decode(XgpFixture, keepBytes: false);
        var row = WebSources.Rows(withoutBytes).Single();

        var kept = Build([new XgpDecision(row, withoutBytes)], new XgpExportOptions(), null, out var skipped);
        Assert.Equal(row.Id, Assert.Single(skipped).DecisionId);
        Assert.Equal(0, kept.Commit.Count);

        var anonymized = Build([new XgpDecision(row, withoutBytes)], new XgpExportOptions(), null, out var none, anonymize: true);
        Assert.Empty(none);
        Assert.Equal(1, anonymized.Commit.Count);
    }

    [Fact]
    public void InvalidOptions_Throw()
    {
        var source = WebSources.Decode(XgpFixture);
        var decisions = WebSources.Rows(source).With(source);

        Assert.Throws<ArgumentException>(() => Build(decisions, new XgpExportOptions { SuffixLength = 0 }, null, out _));
        Assert.Throws<ArgumentException>(() => Build(decisions, new XgpExportOptions { NamePattern = "a/b{n}" }, null, out _));
        Assert.Throws<ArgumentException>(() => Build(decisions, new XgpExportOptions { StartNumber = 0 }, null, out _));
    }
}
