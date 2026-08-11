using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingRisk.Infrastructure.Persistence;

namespace TradingRisk.Tests.Api;

/// <summary>
/// Boots the real API against one isolated SQLite file per test host. A relational
/// provider catches SQL/mapping behavior that EF's non-relational in-memory provider cannot.
/// </summary>
internal sealed class SqliteWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"trading-risk-api-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var connectionString =
            $"Data Source={_databasePath};Foreign Keys=True;Pooling=False";
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:RiskDatabase"] = connectionString
                }));

        // Minimal-hosting application configuration is assembled very early. Replacing the
        // registered context options guarantees the test never falls back to the local file.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<RiskDbContext>();
            services.RemoveAll<DbContextOptions<RiskDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<RiskDbContext>>();
            services.AddDbContext<RiskDbContext>(options =>
                options.UseSqlite(connectionString));
        });
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing)
            {
                DeleteDatabaseFiles();
            }
        }
    }

    private void DeleteDatabaseFiles()
    {
        foreach (var path in new[]
                 {
                     _databasePath,
                     $"{_databasePath}-shm",
                     $"{_databasePath}-wal"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
