using ExtractFromXgToCsv.Services;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Unit tests for <see cref="FilterDocumentStore"/> — the server's text-file
/// relay for saved-filters documents. The safety half is the filename-shape
/// rule (nothing but a simple file name may reach the folder); the IO half is
/// the null-for-absent read contract and the overwriting write. The Local-mode
/// gate is not this type's concern — it lives in the controller and is pinned
/// by <see cref="FilterDocumentEndpointTests"/>.
/// </summary>
public class FilterDocumentStoreTests : IDisposable
{
    private readonly string _folder =
        Directory.CreateTempSubdirectory("xg-filterdoc-").FullName;

    private readonly FilterDocumentStore _store = new();

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("xg-filters.json")]
    [InlineData("bgquiz-filters.json")]
    [InlineData("some other name.txt")]
    public void IsValidFileName_AcceptsSimpleNames(string name) =>
        Assert.True(FilterDocumentStore.IsValidFileName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b.json")]
    [InlineData(@"a\b.json")]
    [InlineData(@"..\up.json")]
    [InlineData("../up.json")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"C:\rooted.json")]
    [InlineData("C:rooted.json")]
    [InlineData("bad\"quote.json")]
    [InlineData("bad|pipe.json")]
    public void IsValidFileName_RejectsEverythingButASimpleName(string? name) =>
        Assert.False(FilterDocumentStore.IsValidFileName(name));

    [Fact]
    public async Task ReadAsync_AbsentFile_ReturnsNull() =>
        Assert.Null(await _store.ReadAsync(_folder, "absent.json"));

    [Fact]
    public async Task ReadAsync_AbsentFolder_ReturnsNull()
    {
        // A folder that doesn't exist is the same absence as a missing file —
        // a value, not an error — so a mistyped source folder degrades to an
        // empty saved-filters context rather than a load failure.
        var gone = Path.Combine(_folder, "no-such-subfolder");
        Assert.Null(await _store.ReadAsync(gone, "xg-filters.json"));
    }

    [Fact]
    public async Task WriteAsync_RoundTrips_AndOverwrites()
    {
        await _store.WriteAsync(_folder, "doc.json", "first");
        Assert.Equal("first", await _store.ReadAsync(_folder, "doc.json"));

        await _store.WriteAsync(_folder, "doc.json", "second");
        Assert.Equal("second", await _store.ReadAsync(_folder, "doc.json"));
    }

    [Fact]
    public async Task WriteAsync_AbsentFolder_Throws()
    {
        // The folder is never created: writing into a folder that doesn't
        // exist is the caller's mistake and surfaces as the IO exception it
        // is (→ 500 at the endpoint → the client's WriteFailed degrade).
        var gone = Path.Combine(_folder, "no-such-subfolder");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _store.WriteAsync(gone, "doc.json", "content"));
    }

    [Fact]
    public async Task BothOperations_RejectBadNamesAndBlankFolders()
    {
        // Defense in depth behind the controller's 400: the shape rules hold
        // even for a caller that bypasses the controller.
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.ReadAsync(_folder, @"..\up.json"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.WriteAsync(_folder, "a/b.json", "content"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.ReadAsync("", "doc.json"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.WriteAsync("   ", "doc.json", "content"));
    }
}
