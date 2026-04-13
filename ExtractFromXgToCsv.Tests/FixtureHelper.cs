namespace ExtractFromXgToCsv.Tests;

internal static class FixtureHelper
{
    internal static string FixtureDir =>
        Path.Combine(AppContext.BaseDirectory, "FixtureFiles");

    internal static byte[] ReadFixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(FixtureDir, fileName));

    internal static string[] FixtureFileNames() =>
        Directory.GetFiles(FixtureDir, "*.xg")
            .Select(Path.GetFileName)
            .ToArray()!;
}