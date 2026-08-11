using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TradingRisk.Api.Contracts;
using TradingRisk.Api.Options;
using TradingRisk.Application.Common;
using TradingRisk.Application.Portfolios;
using TradingRisk.Application.Risk;

namespace TradingRisk.Api.Controllers;

/// <summary>
/// Thin HTTP adapter. Business decisions belong in application handlers or the domain model.
/// </summary>
[ApiController]
[Route("api/v1/portfolios")]
public sealed partial class PortfoliosController(
    CreatePortfolioHandler createPortfolio,
    GetPortfolioHandler getPortfolio,
    SearchPortfoliosHandler searchPortfolios,
    GetPortfolioStatisticsHandler getPortfolioStatistics,
    CalculatePortfolioRiskHandler calculateRisk,
    IOptions<RiskApiOptions> options,
    ILogger<PortfoliosController> logger) : ControllerBase
{
    // A named route lets CreatedAtRoute generate the Location header without duplicating a URL.
    private const string GetPortfolioRouteName = "GetPortfolioById";

    [HttpGet]
    [ProducesResponseType<PortfolioSearchPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortfolioSearchPageDto>> SearchAsync(
        [FromQuery] PortfolioSearchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await searchPortfolios.HandleAsync(
            new SearchPortfoliosQuery(
                request.Name,
                request.BaseCurrency,
                request.InstrumentId,
                request.MinimumPositionCount,
                request.Page,
                request.PageSize),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("statistics/by-currency")]
    [ProducesResponseType<PortfolioStatisticsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PortfolioStatisticsDto>> GetStatisticsAsync(
        CancellationToken cancellationToken)
    {
        return Ok(await getPortfolioStatistics.HandleAsync(cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<PortfolioDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortfolioDto>> CreateAsync(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        // API contracts do not cross inward unchanged. Explicit mapping keeps HTTP
        // validation/versioning concerns separate from the Application message.
        var command = new CreatePortfolioCommand(
            request.Name,
            request.BaseCurrency,
            request.Positions
                .Select(position => new CreatePositionInput(
                    position.InstrumentId,
                    position.Quantity,
                    position.Price))
                .ToArray());

        PortfolioDto result = await createPortfolio.HandleAsync(command, cancellationToken);

        LogPortfolioCreated(logger, result.Id, result.Positions.Count);

        // HTTP 201 includes both the representation and a Location for its GET endpoint.
        return CreatedAtRoute(
            GetPortfolioRouteName,
            new { portfolioId = result.Id },
            result);
    }

    [HttpGet("{portfolioId:guid}", Name = GetPortfolioRouteName)]
    [ProducesResponseType<PortfolioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioDto>> GetAsync(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        return Ok(await getPortfolio.HandleAsync(portfolioId, cancellationToken));
    }

    [HttpPost("{portfolioId:guid}/risk")]
    [ProducesResponseType<RiskReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskReportDto>> CalculateRiskAsync(
        Guid portfolioId,
        CalculateRiskRequest request,
        CancellationToken cancellationToken)
    {
        // This is an HTTP resource-protection limit. Domain validation still protects
        // finance invariants when the handler is invoked by a non-HTTP adapter.
        if (request.Scenarios.Count > options.Value.MaxScenarioCount)
        {
            throw new RequestValidationException(
                $"A request cannot contain more than {options.Value.MaxScenarioCount} scenarios.");
        }

        // The scope enriches logs produced during this calculation with PortfolioId.
        using var logScope = RiskCalculationScope(logger, portfolioId);

        var query = new CalculatePortfolioRiskQuery(
            portfolioId,
            request.ConfidenceLevel ?? options.Value.DefaultConfidenceLevel,
            request.Scenarios
                .Select(scenario => new HistoricalScenarioInput(
                    scenario.AsOfDate,
                    scenario.Returns))
                .ToArray());

        var result = await calculateRisk.HandleAsync(query, cancellationToken);

        LogRiskCalculated(
            logger,
            result.ConfidenceLevel,
            result.ScenarioCount);

        return Ok(result);
    }

    // DefineScope and LoggerMessage keep structured property names stable and avoid
    // reparsing/interpolating templates on every request.
    private static readonly Func<ILogger, Guid, IDisposable?> RiskCalculationScope =
        LoggerMessage.DefineScope<Guid>(
            "Risk calculation for portfolio {PortfolioId}");

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Created portfolio {PortfolioId} with {PositionCount} positions")]
    private static partial void LogPortfolioCreated(
        ILogger logger,
        Guid portfolioId,
        int positionCount);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Calculated {ConfidenceLevel:P2} VaR from {ScenarioCount} scenarios")]
    private static partial void LogRiskCalculated(
        ILogger logger,
        decimal confidenceLevel,
        int scenarioCount);
}
