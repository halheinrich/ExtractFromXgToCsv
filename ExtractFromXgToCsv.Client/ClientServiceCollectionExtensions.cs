using ExtractFromXgToCsv.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Razor;

namespace ExtractFromXgToCsv.Client;

/// <summary>
/// The services this client's components are rendered with, registered once
/// for the app (<c>Program.cs</c>) and for the test suite alike, so a test
/// renders a component over exactly the registrations — and the lifetimes —
/// the app gives it. The <see cref="HttpClient"/> is not among them: its base
/// address is the host's, and a test supplies its own stub.
/// </summary>
internal static class ClientServiceCollectionExtensions
{
    /// <summary>Registers the client's component services, scoped.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddClientServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Local mode's job protocol (SPEC-notices.md §7.5): its wire, how
        // patiently a page starts and follows a job, and the identities the
        // browser mints for its starts. All three are services: a test can
        // hold an answer at the point the page takes it, shorten the timing,
        // and name the identities.
        services.AddScoped<ILocalJobs, LocalJobsClient>();
        services.AddSingleton(LocalJobTiming.Default);
        services.AddSingleton<IJobIdentities, GuidJobIdentities>();

        // The filter surface's owner (FilterSetup) and its storage, over
        // BgUiPrimitives_Razor's shared storage: the app's one
        // BrowserStorageCondition, and the ReportingBrowserStorage that this
        // app's own settings keys go through, both scoped — one per loaded
        // app under WebAssembly — with XgFilter_Razor's adapter as the sink
        // every refusal of the surface's own calls goes to. One owner of the
        // condition and its access across the apps (Hal's ruling,
        // halheinrich/backgammon#424, 2026-10-19). Home reports the source and
        // reads the gate from the owner; FilterSurface injects the same
        // instance.
        services.AddFilterSurface();

        return services;
    }
}
