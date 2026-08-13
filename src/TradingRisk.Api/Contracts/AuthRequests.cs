namespace TradingRisk.Api.Contracts;

public sealed record DemoTokenRequest(string UserName = "", string Password = "");

public sealed record DemoTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    string UserName,
    string Role);
