using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TradingRisk.Application.Common;
using TradingRisk.Domain.Common;

namespace TradingRisk.Api.ErrorHandling;

/// <summary>
/// Converts internal exceptions into the RFC 9457-style Problem Details HTTP contract.
/// </summary>
public sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Pattern matching keeps HTTP translation at the outer boundary. Application and
        // Domain exceptions contain no ASP.NET Core status-code dependency.
        var (status, title, detail) = exception switch
        {
            RequestValidationException or DomainValidationException => (
                StatusCodes.Status400BadRequest,
                "Request validation failed",
                exception.Message),
            PortfolioNotFoundException => (
                StatusCodes.Status404NotFound,
                "Portfolio not found",
                exception.Message),
            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred",
                // Never disclose stack traces or internal exception details to callers.
                "The server could not complete the request.")
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(
                logger,
                httpContext.TraceIdentifier,
                exception);
        }
        else
        {
            LogExpectedFailure(logger, status, exception.Message);
        }

        httpContext.Response.StatusCode = status;

        // Problem Details provides one predictable error envelope for API clients.
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path
            }
        });
    }

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Error,
        Message = "Unhandled exception for trace {TraceIdentifier}")]
    private static partial void LogUnhandledException(
        ILogger logger,
        string traceIdentifier,
        Exception exception);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "Request failed with status {StatusCode}: {ErrorMessage}")]
    private static partial void LogExpectedFailure(
        ILogger logger,
        int statusCode,
        string errorMessage);
}
