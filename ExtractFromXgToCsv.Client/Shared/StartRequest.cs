using BgDataTypes_Lib;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// POST body for <c>/api/process/start</c>: one Local run, under the identity
/// the browser minted for it (the umbrella's <c>SPEC-notices.md</c> §7.5).
/// </summary>
/// <remarks>
/// <para>
/// The server judges a start by its identity first: an identity it has
/// admitted returns that job's admission and starts nothing — so a repeat
/// never runs twice — unless the request differs, which is refused.
/// </para>
/// <para>
/// A record, so a repeat is compared with the admitted request by value.
/// </para>
/// </remarks>
/// <param name="JobId">The identity the browser minted for this run; at most 64 letters, digits and hyphens.</param>
/// <param name="Run">What the run does.</param>
public sealed record StartRequest(string JobId, ProcessRequest Run);

/// <summary>
/// The answer to an admitted start — a new job, or a repeat of one already
/// admitted, which started nothing: the job's identity.
/// </summary>
/// <param name="JobId">The job's identity, as the start named it.</param>
public sealed record StartResponse(string JobId);

/// <summary>Why the server refused a protocol request (HTTP 409, body <see cref="ProtocolRefusal"/>).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(StrictJsonStringEnumConverter<RefusalReason>))]
public enum RefusalReason
{
    /// <summary>The identity was admitted for a different request.</summary>
    ChangedRequest,
}

/// <summary>The body of a refusal: its reason.</summary>
/// <param name="Reason">Why the request was refused.</param>
public sealed record ProtocolRefusal(RefusalReason Reason);
