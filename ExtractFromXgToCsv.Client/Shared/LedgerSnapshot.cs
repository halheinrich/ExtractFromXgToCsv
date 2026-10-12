namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// One entry of a server instance's XGP ledger (the umbrella's
/// <c>SPEC-notices.md</c> §7.5): one admitted Local XGP job, numbered in
/// admission order, running or ended, and — once ended — the accounting it
/// committed. An entry ends in the same step as its job's terminal result.
/// </summary>
/// <param name="Number">The entry's number, from 1, in admission order.</param>
/// <param name="JobId">The job's identity.</param>
/// <param name="Running">Whether the job is still running.</param>
/// <param name="Commit">
/// Once ended, what the job committed — <see langword="null"/> when it
/// committed nothing, as a run whose options never validated did; always
/// <see langword="null"/> while it runs.
/// </param>
public sealed record LedgerEntry(int Number, string JobId, bool Running, XgpExportCommit? Commit);

/// <summary>
/// GET <c>/api/process/ledger</c>: the server's instance and its whole XGP
/// ledger, in admission order — what a page reconciles against (the
/// umbrella's <c>SPEC-notices.md</c> §7.5). The ledger is the authority on
/// the instance's XGP jobs, and is kept for the instance's lifetime.
/// </summary>
/// <param name="Instance">The server instance, minted when its process started.</param>
/// <param name="Entries">Every entry, in admission order.</param>
public sealed record LedgerSnapshot(string Instance, IReadOnlyList<LedgerEntry> Entries);

/// <summary>POST body for <c>/api/process/{jobId}/settle</c>: the instance the owed job's start named.</summary>
/// <param name="Instance">The server instance the page owes the job from.</param>
public sealed record SettleRequest(string Instance);

/// <summary>What settling an owed identity found (the umbrella's <c>SPEC-notices.md</c> §7.5).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(StrictJsonStringEnumConverter<SettleOutcome>))]
public enum SettleOutcome
{
    /// <summary>The identity was never admitted, and now never can be: the owed job is resolved.</summary>
    Retired,

    /// <summary>
    /// The start arrived — possibly after a ledger read that showed no entry,
    /// because a delayed start can win the lock first. The job stays owed: the
    /// page follows it and applies its entry.
    /// </summary>
    Admitted,
}

/// <summary>The answer to a settle: its outcome.</summary>
/// <param name="Outcome">What settling found.</param>
public sealed record SettleResponse(SettleOutcome Outcome);
