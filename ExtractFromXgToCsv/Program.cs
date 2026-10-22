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
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Home is the one interactive component, and it runs in the browser
// (WebAssembly); nothing renders interactively on the server.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// The opening book is read and decoded once (~13.6 MB) and shared across
// every job — hence singleton.
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
// The jobs live as long as the process (SPEC-notices.md §7.5): Exit, a crash
// or a restart ends them all.
builder.Services.AddSingleton<JobStore>();

// The saved-filters file relay.
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
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(ExtractFromXgToCsv.Client._Imports).Assembly);

app.Run();
