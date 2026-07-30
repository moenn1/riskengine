using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Domain.Risk;

/// <summary>
/// Monetary risk figures are all expressed in the portfolio's base currency.
/// </summary>
public sealed record RiskReport(
    PortfolioId PortfolioId,
    Currency Currency,
    decimal ConfidenceLevel,
    decimal NetMarketValue,
    decimal GrossExposure,
    decimal ValueAtRisk,
    decimal ExpectedShortfall,
    decimal WorstLoss,
    decimal DailyPnlVolatility,
    decimal AnnualizedPnlVolatility,
    IReadOnlyList<ScenarioResult> ScenarioResults);

/// <summary>
/// Positive P&amp;L is a gain; positive Loss is adverse. Loss is the negated P&amp;L.
/// </summary>
public sealed record ScenarioResult(DateOnly AsOfDate, decimal ProfitAndLoss, decimal Loss);
