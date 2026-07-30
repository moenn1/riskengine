using System.Collections.ObjectModel;
using TradingRisk.Domain.Common;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Domain.Risk;

/// <summary>
/// Observed one-period returns used as a historical shock.
/// A return of -0.02 means that the instrument fell by 2% in that period.
/// </summary>
public sealed class HistoricalScenario
{
    private readonly ReadOnlyDictionary<InstrumentId, decimal> _returns;

    private HistoricalScenario(
        DateOnly asOfDate,
        IDictionary<InstrumentId, decimal> returns)
    {
        AsOfDate = asOfDate;
        // The dictionary and read-only wrapper create an owned snapshot. A read-only
        // interface over the caller's original dictionary would still allow mutation.
        _returns = new ReadOnlyDictionary<InstrumentId, decimal>(
            new Dictionary<InstrumentId, decimal>(returns));
    }

    public DateOnly AsOfDate { get; }

    public IReadOnlyDictionary<InstrumentId, decimal> Returns => _returns;

    public static HistoricalScenario Create(
        DateOnly asOfDate,
        IEnumerable<KeyValuePair<InstrumentId, decimal>>? returns)
    {
        var returnMap = new Dictionary<InstrumentId, decimal>();

        foreach (var pair in returns ?? [])
        {
            // TryAdd detects duplicate normalized instrument keys in one pass.
            if (!returnMap.TryAdd(pair.Key, pair.Value))
            {
                throw new DomainValidationException(
                    $"Scenario {asOfDate:yyyy-MM-dd} contains duplicate instrument '{pair.Key}'.");
            }

            if (pair.Value < -1m)
            {
                throw new DomainValidationException(
                    $"Return for '{pair.Key}' cannot be below -100%.");
            }
        }

        if (returnMap.Count == 0)
        {
            throw new DomainValidationException(
                $"Scenario {asOfDate:yyyy-MM-dd} must contain at least one return.");
        }

        return new HistoricalScenario(asOfDate, returnMap);
    }

    public decimal ReturnFor(InstrumentId instrumentId)
    {
        if (_returns.TryGetValue(instrumentId, out var value))
        {
            return value;
        }

        // Missing does not mean zero. Failing avoids understating risk with invented data.
        throw new DomainValidationException(
            $"Scenario {AsOfDate:yyyy-MM-dd} has no return for instrument '{instrumentId}'.");
    }
}
