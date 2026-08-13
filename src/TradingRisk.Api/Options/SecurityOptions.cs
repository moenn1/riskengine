namespace TradingRisk.Api.Options;

/// <summary>
/// JWT validation settings. In production the signing key belongs in a secret store or,
/// preferably, validation points at an OIDC provider's public keys instead.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public string Issuer { get; init; } = "riskengine-learning";

    public string Audience { get; init; } = "riskengine-api";

    // Deliberately a development-only sample value; never commit a real secret.
    public string DemoSigningKey { get; init; } = "development-only-change-me-32-bytes-long";

    public int MaxFailedAttempts { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 5;

    public IReadOnlyList<DemoUserOptions> DemoUsers { get; init; } = [];
}

public sealed class DemoUserOptions
{
    public string UserName { get; init; } = "";

    public string Role { get; init; } = "risk-reader";

    // PBKDF2-SHA256$iterations$base64Salt$base64Hash. Never store plaintext here.
    public string PasswordHash { get; init; } = "";
}
