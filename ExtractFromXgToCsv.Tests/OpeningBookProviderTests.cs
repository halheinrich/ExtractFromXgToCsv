using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Tests for <see cref="OpeningBookProvider"/>: the pure path resolution
/// (config key absent / empty / explicit) and the load-and-degrade behaviour
/// (a real fixture book loads and yields iterator options; a missing or empty
/// configuration degrades to no book without throwing). The provider is the
/// single place Local mode turns configuration into an
/// <see cref="ConvertXgToJson_Lib.XgIteratorOptions"/> for the pipeline.
/// </summary>
public class OpeningBookProviderTests
{
    private static string BookFixturePath =>
        Path.Combine(FixtureHelper.FixtureDir, "OpeningBookV2.ob");

    private static IConfiguration Config(string? openingBookPath)
    {
        var pairs = new List<KeyValuePair<string, string?>>();
        // A null path models the key being absent entirely; a non-null value
        // (including "") models the key present with that value.
        if (openingBookPath is not null)
            pairs.Add(new("OpeningBookPath", openingBookPath));

        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }

    private static OpeningBookProvider Provider(string? openingBookPath) =>
        new(Config(openingBookPath), NullLogger<OpeningBookProvider>.Instance);

    // -----------------------------------------------------------------------
    //  Pure path resolution — the three branches, no filesystem
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolvePath_KeyAbsent_AutoDetectsDefaultInstallPath()
    {
        var resolved = OpeningBookProvider.ResolvePath(Config(openingBookPath: null));

        Assert.Equal(OpeningBookProvider.PathSource.AutoDetect, resolved.Source);
        Assert.Equal(OpeningBookProvider.DefaultInstallPath, resolved.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvePath_KeyPresentButEmpty_Disabled(string value)
    {
        var resolved = OpeningBookProvider.ResolvePath(Config(value));

        Assert.Equal(OpeningBookProvider.PathSource.Disabled, resolved.Source);
        Assert.Null(resolved.Path);
    }

    [Fact]
    public void ResolvePath_KeyPresentWithPath_Explicit()
    {
        var resolved = OpeningBookProvider.ResolvePath(Config(@"D:\books\OpeningBookV2.ob"));

        Assert.Equal(OpeningBookProvider.PathSource.Explicit, resolved.Source);
        Assert.Equal(@"D:\books\OpeningBookV2.ob", resolved.Path);
    }

    // -----------------------------------------------------------------------
    //  Load and degrade — against the real fixture book
    // -----------------------------------------------------------------------

    [Fact]
    public void ExplicitPath_ToRealBook_LoadsAndExposesIteratorOptions()
    {
        Assert.True(File.Exists(BookFixturePath),
            $"Expected fixture not present: {BookFixturePath}. Copy OpeningBookV2.ob " +
            "from the eXtreme Gammon 2 install directory into TestData/FixtureFiles/.");

        var provider = Provider(BookFixturePath);

        Assert.NotNull(provider.Book);
        Assert.True(provider.Book!.EntryCount > 0, "the shipped book has entries");
        Assert.NotNull(provider.IteratorOptions);
        Assert.Same(provider.Book, provider.IteratorOptions!.OpeningBook);
    }

    [Fact]
    public void EmptyConfig_DisablesEnrichment_NoBookNoOptions()
    {
        var provider = Provider(openingBookPath: "");

        Assert.Null(provider.Book);
        Assert.Null(provider.IteratorOptions);
    }

    [Fact]
    public void MissingFile_DegradesToNoBook_WithoutThrowing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-such-book-{Guid.NewGuid():N}.ob");

        var provider = Provider(missing);

        Assert.Null(provider.Book);
        Assert.Null(provider.IteratorOptions);
    }
}
