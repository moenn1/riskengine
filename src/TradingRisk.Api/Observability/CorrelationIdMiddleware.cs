namespace TradingRisk.Api.Observability;

/// <summary>
/// Propagates a bounded request correlation ID through the response and logging scope.
/// </summary>
public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var requestedId = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsSafe(requestedId)
            ? requestedId!
            : Guid.NewGuid().ToString("N");

        // TraceIdentifier is what ASP.NET and many loggers use as the request ID.
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await next(context);
        }
    }

    private static bool IsSafe(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');
}
