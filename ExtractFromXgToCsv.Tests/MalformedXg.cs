using System.Text;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The synthesized degenerate input for skip tests: a file no XG reader
/// accepts, written by the test that needs it and never committed (the fixture
/// convention for a degenerate case). ASCII text, so its header magic is wrong
/// and the reader rejects it with a message — the reason a skip records.
/// </summary>
internal static class MalformedXg
{
    /// <summary>Writes one at <paramref name="path"/>, creating its folder if needed.</summary>
    public static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("This is not an XG file; its header magic is wrong."));
    }
}
