using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Shared helpers for bUnit-based component tests. Kept narrow on purpose:
/// reflection accessors for private state (used to pin the name allocator's
/// bookkeeping, and to seed panel fields normally restored from browser
/// storage, without exposing either as a permanent seam) and the minimal
/// HttpMessageHandlers the panels' own endpoints need.
/// </summary>
internal static class bUnitTestHelpers
{
    public static T GetPrivateField<T>(object instance, string fieldName) =>
        (T)PrivateField(instance, fieldName).GetValue(instance)!;

    public static void SetPrivateField(object instance, string fieldName, object? value) =>
        PrivateField(instance, fieldName).SetValue(instance, value);

    private static FieldInfo PrivateField(object instance, string fieldName) =>
        instance.GetType().GetField(
            fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"Field '{fieldName}' not found on {instance.GetType().Name}.");
}

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> stub. Returns the
/// <see cref="ExtractFromXgToCsv.Client.Shared.AppModeResponse"/> JSON
/// object shape for <c>/api/appmode</c>, matching the real server's
/// <see cref="ExtractFromXgToCsv.Controllers.AppModeController"/>, and an
/// in-memory edition of the saved-filters file relay
/// (<c>GET</c>/<c>PUT /api/filterdocument</c>, matching
/// <see cref="ExtractFromXgToCsv.Controllers.FilterDocumentController"/>'s
/// 200/204 contract) — Home hosts a <c>FilterSurface</c> whose store reads
/// the document at mount and on every source change, so a Local-mode Home
/// test without these routes would degrade to LoadFailed. Anything else goes
/// to <see cref="Processing"/> when a test supplies one, and otherwise 404s so
/// a stray request fails loudly rather than hanging.
/// </summary>
internal sealed class StubAppModeHandler(string mode) : HttpMessageHandler
{
    private readonly Queue<HeldAppMode> _appModeHolds = new();

    /// <summary>
    /// The Local processing endpoints, for a test that runs a job through
    /// Home; <see langword="null"/> leaves them 404.
    /// </summary>
    public CapturingProcessHandler? Processing { get; init; }

    /// <summary>
    /// Hold the next <c>/api/appmode</c> request pending until its
    /// <see cref="HeldAppMode.Release"/> — the seam that makes Home's
    /// mode-unresolved window observable instead of a race. Each call holds
    /// one request, in order; later requests are answered at once.
    /// </summary>
    public HeldAppMode HoldNextAppMode()
    {
        var hold = new HeldAppMode();
        lock (_appModeHolds) _appModeHolds.Enqueue(hold);
        return hold;
    }

    /// <summary>One held <c>/api/appmode</c> request.</summary>
    public sealed class HeldAppMode
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _reached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _ended =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the request has arrived and is being held.</summary>
        public Task Reached => _reached.Task;

        /// <summary>
        /// Completes when the request has ended: <see langword="true"/> if the
        /// caller cancelled it while held, <see langword="false"/> if it was
        /// answered.
        /// </summary>
        public Task<bool> Cancelled => _ended.Task;

        /// <summary>Answer the request, unless it was cancelled first.</summary>
        public void Release() => _release.TrySetResult();

