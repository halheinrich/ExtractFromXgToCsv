using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Shared helpers for bUnit-based component tests. Kept narrow on purpose:
/// a reflection reader for private state (used to pin the name allocator's
/// bookkeeping without exposing it as a permanent seam) and the minimal
/// HttpMessageHandlers the panels' own endpoints need. Nothing seeds a
/// component's or the coordinator's state: a test reaches it through the
/// coordinator's commands and the wire.
/// </summary>
internal static class bUnitTestHelpers
{
    public static T GetPrivateField<T>(object instance, string fieldName) =>
        (T)PrivateField(instance, fieldName).GetValue(instance)!;

    private static FieldInfo PrivateField(object instance, string fieldName) =>
        instance.GetType().GetField(
            fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"Field '{fieldName}' not found on {instance.GetType().Name}.");
}

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> stub of the server: an in-memory
/// edition of the saved-filters file relay
/// (<c>GET</c>/<c>PUT /api/filterdocument</c>, matching
/// <see cref="ExtractFromXgToCsv.Controllers.FilterDocumentController"/>'s
/// 200/204 contract) — Home hosts a <c>FilterSurface</c> whose store reads
/// the document at mount and on every source change, so a Home test without
/// these routes would degrade to LoadFailed. Anything else goes
/// to <see cref="Processing"/> when a test supplies one; without one, it
/// 404s so a stray request fails loudly rather than hanging.
/// </summary>
internal sealed class StubServerHandler : HttpMessageHandler
{
    /// <summary>
    /// The Local processing endpoints, for a test that runs a job through
    /// Home — a <see cref="CapturingProcessHandler"/>, or the real server over
    /// a <see cref="ProtocolWire"/>; <see langword="null"/> leaves them 404.
    /// </summary>
    public HttpMessageHandler? Processing { get; init; }

    /// <summary>
    /// The in-memory documents served and updated by the filterdocument
    /// routes, keyed by (folder, name) exactly as the query names them.
    /// Seed before rendering to give a folder a saved-filters document.
    /// </summary>
    public Dictionary<(string Folder, string Name), string> Documents { get; } = new();

    /// <summary>Every filterdocument read, in order — (folder, name).</summary>
    public List<(string Folder, string Name)> DocumentReads { get; } = new();

    /// <summary>Every filterdocument write, in order — (folder, name, content).</summary>
    public List<(string Folder, string Name, string Content)> DocumentWrites { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == "/api/filterdocument")
        {
            var query = ParseQuery(request.RequestUri!);
            var key = (query["folder"], query["name"]);

            if (request.Method == HttpMethod.Get)
            {
                DocumentReads.Add(key);
                return Documents.TryGetValue(key, out var content)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(content, Encoding.UTF8, "text/plain"),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Put)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Documents[key] = body;
                DocumentWrites.Add((key.Item1, key.Item2, body));
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }

        if (Processing is { } processing)
        {
            using var invoker = new HttpMessageInvoker(processing, disposeHandler: false);
            return await invoker.SendAsync(request, cancellationToken);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                kv => Uri.UnescapeDataString(kv[0]),
                kv => kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty);
}

/// <summary>
/// Job identities a test can name: <c>job-1</c>, <c>job-2</c>, … in the order
/// minted. Fresh for each test context, so never repeated within one.
/// </summary>
internal sealed class SequentialJobIdentities : IJobIdentities
{
    private int _minted;

    public string Mint() => $"job-{Interlocked.Increment(ref _minted)}";
}

/// <summary>The JSON options both ends of the Local wire use: the web defaults.</summary>
internal static class JsonOptions
{
    /// <summary><see cref="JsonSerializerDefaults.Web"/>, as <c>HttpClient</c> and a bare <c>AddControllers()</c> use them.</summary>
    public static JsonSerializerOptions Web { get; } = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// Fakes the Local-mode processing endpoints so the coordinator's run
/// runs its real request-building, start and polling loop against an in-memory
/// server speaking the job protocol (the umbrella's <c>SPEC-notices.md</c>
/// §7.5) in its simplest form: it captures the <see cref="StartRequest"/>
/// POSTed to <c>/api/process/start</c> and admits it, and answers each status
/// poll, whatever the job's identity, with the next of its snapshots: by
/// default one terminal snapshot (three rows written), so the click resolves
/// in one iteration. Acknowledgements and cancels are answered; everything
/// else 404s, so a stray request fails loudly rather than hanging. The real
/// server's judgements are pinned against the real server, not here.
/// </summary>
/// <remarks>
/// Binds with <see cref="JsonSerializerDefaults.Web"/> — what a bare
/// <c>AddControllers()</c> actually gives <c>ProcessController.Start</c> on the
/// real server — so what <see cref="Captured"/> holds is what the server would
/// have bound, and a payload the server would reject fails here too. (It
/// previously used a hand-rolled case-insensitive options object that mirrored
/// the panel's own; the panel no longer has one, and mirroring the *server* is
/// the point of a stand-in.)
/// </remarks>
internal sealed class CapturingProcessHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions _opts = JsonOptions.Web;

    private readonly object _gate = new();

    private readonly IReadOnlyList<string> _statusBodies;
    private int _polls;

