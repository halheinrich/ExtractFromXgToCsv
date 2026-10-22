using System.Net;
using System.Text;
using BgUiPrimitives_Razor;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Razor;
using XgFilter_Razor.Components;
using XgFilter_Razor.TestSupport;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The coordinator's table (halheinrich/backgammon#424), as <c>QuizAppTests</c>
/// is BgQuiz's: which command does what in which phase, driven on
/// <see cref="ExtractApp"/> itself over the app's registrations, a stub server
/// reached over a <see cref="ProtocolWire"/> a test can hold, and an in-memory
/// browser storage. No page is rendered. The one component is the filter
/// surface, rendered once where a run needs it, so the filter owner's
/// restoration settles and the empty selection can be in effect, as on Home.
/// Commands go through the renderer's context, as a page's gestures do.
/// </summary>
public class ExtractAppTests : BunitContext
{
    private const string Folder = @"D:\xg\matches";
    private const string FolderB = @"D:\xg\other";
    private const string CsvOutputPath = @"D:\xg\out.csv";
    private const string XgpOutputFolder = @"D:\xg\positions";

    private static readonly TimeSpan Bound = NoticeTesting.OperationBound;

    private readonly MemoryBrowserStorage _storage = new();
    private CapturingProcessHandler _server = default!;
    private ProtocolWire _wire = default!;
    private IRenderedComponent<FilterSurface>? _surface;

