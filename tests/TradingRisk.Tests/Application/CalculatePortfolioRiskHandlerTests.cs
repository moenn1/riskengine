using TradingRisk.Application.Portfolios;
using TradingRisk.Application.Risk;
using TradingRisk.Domain.Risk;
using TradingRisk.Infrastructure.Persistence;

namespace TradingRisk.Tests.Application;

public sealed class CalculatePortfolioRiskHandlerTests
{
    [Fact]
    public async Task HandleAsyncLoadsPortfolioAndAddsCalculationTime()
    {
        // Manual construction is dependency injection without the framework container.
        // The real fake repository and calculator keep this focused component test simple.
        var repository = new InMemoryPortfolioRepository();
        var createHandler = new CreatePortfolioHandler(repository);
        var now = new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);
        var riskHandler = new CalculatePortfolioRiskHandler(
            repository,
            new HistoricalSimulationRiskCalculator(),
            new FixedTimeProvider(now));
        var portfolio = await createHandler.HandleAsync(
            new CreatePortfolioCommand(
                "Learning book",
                "USD",
                [new CreatePositionInput("ABC", 100m, 10m)]),
            TestContext.Current.CancellationToken);

        var report = await riskHandler.HandleAsync(
            new CalculatePortfolioRiskQuery(
                portfolio.Id,
                0.95m,
                [
                    new HistoricalScenarioInput(
                        new DateOnly(2026, 1, 1),
                        new Dictionary<string, decimal> { ["ABC"] = -0.10m })
                ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(portfolio.Id, report.PortfolioId);
        Assert.Equal(now, report.CalculatedAtUtc);
        Assert.Equal(100m, report.ValueAtRisk);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        // Controlling time avoids a flaky assertion around the machine's wall clock.
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
