# Deep dive: ASP.NET Core HTTP, configuration, errors, and operations

This chapter follows an HTTP request through this application and explains the
ASP.NET Core concepts that replace familiar Spring MVC and Spring Boot
mechanisms.

Read it with:

- [`Program.cs`](../src/TradingRisk.Api/Program.cs);
- [`PortfoliosController.cs`](../src/TradingRisk.Api/Controllers/PortfoliosController.cs);
- [`PortfolioRequests.cs`](../src/TradingRisk.Api/Contracts/PortfolioRequests.cs);
- the [request sequence diagram](diagrams/03-risk-request-sequence.puml); and
- the [startup comparison diagram](diagrams/02-startup-comparison.puml).

## 1. HTTP stack translation

| Spring Boot / Java | ASP.NET Core in this project |
|---|---|
| embedded Tomcat/Jetty/Undertow | Kestrel web server |
| servlet filter | middleware |
| `DispatcherServlet` | endpoint routing plus MVC controller pipeline |
| `@RestController` | `[ApiController]` class deriving `ControllerBase` |
| `@RequestMapping` | `[Route]`, `[HttpGet]`, `[HttpPost]` |
| Jackson | `System.Text.Json` web defaults |
| Bean Validation | Data Annotations plus custom/domain validation |
| `@ControllerAdvice` / `@ExceptionHandler` | exception middleware plus `IExceptionHandler` |
| `ResponseEntity<T>` | `ActionResult<T>` |
| `application.yml` | `appsettings.json` plus configuration providers |
| `@ConfigurationProperties` | options pattern, such as `IOptions<RiskApiOptions>` |
| Actuator health | ASP.NET Core health checks |
| springdoc/OpenAPI | ASP.NET Core OpenAPI generation |
| SLF4J + logging implementation | `ILogger<T>` plus configured providers |
| request thread interruption is uncommon | cooperative `CancellationToken` |

The analogies help navigation, but the lifecycle and defaults differ. Do not
expect a servlet container or a Spring `ApplicationContext` hidden behind the
same annotations.

## 2. What Kestrel and the host do

`WebApplication.CreateBuilder(args)` prepares the generic host and web host. In
broad terms, the host owns:

- process lifetime and graceful shutdown;
- dependency injection;
- configuration;
- logging;
- hosted/background services;
- Kestrel server configuration;
- environment and content-root information.

Kestrel accepts HTTP connections and feeds requests into the ASP.NET Core
pipeline. In production, it may be directly exposed or sit behind a reverse
proxy/load balancer. Proxy configuration matters because scheme, host, and
client IP may otherwise describe the proxy rather than the original client.

`app.Run()` starts the host and normally blocks until shutdown. When a container
receives its termination signal, the host begins graceful shutdown and
cancellation is propagated to hosted work.

## 3. Service registration and middleware are different

These two phases are often confused.

### Register services

```csharp
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
```

Registration describes objects/capabilities that the container can later
create. It does not place anything in the request path by itself.

### Build the service provider and application

```csharp
var app = builder.Build();
```

After this line, the root container exists. Treat registration as complete.
ASP.NET Core technically exposes ways to access services, but adding ordinary
application registrations after build is not the intended pattern.

### Configure request behavior

```csharp
app.UseExceptionHandler();
app.UseRateLimiter();
app.MapControllers();
```

`Use...` methods normally add middleware to the pipeline. `Map...` methods
normally define terminal endpoints selected by routing.

Spring roughly separates the same responsibilities into bean definitions,
filter/security configuration, MVC configuration, and controller mappings;
minimal-hosting .NET keeps the main composition visible in one file.

## 4. Middleware is a nested pipeline

A middleware receives an `HttpContext` and may:

1. inspect or change the request;
2. stop processing and write a response;
3. invoke the next middleware;
4. inspect or change the response while control unwinds.

Conceptually:

```csharp
async Task InvokeAsync(HttpContext context, RequestDelegate next)
{
    // request-side work
    await next(context);
    // response-side work
}
```

