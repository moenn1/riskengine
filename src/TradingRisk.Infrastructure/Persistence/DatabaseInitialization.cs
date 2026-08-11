using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingRisk.Application.Abstractions;

namespace TradingRisk.Infrastructure.Persistence;

public static partial class DatabaseInitialization
{
    /// <summary>
    /// Registers the scoped DbContext and maps Application ports to one scoped adapter.
    /// The relative SQLite path is anchored to the host content root for predictable CLI,
    /// Rider, published, and container behavior.
    /// </summary>
    public static IServiceCollection AddTradingRiskPersistence(
        this IServiceCollection services,
        string connectionString,
        string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var resolvedConnectionString = ResolveConnectionString(
            connectionString,
            contentRootPath);

        services.AddDbContext<RiskDbContext>(options =>
            options.UseSqlite(resolvedConnectionString));
        services.AddScoped<SqlitePortfolioRepository>();
        services.AddScoped<IPortfolioRepository>(provider =>
            provider.GetRequiredService<SqlitePortfolioRepository>());
        services.AddScoped<IPortfolioQueries>(provider =>
            provider.GetRequiredService<SqlitePortfolioRepository>());

        return services;
    }

    /// <summary>
    /// Applies checked-in migrations for this self-contained learning service. Production
    /// systems should normally run reviewed scripts or a migration bundle as a deployment step.
    /// </summary>
    public static async Task MigrateTradingRiskDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILogger<RiskDbContext>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<RiskDbContext>();
        var pendingMigrations = (await dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken))
            .ToArray();

        if (pendingMigrations.Length > 0)
        {
            LogApplyingMigrations(logger, pendingMigrations.Length);
        }

        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static string ResolveConnectionString(
        string connectionString,
        string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:RiskDatabase must define a SQLite Data Source.");
        }

        if (builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(builder.DataSource))
        {
            return builder.ConnectionString;
        }

        var fullPath = Path.GetFullPath(builder.DataSource, contentRootPath);
        var directory = Path.GetDirectoryName(fullPath);

        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        builder.DataSource = fullPath;
        return builder.ConnectionString;
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Applying {MigrationCount} database migration(s)")]
    private static partial void LogApplyingMigrations(
        ILogger logger,
        int migrationCount);
}
