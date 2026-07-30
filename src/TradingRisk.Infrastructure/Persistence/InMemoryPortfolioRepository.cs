using System.Collections.Concurrent;
using TradingRisk.Application.Abstractions;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Infrastructure.Persistence;

/// <summary>
/// A process-local adapter used for the first learning milestone.
/// It deliberately satisfies the same port that a later EF Core adapter will implement.
/// Data disappears when the API process stops.
/// </summary>
public sealed class InMemoryPortfolioRepository : IPortfolioRepository
{
    // A singleton repository is called concurrently by requests. ConcurrentDictionary
    // protects dictionary operations, while immutable Portfolio objects protect values.
    private readonly ConcurrentDictionary<PortfolioId, Portfolio> _portfolios = new();

    public Task AddAsync(
        Portfolio portfolio,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(portfolio);

        if (!_portfolios.TryAdd(portfolio.Id, portfolio))
        {
            throw new InvalidOperationException(
                $"Portfolio '{portfolio.Id}' already exists.");
        }

        // The port is async for future I/O; this in-memory adapter has no asynchronous work.
        return Task.CompletedTask;
    }

    public Task<Portfolio?> GetByIdAsync(
        PortfolioId portfolioId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _portfolios.TryGetValue(portfolioId, out var portfolio);

        return Task.FromResult(portfolio);
    }
}
