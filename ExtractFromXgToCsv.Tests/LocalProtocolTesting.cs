using System.Net;
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
/// <see cref="ProtocolWire"/> a test can fault. Its XGP file creation can be
/// held (<see cref="Gate"/>), so a job can be kept running.
/// </summary>
internal sealed class LocalServer : IDisposable
{
    private const string Fixture = "MTCH4064.xg";

    private readonly string _root = Directory.CreateTempSubdirectory("xg-protocol-").FullName;
    private readonly WebApplicationFactory<AppModeService> _factory;

    public LocalServer()
    {
        InputFolder = Directory.CreateDirectory(Path.Combine(_root, "matches")).FullName;
        OutputFolder = Path.Combine(_root, "positions");
        File.Copy(Path.Combine(FixtureHelper.FixtureDir, Fixture), Path.Combine(InputFolder, Fixture));

        _factory = new WebApplicationFactory<AppModeService>().WithWebHostBuilder(builder =>
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
                new GatedDestination(Gate))));
        });
        Wire = new ProtocolWire(_factory.Server.CreateHandler());
    }

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

    /// <summary>The file system, with every XGP file's create-new passing <see cref="Gate"/> first.</summary>
    private sealed class GatedDestination(FileGate gate) : IOutputDestination
    {
        public Stream Replace(string path) => FileSystemDestination.Instance.Replace(path);

        public Stream CreateNew(string path)
        {
            gate.Pass();
            return FileSystemDestination.Instance.CreateNew(path);
        }

        public void Delete(string path) => FileSystemDestination.Instance.Delete(path);
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
/// arrives (<see cref="Fail"/>), or one held before it arrives
/// (<see cref="Hold"/>). Rules apply in the order added.
/// </summary>
internal sealed class ProtocolWire(HttpMessageHandler server) : DelegatingHandler(server)
{
    private readonly object _gate = new();
    private readonly List<Rule> _rules = [];

    /// <summary>Every request made over the wire, as "METHOD path", in order.</summary>
    public List<string> Requests { get; } = [];

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

    /// <summary>The next <paramref name="times"/> matching requests reach the server, and their responses are lost.</summary>
    public Rule Drop(Func<HttpRequestMessage, bool> match, int times = 1) => Add(new Rule(match, RuleKind.Drop, times));

    /// <summary>Matching requests never reach the server — the next <paramref name="times"/>, or all until stopped.</summary>
    public Rule Fail(Func<HttpRequestMessage, bool> match, int times = int.MaxValue) => Add(new Rule(match, RuleKind.Fail, times));

    /// <summary>The next matching request is held before it reaches the server, until released.</summary>
    public Rule Hold(Func<HttpRequestMessage, bool> match) => Add(new Rule(match, RuleKind.Hold, 1));

    private Rule Add(Rule rule)
    {
        lock (_gate)
            _rules.Add(rule);
        return rule;
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
            case RuleKind.Drop:
                using (await base.SendAsync(request, cancellationToken))
                    rule.Answered();
                throw new HttpRequestException("The response was lost.");
            case RuleKind.Hold:
                await rule.HeldAsync();
                var response = await base.SendAsync(request, cancellationToken);
                rule.Answered();
                return response;
            default:
                return await base.SendAsync(request, cancellationToken);
        }
    }

    public enum RuleKind { Drop, Fail, Hold }

    /// <summary>One rule: what it matches, what it does, how many more times, and its signals.</summary>
    public sealed class Rule(Func<HttpRequestMessage, bool> match, RuleKind kind, int times)
    {
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
                    throw new JSException("The browser refused the write.");
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

    private LocalPage(LocalServer server, MemoryBrowserStorage storage, IJobIdentities identities, bool reconciled)
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddClientServices();
        _context.Services.AddSingleton(new BrowserStorage(storage));
        _context.Services.AddSingleton(identities);
        _context.Services.AddSingleton(FastTiming);
        var stub = new StubAppModeHandler("Local") { Processing = server.Wire };
        _context.Services.AddSingleton(new HttpClient(stub) { BaseAddress = new Uri("http://localhost/") });

        Home = _context.Render<Home>();
        if (reconciled)
            Home.WaitForModePanel();
        else
            Home.WaitForState(() => Home.FindComponents<LocalModePanel>().Any(), NoticeTesting.OperationBound);
    }

    /// <summary>
    /// Opens a page over <paramref name="server"/> and <paramref name="storage"/>,
    /// and waits — unless <paramref name="reconciled"/> is false — until it
    /// has reconciled.
    /// </summary>
    public static LocalPage Open(LocalServer server, MemoryBrowserStorage storage, IJobIdentities identities, bool reconciled = true) =>
        new(server, storage, identities, reconciled);

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
