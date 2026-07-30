using TradingRisk.Application.Abstractions;
using TradingRisk.Application.Common;
using TradingRisk.Domain.Portfolios;
using TradingRisk.Domain.Risk;

namespace TradingRisk.Application.Risk;

/// <summary>
/// Use-case input independent of JSON/MVC attributes.
/// </summary>
public sealed record CalculatePortfolioRiskQuery(
    Guid PortfolioId,
    decimal ConfidenceLevel,
    IReadOnlyCollection<HistoricalScenarioInput>? Scenarios);

/// <summary>
/// Application representation of one scenario before instrument IDs and return
/// invariants are converted into Domain value objects.
/// </summary>
public sealed record HistoricalScenarioInput(
    DateOnly AsOfDate,
    IReadOnlyDictionary<string, decimal>? Returns);

/// <summary>
/// Serializable use-case output. All monetary values use the named Currency.
/// </summary>
public sealed record RiskReportDto(
    Guid PortfolioId,
    string Currency,
    decimal ConfidenceLevel,
    int ScenarioCount,
    decimal NetMarketValue,
    decimal GrossExposure,
    decimal ValueAtRisk,
    decimal ExpectedShortfall,
    decimal WorstLoss,
    decimal DailyPnlVolatility,
    decimal AnnualizedPnlVolatility,
    DateTimeOffset CalculatedAtUtc,
    IReadOnlyList<ScenarioResultDto> ScenarioResults);

public sealed record ScenarioResultDto(
    DateOnly AsOfDate,
    decimal ProfitAndLoss,
    decimal Loss);

/// <summary>
/// Query handler that loads the aggregate, converts transport-neutral inputs into domain values,
/// invokes the domain service, and maps the result back to an application DTO.
/// </summary>
public sealed class CalculatePortfolioRiskHandler(
    IPortfolioRepository repository,
    IRiskCalculator riskCalculator,
    TimeProvider timeProvider)
{
    public async Task<RiskReportDto> HandleAsync(
        CalculatePortfolioRiskQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.PortfolioId == Guid.Empty)
        {
            throw new RequestValidationException("Portfolio ID cannot be empty.");
        }

        if (query.ConfidenceLevel is < 0.5m or >= 1m)
        {
            throw new RequestValidationException(
                "Confidence level must be at least 0.5 and less than 1.");
        }

        if (query.Scenarios is not { Count: > 0 })
        {
            throw new RequestValidationException(
                "At least one historical scenario is required.");
        }

        // Duplicate dates would accidentally give one historical day extra statistical
        // weight, so the use case rejects them before calculation.
        var duplicateDate = query.Scenarios
            .GroupBy(scenario => scenario.AsOfDate)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateDate is not null)
        {
            throw new RequestValidationException(
                $"Historical scenario date '{duplicateDate.Key:yyyy-MM-dd}' is duplicated.");
        }

        var portfolio = await repository.GetByIdAsync(
            new PortfolioId(query.PortfolioId),
            cancellationToken);

        if (portfolio is null)
        {
            throw new PortfolioNotFoundException(query.PortfolioId);
        }

        // Domain factories normalize IDs, validate return bounds, and take owned snapshots.
        var scenarios = query.Scenarios
            .Select(MapScenario)
            .ToArray();

        var report = riskCalculator.Calculate(
            portfolio,
            scenarios,
            query.ConfidenceLevel);

        // TimeProvider, rather than DateTimeOffset.UtcNow, makes calculation time
        // deterministic in tests and explicit as an external dependency.
        return new RiskReportDto(
            report.PortfolioId.Value,
            report.Currency.Code,
            report.ConfidenceLevel,
            report.ScenarioResults.Count,
            report.NetMarketValue,
            report.GrossExposure,
            report.ValueAtRisk,
            report.ExpectedShortfall,
            report.WorstLoss,
            report.DailyPnlVolatility,
            report.AnnualizedPnlVolatility,
            timeProvider.GetUtcNow(),
            report.ScenarioResults
                .Select(result => new ScenarioResultDto(
                    result.AsOfDate,
                    result.ProfitAndLoss,
                    result.Loss))
                .ToArray());
    }

    private static HistoricalScenario MapScenario(HistoricalScenarioInput input)
    {
        if (input.Returns is null)
        {
            throw new RequestValidationException(
                $"Returns are required for scenario '{input.AsOfDate:yyyy-MM-dd}'.");
        }

        var returns = input.Returns.Select(pair =>
            new KeyValuePair<InstrumentId, decimal>(
                InstrumentId.From(pair.Key),
                pair.Value));

        return HistoricalScenario.Create(input.AsOfDate, returns);
    }
}