If middleware is registered as A, B, C, invocation resembles:

```text
request  -> A -> B -> C -> endpoint
response <- A <- B <- C <-
```

This is similar to a servlet filter chain, but order must still be learned in
ASP.NET Core terms.

### Why exception handling is early

```csharp
app.UseExceptionHandler();
```

An exception handler can catch exceptions thrown only by components invoked
after it. Putting it near the beginning lets it protect rate limiting, routing,
model/controller execution, and later middleware.

### HTTPS redirection

```csharp
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
```

The normal application redirects HTTP to HTTPS. The in-process test server has
no real TLS listener, so this project skips the redirect in its isolated
`Testing` environment.

Production TLS may terminate at a proxy. Forwarded-header and proxy trust
configuration then determine whether ASP.NET Core recognizes the original
request as HTTPS. Configure this for the actual hosting platform; blindly
trusting forwarded headers is a security problem.

### Rate limiter and routing

The named policy is attached here:

```csharp
app.MapControllers().RequireRateLimiting("api");
```

That means controller endpoints use the `api` policy. `/health` and the
development OpenAPI endpoint do not inherit this policy from that mapping.

With the minimal hosting model, routing/endpoints can be inserted implicitly.
If the pipeline becomes more complex—authentication, authorization, CORS,
custom routing, endpoint-specific limiting—make `UseRouting` and the documented
middleware order explicit rather than relying on a guess.

## 5. Endpoint routing and controller discovery

`AddControllers()` adds MVC controller services. `MapControllers()` adds
attribute-routed controller actions as endpoints.

The controller declares a route prefix:

```csharp
[ApiController]
[Route("api/v1/portfolios")]
public sealed partial class PortfoliosController : ControllerBase
```

Actions append templates:

```csharp
[HttpGet("{portfolioId:guid}", Name = GetPortfolioRouteName)]
```

The resulting route is:

```text
GET /api/v1/portfolios/{portfolioId}
```

`:guid` is a route constraint. It helps endpoint selection: text that is not a
GUID does not match this route. A route constraint is not a domain rule and is
not a substitute for checking `Guid.Empty`.

### Spring equivalent

```java
@RestController
@RequestMapping("/api/v1/portfolios")
final class PortfoliosController {
    @GetMapping("/{portfolioId}")
    ResponseEntity<PortfolioDto> get(@PathVariable UUID portfolioId) {
        // ...
    }
}
```

ASP.NET Core attributes use square brackets because they are C# metadata
attributes, not Java annotations.

## 6. `[ApiController]` changes API behavior

`[ApiController]` is more than a marker. Among its API-focused conventions, it
causes invalid model state to produce an automatic HTTP 400 response rather
than requiring every action to check `ModelState.IsValid`.

This means an action normally runs only after route/body conversion and Data
Annotation validation succeeded.

Be careful when debugging “my controller was never called.” The framework may
have rejected:

- malformed JSON;
- an invalid GUID or date conversion;
- a missing required member;
- a string or collection that violates an annotation.

Inspect the 400 Problem Details/validation response before setting a breakpoint
only inside the action.

## 7. Model binding: where action parameters come from

For this action:

```csharp
public async Task<ActionResult<RiskReportDto>> CalculateRiskAsync(
    Guid portfolioId,
    CalculateRiskRequest request,
    CancellationToken cancellationToken)
```

ASP.NET Core supplies:

- `portfolioId` from the route;
- `request` from the JSON request body;
- `cancellationToken` from `HttpContext.RequestAborted`.

With `[ApiController]`, binding-source inference normally identifies complex
types as body input and route-matching simple parameters as route input. Many
teams still use `[FromRoute]` and `[FromBody]` when explicitness or conventions
require them.

Only one parameter can consume the request body with the normal JSON body
binder. HTTP request bodies are streams and are not generally reread
automatically.

### JSON property names

