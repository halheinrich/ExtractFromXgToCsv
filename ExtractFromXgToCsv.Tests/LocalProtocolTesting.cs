using System.Net;
using System.Net.Http.Json;
using BgUiPrimitives_Razor;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using XgFilter_Lib;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The real Local server, in memory, for the recovery protocol's tests (the
/// umbrella's <c>SPEC-notices.md</c> §7.5): its real controller and job store,
/// its real processor over a real input folder, reached over a
/// <see cref="ProtocolWire"/> a test can fault. Opening an output file — an
/// XGP file's create-new, or the one file another format replaces — can be
/// held (<see cref="Gate"/>), so a job can be kept running.
/// </summary>
internal sealed class LocalServer : IDisposable
{
    private const string Fixture = "MTCH4064.xg";

    private readonly string _root = Directory.CreateTempSubdirectory("xg-protocol-").FullName;
    private readonly Action<IWebHostBuilder>? _configure;
    private readonly IOutputDestination _destination;
    private WebApplicationFactory<AppModeService> _factory;

    /// <summary>A server over a fresh input folder holding one fixture match.</summary>
    /// <param name="configure">Anything more the test's host needs — a logger, say.</param>
    /// <param name="destination">Where the runs write, behind the gate; the file system by default.</param>
    public LocalServer(Action<IWebHostBuilder>? configure = null, IOutputDestination? destination = null)
    {
        InputFolder = Directory.CreateDirectory(Path.Combine(_root, "matches")).FullName;
        OutputFolder = Path.Combine(_root, "positions");
        File.Copy(Path.Combine(FixtureHelper.FixtureDir, Fixture), Path.Combine(InputFolder, Fixture));

        _configure = configure;
        _destination = destination ?? FileSystemDestination.Instance;
        _factory = Start();
        Wire = new ProtocolWire(_factory.Server.CreateHandler());
    }

    /// <summary>A second fixture match in the input folder, so a cancel is seen between the files: a file is the cancellation's granularity.</summary>
    public void AddASecondMatch() =>
        File.Copy(Path.Combine(FixtureHelper.FixtureDir, Fixture), Path.Combine(InputFolder, "second.xg"));

    /// <summary>A CSV output path beside the XGP output folder.</summary>
    public string CsvOutputPath => Path.Combine(_root, "out.csv");

    /// <summary>
    /// Restarts the server, as Exit and a fresh start, or a crash, do: a new
    /// process — a new instance, with an empty job store and ledger — over the
    /// same folders, which the same wire now reaches.
    /// </summary>
    public void Restart()
    {
        var ended = _factory;
        _factory = Start();
        Wire.Retarget(_factory.Server.CreateHandler());
        ended.Dispose();
    }

    private WebApplicationFactory<AppModeService> Start() =>
        new WebApplicationFactory<AppModeService>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AppMode", "Local");
            // Empty disables the opening book: these runs need none.
            builder.UseSetting("OpeningBookPath", "");
            builder.ConfigureTestServices(services => services.AddScoped(_ => new LocalFolderProcessor(
                NullLogger<LocalFolderProcessor>.Instance,
                NullLogger<FilteredDecisionIterator>.Instance,
                AppRanking.Ranking,
                bookProvider: null,
                XgpPayload.Prepare,
                new GatedDestination(Gate, _destination))));
            _configure?.Invoke(builder);
        });

    /// <summary>The input folder: one fixture match.</summary>
    public string InputFolder { get; }

    /// <summary>The XGP output folder (created by the first XGP run).</summary>
    public string OutputFolder { get; }

    /// <summary>Holds XGP file creation while closed.</summary>
    public FileGate Gate { get; } = new();

    /// <summary>The wire to the server.</summary>
    public ProtocolWire Wire { get; }

    /// <summary>A client over the wire, as a page has.</summary>
    public HttpClient Client() => new(Wire, disposeHandler: false) { BaseAddress = new Uri("http://localhost/") };

    /// <summary>
    /// One XGP job run straight against the server under
    /// <paramref name="instance"/>, as a page elsewhere would, touching no
    /// browser's storage — into its own folder, so its files never meet a
    /// test's pages' — and awaited to its end.
    /// </summary>
    public async Task ExportAsync(string jobId, string instance, LedgerMark? mark, XgpExportOptions? options = null)
    {
        using var client = Client();
        var run = new ProcessRequest
        {
            FolderPath = InputFolder,
            OutputPath = Path.Combine(OutputFolder, jobId),
            OutputFormat = OutputFormat.Xgp,
            XgpOptions = options ?? new XgpExportOptions(),
        };
        (await client.PostAsJsonAsync("/api/process/start", new StartRequest(jobId, instance, mark, run))).EnsureSuccessStatusCode();
        var deadline = DateTime.UtcNow + NoticeTesting.OperationBound;
        while ((await client.GetFromJsonAsync<ProcessingProgress>($"/api/process/{jobId}/status")) is not { Complete: true })
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"{jobId} never ended.");
            await Task.Delay(20);
        }
    }

    /// <summary>The XGP files in the output folder, by name, in order.</summary>
    public IReadOnlyList<string> XgpFiles() =>
        Directory.Exists(OutputFolder)
            ? [.. Directory.GetFiles(OutputFolder, "*.xgp").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)]
            : [];

    public void Dispose()
    {
        Gate.Open();
        _factory.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary><paramref name="inner"/>, with every output file's create-new passing <see cref="Gate"/> first.</summary>
    private sealed class GatedDestination(FileGate gate, IOutputDestination inner) : IOutputDestination
    {
        public Stream CreateNew(string path)
        {
            gate.Pass();
            return inner.CreateNew(path);
        }

        public void Delete(string path) => inner.Delete(path);
    }
}

