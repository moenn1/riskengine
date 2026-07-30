using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Application.Abstractions;

/// <summary>
/// An application-layer port. Infrastructure supplies the storage adapter.
/// Async signatures and cancellation are chosen for the future database boundary;
/// the current in-memory implementation can complete synchronously.
/// </summary>
public interface IPortfolioRepository
{
    Task AddAsync(Portfolio portfolio, CancellationToken cancellationToken);

    Task<Portfolio?> GetByIdAsync(
        PortfolioId portfolioId,
        CancellationToken cancellationToken);
}
