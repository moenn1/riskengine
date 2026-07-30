using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Domain.Risk;

/// <summary>
/// Strategy boundary for risk methodologies. Implementations must document horizon,
/// quantile, tail, sign, and valuation conventions rather than sharing only a name.
/// </summary>
public interface IRiskCalculator
{
    RiskReport Calculate(
        Portfolio portfolio,
        IReadOnlyCollection<HistoricalScenario> scenarios,
        decimal confidenceLevel);
}
