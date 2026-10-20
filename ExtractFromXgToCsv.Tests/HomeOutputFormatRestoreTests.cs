using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Razor;
using XgFilter_Razor.TestSupport;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins how Home restores the persisted output format from its
/// <c>xg_outputFormat</c> key: by searching the declared formats for the token
/// the write half emitted, never by parsing (halheinrich/backgammon#164).
///
/// <para>The write half is <c>fmt.ToString()</c> — a declaration name — so the
/// read half must accept those and nothing else. <c>Enum.TryParse</c>, which
/// this replaced, also accepted a numeric ordinal, tying a durable per-user
/// entry to member numbering. <see cref="OutputFormat"/> has been appended to
/// before (<c>Xgp</c> is the newest member), and an insertion rather than an
/// append would silently re-point every stored ordinal at a different
/// format — a user who had chosen PDF getting PowerPoint, with nothing on
/// screen to say so.</para>
///
/// <para>An unrecognized token leaves the <c>Csv</c> default standing rather
/// than throwing, which is the pre-existing behavior and the right one: this
/// reads text the app does not control, and there is no user to show a
/// storage-parse error to.</para>
/// </summary>
public class HomeOutputFormatRestoreTests : BunitContext
{
    private readonly BrowserStoragePlan _storage;

    public HomeOutputFormatRestoreTests()
    {
        this.AddHomeServices();
        _storage = BrowserStoragePlan.On(JSInterop);
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
    }

    private IRenderedComponent<Home> RenderWithStoredFormat(string token)
    {
        _storage.ExpectHomeVisit(new() { [HomeStoragePlan.OutputFormatKey] = token });
        return this.RenderHome();
    }

    /// <summary>The radio the page shows as selected — the observable.</summary>
    private static string SelectedFormatId(IRenderedComponent<Home> cut) =>
        cut.FindAll("input[name='outputFormat']")
           .Single(r => r.HasAttribute("checked"))
           .Id!;

    /// <summary>
    /// Every format round-trips under the exact token the write half persists,
    /// so the strictness costs no legitimate stored value — including as
    /// members are added.
    /// </summary>
    [Theory]
    [InlineData(nameof(OutputFormat.Csv), "fmtCsv")]
    [InlineData(nameof(OutputFormat.DiagramJson), "fmtDiagram")]
    [InlineData(nameof(OutputFormat.Pptx), "fmtPptx")]
    [InlineData(nameof(OutputFormat.Pdf), "fmtPdf")]
    [InlineData(nameof(OutputFormat.Xgp), "fmtXgp")]
    public void StoredDeclarationName_Restores(string token, string expectedRadioId)
    {
        Assert.Equal(expectedRadioId, SelectedFormatId(RenderWithStoredFormat(token)));
        _storage.Verify();
    }

    /// <summary>
    /// The hazard closed: an ordinal is no longer honoured. "3" was Pdf's
    /// number when written; under Enum.TryParse it restored Pdf, and it would
    /// have restored something else after any insertion into the ladder. It now
    /// leaves the default standing.
    /// </summary>
    [Theory]
    [InlineData("3")]    // Pdf's ordinal
    [InlineData("0")]    // Csv's ordinal — right answer, wrong token kind
    [InlineData("99")]   // outside the declared range
    [InlineData("-1")]
    [InlineData("pdf")]  // case variant: TryParse's overload here was already case-sensitive
    [InlineData("NotAFormat")]
    [InlineData("")]
    public void StoredNonNameToken_LeavesTheDefaultStanding(string token)
    {
        Assert.Equal("fmtCsv", SelectedFormatId(RenderWithStoredFormat(token)));
        _storage.Verify();
    }
}
