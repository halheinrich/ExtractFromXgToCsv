// *** CLIENT PROJECT — ExtractFromXgToCsv.Client ***

using ExtractFromXgToCsv.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Everything the components are rendered with — the filter setup and its
// refusal sink, browser storage, the processing service. One registration,
// shared with the test suite; the lifetimes are explained there.
builder.Services.AddClientServices();

await builder.Build().RunAsync();
