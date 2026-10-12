namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// How often, and how patiently, a page follows its Local jobs and reconciles
/// with the server (the umbrella's <c>SPEC-notices.md</c> §7.5). A service, so
/// a test can shorten the waits; the application uses <see cref="Default"/>.
/// </summary>
/// <param name="PollInterval">The wait before each status read of a running job, and between reconciliations while an XGP job runs.</param>
/// <param name="RetryDelay">The wait before retrying a request that failed: a start, a status read, a reconciliation.</param>
/// <param name="FailuresBeforeError">The failures in a row after which a start or a status read shows as the run error; following goes on.</param>
internal sealed record LocalJobTiming(TimeSpan PollInterval, TimeSpan RetryDelay, int FailuresBeforeError)
{
    /// <summary>The application's: a poll a second, a retry every two, the run error from the third failure in a row.</summary>
    public static LocalJobTiming Default { get; } = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 3);
}
