using AngleSharp.Dom;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Razor.Components;
using XgFilter_Razor.TestSupport;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The Local panel rendered over the app's coordinator, as the panel suites
/// drive it — beside it the filter surface, whose mount settles the filter
/// owner's restoration as it does on Home — and the gestures those suites
/// make. The panel holds nothing of its own, so a test reaches a state through
/// the coordinator's commands and the wire: a result by running, a busy panel
/// by a run whose status reads are held, a filter in effect by applying it on
/// the surface. Nothing is seeded.
/// </summary>
internal sealed class LocalPanelHost
{
    public const string Folder = @"D:\xg\matches";
    public const string CsvOutputPath = @"D:\xg\out.csv";
    public const string XgpOutputFolder = @"D:\xg\positions";

    private readonly IRenderedComponent<FilterSurface>? _surface;

    private LocalPanelHost(IRenderedComponent<LocalModePanel> panel, IRenderedComponent<FilterSurface>? surface, ExtractApp app)
    {
        Panel = panel;
        _surface = surface;
        App = app;
    }

    /// <summary>The panel.</summary>
    public IRenderedComponent<LocalModePanel> Panel { get; }

    /// <summary>The coordinator the panel renders.</summary>
    public ExtractApp App { get; }

    private IRenderedComponent<FilterSurface> Surface =>
        _surface ?? throw new InvalidOperationException("This host was rendered without the filter surface.");

    /// <summary>
    /// Over <paramref name="ctx"/>'s registrations: the filter surface, its
    /// restoration settled; the app's restore; then the panel. Nothing is
    /// committed. Without <paramref name="withFilterSurface"/>, for a test
    /// that plans storage without the surface's own calls, the filter owner is
    /// never settled, so no filter is in effect.
    /// </summary>
    public static async Task<LocalPanelHost> RenderAsync(BunitContext ctx, bool withFilterSurface = true)
    {
        var app = ctx.Services.GetRequiredService<ExtractApp>();
        IRenderedComponent<FilterSurface>? surface = null;
        if (withFilterSurface)
        {
            surface = ctx.Render<FilterSurface>();
            surface.WaitForAssertion(() => surface.Find(FilterRestorationMarker.SettledSelector));
        }

        await ctx.Renderer.Dispatcher.InvokeAsync(app.EnsureRestoredAsync);
        return new LocalPanelHost(ctx.Render<LocalModePanel>(), surface, app);
    }

    /// <summary>
    /// A host one click from a run in <paramref name="format"/>: the format
    /// set, <see cref="Folder"/> committed, and an output path typed —
    /// <see cref="XgpOutputFolder"/> for XGP, <see cref="CsvOutputPath"/>
    /// otherwise, whose extension the run's capture makes the format's. The
    /// empty filter is in effect without Apply, so Run is open.
    /// </summary>
    public static async Task<LocalPanelHost> RenderReadyAsync(BunitContext ctx, OutputFormat format = OutputFormat.Csv)
    {
        var host = await RenderAsync(ctx);
        await host.SetOutputFormatAsync(format);
        await host.CommitFolderAsync(Folder);
        await host.SetOutputPathAsync(format == OutputFormat.Xgp ? XgpOutputFolder : CsvOutputPath);
        return host;
    }

    // ── Gestures ────────────────────────────────────────────────────────────

    /// <summary>Type <paramref name="path"/> into the folder input and commit it at the change boundary.</summary>
    public async Task CommitFolderAsync(string path)
    {
        await Panel.Find("#folderPath").InputAsync(new ChangeEventArgs { Value = path });
        await Panel.Find("#folderPath").ChangeAsync(new ChangeEventArgs { Value = path });
    }

    public Task SetOutputPathAsync(string path) =>
        Panel.Find("#outputPath").InputAsync(new ChangeEventArgs { Value = path });

    /// <summary>The format, as Home's radios send it.</summary>
    public Task SetOutputFormatAsync(OutputFormat format) =>
        Panel.InvokeAsync(() => App.SetOutputFormatAsync(format));

    /// <summary>The name pattern, as Home's pattern input sends it.</summary>
    public Task EditPatternAsync(string pattern) =>
        Panel.InvokeAsync(() => App.EditPatternAsync(pattern));

    /// <summary>The anonymize choice, as Home's checkbox sends it.</summary>
    public Task SetAnonymizeAsync(bool anonymize) =>
        Panel.InvokeAsync(() => App.SetAnonymizeAsync(anonymize));

    /// <summary>
    /// An edit of the filter's error minimum, not applied: the selection on
    /// screen moves off what is in effect, so nothing is.
    /// </summary>
    public Task EditErrorMinAsync(string value) =>
        Surface.Find("input[placeholder='Min']").InputAsync(new ChangeEventArgs { Value = value });

    /// <summary>The filter with error minimum <paramref name="value"/>, edited and applied: it is in effect.</summary>
    public async Task ApplyErrorMinAsync(string value)
    {
        await EditErrorMinAsync(value);
        await ApplyAsync();
    }

    /// <summary>The surface's Apply.</summary>
    public Task ApplyAsync() =>
        Surface.FindAll("button").First(b => b.TextContent.Trim() == "Apply Filter").ClickAsync(new MouseEventArgs());

    public IElement RunButton => Panel.FindAll("button").Single(b => b.TextContent.Trim() == "Run");

    /// <summary>Clicks Run; the task is the whole operation, to the job's acknowledgement.</summary>
    public Task ClickRunAsync() => RunButton.ClickAsync(new MouseEventArgs());

    /// <summary>Clicks Run and waits for the run to complete.</summary>
    public Task RunToCompletionAsync() => ClickRunAsync().WaitAsync(CapturingProcessHandler.RunTimeout);
}
