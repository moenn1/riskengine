# Architecture and every-file guide

This document explains every file added by the refactor. Generated `bin/` and
`obj/` files and IDE metadata are not application components.

## Why a modular monolith

The API is one deployable process, but its compile-time boundaries teach the
same dependency discipline needed in a large codebase:

```text
TradingRisk.Api ──────────────┐
    │                         │
    v                         v
TradingRisk.Application <- TradingRisk.Infrastructure
    │                         │
    └──────────> TradingRisk.Domain
```

- Domain contains business concepts and calculations. It has no project or
  package dependencies.
- Application coordinates use cases and declares ports such as repositories.
- Infrastructure implements ports using technical mechanisms.
- API translates HTTP and composes concrete implementations.

This is Clean/Hexagonal Architecture in a restrained form. Microsoft describes
the same inward dependency direction in its
[web architecture guidance](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures).
Projects are not permission boundaries, but they make accidental coupling
visible to the compiler.

## Request flow

For a risk request:

1. `PortfoliosController` binds JSON to `CalculateRiskRequest`.
2. It applies an API request-size policy from `RiskApiOptions`.
3. It maps the request to `CalculatePortfolioRiskQuery`.
4. `CalculatePortfolioRiskHandler` loads a portfolio through
   `IPortfolioRepository`.
5. It maps scenario input to validated domain value objects.
6. `HistoricalSimulationRiskCalculator` computes the distribution and metrics.
7. The handler adds a testable timestamp and maps to `RiskReportDto`.
8. ASP.NET Core serializes that DTO to JSON.
9. Any expected validation/not-found exception is converted by
   `ApiExceptionHandler` to Problem Details.

See the editable
[risk request sequence diagram](diagrams/03-risk-request-sequence.puml) and the
[architecture/domain deep dive](07-architecture-and-domain-design-deep-dive.md)
for the reasoning behind each boundary.

## Root and engineering files

| File | Purpose and lesson |
|---|---|
| `.gitignore` | Keeps compiler output, test output, and local IDE state out of source control. |
| `.dockerignore` | Reduces Docker build context and prevents local/generated files entering an image build. |
| `.editorconfig` | Shares whitespace and selected C# style rules across Rider, Visual Studio, VS Code, and command-line analyzers. |
| `Directory.Build.props` | Applies target framework, C# version, nullable analysis, implicit usings, deterministic builds, and warnings-as-errors to every project. This is similar to common Maven parent/Gradle convention configuration. |
| `Directory.Packages.props` | Central Package Management: one version per NuGet dependency for the solution. It explicitly pins patched `Microsoft.OpenApi` 2.11.0 because the web package's minimum transitive version is vulnerable. |
| `global.json` | Selects .NET 10 SDK policy and Microsoft Testing Platform as the `dotnet test` runner. `latestFeature` allows an installed later .NET 10 feature band. |
| `RiskEngine.slnx` | XML solution file listing production and test projects; a solution groups projects but is not deployed. |
| `Dockerfile` | Multi-stage build: restore/publish with the SDK image, then run as the non-root app user in the smaller ASP.NET runtime image. |
| `.github/workflows/ci.yml` | Restores, builds in Release, and tests on each pull request and main-branch push. |
| `README.md` | Entry point, architecture summary, safety boundary, commands, and navigation. |

## Domain project

`src/TradingRisk.Domain/TradingRisk.Domain.csproj` is a package-free class
library. Keeping framework types out makes the model fast to test and hard to
couple accidentally to HTTP or persistence.

| File | Purpose and lesson |
|---|---|
| `Common/DomainValidationException.cs` | Signals that creating or using a domain value would violate an invariant. |
| `Portfolios/PortfolioId.cs` | Strong `Guid` wrapper; a `readonly record struct` has value semantics without a heap allocation in typical use. |
| `Portfolios/InstrumentId.cs` | Normalized, validated instrument identifier value object. It demonstrates a factory and private constructor. |
| `Portfolios/Currency.cs` | Three-letter normalized currency value object. It validates shape, not whether every code is currently assigned by ISO. |
| `Portfolios/Position.cs` | Immutable linear position with signed quantity and derived market value. It demonstrates a record containing behavior. |
| `Portfolios/Portfolio.cs` | Aggregate root that enforces name, currency, non-empty positions, and no duplicate instruments. It exposes a read-only position collection and net/gross values. |
| `Risk/HistoricalScenario.cs` | Immutable dated risk-factor return map. It copies input collections so callers cannot mutate a calculation underneath the engine. |
| `Risk/IRiskCalculator.cs` | Domain strategy boundary so later historical, parametric, Monte Carlo, or stress calculators can share a deliberate contract. |
| `Risk/RiskReport.cs` | Domain output plus per-scenario P&L/loss. All monetary fields use the portfolio base currency. |
| `Risk/HistoricalSimulationRiskCalculator.cs` | Pure risk service implementing revaluation, nearest-rank VaR, tail-average ES, worst loss, and volatility. This is the core finance code to study first. |

