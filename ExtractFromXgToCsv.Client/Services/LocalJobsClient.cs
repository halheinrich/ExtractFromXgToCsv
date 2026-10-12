using System.Net;
using System.Net.Http.Json;
using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// The browser's side of the Local job protocol's wire (the umbrella's
/// <c>SPEC-notices.md</c> §7.5): one typed call per endpoint of the server's
/// <c>ProcessController</c>. It retries nothing: a call that fails throws, and
/// what to do then — retry, show the run error, reconcile — is its caller's.
/// </summary>
internal sealed class LocalJobsClient(HttpClient http)
{
    /// <summary>The server's instance and its whole XGP ledger.</summary>
    /// <exception cref="HttpRequestException">The server could not be reached, or did not answer.</exception>
    public async Task<LedgerSnapshot> ReadLedgerAsync(CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<LedgerSnapshot>("/api/process/ledger", cancellationToken)
        ?? throw new HttpRequestException("The server answered no ledger.");

    /// <summary>Sends a start: admitted — new, or a repeat that started nothing — or refused, for its reason.</summary>
    /// <exception cref="HttpRequestException">The server could not be reached, or did not answer either way.</exception>
    public async Task<StartAnswer> StartAsync(StartRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/api/process/start", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
            return new StartAnswer.Refused((await ReadAsync<ProtocolRefusal>(response, cancellationToken)).Reason);
        response.EnsureSuccessStatusCode();
        return new StartAnswer.Admitted((await ReadAsync<StartResponse>(response, cancellationToken)).LedgerNumber);
    }

    /// <summary>
    /// The job's current snapshot; <see langword="null"/> when the server does
    /// not hold the job — never started there, released, or lost with an
    /// earlier instance.
    /// </summary>
    /// <exception cref="HttpRequestException">The server could not be reached, or did not answer.</exception>
    public async Task<ProcessingProgress?> ReadStatusAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"/api/process/{jobId}/status", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await ReadAsync<ProcessingProgress>(response, cancellationToken);
    }

    /// <summary>Asks the server to cancel the job; whether it was still running is not the caller's concern.</summary>
    /// <exception cref="HttpRequestException">The server could not be reached.</exception>
    public async Task CancelAsync(string jobId)
    {
        using var _ = await http.PostAsync($"/api/process/{jobId}/cancel", content: null);
    }

    /// <summary>Acknowledges the job's terminal result, releasing it on the server.</summary>
    /// <exception cref="HttpRequestException">The server could not be reached, or did not answer.</exception>
    public async Task AcknowledgeAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync($"/api/process/{jobId}/acknowledge", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Settles an owed identity of <paramref name="instance"/>: retired or
    /// admitted; <see langword="null"/> when the server is another instance
    /// now, and refused it.
    /// </summary>
    /// <exception cref="HttpRequestException">The server could not be reached, or did not answer either way.</exception>
    public async Task<SettleOutcome?> SettleAsync(string jobId, string instance, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            $"/api/process/{jobId}/settle", new SettleRequest(instance), cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
            return null;
        response.EnsureSuccessStatusCode();
        return (await ReadAsync<SettleResponse>(response, cancellationToken)).Outcome;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(cancellationToken)
        ?? throw new HttpRequestException($"The server answered no {typeof(T).Name}.");
}

/// <summary>What the server answered a start.</summary>
internal abstract record StartAnswer
{
    /// <summary>Admitted — a new job, or a repeat of one, which started nothing — with its XGP ledger entry's number.</summary>
    public sealed record Admitted(int? LedgerNumber) : StartAnswer;

    /// <summary>Refused, for <paramref name="Reason"/>; nothing started.</summary>
    public sealed record Refused(RefusalReason Reason) : StartAnswer;
}
