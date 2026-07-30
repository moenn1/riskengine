using TradingRisk.Domain.Common;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Domain.Risk;

/// <summary>
/// Replays historical percentage returns against today's linear position values.
///
/// This is a deliberately transparent learning implementation. It does not perform full
/// instrument repricing and therefore is not appropriate for options or other nonlinear products.
/// </summary>
public sealed class HistoricalSimulationRiskCalculator : IRiskCalculator
{
    private const double TradingDaysPerYear = 252d;

    public RiskReport Calculate(
        Portfolio portfolio,
        IReadOnlyCollection<HistoricalScenario> scenarios,
        decimal confidenceLevel)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(scenarios);

        if (confidenceLevel is <= 0m or >= 1m)
        {
            throw new DomainValidationException(
                "Confidence level must be greater than 0 and less than 1.");
        }

        if (scenarios.Count == 0)
        {
            throw new DomainValidationException(
                "At least one historical scenario is required.");
        }

        // Sorting makes report output deterministic even when callers provide an unordered
        // collection. ToArray evaluates the LINQ pipeline exactly once.
        var scenarioResults = scenarios
            .OrderBy(scenario => scenario.AsOfDate)
            .Select(scenario => Revalue(portfolio, scenario))
            .ToArray();

        // Loss = -P&L, so ascending order ends with the most adverse observation.
        var orderedLosses = scenarioResults
            .Select(result => result.Loss)
            .Order()
            .ToArray();

        // Nearest-rank empirical quantile: ceil(confidence * observation count).
        var valueAtRiskRank = Math.Max(
            1,
            (int)Math.Ceiling(confidenceLevel * orderedLosses.Length));
        var valueAtRisk = Math.Max(0m, orderedLosses[valueAtRiskRank - 1]);

        // Average the worst ceil((1-confidence) * N) losses.
        var tailObservationCount = Math.Max(
            1,
            (int)Math.Ceiling((1m - confidenceLevel) * orderedLosses.Length));
        // TakeLast selects the worst observations because orderedLosses is ascending.
        var expectedShortfall = Math.Max(
            0m,
            orderedLosses.TakeLast(tailObservationCount).Average());

        var dailyPnlVolatility = SampleStandardDeviation(
            scenarioResults.Select(result => result.ProfitAndLoss));
        var annualizedPnlVolatility =
            dailyPnlVolatility * (decimal)Math.Sqrt(TradingDaysPerYear);

        return new RiskReport(
            portfolio.Id,
            portfolio.BaseCurrency,
            confidenceLevel,
            portfolio.NetMarketValue,
            portfolio.GrossExposure,
            valueAtRisk,
            expectedShortfall,
            Math.Max(0m, orderedLosses[^1]),
            dailyPnlVolatility,
            annualizedPnlVolatility,
            scenarioResults);
    }

    private static ScenarioResult Revalue(
        Portfolio portfolio,
        HistoricalScenario scenario)
    {
        // Linear revaluation: today's signed market value multiplied by the scenario's
        // simple return. A short position naturally gains when its instrument falls.
        var profitAndLoss = portfolio.Positions.Sum(
            position => position.MarketValue * scenario.ReturnFor(position.InstrumentId));

        return new ScenarioResult(
            scenario.AsOfDate,
            profitAndLoss,
            -profitAndLoss);
    }

    private static decimal SampleStandardDeviation(IEnumerable<decimal> observations)
    {
        // Monetary inputs remain decimal. Math.Sqrt uses double, so the statistical
        // kernel converts explicitly and the public report converts back.
        var values = observations.Select(decimal.ToDouble).ToArray();

        if (values.Length < 2)
        {
            return 0m;
        }

        var mean = values.Average();
        var squaredDeviations = values.Sum(value => Math.Pow(value - mean, 2d));
        // N-1 is sample variance (Bessel's correction), not population variance.
        var variance = squaredDeviations / (values.Length - 1);

        return (decimal)Math.Sqrt(variance);
    }
}
