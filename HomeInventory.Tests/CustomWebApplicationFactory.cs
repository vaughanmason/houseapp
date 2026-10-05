using System.Net;
using HomeInventory;
using HomeInventory.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HomeInventory.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Header that makes a test request look as if it came from another device (see <see cref="RemoteIpStartupFilter"/>).</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    /// <summary>Per-factory upload folder so tests never touch the real %LOCALAPPDATA% files.</summary>
    public string FilesPath { get; } = Path.Combine(Path.GetTempPath(), "HomeInventoryTests", Guid.NewGuid().ToString("N"));

    /// <summary>Stand-in for Claude so tests never call the real API (off unless a test turns it on).</summary>
    public FakeInventoryAssistant Assistant { get; } = new();

    public CustomWebApplicationFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<InventoryDbContext>();
            services.AddSingleton(_connection);
            services.RemoveAll<FileStore>();
            services.AddSingleton(new FileStore(FilesPath));
            services.RemoveAll<NetworkSettingsStore>();
            services.AddSingleton(new NetworkSettingsStore(Path.Combine(FilesPath, "network.json")));
            services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
            services.RemoveAll<IInventoryAssistant>();
            services.AddSingleton<IInventoryAssistant>(Assistant);
            services.AddDbContext<InventoryDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(serviceProvider.GetRequiredService<SqliteConnection>());
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            if (Directory.Exists(FilesPath)) Directory.Delete(FilesPath, recursive: true);
        }
    }

    /// <summary>TestServer has no remote address; this lets tests simulate requests from other devices.</summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[RemoteIpHeader], out var ip)) context.Connection.RemoteIpAddress = ip;
                await nextMiddleware();
            });
            next(app);
        };
    }
}

/// <summary>Canned AI answers for tests; records what it was asked.</summary>
public sealed class FakeInventoryAssistant : IInventoryAssistant
{
    public bool IsConfigured { get; set; }
    public ReceiptReading? Receipt { get; set; }
    public AssetSuggestionDto? Suggestion { get; set; }
    public string? FailWith { get; set; }
    public string? LastMediaType { get; private set; }
    public string? LastCurrency { get; private set; }

    public Task<ReceiptReading> ReadReceiptAsync(byte[] content, string mediaType, CancellationToken cancellationToken)
    {
        LastMediaType = mediaType;
        return FailWith is null ? Task.FromResult(Receipt!) : throw new AssistantException(FailWith);
    }

    public Task<AssetSuggestionDto> IdentifyItemAsync(byte[] image, string currency, CancellationToken cancellationToken)
    {
        LastCurrency = currency;
        return FailWith is null ? Task.FromResult(Suggestion!) : throw new AssistantException(FailWith);
    }
}
