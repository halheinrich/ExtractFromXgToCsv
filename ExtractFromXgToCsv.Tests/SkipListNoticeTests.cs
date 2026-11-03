using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ConvertXgToJson_Lib.TestSupport;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The one rendering of a skip record (<c>SPEC-notices.md</c> §7.1), directly:
/// what kind of box it is, that an empty record renders nothing, that the
/// headline counts only the kinds of unit skipped, that an entry is one list
/// item whatever its reason holds, and the optional label naming the list's
/// owner. What each surface's list
/// <em>says</em>, and when it stands, is pinned by that surface's own tests.
/// </summary>
public class SkipListNoticeTests : BunitContext
{
    private static readonly SkipRecord TwoFilesAndADecision =
    [
        SkippedItem.ForFile("broken.xg", "Not a valid XG file: bad magic."),
        SkippedItem.ForFile("truncated.xgp", "Unexpected end of stream."),
        SkippedItem.ForDecision("match.xg", new BgDataTypes_Lib.XgDecisionId("match.xg", 3, 12, IsCube: true), "Access denied."),
    ];

    public SkipListNoticeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    [Fact]
    public void EmptyRecord_RendersNothing()
    {
        var cut = Render<SkipListNotice>(p => p.Add(c => c.Record, SkipRecord.Empty));

        Assert.Empty(NoticeBox.AllIn(cut));
        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void Record_IsAnAssertiveNonDismissibleWarning_CarryingTheMarkerAndTheCallersClasses()
    {
        var cut = Render<SkipListNotice>(p => p
            .Add(c => c.Record, TwoFilesAndADecision)
            .AddUnmatched("class", "mt-2"));

        var box = NoticeBox.ByMarkerClass(cut, "skipped-notice").ShouldBe(
            NoticeKind.Warning, NoticeAnnouncement.Assertive, dismissible: false, "class");
        Assert.Equal(["skipped-notice", "mt-2"], box.CallerClasses);
        Assert.Contains("Skipped 2 files and 1 decision — not in the output:", box.Content.TextContent);
        Assert.Equal(3, box.Content.QuerySelectorAll("li").Length);
        Assert.Empty(box.Content.QuerySelectorAll(".skip-list-label"));
    }

    /// <summary>
    /// The record of what was lost is never dismissible (SPEC-notices.md §4),
    /// whatever a caller passes: a caller's <c>Dismissible</c> reaches the box
    /// through the captured attributes, and the rendering's own value, stated
    /// after them, wins.
    /// </summary>
    [Fact]
    public void ACallersDismissible_CannotMakeTheRecordDismissible()
    {
        var cut = Render<SkipListNotice>(p => p
            .Add(c => c.Record, TwoFilesAndADecision)
            .AddUnmatched("Dismissible", true));

        NoticeBox.ByMarkerClass(cut, "skipped-notice").ShouldBe(
            NoticeKind.Warning, NoticeAnnouncement.Assertive, dismissible: false, "class");
        Assert.Empty(cut.FindAll("button"));
    }

    [Theory]
    [InlineData(1, 0, 0, "⚠ Skipped 1 file — not in the output:")]
    [InlineData(2, 0, 0, "⚠ Skipped 2 files — not in the output:")]
    [InlineData(0, 1, 0, "⚠ Skipped 1 game — not in the output:")]
    [InlineData(0, 2, 0, "⚠ Skipped 2 games — not in the output:")]
    [InlineData(0, 0, 1, "⚠ Skipped 1 decision — not in the output:")]
    [InlineData(0, 0, 2, "⚠ Skipped 2 decisions — not in the output:")]
    [InlineData(1, 1, 0, "⚠ Skipped 1 file and 1 game — not in the output:")]
    [InlineData(2, 0, 3, "⚠ Skipped 2 files and 3 decisions — not in the output:")]
    [InlineData(0, 2, 1, "⚠ Skipped 2 games and 1 decision — not in the output:")]
    [InlineData(1, 2, 3, "⚠ Skipped 1 file, 2 games and 3 decisions — not in the output:")]
    public void Headline_CountsOnlyTheKindsThatWereSkipped(int files, int games, int decisions, string headline)
    {
        var record = SkipRecord.Of(
            Enumerable.Range(1, files)
                .Select(i => SkippedItem.ForFile($"broken{i}.xg", "Not a valid XG file: bad magic."))
                .Concat(Enumerable.Range(1, games)
                    .Select(i => SkippedItem.ForGame("match.xg", $"Invalid standing in match.xg, game {i}: refused.")))
                .Concat(Enumerable.Range(1, decisions)
                    .Select(i => SkippedItem.ForDecision("match.xg",
                        new BgDataTypes_Lib.XgDecisionId("match.xg", 1, i, IsCube: false), "Access denied."))));

        var cut = Render<SkipListNotice>(p => p.Add(c => c.Record, record));

        var box = NoticeBox.ByMarkerClass(cut, "skipped-notice");
        Assert.Equal(headline, box.Content.QuerySelector(".fw-bold")?.TextContent);
    }

    /// <summary>
    /// A walk's omission is one line, the rule owner's own refusal; a failure's
    /// message may not be. So the file's reason here is a real failure message
    /// that holds a line break, and the game's is a real omission's text from
    /// the walk. Each stays one list item, whole, with nothing after it.
    /// </summary>
    [Fact]
    public void AFailuresMessageWithALineBreak_AndAnOmissionsLine_AreEachOneListItem_Whole()
    {
        var failure = new ArgumentOutOfRangeException("away1", -1, "Away must be positive.").Message;
        Assert.Contains('\n', failure);
        var omission = Assert.Single(WalkOmissions.Of(
            XgFileEdits.WithRefusedGames(SyntheticXg.Match(games: 1), 1), "match.xg"));
        Assert.DoesNotContain('\n', omission);
        var file = Path.Combine("club", "match.xg");
        SkipRecord record =
        [
            SkippedItem.ForFile(file, failure),
            SkippedItem.ForGame(file, omission),
        ];

        var cut = Render<SkipListNotice>(p => p.Add(c => c.Record, record));

        var items = NoticeBox.ByMarkerClass(cut, "skipped-notice").Content.QuerySelectorAll("li");
        Assert.Equal(2, items.Length);
        Assert.Equal($"{file} — {Normalize(failure)}", Normalize(items[0].TextContent));
        Assert.Equal($"{file} — {omission}", Normalize(items[1].TextContent));
    }

    // Collapses the markup's line breaks and indentation into the single
    // spaces a reader sees (as HomeStorageUnavailableTests does).
    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Label_NamesTheListsOwner_AboveTheHeadline()
    {
        var cut = Render<SkipListNotice>(p => p
            .Add(c => c.Record, TwoFilesAndADecision)
            .Add(c => c.Label, "This selection"));

        var box = NoticeBox.ByMarkerClass(cut, "skipped-notice");
        var label = box.Content.QuerySelector(".skip-list-label");
        Assert.NotNull(label);
        Assert.Equal("This selection", label.TextContent);
        Assert.Same(box.Content.FirstElementChild, label);
    }
}
