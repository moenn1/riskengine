# Deep dive: testing .NET, ASP.NET Core, and risk calculations

This chapter explains the test project from a Java/JUnit/Spring perspective. It
covers test syntax, test doubles, deterministic time, in-process HTTP tests,
finance-specific assertions, and how the test suite should evolve as the system
grows.

## 1. Test stack translation

| Java/Spring | This .NET project |
|---|---|
| JUnit 5 | xUnit.net v3 |
| `@Test` | `[Fact]` |
| `@ParameterizedTest` | `[Theory]` |
| `@ValueSource` / `@CsvSource` | `[InlineData]` |
| AssertJ/JUnit assertions | `Assert.*` |
| Maven Surefire / Gradle test task | Microsoft Testing Platform through `dotnet test` |
| `@SpringBootTest` | `WebApplicationFactory<Program>` integration test |
| MockMvc/WebTestClient | `HttpClient` backed by ASP.NET Core `TestServer` |
| Mockito mock/stub | hand-written fake here; mocking library can be added when useful |
| `Clock` test double | `TimeProvider` test double |
| test application profile | ASP.NET Core `Testing` environment |

Framework syntax matters less than choosing the right boundary and making the
test deterministic.

## 2. Anatomy of the test project

The test project is an executable:

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

- `OutputType=Exe` supports the test runner/application model used here.
- `IsPackable=false` prevents accidentally publishing tests as a NuGet library.
- `IsTestProject=true` identifies its purpose to tooling.

Packages:

```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
<PackageReference Include="xunit.v3.mtp-v2" />
```

`xunit.v3.mtp-v2` integrates xUnit v3 with Microsoft Testing Platform.
`Microsoft.AspNetCore.Mvc.Testing` supplies `WebApplicationFactory`,
`TestServer`, and web-test support.

The package versions are central in `Directory.Packages.props`, just like the
production packages.

The test project references all production projects because different tests
exercise different boundaries. Production projects never reference Tests.

## 3. Why `GlobalUsings.cs` exists

```csharp
global using Xunit;
```

A normal `using` applies to one source file. A `global using` applies to every
source file in the compilation, so every test can write `[Fact]` and `Assert`
without repeating:

```csharp
using Xunit;
```

The .NET SDK also generates implicit global usings for common system
namespaces because `ImplicitUsings` is enabled. Generated files live under
`obj/`; do not edit them.

## 4. `[Fact]`: one test case

```csharp
[Fact]
public void CreateComputesSignedMarketValue()
{
    var longPosition = Position.Create("aapl", 10m, 200m);
    var shortPosition = Position.Create("msft", -5m, 400m);

    Assert.Equal("AAPL", longPosition.InstrumentId.Value);
    Assert.Equal(2_000m, longPosition.MarketValue);
    Assert.Equal(-2_000m, shortPosition.MarketValue);
}
```

Read it as Arrange–Act–Assert even though a tiny test does not label the
sections:

- Arrange: choose input values.
- Act: create the positions.
- Assert: verify normalization and signed values.

The `m` suffix makes literals `decimal`. Without it, `10` is an `int` and
`200.0` is a `double`. Financial tests should make numeric types obvious.

### Java equivalent

```java
@Test
void createComputesSignedMarketValue() {
    var longPosition = Position.create("aapl", new BigDecimal("10"),
        new BigDecimal("200"));

    assertEquals("AAPL", longPosition.instrumentId().value());
    assertEquals(new BigDecimal("2000"), longPosition.marketValue());
}
```

C# `decimal` is a built-in value type with operators; Java `BigDecimal` uses
methods such as `multiply` and needs careful scale/equality handling.

## 5. `[Theory]`: one rule, multiple examples

```csharp
[Theory]
[InlineData(0)]
[InlineData(1)]
public void CalculateRejectsInvalidConfidenceLevel(double confidenceLevel)
{
    // ...
}
```

xUnit executes the method once per data row. Attribute arguments are limited to
types the CLR permits in attribute metadata, so the test accepts `double` and
converts to `decimal` inside the call.

For richer data use:

- `[MemberData]` for static members;
- `[ClassData]` for a data-provider type;
- a loop/property-testing library when the important idea is an invariant over
  many generated inputs.

Do not place dozens of unrelated scenarios in one theory merely to reduce test
method count. Each test should communicate one behavior.

## 6. Testing exceptions

```csharp
var exception = Assert.Throws<DomainValidationException>(
    () => Position.Create("AAPL", 0m, 200m));

Assert.Equal("Position quantity cannot be zero.", exception.Message);
```

The lambda delays execution so `Assert.Throws` can observe it.

For asynchronous code:

```csharp
var exception = await Assert.ThrowsAsync<PortfolioNotFoundException>(
    () => handler.HandleAsync(query, cancellationToken));
```

