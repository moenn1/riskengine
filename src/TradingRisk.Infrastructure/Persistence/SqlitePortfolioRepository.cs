using Microsoft.EntityFrameworkCore;
using TradingRisk.Application.Abstractions;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Infrastructure.Persistence;

/// <summary>
/// EF Core adapter for both aggregate persistence and read-side queries. All IQueryable
/// composition remains here because it is meaningful only to the database provider.
/// </summary>
public sealed class SqlitePortfolioRepository(RiskDbContext dbContext)
    : IPortfolioRepository, IPortfolioQueries
{
    public async Task AddAsync(
        Portfolio portfolio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portfolio);

        // Add marks the complete graph Added. SaveChangesAsync opens a transaction for the
        // portfolio and all positions, executes INSERTs, then accepts tracked changes.
        dbContext.Portfolios.Add(PortfolioEntityMapper.ToEntity(portfolio));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Portfolio?> GetByIdAsync(
        PortfolioId portfolioId,
        CancellationToken cancellationToken)
    {
        // Read-only aggregates do not need change-tracking snapshots. Include expresses the
        // navigation needed to reconstruct the complete aggregate before leaving the adapter.
        var entity = await dbContext.Portfolios
            .AsNoTracking()
            .Include(portfolio => portfolio.Positions)
            .SingleOrDefaultAsync(
                portfolio => portfolio.Id == portfolioId.Value,
                cancellationToken);

        return entity is null ? null : PortfolioEntityMapper.ToDomain(entity);
    }

    public async Task<PortfolioSearchResult> SearchAsync(
        PortfolioSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // IQueryable builds an expression tree. Nothing is sent to SQLite until an async
        // terminal operator such as CountAsync or ToArrayAsync executes the query.
        IQueryable<Entities.PortfolioEntity> query = dbContext.Portfolios.AsNoTracking();

        if (criteria.NameContains is not null)
        {
            query = query.Where(portfolio =>
                portfolio.Name.Contains(criteria.NameContains));
        }

        if (criteria.BaseCurrency is not null)
        {
            query = query.Where(portfolio =>
                portfolio.BaseCurrency == criteria.BaseCurrency);
        }

        if (criteria.InstrumentId is not null)
        {
            // Any over a navigation becomes an SQL EXISTS subquery.
            query = query.Where(portfolio => portfolio.Positions.Any(position =>
                position.InstrumentId == criteria.InstrumentId));
        }

        if (criteria.MinimumPositionCount is not null)
        {
            // Collection Count becomes a correlated SQL COUNT rather than loading rows.
            query = query.Where(portfolio =>
                portfolio.Positions.Count >= criteria.MinimumPositionCount.Value);
        }

        // Count executes against the filtered set before pagination.
        var totalCount = await query.CountAsync(cancellationToken);

        var entities = await query
            // Relational results have no implicit order. ID makes equal-name ordering unique,
            // which prevents unstable offset pages.
            .OrderBy(portfolio => portfolio.Name)
            .ThenBy(portfolio => portfolio.Id)
            .Skip(criteria.Offset)
            .Take(criteria.Limit)
            .Include(portfolio => portfolio.Positions)
            .AsSplitQuery()
            .ToArrayAsync(cancellationToken);

        return new PortfolioSearchResult(
            entities.Select(PortfolioEntityMapper.ToDomain).ToArray(),
            totalCount);
    }

    public async Task<IReadOnlyList<CurrencyPortfolioBreakdown>>
        GetCurrencyBreakdownAsync(CancellationToken cancellationToken)
    {
        // Two simple grouped projections translate reliably across relational providers.
        // Only aggregate scalars cross the database boundary; no entities are materialized.
        var portfolioCounts = await dbContext.Portfolios
            .AsNoTracking()
            .GroupBy(portfolio => portfolio.BaseCurrency)
            .Select(group => new CurrencyCount(
                group.Key,
                group.Count()))
            .ToArrayAsync(cancellationToken);

        var positionCounts = await dbContext.Positions
            .AsNoTracking()
            .GroupBy(position => position.Portfolio.BaseCurrency)
            .Select(group => new CurrencyCount(group.Key, group.Count()))
            .ToDictionaryAsync(
                item => item.BaseCurrency,
                item => item.Count,
                cancellationToken);

        return portfolioCounts
            .OrderBy(item => item.BaseCurrency)
            .Select(item => new CurrencyPortfolioBreakdown(
                item.BaseCurrency,
                item.Count,
                positionCounts.GetValueOrDefault(item.BaseCurrency)))
            .ToArray();
    }

    private sealed record CurrencyCount(string BaseCurrency, int Count);
}
