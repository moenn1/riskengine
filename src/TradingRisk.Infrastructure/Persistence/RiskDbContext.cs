using Microsoft.EntityFrameworkCore;
using TradingRisk.Infrastructure.Persistence.Entities;

namespace TradingRisk.Infrastructure.Persistence;

/// <summary>
/// One EF Core unit of work. AddDbContext registers it as scoped, so a normal HTTP request
/// receives one instance and disposes it at the end of the request.
/// </summary>
public sealed class RiskDbContext(DbContextOptions<RiskDbContext> options)
    : DbContext(options)
{
    internal DbSet<PortfolioEntity> Portfolios => Set<PortfolioEntity>();

    internal DbSet<PositionEntity> Positions => Set<PositionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Finds every IEntityTypeConfiguration<T> in Infrastructure. This is analogous
        // to keeping JPA mapping metadata together without annotations in Domain classes.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RiskDbContext).Assembly);
    }
}
