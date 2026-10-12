using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Local XGP files are never overwritten (the umbrella's
/// <c>SPEC-notices.md</c> §7.5, Hal's F3 ruling of 2026-10-12): each file is
/// created only if none of its name exists, in one operation, and an existing
/// file stops the run as a destination failure naming it, with the files
/// written before it committed, counted and untouched. A file belongs to the
/// run only once its create-new succeeds: if writing or closing it fails, the
/// run removes it — a failure to remove it reported beside the primary one —
/// and a collision, or any other failure to create, removes nothing. None of
/// these is a skip.
/// </summary>
/// <remarks>
/// A write that fails partway, a failed close and a failed removal are made
/// through the processor's destination seam (<see cref="ScriptedDestination"/>),
/// over real files: no real destination is known to fail there.
/// </remarks>
public class LocalFolderProcessorNoOverwriteTests : IDisposable
{
    private const string Fixture = "MTCH4064.xg";

    private readonly string _folder = Directory.CreateTempSubdirectory("xg-no-overwrite-").FullName;
    private readonly string _outputDir;

    public LocalFolderProcessorNoOverwriteTests()
    {
        File.Copy(Path.Combine(FixtureHelper.FixtureDir, Fixture), Path.Combine(_folder, Fixture));
        _outputDir = Path.Combine(_folder, "positions");
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static readonly XgpExportOptions Options = new();

    private string OutputFile(string name) => Path.Combine(_outputDir, name);

    private Task RunAsync(RunRecord record, IOutputDestination? destination = null) =>
        new LocalFolderProcessor(
            NullLogger<LocalFolderProcessor>.Instance, NullLogger<FilteredDecisionIterator>.Instance,
            AppRanking.Ranking, bookProvider: null, XgpPayload.Prepare,
            destination ?? FileSystemDestination.Instance)
        .ProcessXgpAsync(
            _folder, _outputDir, new DecisionFilterSet(), Options, new FilterConfig(),
            new Progress<ProcessingProgress>(), runRecord: record);

    /// <summary>The first decision's file, as the run writes it, re-read: what an untouched earlier file must still hold.</summary>
    private string FirstFilesXgid() =>
        XgDecisionIterator.Iterate(XgFileReader.ReadFile(OutputFile("pos001.xgp")), "pos001.xgp").Single().Xgid;

    private static string FixturesFirstXgid() =>
        XgDecisionIterator.Iterate(XgFileReader.ReadFile(Path.Combine(FixtureHelper.FixtureDir, Fixture)), Fixture,
            options: new(Ranking: AppRanking.Ranking)).First().Xgid;

    // ── A collision stops the run and removes nothing ───────────────────────

    [Fact]
    public async Task AnExistingFile_StopsTheRunNamingIt_IsLeftAsItWas_AndTheFilesBeforeItAreCounted()
    {
        byte[] theirs = [1, 2, 3];
        File.WriteAllBytes(OutputFile("pos002.xgp"), theirs);
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<XgpFileExistsException>(() => RunAsync(record));

        Assert.Equal(
            "pos002.xgp already exists in the output folder, and XGP export never overwrites a file, "
            + "so the run stopped there. The 1 file(s) before it were written.",
            failure.Message);
        Assert.Equal(theirs, File.ReadAllBytes(OutputFile("pos002.xgp")));
        Assert.Equal(FixturesFirstXgid(), FirstFilesXgid());
        Assert.Equal(new XgpExportCommit(Options, 1), record.XgpCommit);
        Assert.Empty(record.Skips);
        Assert.Equal(2, Directory.GetFiles(_outputDir).Length);
    }

    [Fact]
    public async Task AnExistingFirstFile_StopsTheRunBeforeAnythingIsWritten()
    {
        byte[] theirs = [9];
        File.WriteAllBytes(OutputFile("pos001.xgp"), theirs);
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<XgpFileExistsException>(() => RunAsync(record));

        Assert.EndsWith("so the run stopped there. Nothing was written.", failure.Message);
        Assert.Equal(theirs, File.ReadAllBytes(OutputFile("pos001.xgp")));
        Assert.Equal(new XgpExportCommit(Options, 0), record.XgpCommit);
        Assert.Single(Directory.GetFiles(_outputDir));
    }

    /// <summary>
    /// A failure to create the file that is not a collision — a folder holds
    /// its name — removes nothing either: nothing there is the run's.
    /// </summary>
    [Fact]
    public async Task AFailureToCreateTheFile_RemovesNothing()
    {
        Directory.CreateDirectory(OutputFile("pos002.xgp"));
        var destination = new ScriptedDestination();
        var record = new RunRecord();

        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(record, destination));

        Assert.Empty(destination.Removed);
        Assert.True(Directory.Exists(OutputFile("pos002.xgp")));
        Assert.Equal(new XgpExportCommit(Options, 1), record.XgpCommit);
        Assert.Empty(record.Skips);
    }

    // ── The destination says which refusal a create was ─────────────────────

    /// <summary>
    /// A collision is the destination's own classification of its refusal:
    /// one it reports is the run's collision though no file of the name is on
    /// disk, so the run asks the disk nothing itself.
    /// </summary>
    [Fact]
    public async Task ACollisionTheDestinationReports_StopsTheRunAsAnExistingFile()
    {
        var destination = new ScriptedDestination { ExistingCreateOf = "pos002.xgp" };
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<XgpFileExistsException>(() => RunAsync(record, destination));

        Assert.Equal("pos002.xgp", failure.FileName);
        Assert.IsType<OutputFileExistsException>(failure.InnerException);
        Assert.Empty(destination.Removed);
        Assert.Equal(new XgpExportCommit(Options, 1), record.XgpCommit);
    }

    /// <summary>
    /// A create refusal the destination does not call a collision is no
    /// collision, even where a file of the name is on disk: it is the run's
    /// destination failure, with the destination's own message, and it removes
    /// nothing.
    /// </summary>
    [Fact]
    public async Task ACreateRefusalTheDestinationDoesNotCallACollision_IsNone_EvenWhereAFileOfTheNameExists()
    {
        byte[] theirs = [4, 2];
        File.WriteAllBytes(OutputFile("pos002.xgp"), theirs);
        var destination = new ScriptedDestination { FailCreateOf = "pos002.xgp" };
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<IOException>(() => RunAsync(record, destination));

        Assert.Equal(ScriptedDestination.CreateFailure, failure.Message);
        Assert.Empty(destination.Removed);
        Assert.Equal(theirs, File.ReadAllBytes(OutputFile("pos002.xgp")));
        Assert.Equal(new XgpExportCommit(Options, 1), record.XgpCommit);
    }

    /// <summary>
    /// The file system's own classification: a create-new over an existing
    /// file is a collision and leaves the file as it was; a create that fails
    /// for any other reason — its folder is missing — is not.
    /// </summary>
    [Fact]
    public void TheFileSystem_ClassifiesAnExistingFile_AndNothingElse_AsACollision()
    {
        byte[] theirs = [7];
        File.WriteAllBytes(OutputFile("taken.xgp"), theirs);

        var collision = Assert.Throws<OutputFileExistsException>(() => FileSystemDestination.Instance.CreateNew(OutputFile("taken.xgp")));
        Assert.Equal(OutputFile("taken.xgp"), collision.FilePath);
        Assert.Equal(theirs, File.ReadAllBytes(OutputFile("taken.xgp")));

        var other = Assert.ThrowsAny<IOException>(() => FileSystemDestination.Instance.CreateNew(Path.Combine(_outputDir, "missing", "pos001.xgp")));
        Assert.IsNotType<OutputFileExistsException>(other);
    }

    // ── An unfinished file of the run's own is removed ──────────────────────

    [Fact]
    public async Task AFailurePartwayThroughAWrite_FailsTheRun_RemovesThatFile_AndTheFilesBeforeItStand()
    {
        var destination = new ScriptedDestination { FailWriteOf = "pos002.xgp" };
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<UnfinishedFileException>(() => RunAsync(record, destination));

        Assert.Equal(ScriptedDestination.WriteFailure, failure.Message);
        Assert.Equal(["pos002.xgp"], destination.Removed);
        Assert.False(File.Exists(OutputFile("pos002.xgp")));
        Assert.Equal(FixturesFirstXgid(), FirstFilesXgid());
        Assert.Equal(new XgpExportCommit(Options, 1), record.XgpCommit);
        Assert.Empty(record.Skips);
    }

    [Fact]
    public async Task AFailureClosingAFile_FailsTheRun_RemovesThatFile_AndTheFilesBeforeItStand()
    {
        var destination = new ScriptedDestination { FailCloseOf = "pos002.xgp" };
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<UnfinishedFileException>(() => RunAsync(record, destination));

        Assert.Equal(ScriptedDestination.CloseFailure, failure.Message);
        Assert.Equal(["pos002.xgp"], destination.Removed);
        Assert.False(File.Exists(OutputFile("pos002.xgp")));
        Assert.Equal(FixturesFirstXgid(), FirstFilesXgid());
        Assert.Equal(new XgpExportCommit(Options, 1), record.XgpCommit);
        Assert.Empty(record.Skips);
    }

    [Fact]
    public async Task ARemovalThatAlsoFails_IsReportedBesideTheWritesFailure_NeverInItsPlace()
    {
        var destination = new ScriptedDestination { FailWriteOf = "pos002.xgp", FailRemoval = true };
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<UnfinishedFileException>(() => RunAsync(record, destination));

        Assert.Equal(
            $"{ScriptedDestination.WriteFailure} The unfinished pos002.xgp could not be removed: {ScriptedDestination.RemovalFailure}",
            failure.Message);
        Assert.Equal(ScriptedDestination.RemovalFailure, failure.RemovalFailure!.Message);
        Assert.Equal(new XgpExportCommit(Options, 1), record.XgpCommit);
        Assert.Empty(record.Skips);
    }
}
