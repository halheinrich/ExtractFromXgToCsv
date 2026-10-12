using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The one rendering of a skip record (<c>SPEC-notices.md</c> §7.1), directly:
/// what kind of box it is, that an empty record renders nothing, that the
/// headline counts only the kinds of unit skipped, and the optional label
/// naming the list's owner. What each surface's list
/// <em>says</em>, and when it stands, is pinned by that surface's own tests.
/// </summary>
public class SkipListNoticeTests : BunitContext
{
    private static readonly SkipRecord TwoFilesAndADecision =
    [
        new SkippedItem("broken.xg", null, "Not a valid XG file: bad magic."),
        new SkippedItem("truncated.xgp", null, "Unexpected end of stream."),
        new SkippedItem("match.xg", new BgDataTypes_Lib.XgDecisionId("match.xg", 3, 12, IsCube: true), "Access denied."),
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
    [InlineData(1, 0, "⚠ Skipped 1 file — not in the output:")]
    [InlineData(0, 2, "⚠ Skipped 2 decisions — not in the output:")]
    public void Headline_CountsOnlyTheKindsThatWereSkipped(int files, int decisions, string headline)
    {
        var record = SkipRecord.Of(
            Enumerable.Range(1, files)
                .Select(i => new SkippedItem($"broken{i}.xg", null, "Not a valid XG file: bad magic."))
                .Concat(Enumerable.Range(1, decisions)
                    .Select(i => new SkippedItem("match.xg",
                        new BgDataTypes_Lib.XgDecisionId("match.xg", 1, i, IsCube: false), "Access denied."))));

        var cut = Render<SkipListNotice>(p => p.Add(c => c.Record, record));

        var box = NoticeBox.ByMarkerClass(cut, "skipped-notice");
        Assert.Equal(headline, box.Content.QuerySelector(".fw-bold")?.TextContent);
    }

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
