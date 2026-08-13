using TradingRisk.Application.Abstractions;
using TradingRisk.Application.Common;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Application.Portfolios;

/// <summary>
/// A deliberately small pricing/sensitivity read model. For a linear position,
/// delta is the change in value for a one-unit price move, so delta equals quantity.
/// This is the seam where a real product pricer would later replace the simple formula.
/// </summary>
public sealed class GetPortfolioAnalyticsHandler(IPortfolioRepository repository)
{
    public async Task<PortfolioAnalyticsDto> HandleAsync(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        if (portfolioId == Guid.Empty)
        {
            throw new RequestValidationException("Portfolio ID cannot be empty.");
        }

        var portfolio = await repository.GetByIdAsync(
            new PortfolioId(portfolioId), cancellationToken);
        if (portfolio is null)
        {
            throw new PortfolioNotFoundException(portfolioId);
        }

        return PortfolioAnalyticsDto.FromDomain(portfolio);
    }
}

public sealed record PortfolioAnalyticsDto(
    Guid PortfolioId,
    string BaseCurrency,
    decimal NetMarketValue,
    decimal GrossExposure,
    IReadOnlyList<PositionAnalyticsDto> Positions)
{
    public static PortfolioAnalyticsDto FromDomain(Portfolio portfolio) => new(
        portfolio.Id.Value,
        portfolio.BaseCurrency.Code,
        portfolio.NetMarketValue,
        portfolio.GrossExposure,
        portfolio.Positions.Select(position => new PositionAnalyticsDto(
            position.InstrumentId.Value,
            position.Quantity,
            position.Price,
            position.MarketValue,
            position.Quantity)).ToArray());
}

public sealed record PositionAnalyticsDto(
    string InstrumentId,
    decimal Quantity,
    decimal Price,
    decimal MarketValue,
    decimal Delta);
