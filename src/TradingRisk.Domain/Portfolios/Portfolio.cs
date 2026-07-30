using System.Collections.ObjectModel;
using TradingRisk.Domain.Common;

namespace TradingRisk.Domain.Portfolios;

/// <summary>
/// Aggregate root for the positions whose invariants must be kept consistent together.
/// This first milestone makes portfolios immutable, which also makes the in-memory store safe
/// to read concurrently.
/// </summary>
public sealed class Portfolio
{
    private readonly ReadOnlyCollection<Position> _positions;

    private Portfolio(
        PortfolioId id,
        string name,
        Currency baseCurrency,
        IReadOnlyList<Position> positions)
    {
        Id = id;
        Name = name;
        BaseCurrency = baseCurrency;
        // Copying prevents a caller from retaining a mutable list and changing this
        // aggregate behind its back. IReadOnlyList alone would not guarantee ownership.
        _positions = new ReadOnlyCollection<Position>(positions.ToArray());
    }

    public PortfolioId Id { get; }

    public string Name { get; }

    public Currency BaseCurrency { get; }

    public IReadOnlyList<Position> Positions => _positions;

    // Net preserves long/short signs; gross measures absolute exposure before netting.
    public decimal NetMarketValue => _positions.Sum(position => position.MarketValue);

    public decimal GrossExposure => _positions.Sum(position => Math.Abs(position.MarketValue));

    public static Portfolio Create(
        string? name,
        string? baseCurrency,
        IEnumerable<Position>? positions,
        PortfolioId? id = null)
    {
        var normalizedName = name?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new DomainValidationException("Portfolio name is required.");
        }

        if (normalizedName.Length > 100)
        {
            throw new DomainValidationException("Portfolio name cannot exceed 100 characters.");
        }

        // Materialize once because IEnumerable may be lazy, expensive, or stateful.
        var positionList = positions?.ToArray() ?? [];

        if (positionList.Length == 0)
        {
            throw new DomainValidationException("A portfolio must contain at least one position.");
        }

        // InstrumentId is normalized and has value equality, so "aapl" and "AAPL"
        // cannot silently become two positions in the same aggregate.
        var duplicateInstrument = positionList
            .GroupBy(position => position.InstrumentId)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateInstrument is not null)
        {
            throw new DomainValidationException(
                $"Portfolio contains duplicate instrument '{duplicateInstrument.Key}'.");
        }

        return new Portfolio(
            id ?? PortfolioId.New(),
            normalizedName,
            Currency.From(baseCurrency),
            positionList);
    }
}