The default web JSON settings use camel-case JSON names. A C# property named
`BaseCurrency` is sent as:

```json
{
  "baseCurrency": "USD"
}
```

Do not assume casing, enum representation, number handling, reference handling,
or date formats. Treat serializer options as part of the API contract and test
them.

## 8. Request records and Data Annotations

The create contract uses a positional record:

```csharp
public sealed record CreatePortfolioRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,
    [Required]
    [StringLength(3, MinimumLength = 3)]
    string BaseCurrency,
    [Required]
    [MinLength(1)]
    IReadOnlyList<CreatePositionRequest> Positions);
```

The primary constructor parameters become public init-only properties and
participate in value-based record equality.

The attributes describe the API shape:

- `[Required]`: value must be supplied/non-null for validation purposes;
- `[StringLength]`: boundary string size;
- `[MinLength]`: collection must contain at least one element;
- `[Range]`: numeric boundary.

Nullable reference analysis and runtime validation solve different problems:

- `string` versus `string?` is compiler analysis for C# code;
- `[Required]` is runtime validation of external data.

External JSON is never trusted merely because the C# declaration is non-null.

### Decimal range and culture

The confidence range uses string limits:

```csharp
[Range(
    typeof(decimal),
    "0.5",
    "0.9999",
    ParseLimitsInInvariantCulture = true,
    ConvertValueInInvariantCulture = true)]
```

The explicit invariant-culture flags prevent server locale from changing how
the decimal bounds are interpreted. Financial services should avoid
environment-dependent number semantics.

### Boundary validation is not the domain model

`[StringLength(3)]` rejects the wrong currency length before the controller.
`Currency.From` still normalizes and validates the domain value because:

- another adapter may call Application;
- annotations can be bypassed in ordinary C# construction;
- the Domain must protect itself;
- HTTP and domain rules can evolve separately.

## 9. Controller constructor and dependency injection

The class uses a C# primary constructor:

```csharp
public sealed partial class PortfoliosController(
    CreatePortfolioHandler createPortfolio,
    GetPortfolioHandler getPortfolio,
    CalculatePortfolioRiskHandler calculateRisk,
    IOptions<RiskApiOptions> options,
    ILogger<PortfoliosController> logger) : ControllerBase
```

ASP.NET Core activates a controller for a request and resolves all constructor
parameters from DI.

This Java constructor:

```java
PortfoliosController(
        CreatePortfolioHandler createPortfolio,
        GetPortfolioHandler getPortfolio,
        RiskApiProperties options,
        Logger logger) {
    this.createPortfolio = createPortfolio;
    // ...
}
```

is conceptually the same, although Spring discovers the component while
ASP.NET Core knows it through MVC and explicit service registrations.

If DI cannot create a type, examine the exception from the inside out:

- Was the service registered?
- Is the requested interface mapped to an implementation?
- Can DI construct every transitive dependency?
- Is a scoped service being resolved from the root or captured by a singleton?
- Are there multiple constructors or primitive values with no configuration
  binding?

## 10. `Task<ActionResult<T>>` decoded

```csharp
Task<ActionResult<PortfolioDto>>
```

has three layers:

- `Task<...>`: the action completes asynchronously;
- `ActionResult<...>`: it may return either a typed success body or another HTTP
  result;
- `PortfolioDto`: the documented success-body type.

Examples:

```csharp
return Ok(result);                 // 200 + JSON body
return CreatedAtRoute(...);        // 201 + Location + JSON body
return NotFound();                 // 404
```

Spring's closest common form is `CompletableFuture<ResponseEntity<T>>`, though
Spring MVC applications often use synchronous controller signatures and rely
on server/request threading differently.

## 11. Correct response semantics in this API

### Create

```csharp
return CreatedAtRoute(
    GetPortfolioRouteName,
    new { portfolioId = result.Id },
    result);
```

This returns:

- status `201 Created`;
- the created representation;
- a `Location` header generated from the named GET route.

Using the route name avoids hard-coding the URL twice.

