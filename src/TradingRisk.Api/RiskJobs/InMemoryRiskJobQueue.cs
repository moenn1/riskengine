using System.Collections.Concurrent;
using System.Threading.Channels;
using TradingRisk.Application.Risk;

namespace TradingRisk.Api.RiskJobs;

/// <summary>
/// A bounded in-process broker. It demonstrates backpressure and competing workers;
/// replace this adapter with Kafka/RabbitMQ/IBM MQ without changing the controller.
/// State is intentionally non-durable and disappears when the process stops.
/// </summary>
public sealed class InMemoryRiskJobBroker : IRiskJobBroker
{
    private readonly Channel<RiskJobCommand> _channel = Channel.CreateBounded<RiskJobCommand>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<Guid, RiskJobSnapshot> _jobs = new();

    public async ValueTask<Guid> EnqueueAsync(
        CalculatePortfolioRiskQuery query,
        CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid();
        var command = new RiskJobCommand(jobId, query);
        _jobs[jobId] = new RiskJobSnapshot(
            jobId, "queued", null, null, DateTimeOffset.UtcNow, null);
        await _channel.Writer.WriteAsync(command, cancellationToken);
        return jobId;
    }

    public RiskJobSnapshot? Find(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var snapshot) ? snapshot : null;

    public void MarkRunning(Guid jobId) => Update(jobId, snapshot => snapshot with
    {
        Status = "running"
    });

    public void MarkSucceeded(Guid jobId, RiskReportDto report) =>
        Update(jobId, snapshot => snapshot with
        {
            Status = "succeeded",
            Result = report,
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

    public void MarkFailed(Guid jobId, string detail) =>
        Update(jobId, snapshot => snapshot with
        {
            Status = "failed",
            Error = detail,
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

    public IAsyncEnumerable<RiskJobCommand> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    private void Update(Guid jobId, Func<RiskJobSnapshot, RiskJobSnapshot> update)
    {
        _jobs.AddOrUpdate(jobId, _ => throw new InvalidOperationException("Unknown risk job."),
            (_, snapshot) => update(snapshot));
    }
}
