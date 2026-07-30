using TradingRisk.Domain.Common;
using TradingRisk.Domain.Portfolios;
using TradingRisk.Domain.Risk;

namespace TradingRisk.Tests.Domain;

public sealed class HistoricalSimulationRiskCalculatorTests
{
    private readonly HistoricalSimulationRiskCalculator _calculator = new();

    [Fact]
    public void CalculateReturnsKnownEmpiricalRiskMetrics()
    {
        var portfolio = Portfolio.Create(
            "Simple equity book",
            "USD",
            [Position.Create("ABC", 100m, 10m)]);
        var scenarios = CreateScenarios("ABC", -0.10m, -0.05m, 0m, 0.02m, 0.05m);

        var report = _calculator.Calculate(portfolio, scenarios, 0.80m);

        Assert.Equal(50m, report.ValueAtRisk);
        Assert.Equal(100m, report.ExpectedShortfall);
        Assert.Equal(100m, report.WorstLoss);
        // Volatility uses a double square root internally, so assert a documented tolerance
        // instead of brittle exact floating-point equality.
        Assert.InRange(report.DailyPnlVolatility, 59.41m, 59.42m);
        Assert.InRange(report.AnnualizedPnlVolatility, 943.09m, 943.20m);
        Assert.Equal(
            [-100m, -50m, 0m, 20m, 50m],
            report.ScenarioResults.Select(result => result.ProfitAndLoss));
    }

    [Fact]
    public void CalculateRejectsScenarioWithMissingInstrumentReturn()
    {
        var portfolio = Portfolio.Create(
            "Two names",
            "USD",
            [
                Position.Create("ABC", 100m, 10m),
                Position.Create("XYZ", 10m, 20m)
            ]);
        var scenarios = CreateScenarios("ABC", -0.10m);

        var exception = Assert.Throws<DomainValidationException>(
            () => _calculator.Calculate(portfolio, scenarios, 0.95m));

        Assert.Contains("no return for instrument 'XYZ'", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CalculateRejectsInvalidConfidenceLevel(double confidenceLevel)
    {
        var portfolio = Portfolio.Create(
            "Simple equity book",
            "USD",
            [Position.Create("ABC", 100m, 10m)]);
        var scenarios = CreateScenarios("ABC", -0.10m);

        Assert.Throws<DomainValidationException>(
            () => _calculator.Calculate(
                portfolio,
                scenarios,
                (decimal)confidenceLevel));
    }

    private static HistoricalScenario[] CreateScenarios(
        string instrumentId,
        params decimal[] returns)
    {
        var id = InstrumentId.From(instrumentId);

        // Select's index supplies deterministic consecutive dates for compact test data.
        return returns
            .Select((value, index) => HistoricalScenario.Create(
                new DateOnly(2026, 1, 1).AddDays(index),
                [new KeyValuePair<InstrumentId, decimal>(id, value)]))
            .ToArray();
    }
}