### Get and calculate

```csharp
return Ok(result);
```

Both return status 200 with a JSON representation.

### Expected errors

- invalid request/use-case/domain input: 400;
- portfolio absent: 404;
- too many requests: 429;
- unexpected server failure: 500.

Future mutations may add:

- 409 for optimistic concurrency or idempotency conflicts;
- 401 when no acceptable identity is authenticated;
- 403 when identity exists but lacks permission;
- 202 for queued asynchronous calculations.

Status codes are part of the API contract; do not expose internal exception
types and expect clients to infer meaning.

## 12. Mapping keeps boundaries explicit

The controller maps request records to an Application command:

```csharp
var command = new CreatePortfolioCommand(
    request.Name,
    request.BaseCurrency,
    request.Positions
        .Select(position => new CreatePositionInput(
            position.InstrumentId,
            position.Quantity,
            position.Price))
        .ToArray());
```

This is LINQ projection:

1. `Select` lazily describes conversion of each position;
2. `ToArray` evaluates it now and creates a stable input collection;
3. the command is passed to the handler.

Mapping code can feel repetitive, but it is a security and versioning boundary.
Reflection-based automappers can reduce typing, while also hiding which fields
cross the boundary. Use them only if the team accepts that trade-off and tests
the mapping contract.

## 13. Configuration providers and precedence

ASP.NET Core builds configuration from ordered providers. With normal web
defaults, sources include:

```text
appsettings.json
appsettings.{Environment}.json
development user secrets when configured
environment variables
command-line arguments
```

Later providers override earlier values for the same key. Therefore an
environment variable can override a JSON default without modifying the image.

The repository contains:

```json
{
  "RiskApi": {
    "DefaultConfidenceLevel": 0.99,
    "MaxScenarioCount": 1000
  }
}
```

Configuration keys form a hierarchy:

```text
RiskApi:MaxScenarioCount
```

Environment variables use double underscore because colon is not portable in
variable names:

```bash
RiskApi__MaxScenarioCount=250
```

### Environment is not build configuration

These are independent:

- `Debug`/`Release`: build configuration;
- `Development`/`Staging`/`Production`/`Testing`: runtime environment.

A Release build can run in Development. A Debug build can run with Production
configuration. Never infer one from the other.

### Secrets

Do not commit passwords, tokens, client secrets, or production connection
strings to `appsettings.json`.

Typical sources:

- .NET user secrets for local development;
- environment variables or mounted secret files;
- an approved production secret store;
- workload identity instead of long-lived credentials where possible.

Configuration is not automatically secret merely because it arrived through an
environment variable. Logging environment dumps can leak it.

## 14. Strongly typed options

The options class:

```csharp
public sealed class RiskApiOptions
{
    public const string SectionName = "RiskApi";

    public decimal DefaultConfidenceLevel { get; init; } = 0.99m;
    public int MaxScenarioCount { get; init; } = 1_000;
}
```

is bound and validated:

```csharp
builder.Services
    .AddOptions<RiskApiOptions>()
    .Bind(builder.Configuration.GetSection(RiskApiOptions.SectionName))
    .Validate(...)
    .ValidateOnStart();
```

`ValidateOnStart` turns bad deployment configuration into a startup failure
instead of letting the first customer request discover it.

### Spring comparison

```java
@ConfigurationProperties(prefix = "risk-api")
@Validated
public record RiskApiProperties(
        @DecimalMin("0.5") @DecimalMax("0.9999") BigDecimal defaultConfidenceLevel,
        @Min(1) @Max(10000) int maxScenarioCount) {
}
```

### `IOptions`, `IOptionsSnapshot`, and `IOptionsMonitor`

| Interface | Typical lifetime/behavior | Use |
|---|---|---|
| `IOptions<T>` | singleton-style cached value | stable settings; used here |
| `IOptionsSnapshot<T>` | scoped, recomputed per request | request-time snapshot of reloadable settings |
| `IOptionsMonitor<T>` | singleton, current value and change callbacks | singleton consumers or active reload |