    public ExtractAppTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
        Services.AddSingleton(new BrowserStorage(_storage));
        Services.AddSingleton<IJobIdentities>(new SequentialJobIdentities());
        Services.AddSingleton(LocalPage.FastTiming);
    }

    private ExtractApp App => Services.GetRequiredService<ExtractApp>();

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    /// <summary>
    /// The server: the opening book's status, and the Local endpoints answered
    /// by <paramref name="processing"/> — a terminal of three rows by default —
    /// all over one wire.
    /// </summary>
    private void Serve(CapturingProcessHandler? processing = null)
    {
        _server = processing ?? new CapturingProcessHandler();
        _wire = new ProtocolWire(new BookStatusServer(_server));
        Services.AddSingleton(new HttpClient(new StubServerHandler { Processing = _wire }) { BaseAddress = new Uri("http://localhost/") });
    }

    /// <summary>The server's book status — loaded, 42 entries — and everything else <paramref name="processing"/>'s.</summary>
    private sealed class BookStatusServer(HttpMessageHandler processing) : HttpMessageHandler
    {
        private const string Path = "/api/openingbook/status";

        public static bool Request(HttpRequestMessage request) => request.RequestUri!.AbsolutePath == Path;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Request(request))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"loaded\":true,\"entryCount\":42}", Encoding.UTF8, "application/json"),
                };
            }

            using var invoker = new HttpMessageInvoker(processing, disposeHandler: false);
            return await invoker.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>Runs <paramref name="command"/> on the renderer's context, as a page's gesture does.</summary>
    private Task On(Func<Task> command) => Renderer.Dispatcher.InvokeAsync(command);

    private Task On(Action command) => Renderer.Dispatcher.InvokeAsync(command);

    /// <summary>The settings a run needs, stored by an earlier visit: the restore reads them, and nothing is written.</summary>
    private void Store(OutputFormat format = OutputFormat.Csv, string outputPath = CsvOutputPath)
    {
        _storage[HomeStoragePlan.FolderPathKey] = Folder;
        _storage[HomeStoragePlan.OutputPathKey] = outputPath;
        _storage[HomeStoragePlan.OutputFormatKey] = format.ToString();
    }

    /// <summary>Mount the filter surface once, so the owner's restoration settles and the empty selection is in effect for whatever source is reported.</summary>
    private void SettleTheFilterOwner()
    {
        _surface = Render<FilterSurface>();
        _surface.WaitForAssertion(() => _surface.Find(FilterRestorationMarker.SettledSelector));
    }

    /// <summary>The app one Run from a run in <paramref name="format"/>: its settings restored, the empty filter in effect.</summary>
    private async Task ReadyAsync(OutputFormat format = OutputFormat.Csv, string outputPath = CsvOutputPath)
    {
        Store(format, outputPath);
        SettleTheFilterOwner();
        await On(App.EnsureRestoredAsync);
        Assert.True(App.CanRun);
    }

    /// <summary>The filter with error minimum <paramref name="value"/>, edited and applied on the surface: the filter in effect moves.</summary>
    private async Task ApplyErrorMinAsync(string value)
    {
        var surface = _surface ?? throw new InvalidOperationException("The filter surface is not rendered.");
        await surface.Find("input[placeholder='Min']").InputAsync(new ChangeEventArgs { Value = value });
        await surface.FindAll("button").First(b => b.TextContent.Trim() == "Apply Filter").ClickAsync(new MouseEventArgs());
    }

    private int Starts => _wire.CountRequests(r => r == "POST /api/process/start");

    private int Cancels => _wire.CountRequests(r => r.EndsWith("/cancel", StringComparison.Ordinal));

    // ── Refusals by phase ───────────────────────────────────────────────────

    [Fact]
    public async Task WhileRestoring_EverySettingsCommandIsRefused_ChangingAndWritingNothing()
    {
        Serve();
        Assert.Equal(ExtractPhase.Restoring, App.Phase);

        await On(() => App.SetOutputFormatAsync(OutputFormat.Pdf));
        await On(() => App.EditFolderPathAsync(Folder));
        await On(() => App.CommitFolderPath(Folder));
        await On(() => App.SetOutputPathAsync(CsvOutputPath));
        await On(() => App.EditPatternAsync("quiz{n}"));
        await On(() => App.InsertTokenAsync("dice"));
        await On(() => App.EditNextNumberAsync(9));
        await On(() => App.SetSuffixLengthAsync(5));
        await On(() => App.SetAnonymizeAsync(true));

        Assert.Empty(_storage.Writes);
        Assert.Equal(OutputFormat.Csv, App.OutputFormat);
        Assert.Equal(string.Empty, App.FolderPath);
        Assert.Null(App.Source);
        Assert.Null(Setup.Current.Source);
        Assert.Equal(string.Empty, App.OutputPath);
        Assert.Equal(new XgpExportOptions(), App.XgpOptions);
        Assert.False(App.Anonymize);
        Assert.Equal(ExtractPhase.Restoring, App.Phase);
    }

    [Fact]
    public async Task Run_WhileRestoring_IsRefused_StartingNothingAndWritingNothing()
    {
        Serve();
        Store();
        SettleTheFilterOwner();

        await On(App.RunAsync);

        Assert.Equal(0, Starts);
        Assert.Empty(_storage.Writes);
        Assert.Equal(ExtractPhase.Restoring, App.Phase);
    }

    [Fact]
    public async Task Run_WhileRunning_IsRefused_StartingNothingAndWritingNothing()
    {
        Serve();
        await ReadyAsync();
        _server.HoldStatus();
        var run = On(App.RunAsync);
        await _server.StatusReached.WaitAsync(Bound);
        var writes = _storage.Writes.Count;

        await On(App.RunAsync).WaitAsync(Bound);

        Assert.Equal(1, Starts);
        Assert.Equal(writes, _storage.Writes.Count);
        _server.ReleaseStatus();
        await run.WaitAsync(Bound);
    }

    [Fact]
    public async Task Stop_WithNoRunInHand_WhileIdle_SendsNoCancel_AndShowsNoHint()
    {
        Serve();
        await ReadyAsync();

        await On(App.StopAsync);

        Assert.Equal(0, Cancels);
        Assert.False(App.IsStopPending);
        Assert.Equal(ExtractPhase.Idle, App.Phase);
        Assert.Empty(_storage.Writes);
    }

    // ── The phase covers the whole operation (behaviour 1) ──────────────────

    /// <summary>
    /// An XGP run held past publication: its result published — two files
    /// committed, one file skipped — and its commit followed, its
    /// acknowledgement held on the wire. Release the rule to end the run.
    /// </summary>
    private async Task<(Task Run, ProtocolWire.Rule Acknowledgement)> HeldPastPublicationAsync()
    {
        Serve(new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 1,
            Total = 1,
            Complete = true,
            TotalRows = 2,
            Skipped = [new SkippedItem("broken.xg", null, "Not a valid XG file.")],
            XgpCommit = new XgpExportCommit(new XgpExportOptions(), 2),
        }));
        await ReadyAsync(OutputFormat.Xgp, XgpOutputFolder);
        var acknowledgement = _wire.Hold(ProtocolWire.Acknowledge);
        var run = On(App.RunAsync);
        await acknowledgement.Reached.WaitAsync(Bound);
        return (run, acknowledgement);
    }

    /// <summary>
    /// With the acknowledgement held, the result is published and the commit
    /// followed — the next number is past the files written — and the run is
    /// no longer in hand, yet the operation is: Run is refused, so no run can
    /// capture a number the commit was about to move. Released, Run is
    /// accepted.
    /// </summary>
    [Fact]
    public async Task PastPublication_TheNextNumberHasFollowed_AndRunIsRefused_UntilTheAcknowledgementIsAnswered()
    {
        var (run, acknowledgement) = await HeldPastPublicationAsync();

        Assert.Equal(3, App.XgpOptions.StartNumber);
        Assert.NotNull(App.Progress);
        Assert.False(App.IsRunInHand);
        Assert.Equal(ExtractPhase.Running, App.Phase);
        Assert.False(App.CanRun);
        await On(App.RunAsync).WaitAsync(Bound);
        Assert.Equal(1, Starts);

        acknowledgement.Release();
        await run.WaitAsync(Bound);

        Assert.Equal(ExtractPhase.Idle, App.Phase);
        Assert.True(App.CanRun);
        await On(App.RunAsync).WaitAsync(Bound);
        Assert.Equal(2, Starts);
    }

    /// <summary>
    /// Past publication the result is judged like any published one: a filter
    /// change drops it at once, while Run stays refused, and the skip record —
    /// the run's history on its source — is kept.
    /// </summary>
    [Fact]
    public async Task PastPublication_AFilterChange_DropsTheResultAtOnce_KeepingTheSkipRecord_WhileRunStaysRefused()
    {
        var (run, acknowledgement) = await HeldPastPublicationAsync();
        Assert.NotNull(App.Progress);
        Assert.NotNull(App.LastRunSkips);

        await ApplyErrorMinAsync("0.05");

        Assert.Null(App.Progress);
        Assert.NotNull(App.LastRunSkips);
        Assert.Equal(ExtractPhase.Running, App.Phase);
        Assert.False(App.CanRun);

        acknowledgement.Release();
        await run.WaitAsync(Bound);
    }

    /// <summary>Past publication, a source change drops the result and clears the skip record.</summary>
    [Fact]
    public async Task PastPublication_ASourceChange_DropsTheResult_AndClearsTheSkipRecord()
    {
        var (run, acknowledgement) = await HeldPastPublicationAsync();

        await On(() => App.EditFolderPathAsync(FolderB));
        await On(() => App.CommitFolderPath(FolderB));

        Assert.Null(App.Progress);
        Assert.Null(App.LastRunSkips);
        Assert.Equal(ExtractPhase.Running, App.Phase);
        Assert.False(App.CanRun);

        acknowledgement.Release();
        await run.WaitAsync(Bound);
    }

    /// <summary>
    /// Past publication no run is in hand, so Stop does nothing: no cancel is
    /// sent, and no hint says one is pending.
    /// </summary>
    [Fact]
    public async Task PastPublication_AStop_SendsNoCancel_AndShowsNoHint()
    {
        var (run, acknowledgement) = await HeldPastPublicationAsync();

        await On(App.StopAsync).WaitAsync(Bound);

        Assert.Equal(0, Cancels);
        Assert.False(App.IsStopPending);

        acknowledgement.Release();
        await run.WaitAsync(Bound);
    }

    // ── The restore ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TheRestore_RunsOncePerApp_TwoCallersShareOneSetOfReads()
    {
        Serve();
        Store();
        Task first = null!, second = null!;

        await On(() =>
        {
            first = App.EnsureRestoredAsync();
            second = App.EnsureRestoredAsync();
        });
        await first.WaitAsync(Bound);
        await On(App.EnsureRestoredAsync);

        Assert.Same(first, second);
        string[] everyKey =
        [
            HomeStoragePlan.OutputFormatKey, HomeStoragePlan.FolderPathKey, HomeStoragePlan.OutputPathKey,
            HomeStoragePlan.XgpPatternKey, HomeStoragePlan.XgpNextNumberKey, HomeStoragePlan.XgpSuffixLengthKey,
            HomeStoragePlan.XgpAnonymizeKey,
        ];
        Assert.Equal(everyKey.Order(StringComparer.Ordinal), _storage.Reads.Order(StringComparer.Ordinal));
        Assert.Equal(ExtractPhase.Idle, App.Phase);
        Assert.Equal(FilterSourceToken.FromPath(Folder), Setup.Current.Source);
    }

    /// <summary>
    /// Over empty storage every setting lands on its default, and the XGP
    /// options' defaults are the record's own: nothing here restates them.
    /// </summary>
    [Fact]
    public async Task OverEmptyStorage_EverySettingIsItsDefault_TheXgpOptionsTheRecordsOwn()
    {
        Serve();

        await On(App.EnsureRestoredAsync);

        Assert.Equal(new XgpExportOptions(), App.XgpOptions);
        Assert.Equal(OutputFormat.Csv, App.OutputFormat);
        Assert.Equal(string.Empty, App.FolderPath);
        Assert.Equal(string.Empty, App.OutputPath);
        Assert.False(App.Anonymize);
        Assert.Null(App.Source);
        Assert.Equal(ExtractPhase.Idle, App.Phase);
        Assert.Empty(_storage.Writes);
    }

    /// <summary>
    /// The book status is not part of the restore: with its answer held, the
    /// restore ends, the app is idle — Home's controls render on that — and a
    /// Run is accepted. The answer, released, lands as the status.
    /// </summary>
    [Fact]
    public async Task TheBookStatusHeld_TheRestoreEnds_AndRunIsAccepted_ThenTheAnswerLands()
    {
        Serve();
        var book = _wire.Hold(BookStatusServer.Request);
        Store();
        SettleTheFilterOwner();

        await On(App.EnsureRestoredAsync).WaitAsync(Bound);
        await book.Reached.WaitAsync(Bound);

        Assert.Equal(ExtractPhase.Idle, App.Phase);
        Assert.Null(App.BookStatus);
        Assert.True(App.CanRun);
        await On(App.RunAsync).WaitAsync(Bound);
        Assert.Equal(1, Starts);

        var landed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        App.Changed += () =>
        {
            if (App.BookStatus is not null) landed.TrySetResult();
        };
        book.Release();
        await landed.Task.WaitAsync(Bound);

        Assert.Equal(new OpeningBookStatus(Loaded: true, EntryCount: 42), App.BookStatus);
    }

    // ── The output path's extension (behaviour 3) ───────────────────────────

    [Fact]
    public async Task TheExtension_FollowsTheFormat_WhenItChanges()
    {
        Serve();
        await ReadyAsync(OutputFormat.Csv, CsvOutputPath);

        await On(() => App.SetOutputFormatAsync(OutputFormat.Pdf));

        Assert.Equal(@"D:\xg\out.pdf", App.OutputPath);
        Assert.Equal(@"D:\xg\out.pdf", _storage[HomeStoragePlan.OutputPathKey]);
    }

    /// <summary>The control: XGP's output is a folder, so a change to it forces no extension.</summary>
    [Fact]
    public async Task TheExtension_IsLeftAlone_WhenTheFormatChangesToXgp()
    {
        Serve();
        await ReadyAsync(OutputFormat.Csv, CsvOutputPath);

        await On(() => App.SetOutputFormatAsync(OutputFormat.Xgp));

        Assert.Equal(CsvOutputPath, App.OutputPath);
        Assert.DoesNotContain(_storage.Writes, w => w.Key == HomeStoragePlan.OutputPathKey);
    }

    [Fact]
    public async Task TheExtension_FollowsTheRestoredFormat_AfterTheRestore()
    {
        Serve();
        Store(OutputFormat.Pptx, CsvOutputPath);

        await On(App.EnsureRestoredAsync);

        Assert.Equal(@"D:\xg\out.pptx", App.OutputPath);
        Assert.Equal(@"D:\xg\out.pptx", _storage[HomeStoragePlan.OutputPathKey]);
    }

    /// <summary>The control: a restored path that already has the format's extension is left as stored, and nothing is written.</summary>
    [Fact]
    public async Task ARestoredPathWithTheFormatsExtension_IsLeftAsStored()
    {
        Serve();
        Store(OutputFormat.Csv, CsvOutputPath);

        await On(App.EnsureRestoredAsync);

        Assert.Equal(CsvOutputPath, App.OutputPath);
        Assert.Empty(_storage.Writes);
    }

    /// <summary>
    /// A path typed without its extension is sent with the format's: the run
    /// captures its inputs, the extension following the format first, so a
    /// run never writes under the wrong one.
    /// </summary>
    [Fact]
    public async Task TheExtension_FollowsTheFormat_WhenARunCapturesItsInputs()
    {
        Serve();
        await ReadyAsync(OutputFormat.Csv, CsvOutputPath);
        await On(() => App.SetOutputPathAsync(@"D:\xg\out"));

        await On(App.RunAsync).WaitAsync(Bound);

        Assert.Equal(CsvOutputPath, _server.Captured!.OutputPath);
        Assert.Equal(CsvOutputPath, App.OutputPath);
        Assert.Equal(CsvOutputPath, _storage[HomeStoragePlan.OutputPathKey]);
    }

    /// <summary>
    /// The control: between the named moments the path keeps the spelling
    /// typed — mid-typing, and across a filter move, which before
    /// (halheinrich/backgammon#424) rewrote it at whatever re-render came.
    /// </summary>
    [Fact]
    public async Task ATypedPath_KeepsItsSpelling_MidTypingAndAcrossAFilterMove()
    {
        Serve();
        await ReadyAsync(OutputFormat.Csv, CsvOutputPath);
        await On(() => App.SetOutputPathAsync(@"D:\xg\out"));

        await ApplyErrorMinAsync("0.05");

        Assert.Equal(@"D:\xg\out", App.OutputPath);
        Assert.Equal(@"D:\xg\out", _storage[HomeStoragePlan.OutputPathKey]);
    }
}
