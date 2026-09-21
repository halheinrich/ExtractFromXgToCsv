using AngleSharp.Dom;
using Bunit;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Reads a rendered notice box back against the shared <c>Notice</c>'s
/// documented shape (BgUiPrimitives_Razor, Public API): the box is the element
/// carrying <c>alert</c>; the live region is its <c>div.bg-notice-content</c>
/// child; the close button is that region's sibling.
/// </summary>
/// <remarks>
/// These exist because a green build proves nothing about a
/// <c>&lt;Notice&gt;</c> tag (halheinrich/backgammon#248, measured in the first
/// adopter): a parameter name the component does not have compiles clean and
/// is splatted onto the box as a plain attribute, while the real parameter
/// silently takes its default — polite, not dismissible. So every box this
/// host renders is pinned for the two things a misspelling would change, its
/// announcement and its dismissibility, and for carrying nothing on the box
/// that the call site did not mean to put there.
/// </remarks>
internal static class NoticeAssert
{
    private const string ContentWrapper = ":scope > div.bg-notice-content";
    private const string CloseButton = ":scope > button.btn-close";

    /// <summary>
    /// The box rendered through the component as <paramref name="kindClass"/>
    /// (<c>alert-info</c>, <c>alert-warning</c>, <c>alert-danger</c>) and
    /// carries, beyond that, exactly the caller's
    /// <paramref name="callerAttributes"/> — no stray attribute a misspelt
    /// parameter would have become.
    /// </summary>
    public static void IsNoticeBox(IElement box, string kindClass, params string[] callerAttributes)
    {
        Assert.Equal("DIV", box.TagName);
        Assert.Contains("alert", box.ClassList);
        Assert.Contains(kindClass, box.ClassList);
        Assert.NotNull(box.QuerySelector(ContentWrapper));

        // What may sit on the box: its class, what the caller named, the scope
        // attribute CSS isolation stamps (b-…), and bUnit's marker for a bound
        // event handler (blazor:…).
        var unexpected = box.Attributes
            .Select(a => a.Name)
            .Where(name => name != "class"
                && !callerAttributes.Contains(name)
                && !name.StartsWith("b-", StringComparison.Ordinal)
                && !name.StartsWith("blazor:", StringComparison.Ordinal))
            .ToList();
        Assert.True(unexpected.Count == 0,
            $"Unexpected attribute(s) on the notice box: {string.Join(", ", unexpected)}");
    }

    /// <summary>
    /// The notice announces itself with <paramref name="role"/>
    /// (<c>alert</c> = assertive, <c>status</c> = polite), and the role is on
    /// the content wrapper — the box carries no role and no live-region
    /// attribute, so the close button is never part of what is announced.
    /// </summary>
    public static void Announces(IElement box, string role)
    {
        var content = box.QuerySelector(ContentWrapper);
        Assert.NotNull(content);
        Assert.Equal(role, content.GetAttribute("role"));

        Assert.False(box.HasAttribute("role"), "the box itself must carry no role");
        Assert.False(box.HasAttribute("aria-live"), "the box itself must carry no aria-live");
    }

    /// <summary>The box offers the close button and is marked dismissible.</summary>
    public static void IsDismissible(IElement box)
    {
        Assert.Contains("alert-dismissible", box.ClassList);
        Assert.NotNull(box.QuerySelector(CloseButton));
    }

    /// <summary>
    /// The box offers no close button, is not marked dismissible, and no click
    /// on it or its content reaches a handler — bUnit raises
    /// <see cref="MissingEventHandlerException"/> when neither the
    /// element nor any ancestor has one.
    /// </summary>
    public static void IsNotDismissible(IElement box)
    {
        Assert.DoesNotContain("alert-dismissible", box.ClassList);
        Assert.Null(box.QuerySelector(CloseButton));

        Assert.Throws<MissingEventHandlerException>(() => box.Click());
        Assert.Throws<MissingEventHandlerException>(
            () => box.QuerySelector(ContentWrapper)!.Click());
    }
}