Dynamic reload sounds attractive, but changing a risk methodology during
requests can harm reproducibility. Decide which settings are safe to reload and
record the effective value with each calculation.

## 15. Central error handling and Problem Details

Expected failures are represented inside the application without HTTP:

```text
RequestValidationException
PortfolioNotFoundException
DomainValidationException
```

The outer adapter maps them:

```csharp
var (status, title, detail) = exception switch
{
    RequestValidationException or DomainValidationException => (400, ...),
    PortfolioNotFoundException => (404, ...),
    _ => (500, ...)
};
```

This uses C# pattern matching and tuple deconstruction to select three related
values.

The handler writes Problem Details:

```csharp
new ProblemDetails
{
    Status = status,
    Title = title,
    Detail = detail,
    Instance = httpContext.Request.Path
}
```

Problem Details gives clients a standard machine-readable error envelope.
Production systems often add stable application error codes and a trace or
correlation identifier.

### Never return unexpected exception details

For status 500, the response contains:

```text
The server could not complete the request.
```

The full exception is logged server-side. Returning stack traces, SQL,
connection information, file paths, or internal type names can expose sensitive
implementation details.

### Exceptions versus result types

Exceptions fit unexpected failures and relatively uncommon rejected operations.
A result/discriminated-union style can make expected business outcomes
explicit, especially when rejection is frequent:

```csharp
Result<RiskReportDto, PortfolioNotFound>
```

C# does not yet have Java-like checked exceptions or a built-in general
discriminated union. Teams use records, enums, libraries, or exceptions. The
important rule is a consistent boundary contract, not one universal technique.

## 16. Structured and source-generated logging

The controller injects:

```csharp
ILogger<PortfoliosController>
```

The generic type becomes a log category. The source-generated declaration is:

```csharp
[LoggerMessage(
    EventId = 1001,
    Level = LogLevel.Information,
    Message = "Calculated {ConfidenceLevel:P2} VaR from {ScenarioCount} scenarios")]
private static partial void LogRiskCalculated(
    ILogger logger,
    decimal confidenceLevel,
    int scenarioCount);
```

At compile time, a generator supplies the partial method implementation.
Benefits include:

- a stable message template;
- named structured fields;
- less boxing and template parsing;
- a stable event ID;
- compile-time checking of parameter/template correspondence.

Avoid:

```csharp
logger.LogInformation($"Calculated risk for {portfolioId}");
```

String interpolation eagerly creates one formatted string and loses the clean
structured-property boundary.

Prefer a template or source-generated message:

```csharp
logger.LogInformation(
    "Calculated risk for portfolio {PortfolioId}",
    portfolioId);
```

### Log scope

```csharp
using var logScope = RiskCalculationScope(logger, portfolioId);
```

The scope adds `PortfolioId` to logs produced while the scope is active, if the
provider includes scopes. This helps correlate controller, handler, and adapter
events without repeating the value in every message.

### Finance logging rules

Good fields:

- portfolio or job identifier;
- model version;
- scenario count;
- duration;
- outcome/error code;
- trace/correlation ID.

Usually unsafe or too expensive:

- all positions;
- entire market-data payloads;
- access tokens;
- connection strings;
- personal/client information;
- unbounded instrument IDs as metric labels.

Logs are not an audit ledger. Audit records need deliberate integrity,
retention, access, and business semantics.

## 17. Rate limiting

The project defines a fixed window:

```csharp
options.AddFixedWindowLimiter("api", limiter =>
{
    limiter.PermitLimit = 100;
    limiter.Window = TimeSpan.FromMinutes(1);
    limiter.QueueLimit = 0;
});
```

Meaning:

- at most 100 permits per window for the policy's default partition behavior;
- no excess request waits in a queue;
- rejected requests receive 429.

This is a teaching baseline, not a production policy. A real design asks:

