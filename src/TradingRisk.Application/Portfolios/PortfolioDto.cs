using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Application.Portfolios;

/// <summary>
/// Application read model. Mapping is explicit so Domain is not also forced to be the
/// HTTP or persistence serialization shape.
/// </summary>
public sealed record PortfolioDto(
    Guid Id,
    string Name,
    string BaseCurrency,
    decimal NetMarketValue,
    decimal GrossExposure,
    IReadOnlyList<PositionDto> Positions)
{
    public static PortfolioDto FromDomain(Portfolio portfolio)
    {
        // The returned array is a stable snapshot and cannot expose Domain's collection.
        return new PortfolioDto(
            portfolio.Id.Value,
            portfolio.Name,
            portfolio.BaseCurrency.Code,
            portfolio.NetMarketValue,
            portfolio.GrossExposure,
            portfolio.Positions
                .Select(position => new PositionDto(
                    position.InstrumentId.Value,
                    position.Quantity,
                    position.Price,
                    position.MarketValue))
                .ToArray());
    }
}

public sealed record PositionDto(
    string InstrumentId,
    decimal Quantity,
    decimal Price,
    decimal MarketValue);
