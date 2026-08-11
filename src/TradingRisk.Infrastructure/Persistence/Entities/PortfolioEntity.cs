namespace TradingRisk.Infrastructure.Persistence.Entities;

/// <summary>
/// Mutable persistence shape used only by EF Core. It is deliberately separate from the
/// immutable Domain aggregate so database mapping concerns do not weaken Domain invariants.
/// </summary>
internal sealed class PortfolioEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string BaseCurrency { get; set; } = string.Empty;

    public ICollection<PositionEntity> Positions { get; set; } = [];
}