Choose message assertions deliberately:

- exact messages make wording part of the contract and are brittle;
- substring assertions lock down the important context;
- exception properties or stable error codes are better for machine contracts.

The current tests use exact text for a small domain rule and substring matching
when an instrument value is the important detail.

## 7. Domain tests are executable specifications

`HistoricalSimulationRiskCalculatorTests` does more than improve line
coverage. It fixes the methodology:

```csharp
Assert.Equal(50m, report.ValueAtRisk);
Assert.Equal(100m, report.ExpectedShortfall);
```

Those numbers specify:

- loss is negative P&L;
- the quantile is nearest-rank;
- the 80% rank for five observations is four;
- Expected Shortfall averages the worst `ceil((1-c)N)` observations;
- reported losses are floored at zero.

If somebody replaces the quantile function with a library default that
interpolates, a test should fail even if both implementations call themselves
“VaR.”

Finance tests need methodology in their names, inputs, and documentation.

## 8. Exact versus approximate numeric assertions

Most portfolio arithmetic uses `decimal`, so values such as:

```csharp
100m * 10m * -0.10m
```

are exactly representable in decimal arithmetic.

Volatility converts observations to `double` for `Math.Sqrt`, so the test uses
a tolerance range:

```csharp
Assert.InRange(report.DailyPnlVolatility, 59.41m, 59.42m);
```

Never assert binary floating-point results with arbitrary exact equality.
Choose tolerance from domain requirements, algorithm error, and expected input
scale—not simply “a large epsilon that makes the test pass.”

Useful strategies:

- absolute tolerance for values near a known scale;
- relative tolerance across large ranges;
- decimal rounding to a documented report precision;
- golden values from an independently verified implementation.

## 9. Test invariants, not only examples

Example tests are easy to understand, but risk calculations also benefit from
property-style invariants:

- multiplying all exposures by positive \(k\) multiplies VaR, ES, worst loss,
  and P&L volatility by \(k\);
- reordering scenarios does not change aggregate metrics;
- report scenario results are ordered by date in this implementation;
- ES is at least VaR under the documented empirical convention;
- gross exposure is nonnegative and at least absolute net market value;
- a zero-return scenario produces zero P&L;
- for one position, scenario P&L equals quantity × price × return;
- duplicating identical observations does not change their empirical value,
  subject to rank/tail-count behavior.

Property-based testing libraries can generate many cases, but generated data
must respect meaningful domain constraints and produce reproducible failure
seeds.

## 10. Handler tests: one layer wider

The Application test constructs:

```csharp
var repository = new InMemoryPortfolioRepository();
var createHandler = new CreatePortfolioHandler(repository);
var riskHandler = new CalculatePortfolioRiskHandler(
    repository,
    new HistoricalSimulationRiskCalculator(),
    new FixedTimeProvider(now));
```

This is manual dependency injection. No framework container is needed.

The test verifies that the handler:

- loads through the repository;
- invokes the real domain calculator;
- maps output;
- uses the injected clock.

It does not verify routing, JSON, middleware, or the DI registrations in
`Program.cs`. That narrower scope keeps it fast and points failures toward
Application coordination.

### Fake, stub, mock, and spy

Terminology varies, but a useful distinction is:

- **stub**: returns controlled data;
- **fake**: working but simplified implementation, such as the in-memory
  repository;
- **mock**: verifies expected interactions, often generated by a library;
- **spy**: records calls for later assertions.

The in-memory repository is a fake. `FixedTimeProvider` is a stub.

Do not mock every class. Testing a handler with the real pure calculator gives
more confidence with less setup. Mock when an interaction itself matters or a
dependency is slow, nondeterministic, destructive, or difficult to arrange.

## 11. Deterministic time with `TimeProvider`

