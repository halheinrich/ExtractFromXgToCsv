using BgDataTypes_Lib;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// POST body for <c>/api/process/start</c>: one Local run, under the identity
/// the browser minted for it, naming the server instance the page reconciled
/// with (the umbrella's <c>SPEC-notices.md</c> §7.5).
/// </summary>
/// <remarks>
/// <para>
/// The server judges a start by its identity first: an identity it has
/// admitted returns that job's disposition and starts nothing — so a repeat
/// never runs twice — unless the request differs, which is refused, as is an
/// identity it has retired. A start naming another instance is refused, so a
/// retry never runs in a new instance.
/// </para>
/// <para>
/// A record, so a repeat is compared with the admitted request by value.
/// </para>
/// </remarks>
/// <param name="JobId">The identity the browser minted for this run; at most 64 letters, digits and hyphens.</param>
/// <param name="Instance">The server instance the page reconciled with.</param>
/// <param name="Mark">
/// For an XGP run, the page's mark — the last ledger entry it applied, or
/// <see langword="null"/> for none — which the server admits only when it
/// names the instance's latest entry, or the instance has admitted none.
/// <see langword="null"/> for every other format.
/// </param>
/// <param name="Run">What the run does.</param>
public sealed record StartRequest(string JobId, string Instance, LedgerMark? Mark, ProcessRequest Run);

/// <summary>
/// The answer to an admitted start — a new job, or a repeat of one already
/// admitted, which started nothing: the job's identity and, for an XGP job,
/// its ledger entry's number.
/// </summary>
/// <param name="JobId">The job's identity, as the start named it.</param>
/// <param name="LedgerNumber">For an XGP job, its entry's number in the instance's ledger; <see langword="null"/> for any other.</param>
public sealed record StartResponse(string JobId, int? LedgerNumber);

/// <summary>Why the server refused a protocol request (HTTP 409, body <see cref="ProtocolRefusal"/>).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(StrictJsonStringEnumConverter<RefusalReason>))]
public enum RefusalReason
{
    /// <summary>The request names another server instance: the server restarted since the page reconciled.</summary>
    OtherInstance,

    /// <summary>The identity was admitted for a different request.</summary>
    ChangedRequest,

    /// <summary>The identity was retired — settled as never started — so it can never run.</summary>
    Retired,

    /// <summary>An XGP job is running in this instance.</summary>
    XgpRunning,

    /// <summary>The page's mark does not name the instance's latest XGP entry: it has not applied the latest export.</summary>
    StaleMark,
}

/// <summary>The body of a refusal: its reason.</summary>
/// <param name="Reason">Why the request was refused.</param>
public sealed record ProtocolRefusal(RefusalReason Reason);
