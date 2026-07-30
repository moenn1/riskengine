using TradingRisk.Domain.Common;

namespace TradingRisk.Domain.Portfolios;

/// <summary>
/// A linear position. Quantity may be negative for a short; price is expressed in the
/// portfolio's base currency.
/// </summary>
public sealed record Position
{
    private Position(InstrumentId instrumentId, decimal quantity, decimal price)
    {
        InstrumentId = instrumentId;
        Quantity = quantity;
        Price = price;
    }

    public InstrumentId InstrumentId { get; }

    public decimal Quantity { get; }

    public decimal Price { get; }

    // Signed quantity carries direction: long market value is positive; short is negative.
    public decimal MarketValue => Quantity * Price;

    /// <summary>
    /// The factory is the only construction path, so a created position satisfies its
    /// invariants. More product types would need product-specific price rules.
    /// </summary>
    public static Position Create(string instrumentId, decimal quantity, decimal price)
    {
        if (quantity == 0m)
        {
            throw new DomainValidationException("Position quantity cannot be zero.");
        }

        if (price < 0m)
        {
            throw new DomainValidationException("Position price cannot be negative.");
        }

        return new Position(InstrumentId.From(instrumentId), quantity, price);
    }
}
