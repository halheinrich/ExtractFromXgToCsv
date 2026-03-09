// *** CLIENT PROJECT — ExtractFromXgToCsv.Client ***

using ExtractFromXgToCsv.Client.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped<XgProcessingService>();

await builder.Build().RunAsync();
