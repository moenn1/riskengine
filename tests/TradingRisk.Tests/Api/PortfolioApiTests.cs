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
    public async Task RootServesBrowserWorkbenchAndItsStaticAssets()
    {
        DisableConfigurationReload();

        // This is an HTTP integration test of the ASP.NET Core static-file pipeline,
        // not a test that reads source files directly from disk.
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        using var rootResponse = await client.GetAsync("/", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
        Assert.Equal("text/html", rootResponse.Content.Headers.ContentType?.MediaType);
        var html = await rootResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("Risk Engine · Historical Simulation Lab", html);
        Assert.Contains("id=\"portfolio-form\"", html);

        using var styleResponse = await client.GetAsync(
            "/css/site.css",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, styleResponse.StatusCode);
        Assert.Equal("text/css", styleResponse.Content.Headers.ContentType?.MediaType);

        using var scriptResponse = await client.GetAsync(
            "/js/app.js",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);
        Assert.Equal(
            "text/javascript",
            scriptResponse.Content.Headers.ContentType?.MediaType);
        var script = await scriptResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("async function submitPortfolio", script);
        Assert.Contains("async function submitRisk", script);
    }

    [Fact]
    public async Task CreateThenCalculateRiskCompletesVerticalSlice()
    {
        DisableConfigurationReload();

        // This boots the real Program.cs and middleware with an in-process TestServer;
        // no public TCP port is opened.
        await using var factory = CreateFactory();
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

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    private static void DisableConfigurationReload()
    {
        // The managed Codex workspace does not permit the native file watcher used for
        // live appsettings reload. This switch affects only the isolated test process.
        Environment.SetEnvironmentVariable(
            "DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE",
            "false");
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
