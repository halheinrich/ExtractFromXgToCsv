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
using Xunit.Abstractions;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The Local golden baseline (halheinrich/backgammon#424): one fixed input
/// folder run through every <see cref="LocalFolderProcessor"/> pathway into
/// <c>TestData/Golden/&lt;label&gt;/&lt;format&gt;/</c>, with a
/// <c>manifest.json</c> of every output's size and SHA-256, so a later change
/// can show its output byte-for-byte against an earlier label's.
/// <para>
/// Not a gate: it asserts only that each run completes. It runs only when
/// <see cref="LabelVariable"/> is set, per command; unset, it writes one line
/// and passes, so the plain suite stays green. A run that wrote nothing shows
/// as a missing manifest. The run folder is resolved by
/// <see cref="GoldenRunFolder"/>, which refuses an existing one: the harness
/// creates folders and never deletes. The processor is driven directly
/// because it is the one layer the Extract arc leaves alone; the UI and HTTP
/// path are checked in the running app.
/// </para>
/// <para>
/// The deck formats embed render timestamps, so beside their hashes the
/// manifest carries what repeats: for PPTX each zip entry's name and SHA-256
/// and the slide count; for PDF the page count and the SHA-256 with the
/// <c>/CreationDate</c> and <c>/ModDate</c> values masked.
/// </para>
/// </summary>
[Trait("Category", "Golden")]
[Trait("Category", "RequiresFixtureFiles")]
public partial class LocalGoldenBaseline(ITestOutputHelper output)
{
    /// <summary>Environment variable naming the output label, e.g. <c>4d3c8c8-a</c>.</summary>
    private const string LabelVariable = "EXTRACT_GOLDEN_LABEL";

    // What a masked PDF date value is replaced by.
    private static readonly byte[] MaskedDate = Encoding.Latin1.GetBytes("(D:MASKED)");

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
        if (string.IsNullOrEmpty(label))
        {
            output.WriteLine($"{LabelVariable} is unset: the golden baseline did not run and nothing was written");
            return;
        }

        var testData = FindTestData();
        var golden = Path.Combine(testData, "Golden");
        var input = Path.Combine(golden, "input");
        Assert.True(
            Directory.Exists(input) && Directory.EnumerateFiles(input).Any(),
            $"The golden input folder is missing or empty: {input}. Copy the top-level .xg " +
            "and .xgp files of TestData/FixtureFiles/ into it once; it stays frozen after that.");

        if (!GoldenRunFolder.TryResolve(golden, input, label, out var outputRoot, out var refusal))
            Assert.Fail($"{LabelVariable}: {refusal}");
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
            "pdf" => DescribePdf(entry, bytes),
            _ => entry,
        };
    }

    private static FileEntry DescribePdf(FileEntry entry, byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        return entry with { PdfPageCount = PdfPage().Count(text), PdfDateMaskedSha256 = DateMaskedSha256(bytes, text) };
    }

    /// <summary>
    /// The SHA-256 of a PDF with each <c>/CreationDate</c> and <c>/ModDate</c>
    /// value replaced by a fixed token, all else as written.
    /// <paramref name="text"/> is <paramref name="bytes"/> read as Latin-1,
    /// one character per byte, so a match's index is its byte offset.
    /// </summary>
    private static string DateMaskedSha256(byte[] bytes, string text)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var at = 0;
        foreach (Match date in PdfDate().Matches(text))
        {
            var value = date.Groups["value"];
            hash.AppendData(bytes, at, value.Index - at);
            hash.AppendData(MaskedDate);
            at = value.Index + value.Length;
        }
        hash.AppendData(bytes, at, bytes.Length - at);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
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

        public string? PdfDateMaskedSha256 { get; init; }
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

    // A page object's type, not the page tree's (/Pages): the count of pages
    // in an uncompressed page tree, which is what the deck renderer writes.
    [GeneratedRegex(@"/Type\s*/Page(?![A-Za-z])")]
    private static partial Regex PdfPage();

    // The render timestamps, the only bytes two runs of the PDF differ in.
    [GeneratedRegex(@"/(?:CreationDate|ModDate)\s*(?<value>\(D:[^)]*\))")]
    private static partial Regex PdfDate();
}
