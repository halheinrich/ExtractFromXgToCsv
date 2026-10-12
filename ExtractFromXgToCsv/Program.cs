// *** SERVER PROJECT — ExtractFromXgToCsv ***
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Components;
using ExtractFromXgToCsv.Services;
using QuestPDF.Infrastructure;
using XgFilter_Lib;

// BackgammonDiagram_Lib.ExportRaster.DiagramRasterRenderer.RenderPdf requires a
// QuestPDF license to be configured before any call, or it throws. Community is
// appropriate for the current non-commercial posture (QuestPDF terms: ≤ $1M /
// year revenue); revisit if commercial distribution ever becomes a goal.
// QuestPDF reaches the server transitively via the ExportRaster assembly.
// Unconditional at startup is cheapest — Web mode never calls RenderPdf anyway.
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

var appMode = new AppModeService(builder.Configuration["AppMode"] ?? "Web");
builder.Services.AddSingleton(appMode);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

if (appMode.IsLocal)
{
    // The opening book is read and decoded once (~13.6 MB) and shared across
    // every job — hence singleton. Local mode only: Web mode parses .xg files
    // client-side and offers the book through the browser file picker instead.
    builder.Services.AddSingleton<OpeningBookProvider>();
    // The processor runs under the application's one ranking, stated once in
    // AppRanking and handed in here (halheinrich/backgammon#273). Its second
    // logger is the filtering operation's, which also carries the producer's
    // per-decision warnings.
    builder.Services.AddScoped(sp => new LocalFolderProcessor(
        sp.GetRequiredService<ILogger<LocalFolderProcessor>>(),
        sp.GetRequiredService<ILogger<FilteredDecisionIterator>>(),
        AppRanking.Ranking,
        sp.GetRequiredService<OpeningBookProvider>()));
    // The server instance is minted here, as the process starts, and every
    // job, start disposition and XGP ledger entry is this instance's
    // (SPEC-notices.md §7.5): Exit, a crash or a restart ends them all.
    builder.Services.AddSingleton(new ServerInstance());
    builder.Services.AddSingleton<JobStore>();
}

// Deliberately outside the Local guard: the saved-filters file relay is inert
// IO glue with no processing dependencies, and its Local-only gate lives in
// FilterDocumentController as an explicit action guard so Web mode answers
// with an observable 404 instead of a container resolution failure.
builder.Services.AddSingleton<FilterDocumentStore>();

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
