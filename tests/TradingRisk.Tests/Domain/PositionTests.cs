using TradingRisk.Domain.Common;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Tests.Domain;

public sealed class PositionTests
{
    [Fact]
    public void CreateComputesSignedMarketValue()
    {
        var longPosition = Position.Create("aapl", 10m, 200m);
        var shortPosition = Position.Create("msft", -5m, 400m);

        Assert.Equal("AAPL", longPosition.InstrumentId.Value);
        Assert.Equal(2_000m, longPosition.MarketValue);
        Assert.Equal(-2_000m, shortPosition.MarketValue);
    }

    [Fact]
    public void CreateRejectsZeroQuantity()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => Position.Create("AAPL", 0m, 200m));

        Assert.Equal("Position quantity cannot be zero.", exception.Message);
    }
}
