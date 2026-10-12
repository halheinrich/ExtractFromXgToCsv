namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Mints the identity of a Local job the page is about to start: the browser
/// mints it before sending the start, so a lost response can be retried, or
/// the job found again, under the same identity (the umbrella's
/// <c>SPEC-notices.md</c> §7.5). A service, so a test can mint identities it
/// can name.
/// </summary>
internal interface IJobIdentities
{
    /// <summary>A new identity: at most 64 letters, digits and hyphens, never minted before.</summary>
    string Mint();
}

/// <summary>The application's <see cref="IJobIdentities"/>: a fresh GUID each time.</summary>
internal sealed class GuidJobIdentities : IJobIdentities
{
    /// <inheritdoc />
    public string Mint() => Guid.NewGuid().ToString("N");
}
