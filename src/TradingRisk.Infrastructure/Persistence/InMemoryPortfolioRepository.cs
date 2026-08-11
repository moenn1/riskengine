using System.Collections.Concurrent;
using TradingRisk.Application.Abstractions;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Infrastructure.Persistence;

/// <summary>
/// A process-local working fake retained for focused Application tests.
/// Production DI selects SqlitePortfolioRepository; this adapter stays useful when a test
/// needs deterministic port behavior without exercising SQL translation or mapping.
/// </summary>
public sealed class InMemoryPortfolioRepository : IPortfolioRepository
{
    // The fake is safe even if a test/composition shares it concurrently. The concurrent
    // collection protects dictionary operations; immutable Portfolio objects protect values.
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

        // The port models real database I/O; this fake can complete synchronously.
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
