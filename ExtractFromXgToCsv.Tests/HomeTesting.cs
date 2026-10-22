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
/// registrations over a stub server, a render that waits for the
/// coordinator's restore to resolve the source, and the gestures the tests
/// drive.
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
    /// Renders Home and waits until the coordinator's restore has resolved the
    /// source — the moment the settings controls and the panel render and the
    /// source is reported.
    /// </summary>
    public static IRenderedComponent<Home> RenderHome(this BunitContext ctx)
    {
        var cut = ctx.Render<Home>();
        cut.WaitForLocalPanel();
        return cut;
    }

    /// <summary>Waits until the panel is up: the coordinator's restore has ended.</summary>
    public static void WaitForLocalPanel(this IRenderedComponent<Home> cut) =>
        cut.WaitForState(() => cut.FindComponents<LocalModePanel>().Any(), TimeSpan.FromSeconds(5));

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

    /// <summary>The app's coordinator, as the page renders it.</summary>
    public static ExtractApp App(this BunitContext ctx) => ctx.Services.GetRequiredService<ExtractApp>();
}

/// <summary>
/// The app's browser storage as a visit to Home makes it, stated on a
/// <see cref="BrowserStoragePlan"/>: the coordinator's keys as literals — the
/// test's oracle for them, independent of the code under test — and the
/// filter surface's through <see cref="FilterSurfaceStorage"/>, so no producer
/// key is mirrored here.
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
    /// The coordinator's restore, once per app: each of its keys answered from
    /// <paramref name="stored"/>, or absent — the output path and the XGP
    /// numbering's three among them, and no other. When
    /// <paramref name="holdTheLastRead"/>, the restore's last read — the one
    /// after which the source is reported — is held, and returned for the
    /// test to release.
    /// </summary>
    public static BrowserStorageHeldRead? ExpectRestore(
        this BrowserStoragePlan plan,
        Dictionary<string, string>? stored = null,
        bool holdTheLastRead = false)
    {
        BrowserStorageReadAnswer Answer(string key) =>
            stored is not null && stored.TryGetValue(key, out var value)
                ? BrowserStorageReadAnswer.Stored(value)
                : BrowserStorageReadAnswer.Absent;

        foreach (var key in new[] { OutputFormatKey, FolderPathKey, OutputPathKey, XgpPatternKey, XgpNextNumberKey, XgpSuffixLengthKey })
            plan.ExpectRead(BrowserStorageArea.Local, key, Answer(key));
        if (holdTheLastRead)
            return plan.ExpectHeldRead(BrowserStorageArea.Local, XgpAnonymizeKey);
        plan.ExpectRead(BrowserStorageArea.Local, XgpAnonymizeKey, Answer(XgpAnonymizeKey));
        return null;
    }

    /// <summary>The coordinator's restore in a browser that refuses every read.</summary>
    public static void ExpectRestoreRefused(this BrowserStoragePlan plan)
    {
        foreach (var key in new[] { OutputFormatKey, FolderPathKey, OutputPathKey, XgpPatternKey, XgpNextNumberKey, XgpSuffixLengthKey, XgpAnonymizeKey })
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

    /// <summary>
    /// This app's first visit to Home: the coordinator's restore (from
    /// <paramref name="stored"/>) and the filter panel's mount. This boot's
    /// filter restoration is the test's to declare, once per context.
    /// </summary>
    public static void ExpectHomeVisit(this BrowserStoragePlan plan, Dictionary<string, string>? stored = null) =>
        plan.ExpectVisit(stored, holdTheLastRead: false);

    /// <summary>
    /// The first visit to Home, as <see cref="ExpectHomeVisit"/>, with the
    /// restore's last read held: until the test releases it, the source is
    /// unresolved — no settings controls, no panel, nothing reported.
    /// </summary>
    /// <returns>The held read, to release with the browser's answer.</returns>
    public static BrowserStorageHeldRead ExpectHomeVisitWithTheLastReadHeld(
        this BrowserStoragePlan plan, Dictionary<string, string>? stored = null) =>
        plan.ExpectVisit(stored, holdTheLastRead: true)!;

    /// <summary>The first visit to Home in a browser that refuses every read.</summary>
    public static void ExpectHomeVisitRefused(this BrowserStoragePlan plan)
    {
        plan.ExpectRestoreRefused();
        plan.ExpectFilterPanelMountRefused();
    }

    /// <summary>
    /// A later visit to Home in the same app — a remount. The restore has run
    /// for the app, so none of the coordinator's settings is read again; the
    /// only reads are the filter panel's two display preferences, which every
    /// mount of it makes (XgFilter_Razor's contract).
    /// </summary>
    public static void ExpectHomeRemount(this BrowserStoragePlan plan) => plan.ExpectFilterPanelMount();

    /// <summary>A remount, as <see cref="ExpectHomeRemount"/>, in a browser that refuses every read.</summary>
    public static void ExpectHomeRemountRefused(this BrowserStoragePlan plan) => plan.ExpectFilterPanelMountRefused();

    private static BrowserStorageHeldRead? ExpectVisit(
        this BrowserStoragePlan plan, Dictionary<string, string>? stored, bool holdTheLastRead)
    {
        var held = plan.ExpectRestore(stored, holdTheLastRead);
        plan.ExpectFilterPanelMount();
        return held;
    }
}