/// <summary>A gate a server-side run passes before creating each XGP file: open until a test closes it.</summary>
internal sealed class FileGate
{
    private readonly ManualResetEventSlim _open = new(initialState: true);
    private TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when a run has reached the closed gate.</summary>
    public Task Reached => _reached.Task;

    /// <summary>Holds every run that reaches the gate from now on.</summary>
    public void Close()
    {
        _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _open.Reset();
    }

    /// <summary>Lets every held run, and every later one, through.</summary>
    public void Open() => _open.Set();

    internal void Pass()
    {
        if (!_open.IsSet)
            _reached.TrySetResult();
        if (!_open.Wait(NoticeTesting.OperationBound))
            throw new TimeoutException("The file gate was never opened.");
    }
}

/// <summary>
/// The wire between a page and the server, which a test can fault the way a
/// network does (the umbrella's <c>SPEC-notices.md</c> §7.5): a response
/// lost after the server acted (<see cref="Drop"/>), a request that never
/// arrives (<see cref="Fail"/>), one held before it arrives
/// (<see cref="Hold"/>), a response held after the server answered
/// (<see cref="HoldResponse"/>), or an answer the server never gave
/// (<see cref="Respond"/>). Rules apply in the order added. A restarted
/// server is reached over the same wire (<see cref="Retarget"/>).
/// </summary>
internal sealed class ProtocolWire(HttpMessageHandler server) : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<Rule> _rules = [];
    private HttpMessageInvoker _server = new(server, disposeHandler: false);

    /// <summary>Points the wire at <paramref name="server"/>: the restart of the one it reached.</summary>
    public void Retarget(HttpMessageHandler server)
    {
        lock (_gate)
            _server = new HttpMessageInvoker(server, disposeHandler: false);
    }

    /// <summary>Every request made over the wire, as "METHOD path", in order.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>How many requests made so far <paramref name="match"/> takes, counted under the wire's lock.</summary>
    public int CountRequests(Func<string, bool> match)
    {
        lock (_gate)
            return Requests.Count(match);
    }

    /// <summary>Starts.</summary>
    public static bool Start(HttpRequestMessage r) => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/process/start";

    /// <summary>Status reads.</summary>
    public static bool Status(HttpRequestMessage r) => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal);

    /// <summary>Ledger reads.</summary>
    public static bool Ledger(HttpRequestMessage r) => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == "/api/process/ledger";

    /// <summary>Settles.</summary>
    public static bool Settle(HttpRequestMessage r) => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/settle", StringComparison.Ordinal);

    /// <summary>Acknowledgements.</summary>
    public static bool Acknowledge(HttpRequestMessage r) => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/acknowledge", StringComparison.Ordinal);

    /// <summary>Cancels.</summary>
    public static bool Cancel(HttpRequestMessage r) => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal);

    /// <summary>The next <paramref name="times"/> matching requests reach the server, and their responses are lost.</summary>
    public Rule Drop(Func<HttpRequestMessage, bool> match, int times = 1) => Add(new Rule(match, RuleKind.Drop, times));

    /// <summary>Matching requests never reach the server — the next <paramref name="times"/>, or all until stopped.</summary>
    public Rule Fail(Func<HttpRequestMessage, bool> match, int times = int.MaxValue) => Add(new Rule(match, RuleKind.Fail, times));

    /// <summary>
    /// The next matching request is held before it reaches the server, until
    /// released. It is on the wire: if its sender stops waiting, it still
    /// arrives when released.
    /// </summary>
    public Rule Hold(Func<HttpRequestMessage, bool> match) => Add(new Rule(match, RuleKind.Hold, 1));

    /// <summary>The next matching request reaches the server, and its response is held until released.</summary>
    public Rule HoldResponse(Func<HttpRequestMessage, bool> match) => Add(new Rule(match, RuleKind.HoldResponse, 1));

    /// <summary>The next <paramref name="times"/> matching requests never reach the server, and are answered <paramref name="status"/>.</summary>
    public Rule Respond(Func<HttpRequestMessage, bool> match, System.Net.HttpStatusCode status, int times = 1) =>
        Add(new Rule(match, RuleKind.Respond, times) { Status = status });

    private Rule Add(Rule rule)
    {
        lock (_gate)
            _rules.Add(rule);
        return rule;
    }

    private Task<HttpResponseMessage> ForwardAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpMessageInvoker server;
        lock (_gate)
            server = _server;
        return server.SendAsync(request, cancellationToken);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Rule? rule;
        lock (_gate)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            rule = _rules.FirstOrDefault(r => r.Takes(request));
        }

        switch (rule?.Kind)
        {
            case RuleKind.Fail:
                throw new HttpRequestException("The request never reached the server.");
            case RuleKind.Respond:
                return new HttpResponseMessage(rule.Status);
            case RuleKind.Drop:
                using (await ForwardAsync(request, cancellationToken))
                    rule.Answered();
                throw new HttpRequestException("The response was lost.");
            case RuleKind.Hold:
            {
                // Delivered whatever the sender does meanwhile: a copy, so its
                // content outlives the sender's request.
                var onTheWire = await CopyAsync(request);
                var delivery = Task.Run(async () =>
                {
                    await rule.HeldAsync();
                    var response = await ForwardAsync(onTheWire, CancellationToken.None);
                    rule.Answered();
                    return response;
                });
                return await delivery.WaitAsync(cancellationToken);
            }
            case RuleKind.HoldResponse:
            {
                var response = await ForwardAsync(request, cancellationToken);
                rule.Answered();
                await rule.HeldAsync();
                return response;
            }
            default:
                return await ForwardAsync(request, cancellationToken);
        }
    }

    private static async Task<HttpRequestMessage> CopyAsync(HttpRequestMessage request)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Content is { } content)
        {
            copy.Content = new ByteArrayContent(await content.ReadAsByteArrayAsync());
            foreach (var header in content.Headers)
                copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return copy;
    }

    public enum RuleKind { Drop, Fail, Hold, HoldResponse, Respond }

    /// <summary>One rule: what it matches, what it does, how many more times, and its signals.</summary>
    public sealed class Rule(Func<HttpRequestMessage, bool> match, RuleKind kind, int times)
    {
        /// <summary>For <see cref="RuleKind.Respond"/>, the status answered.</summary>
        public System.Net.HttpStatusCode Status { get; init; }

        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _answered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _left = times;

        public RuleKind Kind { get; } = kind;

        /// <summary>Completes when a request this rule took has arrived.</summary>
        public Task Reached => _reached.Task;

        /// <summary>Completes when the server has answered a request this rule took, whatever the page then saw.</summary>
        public Task Forwarded => _answered.Task;

        /// <summary>Lets a held request through.</summary>
        public void Release() => _release.TrySetResult();

        /// <summary>Takes no more requests.</summary>
        public void Stop() => _left = 0;

        internal bool Takes(HttpRequestMessage request)
        {
            if (_left <= 0 || !match(request))
                return false;
            _left--;
            _reached.TrySetResult();
            return true;
        }

        internal Task HeldAsync() => _release.Task.WaitAsync(NoticeTesting.OperationBound);

        internal void Answered() => _answered.TrySetResult();
    }
}

