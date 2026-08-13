using TradingRisk.Application.Portfolios;
using TradingRisk.Application.Risk;
using TradingRisk.Domain.Risk;
using TradingRisk.Infrastructure.Persistence;

namespace TradingRisk.Tests.Regression;

/// <summary>
/// A golden non-regression fixture: the inputs and approved metrics are intentionally
/// fixed so a release can reveal an accidental financial behavior change.
/// </summary>
public sealed class RiskCalculationRegressionTests
{
    [Fact]
    public async Task ApprovedLearningBookMetricsRemainStable()
    {
        var repository = new InMemoryPortfolioRepository();
        var create = new CreatePortfolioHandler(repository);
        var portfolio = await create.HandleAsync(
            new CreatePortfolioCommand(
                "Approved Learning Book",
                "USD",
                [
                    new CreatePositionInput("AAPL", 100m, 200m),
                    new CreatePositionInput("MSFT", 50m, 400m)
                ]),
            TestContext.Current.CancellationToken);
        var handler = new CalculatePortfolioRiskHandler(
            repository,
            new HistoricalSimulationRiskCalculator(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)));

        var report = await handler.HandleAsync(
            new CalculatePortfolioRiskQuery(
                portfolio.Id,
                0.80m,
                [
                    Scenario("2026-01-02", -0.04m, -0.02m),
                    Scenario("2026-01-05", -0.02m, 0.01m),
                    Scenario("2026-01-06", 0m, 0m),
                    Scenario("2026-01-07", 0.01m, 0.02m),
                    Scenario("2026-01-08", 0.03m, 0.02m)
                ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(200m, report.ValueAtRisk);
        Assert.Equal(1_200m, report.ExpectedShortfall);
        Assert.Equal(1_200m, report.WorstLoss);
        Assert.Equal(1_200m, report.ScenarioResults[0].Loss);
        Assert.Equal(200m, report.ScenarioResults[1].Loss);
        Assert.Equal(0m, report.ScenarioResults[2].Loss);
        Assert.Equal(-600m, report.ScenarioResults[3].Loss);
        Assert.Equal(-1_000m, report.ScenarioResults[4].Loss);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero), report.CalculatedAtUtc);
    }

    private static HistoricalScenarioInput Scenario(string date, decimal aapl, decimal msft) =>
        new(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), new Dictionary<string, decimal>
        {
            ["AAPL"] = aapl,
            ["MSFT"] = msft
        });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
