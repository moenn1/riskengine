using System.ComponentModel.DataAnnotations;

namespace TradingRisk.Api.Contracts;

public sealed record SubmitRiskJobRequest(
    [Required] Guid PortfolioId,
    [Range(typeof(decimal), "0.5", "0.9999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    decimal? ConfidenceLevel,
    [Required, MinLength(1)] IReadOnlyList<HistoricalScenarioRequest> Scenarios);
