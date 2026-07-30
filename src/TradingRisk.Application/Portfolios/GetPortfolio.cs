using TradingRisk.Application.Abstractions;
using TradingRisk.Application.Common;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Application.Portfolios;

/// <summary>
/// Query handler that keeps not-found semantics in Application rather than returning
/// an ASP.NET Core result or status code.
/// </summary>
public sealed class GetPortfolioHandler(IPortfolioRepository repository)
{
    public async Task<PortfolioDto> HandleAsync(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        if (portfolioId == Guid.Empty)
        {
            throw new RequestValidationException("Portfolio ID cannot be empty.");
        }

        // Convert the transport-friendly Guid to the domain's strongly typed identifier.
        var portfolio = await repository.GetByIdAsync(
            new PortfolioId(portfolioId),
            cancellationToken);

        if (portfolio is null)
        {
            throw new PortfolioNotFoundException(portfolioId);
        }

        return PortfolioDto.FromDomain(portfolio);
    }
}
