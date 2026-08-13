using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TradingRisk.Api.Contracts;

namespace TradingRisk.Tests.Api;

public sealed class AuthenticationApiTests
{
    [Fact]
    public async Task ValidCredentialsReceiveTheServerAssignedRole()
    {
        using var factory = new SqliteWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/token",
            new DemoTokenRequest("risk-operator", "operator-learning"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<DemoTokenResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(token);
        Assert.Equal("risk-operator", token.UserName);
        Assert.Equal("risk-operator", token.Role);
        Assert.NotEmpty(token.AccessToken);
    }

    [Fact]
    public async Task InvalidPasswordHasGenericFailureAndDoesNotRevealRoleData()
    {
        using var factory = new SqliteWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/token",
            new DemoTokenRequest("risk-operator", "wrong-password"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal("Invalid credentials", problem.Title);
        Assert.DoesNotContain("risk-operator", problem.Detail);
    }
}