Production registration:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
```

Handler use:

```csharp
timeProvider.GetUtcNow()
```

Test replacement:

```csharp
private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
```

If the handler called `DateTimeOffset.UtcNow` directly, the assertion could only
check a time range or depend on the wall clock. Injecting time makes the output
exact and repeatable.

Use the same idea for:

- random number generation with a deterministic seed/source;
- generated IDs when exact IDs matter;
- external market data;
- message publishing;
- filesystem access;
- exchange calendars.

Do not abstract deterministic, pure platform operations merely for uniformity.

## 12. API integration test: real HTTP stack in process

The API test begins:

```csharp
using var factory = new SqliteWebApplicationFactory();
using var client = factory.CreateClient();
```

`WebApplicationFactory<Program>`:

1. locates the API entry point;
2. boots the real `Program.cs`;
3. constructs the real DI container and middleware;
4. hosts its HTTP pipeline with an in-process `TestServer`;
5. supplies an `HttpClient` that sends requests to that server.

It does not bind a public TCP port. JSON serialization, routing, model binding,
controllers, handlers, the real EF adapter, temporary SQLite database,
default-file rewriting, and static-file middleware still run.

The word “in-process” describes the HTTP transport; the persistence provider is
real relational SQLite. `SqliteWebApplicationFactory` removes the production
`RiskDbContext` options and adds a unique temporary database per host, so tests
cannot read or pollute a developer's `App_Data` file.

### Why `public partial class Program` exists

Top-level statements generate an entry-point type that is not normally visible
to the separate test assembly. This line in `Program.cs`:

```csharp
public partial class Program;
```

adds a visible declaration for `WebApplicationFactory<Program>` without
rewriting startup into a traditional `Main` method.

### Why `using`

`SqliteWebApplicationFactory` overrides disposal to stop the test host and then
remove the exact temporary database and SQLite journal files even if an
assertion fails.

`HttpClient` and each `HttpResponseMessage` use ordinary `using` so their
disposable resources are released deterministically.

### Static UI integration test

`RootServesBrowserWorkbenchAndItsStaticAssets` requests `/`, CSS, and JavaScript
through that same test host. It checks HTTP success, content types, and small
identifying strings.

That proves the Web SDK/test content root, `UseDefaultFiles`,
`UseStaticFiles`, and asset discovery work together. Reading files directly in
the test would not prove the application can serve them.

The test does not execute JavaScript or assess rendered layout. Those are
different boundaries for browser end-to-end, accessibility, and visual tests.
The [browser UI deep dive](11-browser-ui-deep-dive.md) explains the testing
ladder.

## 13. The vertical-slice test

The test sends a create request:

```csharp
using var createResponse = await client.PostAsJsonAsync(
    "/api/v1/portfolios",
    new CreatePortfolioRequest(...),
    cancellationToken);
```

`PostAsJsonAsync` serializes the C# request and sets the JSON content type.
Then:

```csharp
var portfolio =
    await createResponse.Content.ReadFromJsonAsync<PortfolioDto>(cancellationToken);
```

deserializes the response.

The returned portfolio ID is used in the risk route. This proves that the first
request commits to SQLite and a later request scope, with a new scoped
`RiskDbContext`, can reconstruct the aggregate.

The test covers:

```text
JSON serialization
  -> route selection
  -> model binding and validation
  -> controller activation through DI
  -> create handler
  -> EF Core translation and SQLite storage
  -> risk handler
  -> risk calculator
  -> JSON response
```

One test crossing many layers is valuable for the critical happy path. It is
not the place to enumerate every risk formula edge case.

## 14. Test environment and configuration

The factory sets:

```csharp
builder.UseEnvironment("Testing")
```

`Program.cs` uses that environment to skip HTTPS redirection. The environment
name is a runtime choice, independent of Debug/Release compilation.

The test also disables configuration file reload for its restricted process:

```csharp
Environment.SetEnvironmentVariable(
    "DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE",
    "false");
