using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.Data;

public static class DatabaseInitializer
{
    /// <summary>Applies pending migrations and switches SQLite to WAL. Runs before the supervisor starts.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IOptions<GameWardenOptions>>().Value;
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);
        var factory = services.GetRequiredService<IDbContextFactory<GameWardenDb>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
    }
}
