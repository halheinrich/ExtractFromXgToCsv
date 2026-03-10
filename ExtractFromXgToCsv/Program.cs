// *** SERVER PROJECT — ExtractFromXgToCsv ***
using ExtractFromXgToCsv.Components;
using ExtractFromXgToCsv.Services;

var builder = WebApplication.CreateBuilder(args);

var appMode = builder.Configuration["AppMode"] ?? "Web";
builder.Services.AddSingleton(new AppModeService(appMode));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

if (appMode == "Local")
{
    builder.Services.AddScoped<LocalFolderProcessor>();
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