```

Environment variables are process-global mutable state. In a larger suite,
centralize such configuration in a factory and account for parallel tests.
Prefer supplying test configuration through the test host when practical.

Do not make production code depend on the `Testing` environment for business
rules. Environment branches should be rare and infrastructure-specific.

## 15. `TestContext.Current.CancellationToken`

xUnit v3 exposes a cancellation token for the current test:

```csharp
var cancellationToken = TestContext.Current.CancellationToken;
```

Passing it into async operations allows the test runner to cancel work when the
run is aborted or times out. This is better than repeatedly using
`CancellationToken.None`.

A product cancellation test should use its own
`CancellationTokenSource`, cancel at a controlled point, and assert the
documented outcome.

## 16. Test isolation and parallel execution

xUnit may run eligible tests in parallel. Shared state can create flaky tests:

- static variables;
- environment variables;
- shared databases;
- fixed ports;
- filesystem paths;
- reused singleton fakes;
- clock or culture changes.

The current API test creates a new factory inside the test, so it gets a new
application service provider and repository.

When using a real database:

- give each test a unique database/schema, or reset state safely;
- make migrations part of test setup;
- never point tests at developer or production data;
- use transactions only when they accurately model application behavior;
- ensure parallel tests cannot delete one another's data.

Containers are useful for real-engine tests, but container presence alone does
not guarantee isolation or deterministic seed data.

## 17. Unit, integration, contract, and end-to-end scope

### Unit

Runs one small unit with no external infrastructure:

- `PositionTests`;
- `PortfolioTests`;
- `HistoricalSimulationRiskCalculatorTests`.

Fastest and best for formula/invariant permutations.

### Application/component

Combines a handler with controlled adapters:

- `CalculatePortfolioRiskHandlerTests`.

Good for orchestration, mapping, cancellation propagation, and not-found paths.

### API integration

Boots the ASP.NET Core app in process:

- `PortfolioApiTests`.

Good for routes, DI, JSON, error shapes, and important vertical slices.
It also covers the static UI shell because that shell is part of the same
deployable ASP.NET Core application.

### Infrastructure integration

`SqlitePortfolioRepositoryTests` applies the real migration to a unique
temporary SQLite file. It proves cross-context persistence and exercises
translated filtering, `Any`, navigation `Count`, deterministic paging, split
relationship loading, and grouped projections. PostgreSQL provider tests remain
a later step for production-provider semantics.

### Contract

Not present yet. Contract tests can verify OpenAPI compatibility, broker event
schemas, or provider/consumer expectations.

### End-to-end

Not present yet. A deployed test would cross real networking and production-like
dependencies. Keep this set small and focused because diagnosis and runtime are
expensive.

The “test pyramid” is a cost model, not a quota. Put each rule at the narrowest
boundary that can prove it, then keep a few broad tests for wiring.

## 18. What to test next

High-value missing tests include:

### API error contracts

- malformed or invalid create request returns validation Problem Details;
- unknown portfolio returns 404 Problem Details;
- too many scenarios returns 400;
- OpenAPI exists only in Development;
- rate limiting returns 429 under a controlled policy.

### Application behavior

- empty ID and invalid confidence are rejected;
- duplicate scenario dates are rejected;
- repository cancellation propagates;
- domain exceptions are not changed into HTTP types.

### Domain edge cases

- one scenario produces zero sample volatility;
- all profitable scenarios floor risk loss metrics at zero;
- return below -100% is rejected;
- missing instrument return names the date and instrument;
- long and short positions respond oppositely to the same return;
- input collection mutation cannot change an existing scenario/portfolio.

### Build architecture

Add an architecture test or CI check if project-reference rules become easier
to violate as the solution grows.

## 19. Persistence tests: current and next

The current EF repository is tested against SQLite, the provider used by this
application. EF's in-memory provider does not reproduce relational SQL,
constraints, transactions, indexes, collation, or provider-specific behavior.

Test:

- schema migrations apply from an empty database;
- decimal precision and scale;
- unique/foreign/check constraints;
- optimistic concurrency;
- transaction rollback;
- cancellation;
- query count and generated SQL for important paths;
- time-zone and date mapping;
- large portfolio behavior.

Use unit tests for domain rules and real database tests for database behavior.
Mocking `DbSet` usually tests the mock arrangement rather than the query
provider.

## 20. Finance model validation is larger than software testing

Passing unit tests does not make a model fit for risk decisions.

Software verification asks:

- Did we implement the stated formula correctly?
- Are edge cases and failures handled?
- Is the system deterministic and observable?

Model validation asks:

- Is the methodology appropriate for products and decisions?
- Are assumptions empirically supported?
- Is the sample window governed?
- How does the model behave in stress?
- Does backtesting reveal underestimation?
- Are limitations, overrides, and approvals controlled?
- Is an independent implementation/data set used for comparison?

Keep model version and methodology explicit. A formula change should update:

- focused tests;
- golden data;
- risk documentation;
- output methodology/version;
- model governance evidence.

## 21. CI command sequence

Use:

```bash
dotnet restore RiskEngine.slnx
dotnet build RiskEngine.slnx --configuration Release --no-restore
dotnet test RiskEngine.slnx --configuration Release --no-build
```

Why:

- restore failures are isolated;
- warnings-as-errors are evaluated in Release;
- tests execute exactly the assemblies just built;
- repeated implicit work is avoided.

During development, a shorter command is fine:

```bash
dotnet test RiskEngine.slnx
```

It restores/builds implicitly if needed.

Useful filters depend on the active test platform/framework version. Rider's
test explorer is often simplest for one method; use `dotnet test --help` for
the installed SDK's exact command-line filtering syntax.

## 22. Test review checklist

- Does the test name state behavior and outcome?
- Is it deterministic across time, locale, order, and machine?
- Is the assertion at the narrowest useful boundary?
- Is the expected finance convention explicit?
- Are `decimal` and tolerance choices deliberate?
- Does a test double simplify a real boundary, or only mirror implementation?
- Is asynchronous work awaited and cancellable?
- Are disposable hosts/responses cleaned up?
- Can parallel tests interfere through global state?
- Does the API test assert status before deserializing?
- Does an integration test use the real infrastructure behavior it claims to
  test?
- Is a broad happy-path test backed by focused failure/edge tests?
- Does a changed methodology update validation and governance evidence?

## Official references

- [Testing in .NET](https://learn.microsoft.com/en-us/dotnet/core/testing/)
- [Integration tests in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
- [xUnit.net documentation](https://xunit.net/)
- [TimeProvider overview](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview)
