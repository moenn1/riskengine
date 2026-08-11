using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TradingRisk.Application.Abstractions;
using TradingRisk.Domain.Portfolios;
using TradingRisk.Infrastructure.Persistence;

namespace TradingRisk.Tests.Infrastructure;

public sealed class SqlitePortfolioRepositoryTests
{
    [Fact]
    public async Task AddThenReadFromAnotherContextReconstructsDomainAggregate()
    {
        await using var database = await TemporarySqliteDatabase.CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var portfolio = Portfolio.Create(
            "Persistent book",
            "USD",
            [
                Position.Create("AAPL", 10m, 200m),
                Position.Create("MSFT", -5m, 400m)
            ]);

        await using (var writeContext = database.CreateContext())
        {
            var writer = new SqlitePortfolioRepository(writeContext);
            await writer.AddAsync(portfolio, cancellationToken);
        }

        await using var readContext = database.CreateContext();
        var reader = new SqlitePortfolioRepository(readContext);
        var reloaded = await reader.GetByIdAsync(portfolio.Id, cancellationToken);

        Assert.NotNull(reloaded);
        Assert.NotSame(portfolio, reloaded);
        Assert.Equal(portfolio.Id, reloaded.Id);
        Assert.Equal(0m, reloaded.NetMarketValue);
        Assert.Equal(4_000m, reloaded.GrossExposure);
        Assert.Equal(["AAPL", "MSFT"], reloaded.Positions
            .Select(position => position.InstrumentId.Value)
            .ToArray());
    }

    [Fact]
    public async Task SearchAndBreakdownTranslateFilteringPagingAndGrouping()
    {
        await using var database = await TemporarySqliteDatabase.CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var context = database.CreateContext();
        var repository = new SqlitePortfolioRepository(context);
        await repository.AddAsync(
            Book("Alpha book", "USD", ("AAPL", 1m), ("MSFT", 2m)),
            cancellationToken);
        await repository.AddAsync(
            Book("Euro book", "EUR", ("SAP", 3m)),
            cancellationToken);
        await repository.AddAsync(
            Book(
                "Gamma book",
                "USD",
                ("AAPL", 4m),
                ("NVDA", 5m),
                ("TSLA", 6m)),
            cancellationToken);

        var firstPage = await repository.SearchAsync(
            new PortfolioSearchCriteria(
                NameContains: "book",
                BaseCurrency: "USD",
                InstrumentId: "AAPL",
                MinimumPositionCount: 2,
                Offset: 0,
                Limit: 1),
            cancellationToken);
        var secondPage = await repository.SearchAsync(
            new PortfolioSearchCriteria(
                NameContains: "book",
                BaseCurrency: "USD",
                InstrumentId: "AAPL",
                MinimumPositionCount: 2,
                Offset: 1,
                Limit: 1),
            cancellationToken);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal("Alpha book", Assert.Single(firstPage.Portfolios).Name);
        Assert.Equal("Gamma book", Assert.Single(secondPage.Portfolios).Name);

        var breakdown = await repository.GetCurrencyBreakdownAsync(cancellationToken);

        Assert.Collection(
            breakdown,
            eur =>
            {
                Assert.Equal("EUR", eur.BaseCurrency);
                Assert.Equal(1, eur.PortfolioCount);
                Assert.Equal(1, eur.PositionCount);
            },
            usd =>
            {
                Assert.Equal("USD", usd.BaseCurrency);
                Assert.Equal(2, usd.PortfolioCount);
                Assert.Equal(5, usd.PositionCount);
            });
    }

    private static Portfolio Book(
        string name,
        string currency,
        params (string InstrumentId, decimal Quantity)[] positions)
    {
        return Portfolio.Create(
            name,
            currency,
            positions.Select(position => Position.Create(
                position.InstrumentId,
                position.Quantity,
                100m)));
    }

    private sealed class TemporarySqliteDatabase(string path) : IAsyncDisposable
    {
        private string ConnectionString =>
            $"Data Source={path};Foreign Keys=True;Pooling=False";

        public static async Task<TemporarySqliteDatabase> CreateAsync()
        {
            var database = new TemporarySqliteDatabase(Path.Combine(
                Path.GetTempPath(),
                $"trading-risk-repository-{Guid.NewGuid():N}.db"));
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            return database;
        }

        public RiskDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<RiskDbContext>()
                .UseSqlite(ConnectionString)
                .Options;

            return new RiskDbContext(options);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();

            foreach (var databaseFile in new[] { path, $"{path}-shm", $"{path}-wal" })
            {
                if (File.Exists(databaseFile))
                {
                    File.Delete(databaseFile);
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
