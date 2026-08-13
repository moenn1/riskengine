using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TradingRisk.Api.Contracts;
using TradingRisk.Api.Options;
using TradingRisk.Api.RiskJobs;
using TradingRisk.Application.Common;
using TradingRisk.Application.Risk;

namespace TradingRisk.Api.Controllers;

[ApiController]
[Authorize(Policy = "RiskReader")]
[Route("api/v1/risk-jobs")]
public sealed class RiskJobsController(
    IRiskJobBroker queue,
    IOptions<RiskApiOptions> options) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<RiskJobSnapshot>> SubmitAsync(
        SubmitRiskJobRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Scenarios.Count > options.Value.MaxScenarioCount)
        {
            throw new RequestValidationException(
                $"A request cannot contain more than {options.Value.MaxScenarioCount} scenarios.");
        }

        var query = new CalculatePortfolioRiskQuery(
            request.PortfolioId,
            request.ConfidenceLevel ?? options.Value.DefaultConfidenceLevel,
            request.Scenarios.Select(scenario => new HistoricalScenarioInput(
                scenario.AsOfDate, scenario.Returns)).ToArray());
        var jobId = await queue.EnqueueAsync(query, cancellationToken);
        return AcceptedAtRoute("GetRiskJob", new { jobId }, queue.Find(jobId));
    }

    [HttpGet("{jobId:guid}", Name = "GetRiskJob")]
    public ActionResult<RiskJobSnapshot> Get(Guid jobId)
    {
        var job = queue.Find(jobId);
        return job is null ? NotFound() : Ok(job);
    }
}
