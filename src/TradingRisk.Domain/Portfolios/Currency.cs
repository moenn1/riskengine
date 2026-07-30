using TradingRisk.Domain.Common;

namespace TradingRisk.Domain.Portfolios;

/// <summary>
/// A minimal ISO-4217-style currency value object.
/// </summary>
public readonly record struct Currency
{
    private Currency(string code)
    {
        Code = code;
    }

    public string Code { get; }

    public static Currency From(string? code)
    {
        // Invariant casing makes behavior independent of the server's current culture.
        var normalized = code?.Trim().ToUpperInvariant();

        if (normalized is null ||
            normalized.Length != 3 ||
            normalized.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new DomainValidationException(
                "Base currency must be a three-letter code such as USD, EUR, or GBP.");
        }

        return new Currency(normalized);
    }

    public override string ToString()
    {
        return Code;
    }
}