    /// <summary>A server whose every status poll answers <paramref name="terminalStatusJson"/>.</summary>
    public CapturingProcessHandler(string terminalStatusJson = "{\"complete\":true,\"totalRows\":3}")
        : this([terminalStatusJson])
    {
    }

    /// <summary>
    /// A server whose status polls answer <paramref name="statuses"/> in turn,
    /// the last answering every poll after it — progress snapshots before a
    /// terminal one, or the terminals of consecutive runs (the polls are
    /// counted across runs, whichever job asks).
    /// </summary>
    public CapturingProcessHandler(params ProcessingProgress[] statuses)
        : this(statuses.Select(Terminal).ToList())
    {
    }

    private CapturingProcessHandler(IReadOnlyList<string> statusBodies)
    {
        if (statusBodies.Count == 0)
            throw new ArgumentException("A server needs at least one status to answer.", nameof(statusBodies));
        _statusBodies = statusBodies;
    }

    /// <summary>
    /// <paramref name="snapshot"/> as the status endpoint writes it — the
    /// terminal a test hands this server, stated as the typed snapshot rather
    /// than as hand-written JSON.
    /// </summary>
    public static string Terminal(ProcessingProgress snapshot) => JsonSerializer.Serialize(snapshot, _opts);

    private readonly TaskCompletionSource _statusReached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _statusGate;
    private TaskCompletionSource _heldPollReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _holdFromPoll = int.MaxValue;
    private int _holdThroughPoll = int.MaxValue;

    /// <summary>
    /// Hold every status poll from now on pending until
    /// <see cref="ReleaseStatus"/>, so a test can act while a run is in flight.
    /// Call before the run starts.
    /// </summary>
    public void HoldStatus() => Hold(from: 1, through: int.MaxValue);

    /// <summary>
    /// Hold only status poll number <paramref name="pollNumber"/> (1-based,
    /// counted across runs) until <see cref="ReleaseStatus"/>; the polls
    /// before it answer at once. <see cref="HeldPollReached"/> completes when
    /// it arrives.
    /// </summary>
    public void HoldPoll(int pollNumber) => Hold(from: pollNumber, through: pollNumber);

    private void Hold(int from, int through)
    {
        _statusGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _heldPollReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _holdFromPoll = from;
        _holdThroughPoll = through;
    }

    /// <summary>Let a held status poll answer.</summary>
    public void ReleaseStatus() =>
        (_statusGate ?? throw new InvalidOperationException("No status poll is held.")).TrySetResult();

    /// <summary>Completes when the first status poll has arrived — the run is in flight.</summary>
    public Task StatusReached => _statusReached.Task;

    /// <summary>Completes when a held poll has arrived and is being held.</summary>
    public Task HeldPollReached => _heldPollReached.Task;

    /// <summary>
    /// The bound on waiting for a run through this server: the panel's
    /// polling loop waits a whole second before its first status poll, so
    /// bUnit's one-second <c>DefaultWaitTimeout</c> would fail a run that is
    /// merely on schedule. The bound only guards against a run that never
    /// completes; the completion itself is the evidence.
    /// </summary>
    public static TimeSpan RunTimeout { get; } = TimeSpan.FromSeconds(10);

    /// <summary>The run of the last start POSTed to <c>/api/process/start</c>; null until one is.</summary>
    public ProcessRequest? Captured => CapturedStart?.Run;

    /// <summary>The last start POSTed to <c>/api/process/start</c>, whole; null until one is.</summary>
    public StartRequest? CapturedStart { get; private set; }

    /// <summary>
    /// The raw body of that POST, so a test can assert on the bytes actually on
    /// the wire rather than only on what they bound to
    /// (halheinrich/backgammon#164 / halheinrich/backgammon#37: the question is
    /// whether enums cross as names or as ordinals, which binding hides).
    /// </summary>
    public string? CapturedJson { get; private set; }

    /// <summary>The identities acknowledged, in order.</summary>
    public List<string> Acknowledged { get; } = [];

    /// <summary>How many starts have been POSTed.</summary>
    public int Starts => Volatile.Read(ref _starts);

    private int _starts;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (request.Method == HttpMethod.Post && path == "/api/process/start")
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            CapturedJson = json;
            var start = JsonSerializer.Deserialize<StartRequest>(json, _opts)!;
            CapturedStart = start;
            Interlocked.Increment(ref _starts);
            return Json(JsonSerializer.Serialize(new StartResponse(start.JobId), _opts));
        }

        if (path.StartsWith("/api/process/", StringComparison.Ordinal))
        {
            var jobId = path["/api/process/".Length..].Split('/')[0];

            if (request.Method == HttpMethod.Get && path.EndsWith("/status", StringComparison.Ordinal))
            {
                var poll = Interlocked.Increment(ref _polls);
                _statusReached.TrySetResult();
                if (_statusGate is { } gate && poll >= _holdFromPoll && poll <= _holdThroughPoll)
                {
                    _heldPollReached.TrySetResult();
                    await gate.Task.WaitAsync(cancellationToken);
                }
                return Json(_statusBodies[Math.Min(poll, _statusBodies.Count) - 1]);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/acknowledge", StringComparison.Ordinal))
            {
                lock (_gate)
                    Acknowledged.Add(jobId);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/cancel", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
