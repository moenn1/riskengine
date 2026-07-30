namespace TradingRisk.Domain.Common;

/// <summary>
/// Thrown when an operation would create an invalid domain object.
/// </summary>
public sealed class DomainValidationException(string message) : Exception(message);
