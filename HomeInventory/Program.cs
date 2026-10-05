using HomeInventory.Components;
using HomeInventory;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeInventory");
Directory.CreateDirectory(dataDirectory);
builder.Services.AddDbContext<InventoryDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "inventory.db")}"));
builder.Services.AddSingleton(new FileStore(builder.Configuration["Storage:FilesPath"] ?? Path.Combine(dataDirectory, "files")));
builder.Services.AddHostedService<OrphanFileSweeper>();

// Other devices on the home network can use the app (behind a PIN) once network access is turned on; it takes effect after a restart.
var networkSettingsPath = Path.Combine(dataDirectory, "network.json");
var networkSettings = NetworkSettingsStore.Load(networkSettingsPath);
if (networkSettings.Enabled && networkSettings.HasPin) builder.WebHost.UseUrls($"http://0.0.0.0:{networkSettings.Port}");
builder.Services.AddNetworkAccess(networkSettingsPath);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseNetworkAccess();
// API callers get raw status codes; re-executing /not-found would turn e.g. DELETE 404s into 405s.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(HomeInventory.Client._Imports).Assembly);

app.MapInventoryApi();
app.MapNetworkAccess();

app.Run();

public partial class Program;