/// <summary>
/// A browser's local and session storage, in memory, through the real
/// <see cref="BrowserStorage"/> accessor: it outlives a page, as the browser's
/// does, so a reload is a new page over the same store. A test can make the
/// browser refuse writes, or every call.
/// </summary>
internal sealed class MemoryBrowserStorage : IJSRuntime
{
    private readonly Dictionary<string, string> _local = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _session = new(StringComparer.Ordinal);

    /// <summary>Whether the browser refuses every write.</summary>
    public bool RefuseWrites { get; set; }

    /// <summary>Every local write the browser stored, as (key, value), in order.</summary>
    public List<(string Key, string Value)> Writes { get; } = [];

    /// <summary>The keys of every write the browser refused, in order.</summary>
    public List<string> RefusedWrites { get; } = [];

    /// <summary>Whether the browser refuses every read.</summary>
    public bool RefuseReads { get; set; }

    /// <summary>The local item under <paramref name="key"/>; <see langword="null"/> for none.</summary>
    public string? this[string key]
    {
        get => _local.TryGetValue(key, out var value) ? value : null;
        set
        {
            if (value is null) _local.Remove(key);
            else _local[key] = value;
        }
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        var (area, operation) = identifier.Split('.') switch
        {
            ["localStorage", var op] => (_local, op),
            ["sessionStorage", var op] => (_session, op),
            _ => throw new InvalidOperationException($"Not a storage call: {identifier}."),
        };
        var key = (string)args![0]!;
        switch (operation)
        {
            case "getItem":
                if (RefuseReads)
                    throw new JSException("The browser refused the read.");
                return new ValueTask<TValue>((TValue)(object?)(area.TryGetValue(key, out var value) ? value : null)!);
            case "setItem":
                if (RefuseWrites)
                {
                    RefusedWrites.Add(key);
                    throw new JSException("The browser refused the write.");
                }
                area[key] = (string)args[1]!;
                if (ReferenceEquals(area, _local))
                    Writes.Add((key, (string)args[1]!));
                return default;
            case "removeItem":
                if (RefuseWrites)
                    throw new JSException("The browser refused the write.");
                area.Remove(key);
                return default;
            default:
                throw new InvalidOperationException($"Not a storage call: {identifier}.");
        }
    }
}

