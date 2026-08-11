using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Application.Abstractions;

/// <summary>
/// Read-side port for database-backed search and reporting. Returning materialized
/// results rather than IQueryable keeps EF Core and query-provider behavior inside
/// Infrastructure.
/// </summary>
public interface IPortfolioQueries
{
    Task<PortfolioSearchResult> SearchAsync(
        PortfolioSearchCriteria criteria,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CurrencyPortfolioBreakdown>> GetCurrencyBreakdownAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Provider-neutral filters. Application normalizes user input before constructing this
/// value, so an adapter receives canonical currency and instrument identifiers.
/// </summary>
public sealed record PortfolioSearchCriteria(
    string? NameContains,
    string? BaseCurrency,
    string? InstrumentId,
    int? MinimumPositionCount,
    int Offset,
    int Limit);

public sealed record PortfolioSearchResult(
    IReadOnlyList<Portfolio> Portfolios,
    int TotalCount);

public sealed record CurrencyPortfolioBreakdown(
    string BaseCurrency,
    int PortfolioCount,
    int PositionCount);