- Is the partition per user, client, tenant, IP, desk, or endpoint?
- Do all calculations have equal cost?
- Is limiting also enforced at the API gateway?
- How do multiple app replicas share a global quota?
- Should clients receive `Retry-After`?
- Can a malicious client create high CPU cost below the request-count limit?

Rate limiting protects capacity and fairness. It is not authentication, DDoS
protection, or a substitute for bounded work inside the application.

## 18. Health checks

This project registers and maps:

```csharp
builder.Services.AddHealthChecks();
app.MapHealthChecks("/health");
```

No dependency checks are registered, so this endpoint proves only that the
process and pipeline can answer. It is essentially a liveness-style baseline.

When a database is added, distinguish:

- **liveness**: should the orchestrator restart this process?
- **readiness**: should traffic be sent to this instance?

A temporary database outage should often make the app unready without causing
every replica to restart continuously. Health probes must be cheap; aggressive
queries can overload the dependency they are meant to observe.

## 19. OpenAPI

```csharp
builder.Services.AddOpenApi();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
```

The JSON document is available only in Development at:

```text
/openapi/v1.json
```

This is the machine-readable OpenAPI description, not an interactive Swagger UI.
The controller's route, parameter, response, and `ProducesResponseType`
metadata contribute to the document.

OpenAPI is a contract aid, not proof that behavior is backward compatible.
Review:

- removed/renamed fields;
- requiredness changes;
- enum additions and consumer behavior;
- status/error changes;
- numeric unit/precision changes;
- semantic changes with identical schemas.

## 20. Cancellation

ASP.NET Core binds an action `CancellationToken` to request abortion:

```csharp
public async Task<ActionResult<PortfolioDto>> GetAsync(
    Guid portfolioId,
    CancellationToken cancellationToken)
```

The token is passed:

```text
controller -> handler -> repository
```

Cancellation is cooperative. Passing a token does nothing unless the called
operation observes it. EF Core and `HttpClient` asynchronous APIs usually
accept tokens. The in-memory repository explicitly calls:

```csharp
cancellationToken.ThrowIfCancellationRequested();
```

The current calculation is fast and synchronous. For a large CPU-bound loop,
periodically check cancellation at a sensible granularity:

```csharp
foreach (var scenario in scenarios)
{
    cancellationToken.ThrowIfCancellationRequested();
    // calculate
}
```

Do not:

- create a fresh unrelated token instead of propagating the request token;
- swallow `OperationCanceledException` and log it as an unexpected 500;
- assume cancellation rolls back an external side effect;
- make public methods async only to add `Task.Run` around server CPU work.

For durable risk jobs, cancellation is a domain/job state transition as well as
a .NET token.

## 21. Async I/O and request scalability

This handler uses async because repository access will eventually be I/O:

```csharp
var portfolio = await repository.GetByIdAsync(...);
```

While a true asynchronous database operation waits, the request does not need
to hold a thread blocked on the response. The thread can process other work.

`async` does not:

- create a new thread automatically;
- make CPU arithmetic faster;
- make shared state thread-safe;
- execute sequential awaits in parallel;
- guarantee lower latency for one request.

Never block on tasks in request code:

```csharp
var result = SomeAsync().Result;       // avoid
SomeAsync().GetAwaiter().GetResult();  // avoid
```

Sync-over-async can waste thread-pool threads and under load cause starvation.

For independent I/O, controlled concurrency may use `Task.WhenAll`; for
thousands of calls, add a concurrency bound. Unlimited fan-out moves the
bottleneck into sockets, memory, databases, or downstream rate limits.

## 22. JSON, dates, and finance-sensitive values

### Decimal

Prices, quantities, and money-like output use `decimal`, represented in JSON as
numbers. Clients must preserve adequate decimal precision. Java clients should
prefer `BigDecimal` rather than binary `double` for contract values where exact
decimal representation matters.

### Date

`DateOnly` represents a calendar date without time zone:

```json
"asOfDate": "2026-01-02"
```

