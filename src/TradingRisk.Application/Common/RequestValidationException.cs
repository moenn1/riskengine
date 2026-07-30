namespace TradingRisk.Application.Common;

/// <summary>
/// Indicates that input violates a use-case rule after it crosses the transport
/// boundary. It remains valid for HTTP, messaging, CLI, or tests.
/// </summary>
public sealed class RequestValidationException(string message) : Exception(message);
