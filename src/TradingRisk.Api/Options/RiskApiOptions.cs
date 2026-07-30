namespace TradingRisk.Api.Options;

/// <summary>
/// Strongly typed view of the RiskApi configuration section. Defaults support local
/// startup, while Program.cs validates effective values from every provider.
/// </summary>
public sealed class RiskApiOptions
{
    // Keeping the key beside the options type avoids repeating a magic string.
    public const string SectionName = "RiskApi";

    public decimal DefaultConfidenceLevel { get; init; } = 0.99m;

    public int MaxScenarioCount { get; init; } = 1_000;
}
