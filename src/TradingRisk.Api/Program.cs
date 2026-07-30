using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using TradingRisk.Api.ErrorHandling;
using TradingRisk.Api.Options;
using TradingRisk.Application.Abstractions;
using TradingRisk.Application.Portfolios;
using TradingRisk.Application.Risk;
using TradingRisk.Domain.Risk;
using TradingRisk.Infrastructure.Persistence;

// Top-level statements are compiled into a generated Main method. Execution begins here,
// just as it begins in Java's public static void main(String[] args).
var builder = WebApplication.CreateBuilder(args);

// Spring equivalents:
// AddControllers -> Spring MVC / @RestController infrastructure
// AddScoped/AddSingleton -> @Bean scopes and constructor injection
// AddOptions -> @ConfigurationProperties
// Registration describes what the service provider can create; it does not yet build it.
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services
    .AddOptions<RiskApiOptions>()
    // Configuration providers expose hierarchical keys; this binds the RiskApi section
    // to a typed object instead of scattering string lookups throughout controllers.
    .Bind(builder.Configuration.GetSection(RiskApiOptions.SectionName))
    .Validate(
        options => options.DefaultConfidenceLevel is >= 0.5m and < 1m,
        "RiskApi:DefaultConfidenceLevel must be at least 0.5 and less than 1.")
    .Validate(
        options => options.MaxScenarioCount is > 0 and <= 10_000,
        "RiskApi:MaxScenarioCount must be between 1 and 10,000.")
    .ValidateOnStart();

// This named policy is attached only to controller endpoints below. A fixed window is
// intentionally simple; a real distributed quota needs an identity/partition strategy.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("api", limiter =>
    {
        limiter.PermitLimit = 100;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// The in-memory repository must be a singleton so data survives across HTTP requests.
// Handlers are scoped, matching the usual lifetime of a web request.
builder.Services.AddSingleton<IPortfolioRepository, InMemoryPortfolioRepository>();
builder.Services.AddSingleton<IRiskCalculator, HistoricalSimulationRiskCalculator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreatePortfolioHandler>();
builder.Services.AddScoped<GetPortfolioHandler>();
builder.Services.AddScoped<CalculatePortfolioRiskHandler>();

// Build creates the root service provider and WebApplication. app.Run below, not Build,
// starts Kestrel and waits for shutdown.
var app = builder.Build();

// Middleware runs in registration order on the request and unwinds in reverse order for
// the response. Error handling is early so it can catch failures from later components.
app.UseExceptionHandler();

// TestServer is already an in-process transport and has no TLS listener to redirect to.
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    // This maps an OpenAPI JSON document, not an interactive Swagger UI.
    app.MapOpenApi();
}

// Endpoint mappings are terminal destinations selected by routing.
app.MapHealthChecks("/health");
app.MapControllers().RequireRateLimiting("api");

// Starts the host/server and normally blocks until the process receives a shutdown signal.
app.Run();

// WebApplicationFactory uses this visible entry point for in-process API tests.
public partial class Program;