This is appropriate for a governed daily scenario label. Intraday market data
needs an instant such as `DateTimeOffset`, plus explicit market/time-zone
semantics.

### Timestamp

`CalculatedAtUtc` is a `DateTimeOffset`. The `Utc` name documents intended
offset/meaning. Prefer instants for events and audit data; do not store a local
wall-clock time with an ambiguous time zone.

### Dictionary keys

Scenario returns are:

```json
"returns": {
  "AAPL": -0.04,
  "MSFT": -0.02
}
```

The Domain normalizes instrument IDs. Be explicit about aliases, corporate
actions, exchange, currency, and identifier version in a production model;
`AAPL` alone is not a universal security master key.

## 23. Walk a request manually

Start the app:

```bash
dotnet run --project src/TradingRisk.Api --launch-profile http
```

Create a portfolio:

```http
POST /api/v1/portfolios HTTP/1.1
Host: localhost:5229
Content-Type: application/json

{
  "name": "Learning book",
  "baseCurrency": "USD",
  "positions": [
    {
      "instrumentId": "AAPL",
      "quantity": 100,
      "price": 200
    }
  ]
}
```

The server should return 201 and a `Location` header. Copy the ID and calculate:

```http
POST /api/v1/portfolios/{id}/risk HTTP/1.1
Host: localhost:5229
Content-Type: application/json

{
  "confidenceLevel": 0.8,
  "scenarios": [
    {
      "asOfDate": "2026-01-02",
      "returns": {
        "AAPL": -0.10
      }
    },
    {
      "asOfDate": "2026-01-05",
      "returns": {
        "AAPL": 0.05
      }
    }
  ]
}
```

Use the committed
[`TradingRisk.Api.http`](../src/TradingRisk.Api/TradingRisk.Api.http) file in
Rider to run the fuller request set.

## 24. Debugging checklist

### App does not start

- Read the innermost exception.
- Check SDK selection with `dotnet --info`.
- Check option validation errors.
- Check port conflicts.
- Check whether the expected environment/config provider is active.
- Check DI construction errors.

### Route returns 404

- Confirm HTTP method and complete route prefix.
- Check the `:guid` constraint.
- Confirm `MapControllers()` executes.
- Distinguish routing 404 from application “portfolio not found” 404 by the
  response body.

### Request returns 400 before breakpoint

- Inspect validation Problem Details.
- Validate JSON syntax and content type.
- Check date/GUID/decimal conversion.
- Check Data Annotations and required collections.

### Created data disappears

- The adapter is process-local.
- Restarting the process creates a new singleton repository.
- Multiple replicas would each have different memory.

### HTTP redirects unexpectedly

- Check the launch profile and HTTPS URL.
- Use the `http` profile for the provided `.http` file.
- In a proxy deployment, inspect forwarded-header setup.

## 25. Web/API review checklist

- Does the route and HTTP method describe the operation?
- Are success status, body, and `Location`/headers correct?
- Is invalid JSON distinguished from domain rejection?
- Are response/error shapes stable and documented?
- Is model binding explicit enough to understand?
- Does the controller only map, apply HTTP policy, log, and delegate?
- Does configuration fail fast?
- Are secrets excluded from source and logs?
- Is cancellation propagated into every I/O boundary?
- Is middleware ordered for exception handling, proxying, CORS, auth, rate
  limiting, and endpoints?
- Are health endpoints cheap and separated into liveness/readiness as needed?
- Is rate limiting partitioned by the correct identity and cost?
- Does OpenAPI match tested behavior?
- Are time, units, currency, precision, and versioning explicit?

## Official references

- [ASP.NET Core fundamentals](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/?view=aspnetcore-10.0)
- [ASP.NET Core middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0)
- [Model binding](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-10.0)
- [Model validation](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-10.0)
- [Options pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0)
- [Error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0)
- [Rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [Health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)
- [Logging guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging-library-authors)
- [Task cancellation](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-cancellation)
