using TradingRisk.Domain.Common;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Tests.Domain;

public sealed class PortfolioTests
{
    [Fact]
    public void CreateComputesNetValueAndGrossExposure()
    {
        var portfolio = Portfolio.Create(
            "Long/short book",
            "usd",
            [
                Position.Create("AAPL", 10m, 200m),
                Position.Create("MSFT", -5m, 400m)
            ]);

        Assert.Equal("USD", portfolio.BaseCurrency.Code);
        Assert.Equal(0m, portfolio.NetMarketValue);
        Assert.Equal(4_000m, portfolio.GrossExposure);
    }

    [Fact]
    public void CreateRejectsDuplicateInstruments()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => Portfolio.Create(
                "Duplicate book",
                "USD",
                [
                    Position.Create("AAPL", 10m, 200m),
                    Position.Create("aapl", 5m, 201m)
                ]));

        Assert.Contains("duplicate instrument 'AAPL'", exception.Message);
    }
}