/// <summary>
/// One page of the app in Local mode — Home, with the real panel — over a
/// shared <see cref="LocalServer"/> and a shared browser storage. A reload is
/// a new page over the same two; the old one is disposed with its context.
/// </summary>
internal sealed class LocalPage : IAsyncDisposable
{
    /// <summary>Short waits, so a test follows and retries quickly.</summary>
    public static LocalJobTiming FastTiming { get; } = new(TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(40), 2);

    private readonly BunitContext _context = new();

    private LocalPage(
        LocalServer server, MemoryBrowserStorage storage, IJobIdentities identities, bool reconciled,
        Func<ILocalJobs, ILocalJobs>? jobs)
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddClientServices();
        _context.Services.AddSingleton(new BrowserStorage(storage));
        _context.Services.AddSingleton(identities);
        _context.Services.AddSingleton(FastTiming);
        var stub = new StubAppModeHandler("Local") { Processing = server.Wire };
        _context.Services.AddSingleton(new HttpClient(stub) { BaseAddress = new Uri("http://localhost/") });
        if (jobs is not null)
            _context.Services.AddScoped(services => jobs(new LocalJobsClient(services.GetRequiredService<HttpClient>())));

        Home = _context.Render<Home>();
        if (reconciled)
            Home.WaitForModePanel();
        else
            Home.WaitForState(() => Home.FindComponents<LocalModePanel>().Any(), NoticeTesting.OperationBound);
    }

    /// <summary>
    /// Opens a page over <paramref name="server"/> and <paramref name="storage"/>,
    /// and waits — unless <paramref name="reconciled"/> is false — until it
    /// has reconciled. <paramref name="jobs"/>, when given, wraps the page's
    /// jobs client.
    /// </summary>
    public static LocalPage Open(
        LocalServer server, MemoryBrowserStorage storage, IJobIdentities identities, bool reconciled = true,
        Func<ILocalJobs, ILocalJobs>? jobs = null) =>
        new(server, storage, identities, reconciled, jobs);

    /// <summary>The page.</summary>
    public IRenderedComponent<Home> Home { get; }

    /// <summary>The page's Local panel.</summary>
    public LocalModePanel Panel => Home.FindComponent<LocalModePanel>().Instance;

    /// <summary>The options the panel would run with now.</summary>
    public XgpExportOptions Options => Panel.XgpOptions;

    /// <summary>What a start may do now.</summary>
    public LocalStartState StartState => Panel.StartState;

    /// <summary>The Run button.</summary>
    public AngleSharp.Dom.IElement RunButton => Home.RunButton();

    /// <summary>Clicks Run; the task is the whole run.</summary>
    public Task RunAsync() => RunButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

    /// <summary>Waits until <paramref name="condition"/> holds.</summary>
    /// <remarks>
    /// A condition on component state can hold before the DOM shows it: the
    /// panel's parameters are set during Home's render, ahead of the panel's
    /// own. What the page shows is waited for with <see cref="WaitForScreen"/>.
    /// </remarks>
    public void WaitFor(Func<bool> condition) => Home.WaitForState(condition, NoticeTesting.OperationBound);

    /// <summary>Waits until <paramref name="assertion"/> holds of what the page shows.</summary>
    public void WaitForScreen(Action assertion) => Home.WaitForAssertion(assertion, NoticeTesting.OperationBound);

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();
}

