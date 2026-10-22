using AngleSharp.Dom;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Razor.TestSupport;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// What a test that renders <see cref="Home"/> needs: the app's own
/// registrations over a stub server, a render that waits for Home's restore to
/// resolve the source, and the gestures the tests drive.
/// </summary>
internal static class HomeTesting
{
    /// <summary>
    /// Registers what the app registers (<c>AddClientServices</c>) and an
    /// <see cref="HttpClient"/> over a stub server, whose processing
    /// endpoints are <paramref name="processing"/>'s when given; returns the stub.
    /// </summary>
    public static StubServerHandler AddHomeServices(
        this BunitContext ctx, HttpMessageHandler? processing = null)
    {
        ctx.Services.AddClientServices();
        // Identities a test can name.
        ctx.Services.AddSingleton<IJobIdentities>(new SequentialJobIdentities());
        var server = new StubServerHandler { Processing = processing };
        ctx.Services.AddSingleton(new HttpClient(server) { BaseAddress = new Uri("http://localhost/") });
        return server;
    }

    /// <summary>
    /// Renders Home and waits until its restore has resolved the source — the
    /// moment the panel renders and the source is reported.
    /// </summary>
    public static IRenderedComponent<Home> RenderHome(this BunitContext ctx)
    {
        var cut = ctx.Render<Home>();
        cut.WaitForLocalPanel();
        return cut;
    }

    /// <summary>
    /// Waits until the panel is up — Home's restore has resolved the source —
    /// and its first after-render has run.
    /// </summary>
    public static void WaitForLocalPanel(this IRenderedComponent<Home> cut)
    {
        cut.WaitForState(() => cut.FindComponents<LocalModePanel>().Any(), TimeSpan.FromSeconds(5));

        // The wait sees the panel in the tree, which can be before the
        // renderer has run the panel's first OnAfterRenderAsync — where it
        // restores its settings. Queueing an empty item behind it on the
        // renderer's own context means the panel's restore reads have been
        // made when this returns.
        cut.InvokeAsync(() => { }).GetAwaiter().GetResult();
    }

    // ── Gestures ────────────────────────────────────────────────────────────

