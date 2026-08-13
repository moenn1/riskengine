using TradingRisk.Application.Risk;

namespace TradingRisk.Api.RiskJobs;

public interface IRiskJobBroker
{
    ValueTask<Guid> EnqueueAsync(CalculatePortfolioRiskQuery query, CancellationToken cancellationToken);

    RiskJobSnapshot? Find(Guid jobId);

    void MarkRunning(Guid jobId);

    void MarkSucceeded(Guid jobId, RiskReportDto report);

    void MarkFailed(Guid jobId, string detail);

    IAsyncEnumerable<RiskJobCommand> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed record RiskJobCommand(Guid JobId, CalculatePortfolioRiskQuery Query);

public sealed record RiskJobSnapshot(
    Guid JobId,
    string Status,
    RiskReportDto? Result,
    string? Error,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset? CompletedAtUtc);
