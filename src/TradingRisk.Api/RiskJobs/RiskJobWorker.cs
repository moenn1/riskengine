using TradingRisk.Application.Risk;

namespace TradingRisk.Api.RiskJobs;

public sealed partial class RiskJobWorker(
    IRiskJobBroker queue,
    IServiceScopeFactory scopeFactory,
    ILogger<RiskJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var command in queue.ReadAllAsync(stoppingToken))
        {
            queue.MarkRunning(command.JobId);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<CalculatePortfolioRiskHandler>();
                var report = await handler.HandleAsync(command.Query, stoppingToken);
                queue.MarkSucceeded(command.JobId, report);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                queue.MarkFailed(command.JobId, exception.Message);
                LogRiskJobFailed(logger, exception, command.JobId);
            }
        }
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Error,
        Message = "Risk job {JobId} failed")]
    private static partial void LogRiskJobFailed(
        ILogger logger, Exception exception, Guid jobId);
}
