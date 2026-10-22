using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Infrastructure;
using System.Text;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Local output files are never overwritten (the umbrella's
/// <c>SPEC-notices.md</c> §7.5, Hal's ruling of 2026-10-13): each file is
/// created only if none of its name exists, in one operation, and where one
/// does it is created under the next free name of its sequence,
/// <c>" (2)"</c>, <c>" (3)"</c>, …, and the run carries on. That holds for each
/// XGP file and for the one file a CSV, Diagram JSON, PPTX or PDF run writes;
/// a file that existed before the run keeps its contents. An XGP file belongs
/// to the run only once its create-new succeeds: if writing or closing it
/// fails, the run removes it under the name it was created with — a failure
/// to remove it reported beside the primary one — and a failure to create
/// removes nothing. None of these is a skip.
/// </summary>
/// <remarks>
/// A write that fails partway, a failed close and a failed removal are made
/// through the processor's destination seam (<see cref="ScriptedDestination"/>),
/// over real files: no real destination is known to fail there.
/// </remarks>
public class LocalFolderProcessorNoOverwriteTests : IDisposable
{
    private const string Fixture = "MTCH4064.xg";

    // The PDF run renders through QuestPDF, whose license must be set before
    // any call; as the suite's other PDF tests do, this class sets its own.
    static LocalFolderProcessorNoOverwriteTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly string _root = Directory.CreateTempSubdirectory("xg-no-overwrite-").FullName;
    private readonly string _folder;
    private readonly string _outputDir;

    // The output folder is beside the input folder, not in it: discovery
    // reads every .xgp under the input folder, so a file placed in the way of
    // the run's output would otherwise be read as an input too.
    public LocalFolderProcessorNoOverwriteTests()
    {
        _folder = Path.Combine(_root, "input");
        Directory.CreateDirectory(_folder);
        File.Copy(Path.Combine(FixtureHelper.FixtureDir, Fixture), Path.Combine(_folder, Fixture));
        _outputDir = Path.Combine(_root, "positions");
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static readonly XgpExportOptions Options = new();

    private string OutputFile(string name) => Path.Combine(_outputDir, name);

    private static LocalFolderProcessor Processor(IOutputDestination? destination = null) =>
        new(NullLogger<LocalFolderProcessor>.Instance, NullLogger<FilteredDecisionIterator>.Instance,
            AppRanking.Ranking, bookProvider: null, XgpPayload.Prepare,
            destination ?? FileSystemDestination.Instance);

    private Task RunAsync(RunRecord record, IOutputDestination? destination = null, XgpExportOptions? options = null) =>
        Processor(destination).ProcessXgpAsync(
            _folder, _outputDir, new DecisionFilterSet(), options ?? Options, new FilterConfig(),
            new Progress<ProcessingProgress>(), runRecord: record);

    /// <summary>The position an XGP file in the output folder holds.</summary>
    private string XgidIn(string name) =>
        XgDecisionIterator.Iterate(XgFileReader.ReadFile(OutputFile(name)), name).Single().Xgid;

    /// <summary>The fixture's decisions' positions, in the order the run writes them.</summary>
    private static List<string> FixtureXgids() =>
        [.. XgDecisionIterator.Iterate(XgFileReader.ReadFile(Path.Combine(FixtureHelper.FixtureDir, Fixture)), Fixture,
            options: new(Ranking: AppRanking.Ranking)).Select(row => row.Xgid)];

    private string[] OutputNames() =>
        [.. Directory.GetFiles(_outputDir).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    // ── An XGP file of an existing name takes the next free one ─────────────

    [Fact]
    public async Task AnExistingFile_IsLeftAsItWas_ItsDecisionIsWrittenAsName2_AndTheRunContinues()
    {
        byte[] theirs = [1, 2, 3];
        File.WriteAllBytes(OutputFile("pos002.xgp"), theirs);
        var fixture = FixtureXgids();
        var record = new RunRecord();

        await RunAsync(record);

        Assert.Equal(theirs, File.ReadAllBytes(OutputFile("pos002.xgp")));
        Assert.Equal(fixture[1], XgidIn("pos002 (2).xgp"));
        Assert.Equal(fixture[2], XgidIn("pos003.xgp"));
        Assert.Equal(fixture.Count + 1, OutputNames().Length);
        Assert.Equal(new XgpExportCommit(Options, fixture.Count), record.XgpCommit);
        Assert.Empty(record.Skips);
    }

    [Fact]
    public async Task WithTheNameAndItsName2Existing_TheDecisionIsWrittenAsName3()
    {
        byte[] theirs = [1];
        byte[] theirs2 = [2];
        File.WriteAllBytes(OutputFile("pos001.xgp"), theirs);
        File.WriteAllBytes(OutputFile("pos001 (2).xgp"), theirs2);
        var fixture = FixtureXgids();
        var record = new RunRecord();

        await RunAsync(record);

        Assert.Equal(theirs, File.ReadAllBytes(OutputFile("pos001.xgp")));
        Assert.Equal(theirs2, File.ReadAllBytes(OutputFile("pos001 (2).xgp")));
        Assert.Equal(fixture[0], XgidIn("pos001 (3).xgp"));
        Assert.Equal(fixture.Count + 2, OutputNames().Length);
        Assert.Equal(new XgpExportCommit(Options, fixture.Count), record.XgpCommit);
    }

    /// <summary>
    /// The within-run numbering and existing files meet: the run's names and
    /// the files on disk share one sequence, and every decision still gets a
    /// file of its own, under a name no other file has.
    /// </summary>
    [Fact]
    public async Task ARepeatedNameThatAlsoMeetsExistingFiles_StillWritesEveryFile_EachUnderAUniqueName()
    {
        var options = new XgpExportOptions { NamePattern = "same" };
        byte[] theirs = [1];
        byte[] theirs3 = [3];
        File.WriteAllBytes(OutputFile("same.xgp"), theirs);
        File.WriteAllBytes(OutputFile("same (3).xgp"), theirs3);
        var fixture = FixtureXgids();
        var record = new RunRecord();

        await RunAsync(record, options: options);

        Assert.Equal(theirs, File.ReadAllBytes(OutputFile("same.xgp")));
        Assert.Equal(theirs3, File.ReadAllBytes(OutputFile("same (3).xgp")));
        var written = OutputNames().Where(name => name is not "same.xgp" and not "same (3).xgp").ToList();
        Assert.Equal(fixture.Count, written.Count);
        Assert.Equal(fixture.Order(StringComparer.Ordinal), written.Select(XgidIn).Order(StringComparer.Ordinal));
        Assert.Equal(fixture[0], XgidIn("same (2).xgp"));
        Assert.Equal(new XgpExportCommit(options, fixture.Count), record.XgpCommit);
        Assert.Empty(record.Skips);
    }

    // ── The destination says which refusal a create was ─────────────────────

    /// <summary>
    /// A collision is the destination's own classification of its refusal:
    /// one it reports moves the run on to the next name though no file of the
    /// name is on disk, so the run asks the disk nothing itself.
    /// </summary>
    [Fact]
    public async Task ACollisionTheDestinationReports_MovesTheRunOnToTheNextName()
    {
        var destination = new ScriptedDestination { ExistingCreateOf = "pos002.xgp" };
        var fixture = FixtureXgids();
        var record = new RunRecord();

        await RunAsync(record, destination);

        Assert.False(File.Exists(OutputFile("pos002.xgp")));
        Assert.Equal(fixture[1], XgidIn("pos002 (2).xgp"));
        Assert.Empty(destination.Removed);
        Assert.Equal(new XgpExportCommit(Options, fixture.Count), record.XgpCommit);
    }

    /// <summary>
    /// A create refusal the destination does not call a collision is no
    /// collision, even where a file of the name is on disk: it is the run's
    /// destination failure, with the destination's own message, and it removes
    /// nothing.
    /// </summary>
    [Fact]
    public async Task ACreateRefusalTheDestinationDoesNotCallACollision_FailsTheRun_EvenWhereAFileOfTheNameExists()
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
    /// A failure to create the file that is not a collision — a folder holds
    /// its name — fails the run and removes nothing: nothing there is the
    /// run's. No removal is even asked, so a removal the file system would
    /// refuse for a folder cannot stand in for one never made, and the folder
    /// and what it holds are left as they were.
    /// </summary>
    [Fact]
    public async Task AFolderOfTheFilesName_FailsTheCreate_AndRemovesNothing()
    {
        Directory.CreateDirectory(OutputFile("pos002.xgp"));
        byte[] theirs = [5, 4];
        File.WriteAllBytes(Path.Combine(OutputFile("pos002.xgp"), "theirs.bin"), theirs);
        var destination = new ScriptedDestination();
        var record = new RunRecord();

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(record, destination));

        Assert.IsNotType<OutputFileExistsException>(failure);
        Assert.IsNotType<UnfinishedFileException>(failure);
        Assert.Empty(destination.RemovalsAsked);
        Assert.Equal(theirs, File.ReadAllBytes(Path.Combine(OutputFile("pos002.xgp"), "theirs.bin")));
        Assert.Equal(new XgpExportCommit(Options, 1), record.XgpCommit);
        Assert.Empty(record.Skips);
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

    // ── An unfinished XGP file of the run's own is removed ──────────────────

    [Fact]
    public async Task AFailurePartwayThroughAWrite_FailsTheRun_RemovesThatFile_AndTheFilesBeforeItStand()
    {
        var destination = new ScriptedDestination { FailWriteOf = "pos002.xgp" };
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<UnfinishedFileException>(() => RunAsync(record, destination));

        Assert.Equal(ScriptedDestination.WriteFailure, failure.Message);
        Assert.Equal(["pos002.xgp"], destination.Removed);
        Assert.False(File.Exists(OutputFile("pos002.xgp")));
        Assert.Equal(FixtureXgids()[0], XgidIn("pos001.xgp"));
        Assert.Equal(new XgpExportCommit(Options, 1), record.XgpCommit);
        Assert.Empty(record.Skips);
    }

    /// <summary>
    /// The file the run created under a numbered name is the one it removes:
    /// the existing file whose name it stepped past is left as it was.
    /// </summary>
    [Fact]
    public async Task AWriteThatFailsAfterANumberedCreate_RemovesTheNumberedFile_AndLeavesTheOriginalUntouched()
    {
        byte[] theirs = [8, 6];
        File.WriteAllBytes(OutputFile("pos002.xgp"), theirs);
        var destination = new ScriptedDestination { FailWriteOf = "pos002 (2).xgp" };
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<UnfinishedFileException>(() => RunAsync(record, destination));

        Assert.Equal("pos002 (2).xgp", failure.FileName);
        Assert.Equal(ScriptedDestination.WriteFailure, failure.Message);
        Assert.Equal(["pos002 (2).xgp"], destination.Removed);
        Assert.False(File.Exists(OutputFile("pos002 (2).xgp")));
        Assert.Equal(theirs, File.ReadAllBytes(OutputFile("pos002.xgp")));
        Assert.Equal(["pos001.xgp", "pos002.xgp"], OutputNames());
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
        Assert.Equal(FixtureXgids()[0], XgidIn("pos001.xgp"));
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

    /// <summary>
    /// A write that fails, then a close that fails too: the write's failure is
    /// the run's error, with the close failure reported beside it, never in
    /// its place, and the unfinished file is still removed.
    /// </summary>
    [Fact]
    public async Task ACloseThatAlsoFails_AfterAFailedWrite_IsReportedBesideTheWritesFailure()
    {
        var destination = new ScriptedDestination { FailWriteOf = "pos002.xgp", FailCloseOf = "pos002.xgp" };
        var record = new RunRecord();

        var failure = await Assert.ThrowsAsync<UnfinishedFileException>(() => RunAsync(record, destination));

        Assert.Equal(
            $"{ScriptedDestination.WriteFailure} Closing the unfinished pos002.xgp failed too: {ScriptedDestination.CloseFailure}",
            failure.Message);
        Assert.Equal(ScriptedDestination.CloseFailure, failure.CloseFailure!.Message);
        Assert.Equal(["pos002.xgp"], destination.Removed);
        Assert.False(File.Exists(OutputFile("pos002.xgp")));
        Assert.Equal(new XgpExportCommit(Options, 1), record.XgpCommit);
        Assert.Empty(record.Skips);
    }

    // ── The one file of the other formats takes the next free name ──────────

    /// <summary>
    /// The one file a CSV, Diagram JSON, PPTX or PDF run writes, where a file
    /// of the name the user gave exists: the run writes its output beside it
    /// as <c>name (2).ext</c>, the existing file keeps its contents, and the
    /// run's completed result names the file it wrote, not the one it was
    /// asked for. The run is filtered to one decision, so a deck renders one
    /// slide pair.
    /// </summary>
    [Theory]
    [InlineData(OutputFormat.Csv)]
    [InlineData(OutputFormat.DiagramJson)]
    [InlineData(OutputFormat.Pptx)]
    [InlineData(OutputFormat.Pdf)]
    public async Task AnExistingOutputFile_IsLeftAsItWas_AndTheRunWritesName2(OutputFormat format)
    {
        var extension = Extension(format);
        byte[] theirs = [1, 2, 3];
        File.WriteAllBytes(OutputFile("out" + extension), theirs);
        var record = new RunRecord();

        var completed = await RunSingleFileAsync(format, OutputFile("out" + extension), record);

        Assert.Equal(theirs, File.ReadAllBytes(OutputFile("out" + extension)));
        AssertWrittenAs(format, File.ReadAllBytes(OutputFile($"out (2){extension}")));
        Assert.Equal(["out (2)" + extension, "out" + extension], OutputNames());
        Assert.Equal(OutputFile($"out (2){extension}"), completed.WrittenPath);
        Assert.Empty(record.Skips);
    }

    [Fact]
    public async Task Csv_WithTheNameAndItsName2Existing_TheRunWritesName3()
    {
        byte[] theirs = [1];
        byte[] theirs2 = [2];
        File.WriteAllBytes(OutputFile("out.csv"), theirs);
        File.WriteAllBytes(OutputFile("out (2).csv"), theirs2);

        await RunSingleFileAsync(OutputFormat.Csv, OutputFile("out.csv"), new RunRecord());

        Assert.Equal(theirs, File.ReadAllBytes(OutputFile("out.csv")));
        Assert.Equal(theirs2, File.ReadAllBytes(OutputFile("out (2).csv")));
        AssertWrittenAs(OutputFormat.Csv, File.ReadAllBytes(OutputFile("out (3).csv")));
    }

    /// <summary>Runs <paramref name="format"/>'s pathway over the first decision alone.</summary>
    /// <returns>The run's completed snapshot: the last it reported.</returns>
    private async Task<ProcessingProgress> RunSingleFileAsync(OutputFormat format, string outputPath, RunRecord record)
    {
        var processor = Processor();
        var filters = new DecisionFilterSet().Add(new FirstDecisionOnly());
        var progress = new RecordingProgress();
        await (format switch
        {
            OutputFormat.Csv => processor.ProcessAsync(_folder, outputPath, filters, progress, record),
            OutputFormat.DiagramJson => processor.ProcessDiagramAsync(_folder, outputPath, filters, progress, record),
            OutputFormat.Pptx => processor.ProcessPptxAsync(_folder, outputPath, filters, progress, record),
            OutputFormat.Pdf => processor.ProcessPdfAsync(_folder, outputPath, filters, progress, record),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        });
        var completed = progress.Last!;
        Assert.True(completed.Complete);
        return completed;
    }

    private static string Extension(OutputFormat format) => format switch
    {
        OutputFormat.Csv => ".csv",
        OutputFormat.DiagramJson => ".json",
        OutputFormat.Pptx => ".pptx",
        OutputFormat.Pdf => ".pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>The run's own output, recognized by how its format begins.</summary>
    private static void AssertWrittenAs(OutputFormat format, byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        switch (format)
        {
            case OutputFormat.Csv:
                Assert.StartsWith(CsvLayout.Header, text);
                Assert.Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
                break;
            case OutputFormat.DiagramJson:
                Assert.StartsWith("[", text);
                break;
            case OutputFormat.Pptx:
                Assert.StartsWith("PK", text);
                break;
            case OutputFormat.Pdf:
                Assert.StartsWith("%PDF", text);
                break;
        }
    }

    /// <summary>A filter that passes the run's first decision and no other.</summary>
    private sealed class FirstDecisionOnly : IDecisionFilter
    {
        private int _calls;

        public bool Matches(IDecisionFilterData data) => ++_calls == 1;
    }
}
