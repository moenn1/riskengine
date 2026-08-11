using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Application.Abstractions;

/// <summary>
/// An application-layer port. Infrastructure supplies the storage adapter.
/// Async signatures and cancellation reflect that the production adapter performs I/O;
/// the in-memory test fake can still complete synchronously.
/// </summary>
public interface IPortfolioRepository
{
    Task AddAsync(Portfolio portfolio, CancellationToken cancellationToken);

    Task<Portfolio?> GetByIdAsync(
        PortfolioId portfolioId,
        CancellationToken cancellationToken);
}
