using TradingRisk.Application.Abstractions;
using TradingRisk.Application.Common;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Application.Portfolios;

public sealed record SearchPortfoliosQuery(
    string? Name,
    string? BaseCurrency,
    string? InstrumentId,
    int? MinimumPositionCount,
    int Page,
    int PageSize);

public sealed record PortfolioSearchPageDto(
    IReadOnlyList<PortfolioListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record PortfolioListItemDto(
    Guid Id,
    string Name,
    string BaseCurrency,
    int PositionCount,
    decimal NetMarketValue,
    decimal GrossExposure);

public sealed record PortfolioStatisticsDto(
    int PortfolioCount,
    int PositionCount,
    IReadOnlyList<CurrencyPortfolioBreakdownDto> ByCurrency);

public sealed record CurrencyPortfolioBreakdownDto(
    string BaseCurrency,
    int PortfolioCount,
    int PositionCount);

/// <summary>
/// Validates and normalizes search input, then delegates query translation to the
/// Infrastructure adapter. Application never exposes IQueryable to its callers.
/// </summary>
public sealed class SearchPortfoliosHandler(IPortfolioQueries queries)
{
    public async Task<PortfolioSearchPageDto> HandleAsync(
        SearchPortfoliosQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Page is < 1 or > 100_000)
        {
            throw new RequestValidationException("Page must be between 1 and 100,000.");
        }

        if (query.PageSize is < 1 or > 100)
        {
            throw new RequestValidationException("Page size must be between 1 and 100.");
        }

        if (query.MinimumPositionCount is < 1 or > 10_000)
        {
            throw new RequestValidationException(
                "Minimum position count must be between 1 and 10,000 when supplied.");
        }

        var normalizedName = NormalizeOptionalText(query.Name);
        var normalizedCurrency = query.BaseCurrency is null
            ? null
            : Currency.From(query.BaseCurrency).Code;
        var normalizedInstrument = query.InstrumentId is null
            ? null
            : InstrumentId.From(query.InstrumentId).Value;
        var offset = checked((query.Page - 1) * query.PageSize);

        var result = await queries.SearchAsync(
            new PortfolioSearchCriteria(
                normalizedName,
                normalizedCurrency,
                normalizedInstrument,
                query.MinimumPositionCount,
                offset,
                query.PageSize),
            cancellationToken);

        var totalPages = result.TotalCount == 0
            ? 0
            : (result.TotalCount + query.PageSize - 1) / query.PageSize;

        return new PortfolioSearchPageDto(
            result.Portfolios.Select(MapListItem).ToArray(),
            query.Page,
            query.PageSize,
            result.TotalCount,
            totalPages);
    }

    private static PortfolioListItemDto MapListItem(Portfolio portfolio)
    {
        return new PortfolioListItemDto(
            portfolio.Id.Value,
            portfolio.Name,
            portfolio.BaseCurrency.Code,
            portfolio.Positions.Count,
            portfolio.NetMarketValue,
            portfolio.GrossExposure);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

/// <summary>
/// Maps a grouped database projection into an API-friendly read model.
/// </summary>
public sealed class GetPortfolioStatisticsHandler(IPortfolioQueries queries)
{
    public async Task<PortfolioStatisticsDto> HandleAsync(
        CancellationToken cancellationToken)
    {
        var breakdown = await queries.GetCurrencyBreakdownAsync(cancellationToken);

        return new PortfolioStatisticsDto(
            breakdown.Sum(item => item.PortfolioCount),
            breakdown.Sum(item => item.PositionCount),
            breakdown
                .Select(item => new CurrencyPortfolioBreakdownDto(
                    item.BaseCurrency,
                    item.PortfolioCount,
                    item.PositionCount))
                .ToArray());
    }
}
