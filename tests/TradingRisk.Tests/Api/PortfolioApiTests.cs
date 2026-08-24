using System.Net;
using System.Net.Http.Json;
using TradingRisk.Api.Contracts;
using TradingRisk.Api.RiskJobs;
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
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        using var rootResponse = await client.GetAsync("/", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
        Assert.Equal("text/html", rootResponse.Content.Headers.ContentType?.MediaType);
        var html = await rootResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("Risk Engine · Historical Simulation Lab", html);
        Assert.Contains("id=\"portfolio-form\"", html);
        Assert.Contains("Portfolio overview", html);
        Assert.Contains("data-view-link=\"workbench\"", html);
        Assert.Contains("id=\"dashboard-table-body\"", html);

        using var loginResponse = await client.GetAsync("/login.html", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginHtml = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("id=\"login-form\"", loginHtml);
        Assert.Contains("id=\"login-password\"", loginHtml);

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

        // The health check opens the scoped EF Core context and verifies SQLite connectivity.
        using var healthResponse = await client.GetAsync("/health", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(
            healthResponse.Headers.GetValues("X-Correlation-ID").Single()));

        using var livenessResponse = await client.GetAsync("/health/live", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, livenessResponse.StatusCode);

        using var readinessResponse = await client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, readinessResponse.StatusCode);
    }

    [Fact]
    public async Task CorrelationIdIsPreservedAndReturnedToTheCaller()
    {
        DisableConfigurationReload();
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-ID", "risk-test-2026-01");

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "risk-test-2026-01",
            response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task CreateThenCalculateRiskCompletesVerticalSlice()
    {
        DisableConfigurationReload();

        // This boots the real Program.cs and middleware with an in-process TestServer;
        // no public TCP port is opened.
        using var factory = CreateFactory();
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

        // Reusing the returned ID across request scopes proves the first request committed
        // the aggregate and a new scoped DbContext can reconstruct it from SQLite.
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

        using var analyticsResponse = await client.GetAsync(
            $"/api/v1/portfolios/{portfolio.Id}/analytics",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, analyticsResponse.StatusCode);
        var analytics = await analyticsResponse.Content
            .ReadFromJsonAsync<PortfolioAnalyticsDto>(cancellationToken);
        Assert.NotNull(analytics);
        Assert.Equal(100m, Assert.Single(analytics.Positions, p => p.InstrumentId == "AAPL").Delta);
    }

    [Fact]
    public async Task SearchAndStatisticsExecuteDatabaseReadModels()
    {
        DisableConfigurationReload();
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        await CreatePortfolioAsync(
            client,
            new CreatePortfolioRequest(
                "Alpha book",
                "USD",
                [
                    new CreatePositionRequest("AAPL", 10m, 200m),
                    new CreatePositionRequest("MSFT", 5m, 400m)
                ]),
            cancellationToken);
        await CreatePortfolioAsync(
            client,
            new CreatePortfolioRequest(
                "Euro book",
                "EUR",
                [new CreatePositionRequest("SAP", 20m, 150m)]),
            cancellationToken);

        using var searchResponse = await client.GetAsync(
            "/api/v1/portfolios?baseCurrency=usd&instrumentId=aapl" +
            "&minimumPositionCount=2&page=1&pageSize=10",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var page = await searchResponse.Content
            .ReadFromJsonAsync<PortfolioSearchPageDto>(cancellationToken);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Alpha book", Assert.Single(page.Items).Name);

        using var statisticsResponse = await client.GetAsync(
            "/api/v1/portfolios/statistics/by-currency",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, statisticsResponse.StatusCode);
        var statistics = await statisticsResponse.Content
            .ReadFromJsonAsync<PortfolioStatisticsDto>(cancellationToken);
        Assert.NotNull(statistics);
        Assert.Equal(2, statistics.PortfolioCount);
        Assert.Equal(3, statistics.PositionCount);
        Assert.Equal(["EUR", "USD"], statistics.ByCurrency
            .Select(item => item.BaseCurrency)
            .ToArray());
    }

    [Fact]
    public async Task QueuedRiskJobIsAcceptedAndCompletedByBackgroundWorker()
    {
        DisableConfigurationReload();
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var portfolio = await CreatePortfolioReturningAsync(client, new CreatePortfolioRequest(
            "Queued book", "USD", [new CreatePositionRequest("AAPL", 10m, 200m)]), cancellationToken);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/risk-jobs",
            new SubmitRiskJobRequest(
                portfolio.Id,
                0.80m,
                [new HistoricalScenarioRequest(
                    new DateOnly(2026, 1, 2), new Dictionary<string, decimal> { ["AAPL"] = -0.1m })]),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<RiskJobSnapshot>(cancellationToken);
        Assert.NotNull(accepted);

        RiskJobSnapshot? completed = null;
        for (var attempt = 0; attempt < 20 && completed?.Status != "succeeded"; attempt++)
        {
            using var statusResponse = await client.GetAsync(
                $"/api/v1/risk-jobs/{accepted.JobId}", cancellationToken);
            completed = await statusResponse.Content.ReadFromJsonAsync<RiskJobSnapshot>(cancellationToken);
            if (completed?.Status != "succeeded") await Task.Delay(25, cancellationToken);
        }

        Assert.NotNull(completed);
        Assert.Equal("succeeded", completed.Status);
        Assert.NotNull(completed.Result);
    }

    private static SqliteWebApplicationFactory CreateFactory()
    {
        return new SqliteWebApplicationFactory();
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

    private static async Task CreatePortfolioAsync(
        HttpClient client,
        CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/portfolios",
            request,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<PortfolioDto> CreatePortfolioReturningAsync(
        HttpClient client,
        CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/portfolios", request, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PortfolioDto>(cancellationToken))!;
    }
}
