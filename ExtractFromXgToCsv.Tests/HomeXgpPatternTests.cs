using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using XgFilter_Razor;
using XgFilter_Razor.TestSupport;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// bUnit tests for the Home-owned XGP name-pattern UI: the pattern textbox
/// binding, the registry-driven insert-token dropdown, the live preview /
/// inline error branch, and the numbering as remembered controls — the
/// pattern, the next number and the number length, each under its own key
/// (the umbrella's <c>SPEC-notices.md</c> §7.5). Naming mechanics themselves
/// are owned by the engine suites — here only the wiring is pinned.
/// <para>
/// Storage is planned (<see cref="BrowserStoragePlan"/>): the numbering's
/// reads and writes are the subject, and every other call the page makes is
/// declared beside them — switching to XGP persists the format.
/// </para>
/// </summary>
public class HomeXgpPatternTests : BunitContext
{
    private readonly BrowserStoragePlan _storage;

    public HomeXgpPatternTests()
    {
        this.AddHomeServices();
        _storage = BrowserStoragePlan.On(JSInterop);
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
    }

    private IRenderedComponent<Home> RenderHomeWithXgpSelected(Dictionary<string, string>? stored = null)
    {
        _storage.ExpectHomeVisit(stored);
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.OutputFormatKey, nameof(OutputFormat.Xgp),
            BrowserStorageWriteAnswer.Succeeded);

