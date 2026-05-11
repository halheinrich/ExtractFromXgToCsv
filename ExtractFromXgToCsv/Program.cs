// *** SERVER PROJECT — ExtractFromXgToCsv ***
using ExtractFromXgToCsv.Components;
using ExtractFromXgToCsv.Services;
using QuestPDF.Infrastructure;

// BackgammonDiagram_Lib.DiagramRenderer.RenderPdf requires a QuestPDF license
// to be configured before any call, or it throws. Community is appropriate for
// the current non-commercial posture (QuestPDF terms: ≤ $1M / year revenue);
// revisit if commercial distribution ever becomes a goal. Unconditional at
// startup is cheapest — Web mode never calls RenderPdf anyway.
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

var appMode = builder.Configuration["AppMode"] ?? "Web";
builder.Services.AddSingleton(new AppModeService(appMode));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

if (appMode == "Local")
{
    builder.Services.AddScoped<LocalFolderProcessor>();
    builder.Services.AddSingleton<JobStore>();
}

builder.Services.AddControllers();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapStaticAssets();
app.UseAntiforgery();
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(ExtractFromXgToCsv.Client._Imports).Assembly);

app.Run();