        internal async Task WaitAsync(CancellationToken cancellationToken)
        {
            _reached.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _ended.TrySetResult(true);
                throw;
            }
            _ended.TrySetResult(false);
        }
    }

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

        if (path == "/api/appmode")
        {
            HeldAppMode? hold;
            lock (_appModeHolds) _appModeHolds.TryDequeue(out hold);
            if (hold is not null)
                await hold.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"Mode\":\"{mode}\"}}",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
        }

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
/// Fakes the Local-mode processing endpoints so <c>LocalModePanel.RunLocalAsync</c>
/// runs its real request-building and polling loop against an in-memory server:
/// captures the <see cref="ProcessRequest"/> POSTed to <c>/api/process/start</c>
/// and answers the first status poll with a terminal snapshot
/// (<paramref name="terminalStatusJson"/>; three rows written by default) so
/// the click resolves in one iteration. Everything else 404s, so a stray
/// request fails loudly rather than hanging.
/// </summary>
/// <param name="terminalStatusJson">The terminal snapshot the first status poll answers.</param>
/// <remarks>
/// Binds with <see cref="JsonSerializerDefaults.Web"/> — what a bare
/// <c>AddControllers()</c> actually gives <c>ProcessController.Start</c> on the
/// real server — so what <see cref="Captured"/> holds is what the server would
/// have bound, and a payload the server would reject fails here too. (It
/// previously used a hand-rolled case-insensitive options object that mirrored
/// the panel's own; the panel no longer has one, and mirroring the *server* is
/// the point of a stand-in.)
/// </remarks>
internal sealed class CapturingProcessHandler(
    string terminalStatusJson = "{\"complete\":true,\"totalRows\":3}") : HttpMessageHandler
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    private readonly TaskCompletionSource _statusReached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _statusGate;

    /// <summary>
    /// Hold the next status poll pending until <see cref="ReleaseStatus"/>, so
    /// a test can act while a run is in flight. Call before the run starts.
    /// </summary>
    public void HoldStatus() =>
        _statusGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Let a held status poll answer with the terminal snapshot.</summary>
    public void ReleaseStatus() =>
        (_statusGate ?? throw new InvalidOperationException("No status poll is held.")).TrySetResult();

    /// <summary>Completes when the first status poll has arrived — the run is in flight.</summary>
    public Task StatusReached => _statusReached.Task;

    /// <summary>
    /// The bound on waiting for a run through this server: the panel's
    /// polling loop waits a whole second before its first status poll, so
    /// bUnit's one-second <c>DefaultWaitTimeout</c> would fail a run that is
    /// merely on schedule. The bound only guards against a run that never
    /// completes; the completion itself is the evidence.
    /// </summary>
    public static TimeSpan RunTimeout { get; } = TimeSpan.FromSeconds(10);

    /// <summary>The last request POSTed to <c>/api/process/start</c>; null until one is.</summary>
    public ProcessRequest? Captured { get; private set; }

    /// <summary>
    /// The raw body of that POST, so a test can assert on the bytes actually on
    /// the wire rather than only on what they bound to
    /// (halheinrich/backgammon#164 / halheinrich/backgammon#37: the question is
    /// whether enums cross as names or as ordinals, which binding hides).
    /// </summary>
    public string? CapturedJson { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (request.Method == HttpMethod.Post && path == "/api/process/start")
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            CapturedJson = json;
            Captured = JsonSerializer.Deserialize<ProcessRequest>(json, _opts);
            return Json("{\"jobId\":\"j1\"}");
        }

        if (request.Method == HttpMethod.Get && path == "/api/process/j1/status")
        {
            _statusReached.TrySetResult();
            if (_statusGate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);
            return Json(terminalStatusJson);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}

/// <summary>
/// A <see cref="LocalModePanel"/> one
/// click from a run, and the run itself — for tests whose subject is what a
/// completed run shows. The panel records what a run was computed from when
/// the run starts, so a result is reached by running, never by seeding the
/// progress it shows.
/// </summary>
internal static class LocalPanelRuns
{
    public const string Folder = @"D:\xg\matches";

    /// <summary>
    /// Renders the panel over <see cref="Folder"/> with
    /// <paramref name="filter"/> in effect, and types an output path.
    /// </summary>
    public static IRenderedComponent<LocalModePanel> RenderReadyLocalPanel(
        this BunitContext ctx, FilterConfig filter)
    {
        var cut = ctx.Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.Source, FilterSourceToken.FromPath(Folder))
            .Add(c => c.FilterInEffect, filter)
            .Add(c => c.FolderPath, Folder)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));
        cut.Find("#outputPath").Input(new ChangeEventArgs { Value = @"D:\xg\out.csv" });
        return cut;
    }

    /// <summary>Clicks Run and waits for the run to complete.</summary>
    public static Task RunToCompletionAsync(
        this IRenderedComponent<LocalModePanel> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Run")
           .ClickAsync(new MouseEventArgs())
           .WaitAsync(CapturingProcessHandler.RunTimeout);
}
