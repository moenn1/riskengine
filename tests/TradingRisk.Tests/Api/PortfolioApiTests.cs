using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TradingRisk.Api.Contracts;
using TradingRisk.Application.Portfolios;
using TradingRisk.Application.Risk;

namespace TradingRisk.Tests.Api;

public sealed class PortfolioApiTests
{
    [Fact]
    public async Task CreateThenCalculateRiskCompletesVerticalSlice()
    {
        // The managed Codex workspace does not permit the native file watcher used for
        // live appsettings reload. This switch affects only the isolated test process.
        Environment.SetEnvironmentVariable(
            "DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE",
            "false");

        // This boots the real Program.cs and middleware with an in-process TestServer;
        // no public TCP port is opened.
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        // PostAsJsonAsync exercises System.Text.Json request serialization and model binding.
        using var createResponse = await client.PostAsJsonAsync(
            "/api/v1/portfolios",
            new CreatePortfolioRequest(
                "API test book",
                "USD",
                [
                    new CreatePositionRequest("AAPL", 100m, 200m),
                    new CreatePositionRequest("MSFT", 50m, 400m)
                ]),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var portfolio = await createResponse.Content.ReadFromJsonAsync<PortfolioDto>(
            cancellationToken);
        Assert.NotNull(portfolio);

        // Reusing the returned ID also proves the singleton in-memory repository survives
        // across two HTTP request scopes in this application instance.
        using var riskResponse = await client.PostAsJsonAsync(
            $"/api/v1/portfolios/{portfolio.Id}/risk",
            new CalculateRiskRequest(
                0.80m,
                [
                    Scenario(new DateOnly(2026, 1, 2), -0.04m, -0.02m),
                    Scenario(new DateOnly(2026, 1, 5), -0.02m, 0.01m),
                    Scenario(new DateOnly(2026, 1, 6), 0m, 0m),
                    Scenario(new DateOnly(2026, 1, 7), 0.01m, 0.02m),
                    Scenario(new DateOnly(2026, 1, 8), 0.03m, 0.02m)
                ]),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, riskResponse.StatusCode);
        var report = await riskResponse.Content.ReadFromJsonAsync<RiskReportDto>(
            cancellationToken);
        Assert.NotNull(report);
        Assert.Equal(200m, report.ValueAtRisk);
        Assert.Equal(1_200m, report.ExpectedShortfall);
        Assert.Equal(5, report.ScenarioCount);
    }

    private static HistoricalScenarioRequest Scenario(
        DateOnly date,
        decimal aaplReturn,
        decimal msftReturn)
    {
        return new HistoricalScenarioRequest(
            date,
            new Dictionary<string, decimal>
            {
                ["AAPL"] = aaplReturn,
                ["MSFT"] = msftReturn
            });
    }
}
