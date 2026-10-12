namespace ExtractFromXgToCsv.Services;

/// <summary>
/// This server instance (the umbrella's <c>SPEC-notices.md</c> §7.5): minted
/// when the server process starts, and ended by Exit, a crash or a restart.
/// Every Local job, start disposition and XGP ledger entry belongs to one
/// instance, and a request naming another is refused, so nothing a page
/// started can be taken for a job of a later instance.
/// </summary>
public sealed class ServerInstance
{
    /// <summary>A new instance, with a fresh identity.</summary>
    public ServerInstance()
        : this(Guid.NewGuid().ToString("N"))
    {
    }

    /// <summary>The instance <paramref name="id"/> — a test's, which names it.</summary>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null, empty or white space.</exception>
    public ServerInstance(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    /// <summary>The instance's identity, named in every protocol response that needs it.</summary>
    public string Id { get; }
}
