using System.Net;
using System.Net.Http.Json;
using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// The browser's side of the Local job protocol's wire (the umbrella's
/// <c>SPEC-notices.md</c> §7.5): one typed call per endpoint of the server's
/// <c>ProcessController</c>. It retries nothing: a call that fails throws, and
/// what to do then — retry, or show the run error — is its caller's.
/// </summary>
/// <remarks>
/// A service, so an answer is a seam: a test can hold one after it has
/// arrived, at the point the page decides what to do with it — a Stop pressed
/// meanwhile, say — which the real wire cannot express, its own cancellation
/// of the request intervening first. The application's is
/// <see cref="LocalJobsClient"/>.
/// </remarks>
internal interface ILocalJobs
{
    /// <summary>Sends a start: admitted — new, or a repeat that started nothing — or refused, for its reason.</summary>
    /// <exception cref="HttpRequestException">The server could not be reached, or did not answer either way.</exception>
    Task<StartAnswer> StartAsync(StartRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The job's current snapshot; <see langword="null"/> when the server does
    /// not hold the job — never started there, released, or lost with a
    /// restart.
    /// </summary>
    /// <exception cref="HttpRequestException">The server could not be reached, or did not answer.</exception>
    Task<ProcessingProgress?> ReadStatusAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the server to cancel the job: <see langword="true"/> when it
    /// signalled the running job, <see langword="false"/> when it refused —
    /// the job had ended, or the server does not hold it.
    /// </summary>
    /// <exception cref="HttpRequestException">The server could not be reached, or answered neither way.</exception>
    Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>Acknowledges the job's terminal result, releasing it on the server.</summary>
    /// <exception cref="HttpRequestException">The server could not be reached, or did not answer.</exception>
    Task AcknowledgeAsync(string jobId, CancellationToken cancellationToken);
}

/// <summary>The application's <see cref="ILocalJobs"/>: the wire, over HTTP.</summary>
internal sealed class LocalJobsClient(HttpClient http) : ILocalJobs
{
    /// <inheritdoc />
    public async Task<StartAnswer> StartAsync(StartRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/api/process/start", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
            return new StartAnswer.Refused((await ReadAsync<ProtocolRefusal>(response, cancellationToken)).Reason);
        response.EnsureSuccessStatusCode();
        return StartAnswer.Admitted.Value;
    }

    /// <inheritdoc />
    public async Task<ProcessingProgress?> ReadStatusAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"/api/process/{jobId}/status", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await ReadAsync<ProcessingProgress>(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync($"/api/process/{jobId}/cancel", content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task AcknowledgeAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync($"/api/process/{jobId}/acknowledge", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(cancellationToken)
        ?? throw new HttpRequestException($"The server answered no {typeof(T).Name}.");
}

/// <summary>What the server answered a start.</summary>
internal abstract record StartAnswer
{
    /// <summary>Admitted — a new job, or a repeat of one, which started nothing.</summary>
    public sealed record Admitted : StartAnswer
    {
        /// <summary>The one value.</summary>
        public static Admitted Value { get; } = new();
    }

    /// <summary>Refused, for <paramref name="Reason"/>; nothing started.</summary>
    public sealed record Refused(RefusalReason Reason) : StartAnswer;
}