/// <summary>
/// A page's jobs client whose cancels the test answers, each in its own time
/// and order: every cancel the page sends waits where the page awaits it until
/// the test answers it — delivered, by sending it on to the server, or not
/// confirmed, a request that never reached it. Every other call passes
/// straight through.
/// </summary>
internal sealed class CancelAnsweringJobs(ILocalJobs inner) : ILocalJobs
{
    private readonly object _gate = new();
    private readonly List<(string JobId, TaskCompletionSource<bool> Answer)> _cancels = [];

    /// <summary>How many cancels the page has sent.</summary>
    public int Sent
    {
        get
        {
            lock (_gate)
                return _cancels.Count;
        }
    }

    /// <summary>
    /// Answers cancel number <paramref name="index"/> (from 0, in the order
    /// sent): <paramref name="deliver"/> sends it on to the server and hands
    /// the page the server's answer; otherwise the page learns it never
    /// reached the server.
    /// </summary>
    public async Task AnswerAsync(int index, bool deliver)
    {
        (string JobId, TaskCompletionSource<bool> Answer) cancel;
        lock (_gate)
            cancel = _cancels[index];
        if (deliver)
            cancel.Answer.SetResult(await inner.CancelAsync(cancel.JobId, CancellationToken.None));
        else
            cancel.Answer.SetException(new HttpRequestException("The request never reached the server."));
    }

    public Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
            _cancels.Add((jobId, answer));
        return answer.Task.WaitAsync(NoticeTesting.OperationBound, cancellationToken);
    }

    public Task<StartAnswer> StartAsync(StartRequest request, CancellationToken cancellationToken) =>
        inner.StartAsync(request, cancellationToken);

    public Task<LedgerSnapshot> ReadLedgerAsync(CancellationToken cancellationToken) => inner.ReadLedgerAsync(cancellationToken);

    public Task<ProcessingProgress?> ReadStatusAsync(string jobId, CancellationToken cancellationToken) =>
        inner.ReadStatusAsync(jobId, cancellationToken);

    public Task AcknowledgeAsync(string jobId, CancellationToken cancellationToken) => inner.AcknowledgeAsync(jobId, cancellationToken);

    public Task<SettleOutcome?> SettleAsync(string jobId, string instance, CancellationToken cancellationToken) =>
        inner.SettleAsync(jobId, instance, cancellationToken);
}

/// <summary>
/// A page's jobs client with its first start's answer held after it has
/// arrived: the start reaches the server and is answered, and the answer
/// reaches the page only once released — whatever the page does meanwhile, a
/// Stop included, which this wrapper does not let cancel what has already
/// been answered. The real wire cannot express that order: its own
/// cancellation of the request intervenes first. Every other call passes
/// straight through.
/// </summary>
internal sealed class AnswerHeldJobs(ILocalJobs inner) : ILocalJobs
{
    private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _starts;

    /// <summary>Completes when the first start's answer has arrived, and is held.</summary>
    public Task Arrived => _arrived.Task;

    /// <summary>Hands the held answer to the page.</summary>
    public void Release() => _release.TrySetResult();

    public async Task<StartAnswer> StartAsync(StartRequest request, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _starts) != 1)
            return await inner.StartAsync(request, cancellationToken);
        var answer = await inner.StartAsync(request, CancellationToken.None);
        _arrived.TrySetResult();
        await _release.Task.WaitAsync(NoticeTesting.OperationBound);
        return answer;
    }

    public Task<LedgerSnapshot> ReadLedgerAsync(CancellationToken cancellationToken) => inner.ReadLedgerAsync(cancellationToken);

    public Task<ProcessingProgress?> ReadStatusAsync(string jobId, CancellationToken cancellationToken) =>
        inner.ReadStatusAsync(jobId, cancellationToken);

    public Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken) => inner.CancelAsync(jobId, cancellationToken);

    public Task AcknowledgeAsync(string jobId, CancellationToken cancellationToken) => inner.AcknowledgeAsync(jobId, cancellationToken);

    public Task<SettleOutcome?> SettleAsync(string jobId, string instance, CancellationToken cancellationToken) =>
        inner.SettleAsync(jobId, instance, cancellationToken);
}
