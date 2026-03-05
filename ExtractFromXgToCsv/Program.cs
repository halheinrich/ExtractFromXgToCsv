using ExtractFromXgToCsv.Components;
using ExtractFromXgToCsv.Services;

var builder = WebApplication.CreateBuilder(args);

// Blazor Web App (Auto) — SSR + interactive WebAssembly
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Application services
builder.Services.AddScoped<XgProcessingService>();
builder.Services.AddScoped<CsvDownloadService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(ExtractFromXgToCsv.Client._Imports).Assembly);

app.Run();
