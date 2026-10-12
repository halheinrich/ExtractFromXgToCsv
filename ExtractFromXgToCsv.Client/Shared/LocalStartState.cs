namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// What the page knows, for starting a Local run (the umbrella's
/// <c>SPEC-notices.md</c> §7.5): the server instance it reconciled with —
/// every start names it — and, for XGP, its mark, or why no XGP start may be
/// sent now. Home's reconciliation owns it; the Local panel reads it.
/// </summary>
/// <param name="Instance">The server instance the page reconciled with; <see langword="null"/> until it has.</param>
/// <param name="Mark">The last XGP ledger entry the page applied; <see langword="null"/> for none.</param>
/// <param name="XgpBlock">Why no XGP start may be sent now; <see langword="null"/> when one may.</param>
public sealed record LocalStartState(string? Instance, LedgerMark? Mark, XgpStartBlock? XgpBlock)
{
    /// <summary>The state before the page has reconciled: no start may name an instance, and XGP waits on the check.</summary>
    public static LocalStartState Checking { get; } = new(null, null, XgpStartBlock.Checking);

    /// <summary>Whether a run of <paramref name="format"/> may be started now.</summary>
    public bool Allows(OutputFormat format) =>
        Instance is not null && (format != OutputFormat.Xgp || XgpBlock is null);
}

/// <summary>Why no XGP start may be sent now (the umbrella's <c>SPEC-notices.md</c> §7.5).</summary>
public enum XgpStartBlock
{
    /// <summary>
    /// The page is checking: it has not reconciled with the server yet, cannot
    /// reach it, or owes a job of this instance it has not resolved.
    /// </summary>
    Checking,

    /// <summary>An XGP job is running in this instance.</summary>
    Running,

    /// <summary>The page owes an XGP job from an earlier instance, which the user must resolve.</summary>
    LostExport,

    /// <summary>
    /// The stored numbering record cannot be read: it may hold owed exports,
    /// so the user must resolve it before XGP export.
    /// </summary>
    UnreadableRecord,
}

/// <summary>
/// A Local XGP job this browser is owed: it minted the job's identity and
/// recorded it before sending the start, and has not yet applied the job's
/// accounting or resolved it (the umbrella's <c>SPEC-notices.md</c> §7.5). It
/// captures what names the export if it is ever lost: its options — pattern
/// and first number — and the folder it writes to.
/// </summary>
/// <param name="JobId">The identity the browser minted for the start.</param>
/// <param name="Instance">The server instance the start named.</param>
/// <param name="Options">The naming options the job captured.</param>
/// <param name="Folder">The output folder the job writes to.</param>
public sealed record OwedXgpJob(string JobId, string Instance, XgpExportOptions Options, string Folder);
