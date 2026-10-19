using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="GoldenRunFolder"/>'s rule, which keeps a golden run out of
/// every folder but a new one of its own. Each test works in a fresh temp
/// root and deletes nothing.
/// </summary>
public class GoldenRunFolderTests
{
    // A fresh Golden folder whose input folder is NOT created, so a label
    // refused as the input is refused by its resolved path, never because
    // the folder exists.
    private static (string Golden, string Input) FreshRoot()
    {
        var golden = Path.Combine(Path.GetTempPath(), $"golden-{Guid.NewGuid():N}", "Golden");
        Directory.CreateDirectory(golden);
        return (golden, Path.Combine(golden, "input"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void RefusesALabelThatIsNotOnePlainSegment(string? label)
    {
        var (golden, input) = FreshRoot();

        Assert.False(GoldenRunFolder.TryResolve(golden, input, label, out var runFolder, out _));
        Assert.Null(runFolder);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("INPUT")]
    [InlineData("input.")]
    public void RefusesALabelThatResolvesToTheInputFolder(string label)
    {
        if (label != "input" && !OperatingSystem.IsWindows())
            return; // Case-folding and trailing-dot trimming are Windows path resolution.
        var (golden, input) = FreshRoot();

        Assert.False(GoldenRunFolder.TryResolve(golden, input, label, out _, out var refusal));
        Assert.Contains("input folder", refusal);
    }

    [Fact]
    public void RefusesALabelWhoseFolderExists()
    {
        var (golden, input) = FreshRoot();
        var existing = Path.Combine(golden, "5fd9eca-a");
        Directory.CreateDirectory(existing);

        Assert.False(GoldenRunFolder.TryResolve(golden, input, "5fd9eca-a", out _, out var refusal));
        Assert.Contains("already exists", refusal);
        Assert.True(Directory.Exists(existing));
    }

    [Fact]
    public void AcceptsAnOrdinaryLabel_AsANewFolderBelowGolden()
    {
        var (golden, input) = FreshRoot();

        Assert.True(GoldenRunFolder.TryResolve(golden, input, "5fd9eca-a", out var runFolder, out var refusal));
        Assert.Null(refusal);
        Assert.Equal(Path.Combine(Path.GetFullPath(golden), "5fd9eca-a"), runFolder);
        Assert.False(Directory.Exists(runFolder));
    }
}
