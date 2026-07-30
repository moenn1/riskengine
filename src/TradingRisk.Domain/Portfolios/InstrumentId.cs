using TradingRisk.Domain.Common;

namespace TradingRisk.Domain.Portfolios;

/// <summary>
/// Identifies both a position and the market-risk-factor return used to shock it.
/// </summary>
public readonly record struct InstrumentId
{
    private InstrumentId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static InstrumentId From(string? value)
    {
        // Normalize before validation so equality/dictionary keys are case-insensitive by
        // construction without relying on a comparer at every call site.
        var normalized = value?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DomainValidationException("Instrument ID is required.");
        }

        if (normalized.Length > 32)
        {
            throw new DomainValidationException("Instrument ID cannot exceed 32 characters.");
        }

        return new InstrumentId(normalized);
    }

    public override string ToString()
    {
        return Value;
    }
}
