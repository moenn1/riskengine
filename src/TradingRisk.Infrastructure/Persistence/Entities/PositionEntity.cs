namespace TradingRisk.Infrastructure.Persistence.Entities;

internal sealed class PositionEntity
{
    public long Id { get; set; }

    public Guid PortfolioId { get; set; }

    public string InstrumentId { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal Price { get; set; }

    public PortfolioEntity Portfolio { get; set; } = null!;
}