        var cut = this.RenderHome();
        cut.Find("#fmtXgp").Change(true);
        return cut;
    }

    [Fact]
    public void PatternInput_BindsToXgpOptions()
    {
        var cut = RenderHomeWithXgpSelected();

        ExpectPatternWrite("Move{dice}");
        cut.Find("#xgpPattern").Input("Move{dice}");

        var panel = cut.FindComponent<LocalModePanel>();
        Assert.Equal("Move{dice}", panel.Instance.XgpOptions.NamePattern);
        _storage.Verify();
    }

    [Fact]
    public void InsertTokenDropdown_ListsEveryRegistryToken_AndAppendsOnSelect()
    {
        var cut = RenderHomeWithXgpSelected();

        // Placeholder option + one option per registry token, in registry
        // (declaration) order.
        var optionValues = cut.FindAll("#xgpInsertToken option")
            .Select(o => o.GetAttribute("value"))
            .ToList();
        Assert.Equal(
            new[] { "" }.Concat(XgpNameTokens.All.Select(t => t.Name)),
            optionValues);

        ExpectPatternWrite("pos{n}{dice}");
        cut.Find("#xgpInsertToken").Change("dice");

        Assert.Equal("pos{n}{dice}",
            cut.FindComponent<LocalModePanel>().Instance.XgpOptions.NamePattern);
        Assert.Equal("pos{n}{dice}", cut.Find("#xgpPattern").GetAttribute("value"));
        _storage.Verify();
    }

    [Fact]
    public void ValidPattern_RendersLivePreview_ShowingCounterAdvance()
    {
        var cut = RenderHomeWithXgpSelected();

        // Default "pos{n}": the two-name preview shows the {n} advance.
        var preview = cut.Find("#xgpPreview").TextContent;
        Assert.Contains("pos001.xgp", preview);
        Assert.Contains("pos002.xgp", preview);
        Assert.Empty(cut.FindAll("#xgpOptions .text-danger"));
        _storage.Verify();
    }

    [Fact]
    public void CounterlessPattern_PreviewShowsTheUniquifier()
    {
        var cut = RenderHomeWithXgpSelected();

        ExpectPatternWrite("quiz");
        cut.Find("#xgpPattern").Input("quiz");

        var preview = cut.Find("#xgpPreview").TextContent;
        Assert.Contains("quiz.xgp", preview);
        Assert.Contains("quiz (2).xgp", preview);
        _storage.Verify();
    }

    [Fact]
    public void InvalidPattern_RendersInlineError_InsteadOfPreview()
    {
        var cut = RenderHomeWithXgpSelected();

        ExpectPatternWrite("pos{bogus}");
        cut.Find("#xgpPattern").Input("pos{bogus}");

        Assert.Empty(cut.FindAll("#xgpPreview"));
        Assert.Contains("{bogus}", cut.Find("#xgpOptions .text-danger").TextContent);
        _storage.Verify();
    }


    /// <summary>
    /// A reload shows the remembered numbering (the umbrella's
    /// <c>SPEC-notices.md</c> §7.5): the pattern, the next number and the
    /// number length, each from its own key. The plan declares every read the
    /// restore makes, so no older key — the numbering document, the last
    /// number, the prefix — is read: there is no migration.
    /// </summary>
    [Fact]
    public void AReload_ShowsTheRememberedNumbering()
    {
        var cut = RenderHomeWithXgpSelected(new()
        {
            [HomeStoragePlan.XgpPatternKey] = "quiz{n}",
            [HomeStoragePlan.XgpNextNumberKey] = "43",
            [HomeStoragePlan.XgpSuffixLengthKey] = "2",
        });

        Assert.Equal("quiz{n}", cut.Find("#xgpPattern").GetAttribute("value"));
        Assert.Equal("43", cut.Find("#xgpNextNumber").GetAttribute("value"));
        Assert.Equal(new XgpExportOptions { NamePattern = "quiz{n}", StartNumber = 43, SuffixLength = 2 },
            cut.FindComponent<LocalModePanel>().Instance.XgpOptions);
        _storage.Verify();
    }

    /// <summary>
    /// A stored number that is not one leaves its control at the default, as
    /// an absent key does.
    /// </summary>
    [Fact]
    public void AStoredNumberThatIsNotANumber_LeavesTheDefault()
    {
        var cut = RenderHomeWithXgpSelected(new()
        {
            [HomeStoragePlan.XgpNextNumberKey] = "forty",
            [HomeStoragePlan.XgpSuffixLengthKey] = "",
        });

        Assert.Equal(new XgpExportOptions(), cut.FindComponent<LocalModePanel>().Instance.XgpOptions);
        _storage.Verify();
    }

    /// <summary>
    /// A pattern edit continues the remembered next number while the pattern
    /// is the one it was remembered under, and starts a fresh series at 1
    /// otherwise; every change is remembered as it is made.
    /// </summary>
    [Fact]
    public void APatternEdit_ContinuesTheRememberedNumber_OnlyUnderItsPattern()
    {
        var cut = RenderHomeWithXgpSelected(new()
        {
            [HomeStoragePlan.XgpPatternKey] = "quiz{n}",
            [HomeStoragePlan.XgpNextNumberKey] = "43",
        });

        ExpectPatternWrite("other{n}");
        ExpectNextNumberWrite("1");
        cut.Find("#xgpPattern").Input("other{n}");
        Assert.Equal(1, cut.FindComponent<LocalModePanel>().Instance.XgpOptions.StartNumber);

        ExpectPatternWrite("quiz{n}");
        ExpectNextNumberWrite("43");
        cut.Find("#xgpPattern").Input("quiz{n}");
        Assert.Equal(43, cut.FindComponent<LocalModePanel>().Instance.XgpOptions.StartNumber);
        _storage.Verify();
    }

    /// <summary>
    /// A next number the user sets while a pattern edit's write is pending
    /// stands: the pattern edit applied its own reset of the number before it
    /// awaited storage, so when its write is answered it writes what the
    /// control holds — never the 1 it computed earlier over the newer 77.
    /// The plan answers only the writes it declares, so a stale write of 1
    /// fails at the call.
    /// </summary>
    [Fact]
    public async Task ANextNumberSetWhileAPatternWriteIsPending_Stands()
    {
        var cut = RenderHomeWithXgpSelected(new()
        {
            [HomeStoragePlan.XgpPatternKey] = "quiz{n}",
            [HomeStoragePlan.XgpNextNumberKey] = "43",
        });

        var patternWrite = _storage.ExpectHeldWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpPatternKey, "other{n}");
        var userWrite = _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpNextNumberKey, "77",
            BrowserStorageWriteAnswer.Succeeded);
        var patternEditsWrite = _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpNextNumberKey, "77",
            BrowserStorageWriteAnswer.Succeeded);
        _storage.RequireOrder(patternWrite, userWrite, patternEditsWrite);

        var patternEdit = cut.Find("#xgpPattern").InputAsync(new ChangeEventArgs { Value = "other{n}" });
        cut.WaitForAssertion(() => Assert.True(patternWrite.IsReached));
        Assert.Equal(1, cut.FindComponent<LocalModePanel>().Instance.XgpOptions.StartNumber);

        await cut.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "77" });
        patternWrite.Release(BrowserStorageWriteAnswer.Succeeded);
        await patternEdit.WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(new XgpExportOptions { NamePattern = "other{n}", StartNumber = 77 },
            cut.FindComponent<LocalModePanel>().Instance.XgpOptions);
        _storage.Verify();
    }

    /// <summary>The user's own next number and length are remembered as typed.</summary>
    [Fact]
    public void TheNextNumberAndTheLength_AreRememberedAsTheyChange()
    {
        var cut = RenderHomeWithXgpSelected();

        ExpectNextNumberWrite("12");
        cut.Find("#xgpNextNumber").Input("12");
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpSuffixLengthKey, "5",
            BrowserStorageWriteAnswer.Succeeded);
        cut.Find("#xgpSuffixLength").Input("5");

        Assert.Equal(new XgpExportOptions { StartNumber = 12, SuffixLength = 5 },
            cut.FindComponent<LocalModePanel>().Instance.XgpOptions);
        _storage.Verify();
    }

    private void ExpectPatternWrite(string pattern) =>
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpPatternKey, pattern,
            BrowserStorageWriteAnswer.Succeeded);

    private void ExpectNextNumberWrite(string number) =>
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.XgpNextNumberKey, number,
            BrowserStorageWriteAnswer.Succeeded);
}
