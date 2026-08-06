// *** CLIENT PROJECT — ExtractFromXgToCsv.Client ***

using ExtractFromXgToCsv.Client.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using XgFilter_Razor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<XgProcessingService>();

// The filter half of the run/download gate — XgFilter_Razor's AppliedFilter,
// mediated by the FilterSurface that Home hosts (a commit Sets it stamped with
// the mode's source token; uncommitted-edit reports Clear it; a source change
// expires it). Registered in DI per the producer contract — the holder must
// outlive the composite, which dies with its page — Scoped so one instance
// serves the app (in WASM, "scoped" is one per loaded app/tab).
builder.Services.AddScoped<AppliedFilter>();

await builder.Build().RunAsync();