## Application project

`src/TradingRisk.Application/TradingRisk.Application.csproj` references only
Domain. It knows the use cases but not ASP.NET Core or a database.

| File | Purpose and lesson |
|---|---|
| `Abstractions/IPortfolioRepository.cs` | Output port owned by the use-case layer. Dependency inversion lets infrastructure depend on the interface rather than business code depending on storage. |
| `Common/RequestValidationException.cs` | Represents transport-neutral invalid use-case input. |
| `Common/PortfolioNotFoundException.cs` | Represents a missing portfolio without embedding an HTTP status in Application. |
| `Portfolios/PortfolioDto.cs` | Application read model and explicit Domain-to-DTO mapping. A DTO is not automatically a domain entity. |
| `Portfolios/CreatePortfolio.cs` | Command, input record, and handler that construct positions/portfolio and persist it asynchronously. |
| `Portfolios/GetPortfolio.cs` | Small query handler demonstrating lookup, strongly typed ID conversion, cancellation, and not-found behavior. |
| `Risk/CalculatePortfolioRisk.cs` | Query/input/output records and handler coordinating repository, scenario mapping, risk strategy, and `TimeProvider`. |

Command and query classes are kept together with their handler here because the
slice is small. A growing codebase can split files or organize by feature, but
folder count is not architecture.

## Infrastructure project

`src/TradingRisk.Infrastructure/TradingRisk.Infrastructure.csproj` references
Application and Domain because it implements an Application-owned port with
Domain objects.

| File | Purpose and lesson |
|---|---|
| `Persistence/InMemoryPortfolioRepository.cs` | Thread-safe process-local adapter using `ConcurrentDictionary`. It is a singleton and loses data on restart. Replace it in Milestone 2 without changing Domain. |

## API project

`src/TradingRisk.Api/TradingRisk.Api.csproj` uses the Web SDK, references all
layers to act as the composition root, and adds the official OpenAPI packages.
The direct `Microsoft.OpenApi` reference is a security override for
GHSA-v5pm-xwqc-g5wc; removing it currently restores a vulnerable transitive
version.

| File | Purpose and lesson |
|---|---|
| `Program.cs` | Top-level entry point, middleware order, options validation, DI lifetimes, rate limiting, health/OpenAPI endpoints, and application startup. The visible partial `Program` enables in-process tests; the `Testing` environment skips HTTPS redirection because `TestServer` has no TLS listener. |
| `Contracts/PortfolioRequests.cs` | JSON request shapes and boundary-level Data Annotations. On positional records, ASP.NET Core validation metadata targets constructor parameters. Decimal range limits use invariant culture so deployment locale cannot change validation. Contracts are separate from domain types to prevent transport concerns leaking inward. |
| `Controllers/PortfoliosController.cs` | Versioned REST adapter for create/get/calculate. It remains thin, passes cancellation, maps contracts, and uses source-generated/cached structured logging to avoid per-call template allocations. |
| `ErrorHandling/ApiExceptionHandler.cs` | Central exception-to-Problem-Details mapping, safe handling of unexpected errors, and source-generated logging with stable event IDs. |
| `Options/RiskApiOptions.cs` | Strongly typed configuration for default confidence and request-size limit. |
| `appsettings.json` | Non-secret defaults and logging configuration. Environment variables can override nested keys with `__`, for example `RiskApi__MaxScenarioCount`. |
| `appsettings.Development.json` | Development-only logging override selected by `ASPNETCORE_ENVIRONMENT`. |
| `Properties/launchSettings.json` | Local Rider/Visual Studio/CLI launch profiles. It is not production deployment configuration. |
| `TradingRisk.Api.http` | Rider/Visual Studio HTTP client request collection for manually walking the vertical slice. |

### Registered lifetimes

| Registration | Lifetime | Reason |
|---|---|---|
| `InMemoryPortfolioRepository` | singleton | Process-local data must survive across requests; its collection and stored aggregates are thread-safe/immutable. |
| `HistoricalSimulationRiskCalculator` | singleton | Pure, stateless, thread-safe service. |
| `TimeProvider.System` | singleton | Clock abstraction contains no request state. |
| use-case handlers | scoped | Conventional per-request ownership and ready for a future scoped `DbContext`. |

