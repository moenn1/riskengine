using TradingRisk.Domain.Portfolios;
using TradingRisk.Infrastructure.Persistence.Entities;

namespace TradingRisk.Infrastructure.Persistence;

internal static class PortfolioEntityMapper
{
    public static PortfolioEntity ToEntity(Portfolio portfolio)
    {
        return new PortfolioEntity
        {
            Id = portfolio.Id.Value,
            Name = portfolio.Name,
            BaseCurrency = portfolio.BaseCurrency.Code,
            Positions = portfolio.Positions
                .Select(position => new PositionEntity
                {
                    PortfolioId = portfolio.Id.Value,
                    InstrumentId = position.InstrumentId.Value,
                    Quantity = position.Quantity,
                    Price = position.Price
                })
                .ToArray()
        };
    }

    public static Portfolio ToDomain(PortfolioEntity entity)
    {
        // Re-enter through Domain factories instead of hydrating invalid private state.
        // Corrupt or incompatible persisted data therefore fails loudly at this boundary.
        var positions = entity.Positions
            .OrderBy(position => position.Id)
            .Select(position => Position.Create(
                position.InstrumentId,
                position.Quantity,
                position.Price))
            .ToArray();

        return Portfolio.Create(
            entity.Name,
            entity.BaseCurrency,
            positions,
            new PortfolioId(entity.Id));
    }
}
