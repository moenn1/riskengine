using System.ComponentModel.DataAnnotations;

namespace TradingRisk.Api.Contracts;

/// <summary>
/// JSON contract for creating a portfolio. These annotations validate the HTTP
/// shape; Domain factories independently enforce business invariants.
/// </summary>
public sealed record CreatePortfolioRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,
    [Required]
    [StringLength(3, MinimumLength = 3)]
    string BaseCurrency,
    [Required]
    [MinLength(1)]
    IReadOnlyList<CreatePositionRequest> Positions);

/// <summary>
/// Nested JSON contract. A signed quantity models long (positive) or short (negative).
/// </summary>
public sealed record CreatePositionRequest(
    [Required]
    [StringLength(32, MinimumLength = 1)]
    string InstrumentId,
    decimal Quantity,
    decimal Price);

/// <summary>
/// JSON contract for historical risk. A nullable confidence lets the controller
/// apply the configured default when the client omits it.
/// </summary>
public sealed record CalculateRiskRequest(
    [Range(
        typeof(decimal),
        "0.5",
        "0.9999",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true)]
    decimal? ConfidenceLevel,
    [Required]
    [MinLength(1)]
    IReadOnlyList<HistoricalScenarioRequest> Scenarios);

/// <summary>
/// One dated, coherent set of simple returns keyed by instrument ID.
/// </summary>
public sealed record HistoricalScenarioRequest(
    DateOnly AsOfDate,
    [Required]
    IReadOnlyDictionary<string, decimal> Returns);
