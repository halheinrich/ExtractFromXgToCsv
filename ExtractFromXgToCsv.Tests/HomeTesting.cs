using AngleSharp.Dom;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Razor.TestSupport;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// What a test that renders <see cref="Home"/> needs: the app's own
/// registrations over a stub server, a render that waits for Home's restore to
/// resolve the mode, and the gestures the tests drive.
/// </summary>
internal static class HomeTesting
{
    /// <summary>
    /// Registers what the app registers (<c>AddClientServices</c>) and an
    /// <see cref="HttpClient"/> over a stub server answering
    /// <paramref name="mode"/>; returns the stub.
    /// </summary>
    public static StubAppModeHandler AddHomeServices(
        this BunitContext ctx, string mode, CapturingProcessHandler? processing = null)
    {
        ctx.Services.AddClientServices();
        var server = new StubAppModeHandler(mode) { Processing = processing };
        ctx.Services.AddSingleton(new HttpClient(server) { BaseAddress = new Uri("http://localhost/") });
        return server;
    }

    /// <summary>
    /// Renders Home and waits until its restore has resolved the mode — the
    /// moment a mode panel renders and the source is reported.
    /// </summary>
    public static IRenderedComponent<Home> RenderHome(this BunitContext ctx)
    {
        var cut = ctx.Render<Home>();
        cut.WaitForModePanel();
        return cut;
    }

    /// <summary>
    /// Waits until a mode panel is up — Home's restore has resolved the mode —
    /// and its first after-render has run.
    /// </summary>
    public static void WaitForModePanel(this IRenderedComponent<Home> cut)
    {
        cut.WaitForState(
            () => cut.FindComponents<LocalModePanel>().Any() || cut.FindComponents<WebModePanel>().Any(),
            TimeSpan.FromSeconds(5));

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

    public static IElement DownloadButton(this IRenderedComponent<Home> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Download", StringComparison.Ordinal));

    /// <summary>Select <paramref name="fixture"/> in Web mode's file picker.</summary>
    public static void UploadFixture(this IRenderedComponent<Home> cut, string fixture) =>
        // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(fixture), fixture));

    /// <summary>
    /// A dismissed picker: a selection gesture that selects nothing. bUnit's
    /// upload helper refuses an empty selection, so the picker's own change
    /// event is raised with no files — what the browser sends.
    /// </summary>
    public static Task UploadNothingAsync(this IRenderedComponent<Home> cut)
    {
        var picker = cut.FindComponents<InputFile>()[0];
        return cut.InvokeAsync(() => picker.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([])));
    }
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
    public const string CsvFileNameKey = "xg_csvFileName";
    public const string LastFolderHintKey = "xg_lastFolderHint";

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
    /// <paramref name="stored"/>, or absent. While no numbering document is
    /// stored, the keys it replaced are read — the legacy prefix only when no
    /// pattern is — and when they hold anything, the record they migrate to
    /// is written as <paramref name="migratedDocument"/> and the keys that
    /// held something are removed.
    /// </summary>
    public static void ExpectHomeRestore(
        this BrowserStoragePlan plan, Dictionary<string, string>? stored = null, string? migratedDocument = null)
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
            var legacy = new List<string> { XgpPatternKey };
            if (!Holds(XgpPatternKey))
                legacy.Add(XgpPrefixKey);
            legacy.Add(XgpLastNumberKey);
            legacy.Add(XgpSuffixLengthKey);
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
        plan.ExpectRead(BrowserStorageArea.Local, XgpAnonymizeKey, Answer(XgpAnonymizeKey));
    }

    /// <summary>Home's first-render restore in a browser that refuses every read.</summary>
    public static void ExpectHomeRestoreRefused(this BrowserStoragePlan plan)
    {
        foreach (var key in new[]
                 {
                     OutputFormatKey, FolderPathKey, NumberingKey, XgpPatternKey, XgpPrefixKey,
                     XgpLastNumberKey, XgpSuffixLengthKey, XgpAnonymizeKey,
                 })
            plan.ExpectRead(BrowserStorageArea.Local, key, BrowserStorageReadAnswer.Refused);
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

    /// <summary>The Web panel's restore — its file name and folder hint.</summary>
    public static void ExpectWebPanelRestore(this BrowserStoragePlan plan, BrowserStorageReadAnswer? answer = null)
    {
        plan.ExpectRead(BrowserStorageArea.Local, CsvFileNameKey, answer ?? BrowserStorageReadAnswer.Absent);
        plan.ExpectRead(BrowserStorageArea.Local, LastFolderHintKey, answer ?? BrowserStorageReadAnswer.Absent);
    }

    /// <summary>
    /// One visit to Home in <paramref name="mode"/>: Home's restore (from
    /// <paramref name="stored"/>), the mode panel's, and the filter panel's
    /// mount. This boot's filter restoration is the test's to declare, once
    /// per context.
    /// </summary>
    public static void ExpectHomeVisit(
        this BrowserStoragePlan plan, string mode, Dictionary<string, string>? stored = null, string? migratedDocument = null)
    {
        plan.ExpectHomeRestore(stored, migratedDocument);
        plan.ExpectModePanelRestore(mode);
        plan.ExpectFilterPanelMount();
    }

    /// <summary>One visit to Home in <paramref name="mode"/> in a browser that refuses every read.</summary>
    public static void ExpectHomeVisitRefused(this BrowserStoragePlan plan, string mode)
    {
        plan.ExpectHomeRestoreRefused();
        plan.ExpectModePanelRestore(mode, BrowserStorageReadAnswer.Refused);
        plan.ExpectFilterPanelMountRefused();
    }

    private static void ExpectModePanelRestore(
        this BrowserStoragePlan plan, string mode, BrowserStorageReadAnswer? answer = null)
    {
        if (mode == "Local")
            plan.ExpectLocalPanelRestore(answer);
        else
            plan.ExpectWebPanelRestore(answer);
    }
}
