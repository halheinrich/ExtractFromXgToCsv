using BgUiPrimitives_Razor;
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

        services.AddScoped<XgProcessingService>();

        // How Web mode converts a decoded file to rows or records. A service
        // so the conversion's completion is a seam: a test can count the
        // conversions a gesture made, or hold one at the point its owner
        // decides whether it may publish.
        services.AddScoped<ISourceConverter, XgSourceConverter>();

        // Local mode's job protocol (SPEC-notices.md §7.5): its wire, how
        // patiently a page follows and reconciles, and the identities the
        // browser mints for its starts. The timing and the identities are
        // services so a test can shorten the one and name the other.
        services.AddScoped<LocalJobsClient>();
        services.AddSingleton(LocalJobTiming.Default);
        services.AddSingleton<IJobIdentities, GuidJobIdentities>();

        // This app's one storage condition: whether the browser has refused a
        // storage call this visit, and the dismissal of the notice that says
        // so. Scoped — one per loaded app under WebAssembly — because the
        // occurrence is the app's, not a page's: a refusal while Home is
        // absent is there on return, and a dismissal survives a navigate-back.
        // A reload constructs a fresh instance, which is a fresh visit.
        // Neither bit is ever stored (SPEC-notices.md §2).
        services.AddScoped<BrowserStorageCondition>();

        // The filter surface's owner (FilterSetup) and its storage, with the
        // condition above as the sink every refusal of the surface's own
        // storage calls goes to (halheinrich/backgammon#374). Home reports the
        // source and reads the gate from the owner; FilterSurface injects the
        // same instance.
        services.AddFilterSurface<BrowserStorageCondition>();

        // BgUiPrimitives' storage accessor, under this app's own settings
        // keys. The registration above already adds the accessor; it is
        // stated here too because SettingsStorage uses it directly, and a
        // second call adds nothing.
        services.AddBrowserStorage();
        services.AddScoped<SettingsStorage>();

        return services;
    }
}
