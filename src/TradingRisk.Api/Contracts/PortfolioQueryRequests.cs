using System.ComponentModel.DataAnnotations;

namespace TradingRisk.Api.Contracts;

/// <summary>
/// Query-string contract for the LINQ/EF Core search demonstration.
/// Defaults make GET /api/v1/portfolios return the first page without filters.
/// </summary>
public sealed record PortfolioSearchRequest
{
    [StringLength(100)]
    public string? Name { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string? BaseCurrency { get; init; }

    [StringLength(32, MinimumLength = 1)]
    public string? InstrumentId { get; init; }

    [Range(1, 10_000)]
    public int? MinimumPositionCount { get; init; }

    [Range(1, 100_000)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