    public static IElement ApplyButton(this IRenderedComponent<Home> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply Filter");

    /// <summary>Commit the filter panel's selection via its own Apply button.</summary>
    public static Task ApplyFiltersAsync(this IRenderedComponent<Home> cut) =>
        cut.ApplyButton().ClickAsync(new MouseEventArgs());

    /// <summary>
    /// A real edit on the always-visible error-range Min input; the empty
    /// string clears it again.
    /// </summary>
    public static Task EditMinAsync(this IRenderedComponent<Home> cut, string value) =>
        cut.Find("input[placeholder='Min']").InputAsync(new ChangeEventArgs { Value = value });

    public static string MinInputValue(this IRenderedComponent<Home> cut) =>
        cut.Find("input[placeholder='Min']").GetAttribute("value") ?? string.Empty;

    /// <summary>
    /// Type <paramref name="path"/> into the Local folder input and commit it
    /// at the change boundary — the gesture that latches the source path.
    /// </summary>
    public static async Task CommitFolderAsync(this IRenderedComponent<Home> cut, string path)
    {
        await cut.Find("#folderPath").InputAsync(new ChangeEventArgs { Value = path });
        await cut.Find("#folderPath").ChangeAsync(new ChangeEventArgs { Value = path });
    }

    public static Task SetOutputPathAsync(this IRenderedComponent<Home> cut, string path) =>
        cut.Find("#outputPath").InputAsync(new ChangeEventArgs { Value = path });

    public static IElement RunButton(this IRenderedComponent<Home> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Run");

}

/// <summary>
/// Home's browser storage, and the storage of the components it renders,
/// stated on a <see cref="BrowserStoragePlan"/>: this app's own keys as
/// literals — the test's oracle for them, independent of the code under test
/// — and the filter surface's through <see cref="FilterSurfaceStorage"/>, so
/// no producer key is mirrored here.
/// </summary>
internal static class HomeStoragePlan
{
    public const string OutputFormatKey = "xg_outputFormat";
    public const string FolderPathKey = "xg_folderPath";
    public const string XgpPatternKey = "xg_xgpPattern";
    public const string XgpNextNumberKey = "xg_xgpNextNumber";
    public const string XgpSuffixLengthKey = "xg_xgpSuffixLength";
    public const string XgpAnonymizeKey = "xg_xgpAnonymize";
    public const string OutputPathKey = "xg_outputPath";

    /// <summary>
    /// Home's first-render restore: each of its keys answered from
    /// <paramref name="stored"/>, or absent — the XGP numbering's three among
    /// them, and no other. When <paramref name="holdTheLastRead"/>, the
    /// restore's last read — the one after which the source is reported — is
    /// held, and returned for the test to release.
    /// </summary>
    public static BrowserStorageHeldRead? ExpectHomeRestore(
        this BrowserStoragePlan plan,
        Dictionary<string, string>? stored = null,
        bool holdTheLastRead = false)
    {
        BrowserStorageReadAnswer Answer(string key) =>
            stored is not null && stored.TryGetValue(key, out var value)
                ? BrowserStorageReadAnswer.Stored(value)
                : BrowserStorageReadAnswer.Absent;

        foreach (var key in new[] { OutputFormatKey, FolderPathKey, XgpPatternKey, XgpNextNumberKey, XgpSuffixLengthKey })
            plan.ExpectRead(BrowserStorageArea.Local, key, Answer(key));
        if (holdTheLastRead)
            return plan.ExpectHeldRead(BrowserStorageArea.Local, XgpAnonymizeKey);
        plan.ExpectRead(BrowserStorageArea.Local, XgpAnonymizeKey, Answer(XgpAnonymizeKey));
        return null;
    }

    /// <summary>Home's first-render restore in a browser that refuses every read.</summary>
    public static void ExpectHomeRestoreRefused(this BrowserStoragePlan plan)
    {
        foreach (var key in new[] { OutputFormatKey, FolderPathKey, XgpPatternKey, XgpNextNumberKey, XgpSuffixLengthKey, XgpAnonymizeKey })
            plan.ExpectRead(BrowserStorageArea.Local, key, BrowserStorageReadAnswer.Refused);
    }

    /// <summary>
    /// The next-number control remembering <paramref name="number"/> — the
    /// write a run's commit makes as the control follows it
    /// (SPEC-notices.md §7.5), or a user's edit. Held, when
    /// <paramref name="hold"/>, and returned for the test to release.
    /// </summary>
    public static BrowserStorageHeldWrite? ExpectNextNumberWrite(this BrowserStoragePlan plan, int number, bool hold = false)
    {
        var value = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (hold)
            return plan.ExpectHeldWrite(BrowserStorageArea.Local, XgpNextNumberKey, value);
        plan.ExpectWrite(BrowserStorageArea.Local, XgpNextNumberKey, value, BrowserStorageWriteAnswer.Succeeded);
        return null;
    }

    /// <summary>The Local panel's restore — its output path.</summary>
    public static void ExpectLocalPanelRestore(this BrowserStoragePlan plan, BrowserStorageReadAnswer? answer = null) =>
        plan.ExpectRead(BrowserStorageArea.Local, OutputPathKey, answer ?? BrowserStorageReadAnswer.Absent);

    /// <summary>
    /// One visit to Home: Home's restore (from <paramref name="stored"/>), the
    /// panel's, and the filter panel's mount. This boot's filter restoration
    /// is the test's to declare, once per context.
    /// </summary>
    public static void ExpectHomeVisit(this BrowserStoragePlan plan, Dictionary<string, string>? stored = null) =>
        plan.ExpectVisit(stored, holdTheLastRead: false);

    /// <summary>
    /// One visit to Home, as <see cref="ExpectHomeVisit"/>, with the restore's
    /// last read held: until the test releases it, the source is unresolved —
    /// no panel, nothing reported.
    /// </summary>
    /// <returns>The held read, to release with the browser's answer.</returns>
    public static BrowserStorageHeldRead ExpectHomeVisitWithTheLastReadHeld(
        this BrowserStoragePlan plan, Dictionary<string, string>? stored = null) =>
        plan.ExpectVisit(stored, holdTheLastRead: true)!;

    /// <summary>One visit to Home in a browser that refuses every read.</summary>
    public static void ExpectHomeVisitRefused(this BrowserStoragePlan plan)
    {
        plan.ExpectHomeRestoreRefused();
        plan.ExpectLocalPanelRestore(BrowserStorageReadAnswer.Refused);
        plan.ExpectFilterPanelMountRefused();
    }

    private static BrowserStorageHeldRead? ExpectVisit(
        this BrowserStoragePlan plan, Dictionary<string, string>? stored, bool holdTheLastRead)
    {
        var held = plan.ExpectHomeRestore(stored, holdTheLastRead);
        plan.ExpectLocalPanelRestore();
        plan.ExpectFilterPanelMount();
        return held;
    }
}
