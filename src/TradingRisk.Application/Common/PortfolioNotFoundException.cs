namespace TradingRisk.Application.Common;

/// <summary>
/// Use-case failure with no HTTP dependency. The API adapter decides that this maps
/// to 404; another adapter could represent it differently.
/// </summary>
public sealed class PortfolioNotFoundException(Guid portfolioId)
    : Exception($"Portfolio '{portfolioId}' was not found.");
