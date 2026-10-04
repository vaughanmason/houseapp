using Microsoft.EntityFrameworkCore;

namespace HomeInventory;

/// <summary>
/// Deletes uploaded files that no photo or document references (e.g. an upload whose form was abandoned).
/// Runs a few minutes after startup and then daily; files younger than <see cref="GracePeriod"/> are kept
/// so uploads still waiting to be attached, or restored from a ZIP but not yet confirmed, are never removed.
/// </summary>
public sealed partial class OrphanFileSweeper(IServiceScopeFactory scopes, FileStore store, ILogger<OrphanFileSweeper> logger) : BackgroundService
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);

    public static async Task<int> SweepAsync(InventoryDbContext db, FileStore store, DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        var candidates = store.KeysOlderThan(olderThanUtc).ToList();
        if (candidates.Count == 0) return 0;
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        referenced.UnionWith(await db.Documents.Select(x => x.StorageKey).ToListAsync(cancellationToken));
        referenced.UnionWith(await db.PropertyPhotos.Select(x => x.StorageKey).ToListAsync(cancellationToken));
        referenced.UnionWith(await db.RoomPhotos.Select(x => x.StorageKey).ToListAsync(cancellationToken));
        referenced.UnionWith(await db.FixturePhotos.Select(x => x.StorageKey).ToListAsync(cancellationToken));
        referenced.UnionWith(await db.AssetPhotos.Select(x => x.StorageKey).ToListAsync(cancellationToken));
        var orphans = candidates.Where(x => !referenced.Contains(x)).ToList();
        foreach (var key in orphans) store.Delete(key);
        return orphans.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                    var removed = await SweepAsync(db, store, DateTime.UtcNow - GracePeriod, stoppingToken);
                    if (removed > 0) LogRemoved(removed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogFailed(ex);
                }
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} unreferenced uploaded file(s).")]
    private partial void LogRemoved(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sweeping unreferenced uploaded files failed.")]
    private partial void LogFailed(Exception exception);
}
