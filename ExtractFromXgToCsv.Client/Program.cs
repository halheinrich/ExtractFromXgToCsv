// *** CLIENT PROJECT — ExtractFromXgToCsv.Client ***

using ExtractFromXgToCsv.Client.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using XgFilter_Razor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<XgProcessingService>();

// The filter half of the run/download gate — XgFilter_Razor's AppliedFilter,
// mediated by the FilterSurface that Home hosts (a commit Sets it keyed to the
// mode's source token; uncommitted-edit reports Clear it; a source change
// clears it). Registered in DI per the producer contract — the holder must
// outlive the composite, which dies with its page — Scoped so one instance
// serves the app (in WASM, "scoped" is one per loaded app/tab). Home only ever
// reads it source-relatively, through its FilterInEffect derivation.
builder.Services.AddScoped<AppliedFilter>();

// The restored-filter notice's state, beside the holder above and for the same
// reason: a full reload constructs a fresh instance, and that construction —
// not any recorded fact — is what tells a boot's localStorage restore (say so,
// per the filtering spec's §4 legibility rule) apart from a navigate-back
// remount over the same setup (say nothing). Scoped, therefore, not per-page.
// Deliberately opaque to this host: every member that moves it is
// producer-internal, so Home's whole contract is to register the instance here
// and bind it to FilterSurface.
builder.Services.AddScoped<FilterRestoreNotice>();

await builder.Build().RunAsync();