## Test project

`tests/TradingRisk.Tests/TradingRisk.Tests.csproj` is an executable xUnit v3
test project using Microsoft Testing Platform. It references the API for
in-process tests and the inner projects for focused tests.

| File | Purpose and lesson |
|---|---|
| `GlobalUsings.cs` | Makes xUnit types available to all test files without repeated `using Xunit`. |
| `Domain/PositionTests.cs` | Small value/validation tests. |
| `Domain/PortfolioTests.cs` | Aggregate invariants and long/short exposure behavior. |
| `Domain/HistoricalSimulationRiskCalculatorTests.cs` | Deterministic formula tests, missing-data failure, and confidence validation. This is also an executable finance specification. |
| `Application/CalculatePortfolioRiskHandlerTests.cs` | Handler test with real in-memory adapter and a fake `TimeProvider`, avoiding ambient clock flakiness. |
| `Api/PortfolioApiTests.cs` | Boots the real ASP.NET app in memory and tests JSON/HTTP, DI, storage, handler, and domain calculation together. It disables configuration reload only in the restricted test process because the workspace blocks the native file watcher. |

The test pyramid here is intentional: many fast domain tests, fewer handler
tests, and one broad HTTP test. Later PostgreSQL tests belong between handler
and full end-to-end coverage.

## Documentation files

| File | Purpose |
|---|---|
| `docs/00-learning-path.md` | Ordered curriculum from language basics through persistence, messaging, security, operations, advanced risk, and architecture. |
| `docs/01-java-to-csharp-dotnet.md` | Translation guide and important semantic differences for a Spring developer. |
| `docs/02-architecture-and-file-guide.md` | This complete component/file catalog and request flow. |
| `docs/03-risk-metrics.md` | Formulas, worked example, terminology, assumptions, limitations, and primary references. |
| `docs/04-exercises.md` | Implementation labs with requirements and review questions. |
| `docs/05-build-dependencies-startup-and-packaging.md` | Detailed project/dependency syntax, restore/build/test/publish behavior, Spring startup comparison, DI, configuration, CI, and Docker packaging. |
| `docs/06-plantuml-diagrams.md` | Diagram index, interpretation guide, and Rider/CLI rendering instructions. |
| `docs/07-architecture-and-domain-design-deep-dive.md` | Project boundaries, Clean/Hexagonal Architecture, DDD vocabulary, commands/queries/DTOs, validation layers, immutability, persistence seams, and modular-monolith trade-offs. |
| `docs/08-aspnet-core-web-api-deep-dive.md` | Kestrel/host, middleware, routing, controllers, model binding, validation, options, errors, logging, rate limiting, health, OpenAPI, cancellation, async, and debugging. |
| `docs/09-testing-dotnet-and-risk-deep-dive.md` | xUnit v3 syntax, test project anatomy, test doubles, deterministic time, `WebApplicationFactory`, isolation, numeric/risk assertions, and future database tests. |
| `docs/10-production-scale-dotnet-deep-dive.md` | EF Core, transactions, async/parallelism, durable jobs/messaging, `HttpClient`, caching, security, observability, performance, scaling, deployment, and model governance. |
| `docs/diagrams/01-component-dependencies.puml` | Compile-time project references and inward dependency direction. |
| `docs/diagrams/02-startup-comparison.puml` | Spring Boot and ASP.NET Core process-start sequence side by side. |
| `docs/diagrams/03-risk-request-sequence.puml` | Successful and not-found runtime flow for the historical-risk endpoint. |
| `docs/diagrams/04-build-and-publish.puml` | Source, restore, build, test, publish, and runtime artifact flow. |
| `docs/diagrams/05-di-lifetimes.puml` | Root provider, request scopes, shared singletons, and scoped handlers. |
| `docs/diagrams/06-domain-model.puml` | Portfolio, positions, scenarios, risk strategy, and report relationships. |

## Rules that protect the architecture

- Domain must never reference Application, Infrastructure, or API.
- Application may reference Domain only.
- Infrastructure implements Application ports and may use Domain types.
- API owns HTTP and dependency composition.
- Domain collections are not exposed as mutable implementation types.
- Controllers map and delegate; they do not decide risk methodology.
- External inputs never become trusted merely because nullable analysis says a
  property is non-null.
- Each risk output makes sign, currency, confidence, observation count, and
  calculation time visible.
