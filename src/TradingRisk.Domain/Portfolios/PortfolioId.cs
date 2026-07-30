namespace TradingRisk.Domain.Portfolios;

/// <summary>
/// A strongly typed identifier prevents accidentally passing another Guid as a portfolio ID.
/// </summary>
public readonly record struct PortfolioId(Guid Value)
{
    /// <summary>
    /// Generates identity inside the domain instead of accepting an unrelated raw Guid.
    /// </summary>
    public static PortfolioId New()
    {
        return new PortfolioId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
