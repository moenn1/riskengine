namespace TradingRisk.Api.Contracts;

public sealed record DemoTokenRequest(string UserName = "learner", string Role = "risk-reader");

public sealed record DemoTokenResponse(string AccessToken, string TokenType, int ExpiresInSeconds);
