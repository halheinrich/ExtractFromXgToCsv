using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Infrastructure;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The Local golden baseline (halheinrich/backgammon#424): one fixed input
/// folder run through every <see cref="LocalFolderProcessor"/> pathway into
/// <c>TestData/Golden/&lt;label&gt;/&lt;format&gt;/</c>, with a
/// <c>manifest.json</c> of every output's size and SHA-256, so a later change
/// can show its output byte-for-byte against an earlier label's.
/// <para>
/// Not a gate: it asserts only that each run completes. It is excluded from
/// the ordinary suite by its <c>Golden</c> category and run on purpose, with
/// the label in <see cref="LabelVariable"/>; an unset label fails rather than
/// skips, so a filter mistake cannot pass silently. The processor is driven
/// directly because it is the one layer the Extract arc leaves alone; the UI
/// and HTTP path are checked in the running app.
/// </para>
/// <para>
/// The deck formats embed render timestamps, so beside their hashes the
/// manifest carries a structural view: for PPTX each zip entry's name and
/// SHA-256 and the slide count, for PDF the page count.
/// </para>
/// </summary>
[Trait("Category", "Golden")]
[Trait("Category", "RequiresFixtureFiles")]
public partial class LocalGoldenBaseline
{
    /// <summary>Environment variable naming the output label, e.g. <c>4d3c8c8-a</c>.</summary>
    private const string LabelVariable = "EXTRACT_GOLDEN_LABEL";

    // The deck renderer requires a QuestPDF license, which the app sets at
    // startup (Program.cs); this class runs alone, so it sets its own.
    static LocalGoldenBaseline()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public async Task WritesEveryLocalFormat_AndItsManifest()
    {
        var label = Environment.GetEnvironmentVariable(LabelVariable);
        if (string.IsNullOrWhiteSpace(label))
            Assert.Fail($"Set {LabelVariable} to the run's label (the tip's short SHA plus a run letter).");
        Assert.True(
            label.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && label != "input",
            $"{LabelVariable} must be a plain folder name other than 'input': '{label}'.");

        var testData = FindTestData();
        var golden = Path.Combine(testData, "Golden");
        var input = Path.Combine(golden, "input");
        Assert.True(
            Directory.Exists(input) && Directory.EnumerateFiles(input).Any(),
            $"The golden input folder is missing or empty: {input}. Copy the top-level .xg " +
            "and .xgp files of TestData/FixtureFiles/ into it once; it stays frozen after that.");

        var outputRoot = Path.Combine(golden, label);
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
        Directory.CreateDirectory(outputRoot);

        var processor = WithBook(Path.Combine(testData, "FixtureFiles", "OpeningBookV2.ob"));
        var formats = new List<FormatEntry>();

        formats.Add(await RunFile("csv", "golden.csv", (path, progress) =>
            processor.ProcessAsync(input, path, new DecisionFilterSet(), progress)));
        formats.Add(await RunFile("diagram-json", "golden.json", (path, progress) =>
            processor.ProcessDiagramAsync(input, path, new DecisionFilterSet(), progress)));
        formats.Add(await RunFile("pptx", "golden.pptx", (path, progress) =>
            processor.ProcessPptxAsync(input, path, new DecisionFilterSet(), progress)));
        formats.Add(await RunFile("pdf", "golden.pdf", (path, progress) =>
            processor.ProcessPdfAsync(input, path, new DecisionFilterSet(), progress)));
        formats.Add(await RunFolder("xgp", (path, progress) =>
            processor.ProcessXgpAsync(
                input, path, new DecisionFilterSet(),
                new XgpExportOptions { NamePattern = XgpExportOptions.DefaultNamePattern, StartNumber = 1, SuffixLength = 3 },
                new FilterConfig(), progress, anonymize: false)));

        var manifest = new Manifest(label, formats);
        await File.WriteAllTextAsync(
            Path.Combine(outputRoot, "manifest.json"),
            JsonSerializer.Serialize(manifest, ManifestJson),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // A file-format run writes exactly one file into its own folder.
        async Task<FormatEntry> RunFile(
            string format, string fileName, Func<string, IProgress<ProcessingProgress>, Task> run)
        {
            var folder = Path.Combine(outputRoot, format);
            var result = await Complete(p => run(Path.Combine(folder, fileName), p));
            return Describe(format, folder, result);
        }

        async Task<FormatEntry> RunFolder(
            string format, Func<string, IProgress<ProcessingProgress>, Task> run)
        {
            var folder = Path.Combine(outputRoot, format);
            var result = await Complete(p => run(folder, p));
            return Describe(format, folder, result);
        }
    }

    private static async Task<ProcessingProgress> Complete(Func<IProgress<ProcessingProgress>, Task> run)
    {
        var progress = new LastReport();
        await run(progress);
        Assert.True(progress.Value?.Complete, "The run did not report completion.");
        return progress.Value!;
    }

    private static FormatEntry Describe(string format, string folder, ProcessingProgress result)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .Select(relative => DescribeFile(format, Path.Combine(folder, relative)) with { Path = $"{format}/{relative}" })
            .ToList();
        return new FormatEntry(format, result.TotalRows, result.Skipped.Count, files.Count, files);
    }

    private static FileEntry DescribeFile(string format, string path)
    {
        var bytes = File.ReadAllBytes(path);
        var entry = new FileEntry(path, bytes.Length, Sha256(bytes));
        return format switch
        {
            "pptx" => entry with { Pptx = DescribePptx(bytes) },
            "pdf" => entry with { PdfPageCount = CountPdfPages(bytes) },
            _ => entry,
        };
    }

    private static PptxStructure DescribePptx(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entries = zip.Entries
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .Select(e =>
            {
                using var content = new MemoryStream();
                using (var stream = e.Open())
                    stream.CopyTo(content);
                return new ZipEntry(e.FullName, Sha256(content.ToArray()));
            })
            .ToList();
        var slides = entries.Count(e => SlidePart().IsMatch(e.Name));
        return new PptxStructure(slides, entries);
    }

    // A page object's type, not the page tree's (/Pages): the count of pages
    // in an uncompressed page tree, which is what the deck renderer writes.
    private static int CountPdfPages(byte[] bytes) =>
        PdfPage().Count(Encoding.Latin1.GetString(bytes));

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // The umbrella's TestData, found by walking up from the test output.
    private static string FindTestData()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TestData", "FixtureFiles");
            if (Directory.Exists(candidate))
                return Path.Combine(dir.FullName, "TestData");
        }
        Assert.Fail("No TestData/FixtureFiles folder above the test output.");
        return null!;
    }

    private static LocalFolderProcessor WithBook(string bookPath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("OpeningBookPath", bookPath)])
            .Build();
        var provider = new OpeningBookProvider(config, NullLogger<OpeningBookProvider>.Instance);
        Assert.True(provider.Book is not null, $"The opening book did not load: {bookPath}.");
        return new LocalFolderProcessor(
            NullLogger<LocalFolderProcessor>.Instance,
            NullLogger<FilteredDecisionIterator>.Instance,
            AppRanking.Ranking,
            provider);
    }

    private sealed record Manifest(string Label, IReadOnlyList<FormatEntry> Formats);

    private sealed record FormatEntry(
        string Format, int Rows, int Skips, int FileCount, IReadOnlyList<FileEntry> Files);

    private sealed record FileEntry(string Path, long Size, string Sha256)
    {
        public PptxStructure? Pptx { get; init; }

        public int? PdfPageCount { get; init; }
    }

    private sealed record PptxStructure(int SlideCount, IReadOnlyList<ZipEntry> Entries);

    private sealed record ZipEntry(string Name, string Sha256);

    /// <summary>A synchronous <see cref="IProgress{T}"/> that keeps the last snapshot reported.</summary>
    private sealed class LastReport : IProgress<ProcessingProgress>
    {
        public ProcessingProgress? Value { get; private set; }

        public void Report(ProcessingProgress value) => Value = value;
    }

    [GeneratedRegex(@"^ppt/slides/slide\d+\.xml$")]
    private static partial Regex SlidePart();

    [GeneratedRegex(@"/Type\s*/Page(?![A-Za-z])")]
    private static partial Regex PdfPage();
}
