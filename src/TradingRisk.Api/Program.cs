using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TradingRisk.Api.ErrorHandling;
using TradingRisk.Api.Options;
using TradingRisk.Application.Portfolios;
using TradingRisk.Application.Risk;
using TradingRisk.Domain.Risk;
using TradingRisk.Infrastructure.Persistence;
using TradingRisk.Api.RiskJobs;

// Top-level statements are compiled into a generated Main method. Execution begins here,
// just as it begins in Java's public static void main(String[] args).
var builder = WebApplication.CreateBuilder(args);

// Spring equivalents:
// AddControllers -> Spring MVC / @RestController infrastructure
// AddScoped/AddSingleton -> @Bean scopes and constructor injection
// AddOptions -> @ConfigurationProperties
// Registration describes what the service provider can create; it does not yet build it.
builder.Services.AddControllers();
builder.Services.AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
    .Validate(options => Encoding.UTF8.GetByteCount(options.DemoSigningKey) >= 32,
        "Security:DemoSigningKey must be at least 32 bytes for the learning HMAC example.")
    .Validate(options => options.MaxFailedAttempts is >= 1 and <= 20,
        "Security:MaxFailedAttempts must be between 1 and 20.")
    .Validate(options => options.LockoutMinutes is >= 1 and <= 60,
        "Security:LockoutMinutes must be between 1 and 60.")
    .Validate(options => options.DemoUsers.Count > 0 && options.DemoUsers.All(user =>
        !string.IsNullOrWhiteSpace(user.UserName) &&
        user.Role is "risk-reader" or "risk-operator" &&
        user.PasswordHash.StartsWith("PBKDF2-SHA256$", StringComparison.Ordinal)),
        "Security:DemoUsers must contain valid usernames, roles, and PBKDF2 hashes.")
    .ValidateOnStart();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var security = builder.Configuration
            .GetSection(SecurityOptions.SectionName)
            .Get<SecurityOptions>() ?? new SecurityOptions();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = security.Issuer,
            ValidateAudience = true,
            ValidAudience = security.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(security.DemoSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("RiskReader", policy => policy.RequireRole("risk-reader", "risk-operator"))
    .AddPolicy("RiskOperator", policy => policy.RequireRole("risk-operator"));
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services
    .AddHealthChecks()
    // The existing /health endpoint now verifies that EF can connect to SQLite.
    .AddDbContextCheck<RiskDbContext>("sqlite");

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

var riskDatabaseConnection = builder.Configuration.GetConnectionString("RiskDatabase")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:RiskDatabase is required.");

// AddDbContext and the EF repositories are scoped by request. This is the .NET analogue
// of a transaction-scoped JPA EntityManager/repository graph.
builder.Services.AddTradingRiskPersistence(
    riskDatabaseConnection,
    builder.Environment.ContentRootPath);
builder.Services.AddSingleton<IRiskCalculator, HistoricalSimulationRiskCalculator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreatePortfolioHandler>();
builder.Services.AddScoped<GetPortfolioHandler>();
builder.Services.AddScoped<SearchPortfoliosHandler>();
builder.Services.AddScoped<GetPortfolioStatisticsHandler>();
builder.Services.AddScoped<GetPortfolioAnalyticsHandler>();
builder.Services.AddScoped<CalculatePortfolioRiskHandler>();
builder.Services.AddSingleton<TradingRisk.Api.Security.DemoTokenService>();
builder.Services.AddSingleton<TradingRisk.Api.Security.CredentialValidator>();
builder.Services.AddSingleton<IRiskJobBroker, InMemoryRiskJobBroker>();
builder.Services.AddHostedService<RiskJobWorker>();

// Build creates the root service provider and WebApplication. app.Run below, not Build,
// starts Kestrel and waits for shutdown.
var app = builder.Build();

// Convenient for this one-process learning project. A production deployment should run
// reviewed migration SQL or a migration bundle before starting application replicas.
await app.Services.MigrateTradingRiskDatabaseAsync();

// Middleware runs in registration order on the request and unwinds in reverse order for
// the response. Error handling is early so it can catch failures from later components.
app.UseExceptionHandler();

// TestServer is already an in-process transport and has no TLS listener to redirect to.
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

// The Web SDK publishes files under wwwroot. DefaultFiles rewrites "/" to
// "/index.html"; StaticFiles serves the HTML, CSS, and JavaScript without a controller.
// Spring Boot provides a similar convention under src/main/resources/static.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    // The official generator serves the machine-readable document; Swashbuckle adds a
    // convenient interactive learning UI at /swagger without exposing it in Production.
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Endpoint mappings are terminal destinations selected by routing.
app.MapHealthChecks("/health");
app.MapControllers().RequireRateLimiting("api");

// Starts the host/server and normally blocks until the process receives a shutdown signal.
app.Run();

// WebApplicationFactory uses this visible entry point for in-process API tests.
public partial class Program;
