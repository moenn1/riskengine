using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradingRisk.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` when no running host supplies DbContext options. Runtime code
/// obtains RiskDbContext from dependency injection instead.
/// </summary>
public sealed class RiskDbContextFactory : IDesignTimeDbContextFactory<RiskDbContext>
{
    public RiskDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RiskDbContext>()
            .UseSqlite("Data Source=App_Data/riskengine-design.db")
            .Options;

        return new RiskDbContext(options);
    }
}
