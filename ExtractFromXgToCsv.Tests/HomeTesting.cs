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
        // Identities a test can name: the owed job's appears in the numbering
        // record's document.
        ctx.Services.AddSingleton<IJobIdentities>(new SequentialJobIdentities());
        var server = new StubServerHandler { Processing = processing };
        ctx.Services.AddSingleton(new HttpClient(server) { BaseAddress = new Uri("http://localhost/") });
        return server;
    }

    /// <summary>
    /// Renders Home and waits until its restore has resolved the source — the
    /// moment the panel renders and the source is reported — and until the
    /// page has reconciled with the server.
    /// </summary>
    public static IRenderedComponent<Home> RenderHome(this BunitContext ctx)
    {
        var cut = ctx.Render<Home>();
        cut.WaitForLocalPanel();
        return cut;
    }

    /// <summary>
    /// Waits until the panel is up — Home's restore has resolved the source —
    /// and its first after-render has run, and until the page has reconciled
    /// with the server, which every start waits on (SPEC-notices.md §7.5).
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

        if (cut.FindComponents<LocalModePanel>() is [var local])
            cut.WaitForState(() => local.Instance.StartState.Instance is not null, TimeSpan.FromSeconds(5));
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
    public const string NumberingKey = "xg_xgpNumbering";
    public const string XgpPatternKey = "xg_xgpPattern";
    public const string XgpPrefixKey = "xg_xgpPrefix";
    public const string XgpLastNumberKey = "xg_xgpLastNumber";
    public const string XgpSuffixLengthKey = "xg_xgpSuffixLength";
    public const string XgpAnonymizeKey = "xg_xgpAnonymize";
    public const string OutputPathKey = "xg_outputPath";

    /// <summary>
    /// The numbering record's document as it is stored, restated here as the
    /// test's oracle: a numbering, an optional mark, and the owed jobs, given
    /// as their stored JSON objects.
    /// </summary>
    public static string NumberingDocument(
        string pattern, int lastNumber, int suffixLength, (string Instance, int Number)? mark = null, params string[] owed) =>
        "{\"version\":1,"
        + $"\"numbering\":{{\"pattern\":\"{pattern}\",\"lastNumber\":{lastNumber},\"suffixLength\":{suffixLength}}},"
        + (mark is { } m ? $"\"mark\":{{\"instance\":\"{m.Instance}\",\"number\":{m.Number}}}," : string.Empty)
        + $"\"owed\":[{string.Join(",", owed)}]}}";

    /// <summary>An owed job as the numbering record stores it.</summary>
    public static string OwedJob(string jobId, string instance, string pattern, int startNumber, int suffixLength, string folder) =>
        $"{{\"jobId\":\"{jobId}\",\"instance\":\"{instance}\","
        + $"\"options\":{{\"namePattern\":\"{pattern}\",\"startNumber\":{startNumber},\"suffixLength\":{suffixLength}}},"
        + $"\"folder\":\"{folder.Replace(@"\", @"\\")}\"}}";

    /// <summary>
    /// Home's first-render restore: each of its keys answered from
    /// <paramref name="stored"/>, or absent. Only while no numbering document
    /// is stored — not one that cannot be read — the keys it replaced are
    /// read, and when they hold anything, the record they migrate to is
    /// written as <paramref name="migratedDocument"/> and every key that held
    /// something is removed. When <paramref name="holdTheLastRead"/>, the
    /// restore's last read — the one after which the source is reported — is
    /// held, and returned for the test to release.
    /// </summary>
    public static BrowserStorageHeldRead? ExpectHomeRestore(
        this BrowserStoragePlan plan,
        Dictionary<string, string>? stored = null,
        string? migratedDocument = null,
        bool holdTheLastRead = false)
    {
        BrowserStorageReadAnswer Answer(string key) =>
            stored is not null && stored.TryGetValue(key, out var value)
                ? BrowserStorageReadAnswer.Stored(value)
                : BrowserStorageReadAnswer.Absent;
        bool Holds(string key) => stored is not null && stored.ContainsKey(key);

        plan.ExpectRead(BrowserStorageArea.Local, OutputFormatKey, Answer(OutputFormatKey));
        plan.ExpectRead(BrowserStorageArea.Local, FolderPathKey, Answer(FolderPathKey));
        plan.ExpectRead(BrowserStorageArea.Local, NumberingKey, Answer(NumberingKey));
        if (!Holds(NumberingKey))
        {
            var legacy = new List<string> { XgpPatternKey, XgpPrefixKey, XgpLastNumberKey, XgpSuffixLengthKey };
            foreach (var key in legacy)
                plan.ExpectRead(BrowserStorageArea.Local, key, Answer(key));

            var held = legacy.Where(Holds).ToList();
            if (held.Count > 0)
            {
                plan.ExpectWrite(BrowserStorageArea.Local, NumberingKey,
                    migratedDocument ?? throw new ArgumentException(
                        "The legacy keys hold a numbering, so the test states the document it migrates to.",
                        nameof(migratedDocument)),
                    BrowserStorageWriteAnswer.Succeeded);
                foreach (var key in held)
                    plan.ExpectRemove(BrowserStorageArea.Local, key, BrowserStorageWriteAnswer.Succeeded);
            }
        }
        if (holdTheLastRead)
            return plan.ExpectHeldRead(BrowserStorageArea.Local, XgpAnonymizeKey);
        plan.ExpectRead(BrowserStorageArea.Local, XgpAnonymizeKey, Answer(XgpAnonymizeKey));
        return null;
    }

    /// <summary>
    /// Home's first-render restore in a browser that refuses every read. A
    /// refused numbering read is not nothing stored, so nothing is migrated.
    /// </summary>
    public static void ExpectHomeRestoreRefused(this BrowserStoragePlan plan)
    {
        foreach (var key in new[] { OutputFormatKey, FolderPathKey, NumberingKey, XgpAnonymizeKey })
            plan.ExpectRead(BrowserStorageArea.Local, key, BrowserStorageReadAnswer.Refused);
    }

    /// <summary>
    /// A Local page's reconciliation with the server (SPEC-notices.md §7.5):
    /// it reads the numbering record afresh — answered
    /// <paramref name="stored"/>, absent by default — before asking the server
    /// for its ledger.
    /// </summary>
    public static void ExpectLocalReconcile(this BrowserStoragePlan plan, BrowserStorageReadAnswer? stored = null) =>
        plan.ExpectRead(BrowserStorageArea.Local, NumberingKey, stored ?? BrowserStorageReadAnswer.Absent);

    /// <summary>
    /// The numbering record's part in one Local XGP run through Home
    /// (SPEC-notices.md §7.5): the job recorded as owed before its start sent
    /// — a checkpoint over <paramref name="before"/>, writing
    /// <paramref name="owing"/> — then, the job ended, the reconciliation pass
    /// that applies its entry from the ledger: the pass's first read, then the
    /// entry's checkpoint, writing <paramref name="applied"/>, numbering, mark
    /// and owed together.
    /// </summary>
    /// <returns>The apply checkpoint's write, held when <paramref name="holdTheApply"/>.</returns>
    public static BrowserStorageHeldWrite? ExpectXgpRunBookkeeping(
        this BrowserStoragePlan plan, string? before, string owing, string applied, bool holdTheApply = false)
    {
        plan.ExpectNumberingCheckpoint(
            before is null ? BrowserStorageReadAnswer.Absent : BrowserStorageReadAnswer.Stored(before), owing);
        plan.ExpectLocalReconcile(BrowserStorageReadAnswer.Stored(owing));
        plan.ExpectRead(BrowserStorageArea.Local, NumberingKey, BrowserStorageReadAnswer.Stored(owing));
        if (holdTheApply)
            return plan.ExpectHeldWrite(BrowserStorageArea.Local, NumberingKey, applied);
        plan.ExpectWrite(BrowserStorageArea.Local, NumberingKey, applied, BrowserStorageWriteAnswer.Succeeded);
        return null;
    }

    /// <summary>
    /// One checkpoint of the numbering record: its fresh read, answered
    /// <paramref name="before"/>, then its one write of
    /// <paramref name="written"/>, answered <paramref name="answer"/>
    /// (stored, by default).
    /// </summary>
    public static void ExpectNumberingCheckpoint(
        this BrowserStoragePlan plan,
        BrowserStorageReadAnswer before,
        string written,
        BrowserStorageWriteAnswer? answer = null)
    {
        plan.ExpectRead(BrowserStorageArea.Local, NumberingKey, before);
        plan.ExpectWrite(BrowserStorageArea.Local, NumberingKey, written, answer ?? BrowserStorageWriteAnswer.Succeeded);
    }

    /// <summary>The Local panel's restore — its output path.</summary>
    public static void ExpectLocalPanelRestore(this BrowserStoragePlan plan, BrowserStorageReadAnswer? answer = null) =>
        plan.ExpectRead(BrowserStorageArea.Local, OutputPathKey, answer ?? BrowserStorageReadAnswer.Absent);

    /// <summary>
    /// One visit to Home: Home's restore (from <paramref name="stored"/>), the
    /// panel's, the filter panel's mount, and the page's reconciliation. This
    /// boot's filter restoration is the test's to declare, once per context.
    /// </summary>
    public static void ExpectHomeVisit(
        this BrowserStoragePlan plan, Dictionary<string, string>? stored = null, string? migratedDocument = null) =>
        plan.ExpectVisit(stored, migratedDocument, holdTheLastRead: false);

    /// <summary>
    /// One visit to Home, as <see cref="ExpectHomeVisit"/>, with the restore's
    /// last read held: until the test releases it, the source is unresolved —
    /// no panel, nothing reported.
    /// </summary>
    /// <returns>The held read, to release with the browser's answer.</returns>
    public static BrowserStorageHeldRead ExpectHomeVisitWithTheLastReadHeld(
        this BrowserStoragePlan plan, Dictionary<string, string>? stored = null) =>
        plan.ExpectVisit(stored, migratedDocument: null, holdTheLastRead: true)!;

    /// <summary>One visit to Home in a browser that refuses every read.</summary>
    public static void ExpectHomeVisitRefused(this BrowserStoragePlan plan)
    {
        plan.ExpectHomeRestoreRefused();
        plan.ExpectLocalPanelRestore(BrowserStorageReadAnswer.Refused);
        plan.ExpectFilterPanelMountRefused();
        plan.ExpectLocalReconcile(BrowserStorageReadAnswer.Refused);
    }

    private static BrowserStorageHeldRead? ExpectVisit(
        this BrowserStoragePlan plan, Dictionary<string, string>? stored, string? migratedDocument, bool holdTheLastRead)
    {
        var held = plan.ExpectHomeRestore(stored, migratedDocument, holdTheLastRead);
        plan.ExpectLocalPanelRestore();
        plan.ExpectFilterPanelMount();
        plan.ExpectLocalReconcile(stored is not null && stored.TryGetValue(NumberingKey, out var document)
            ? BrowserStorageReadAnswer.Stored(document)
            : migratedDocument is not null ? BrowserStorageReadAnswer.Stored(migratedDocument) : null);
        return held;
    }
}
