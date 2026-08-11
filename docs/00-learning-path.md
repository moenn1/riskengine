# Learning path: Java/Spring Boot to enterprise .NET

The goal is not to memorize a second spelling of Java. It is to learn the
different defaults of C#, the .NET runtime, ASP.NET Core, and the engineering
practices around them while building one coherent risk system.

Use two passes for each milestone:

1. read the named code and run its tests;
2. implement the exercise without looking at a finished solution, then explain
   the trade-offs aloud as if reviewing it with your new team.

## Milestone 0 — tooling and language orientation

Status: repository baseline is implemented.

Learn:

- SDK versus runtime, `dotnet` CLI, NuGet, MSBuild, solution and project files;
- assemblies, namespaces, access modifiers, classes, records, and value types;
- nullable reference types and `required`/`init` properties;
- properties versus JavaBean accessors;
- collection expressions, LINQ, pattern matching, and extension methods;
- exceptions, `IDisposable`/`await using`, generics, delegates, and events;
- `Task`, `async`/`await`, cancellation, and why async does not mean “new thread”;
- dependency-injection lifetimes: singleton, scoped, and transient.

Read:

- root build files;
- `Domain/Portfolios`;
- [Java/Spring to C#/.NET map](01-java-to-csharp-dotnet.md);
- [build, dependencies, startup, and packaging deep dive](05-build-dependencies-startup-and-packaging.md).

Exit test: you can explain every line in `Position`, `Portfolio`, and
`Program.cs`, including why `decimal` is used for money-like values and `double`
is used for square roots.

## Milestone 1 — complete HTTP-to-domain vertical slice

Status: implemented.

Learn:

- ASP.NET Core startup and middleware;
- controllers, routing, model binding, Data Annotations, DTOs, and HTTP status
  codes;
- options binding and startup validation;
- explicit dependency registration;
- use-case handlers and dependency inversion;
- structured logging, exception mapping, Problem Details, health checks, rate
  limiting, and OpenAPI;
- `wwwroot`, default files, same-origin `fetch`, semantic HTML, responsive CSS,
  accessible result rendering, and client-side Problem Details handling;
- unit tests, handler tests, and in-process API tests;
- empirical VaR, Expected Shortfall, P&L distributions, and volatility.

Trace this request:

```text
POST /api/v1/portfolios/{id}/risk
  -> PortfoliosController
  -> CalculatePortfolioRiskHandler
  -> IPortfolioRepository + IRiskCalculator
  -> HistoricalSimulationRiskCalculator
  -> RiskReportDto
```

Read:

- [architecture and domain-design deep dive](07-architecture-and-domain-design-deep-dive.md);
- [ASP.NET Core web API deep dive](08-aspnet-core-web-api-deep-dive.md);
- [browser UI deep dive](11-browser-ui-deep-dive.md);
- [risk metrics and finance foundations](03-risk-metrics.md);
- [.NET and risk testing deep dive](09-testing-dotnet-and-risk-deep-dive.md);
- [GitHub-rendered Mermaid diagrams](06-mermaid-diagrams.md).

Exit test: change a formula, predict which test fails, and explain why the
controller and browser do not calculate risk themselves.

## Milestone 2 — durable persistence

Status: the SQLite/EF Core learning foundation is implemented. The production
database transition remains an exercise.

Implemented:

- an embedded SQLite database with a configurable connection string;
- an EF Core `RiskDbContext` scoped to each request;
- separate persistence entities and Fluent API mappings;
- an EF implementation of `IPortfolioRepository`;
- an Application-owned query port with filter, `Any`, `Count`, ordering,
  pagination, relationship loading, and grouped projections;
- a checked-in initial migration and repository-local `dotnet-ef` tool;
- database-aware health checks and isolated temporary-SQLite tests.

Read:

- [EF Core, SQLite, and LINQ deep dive](12-ef-core-sqlite-linq-deep-dive.md);
- [persistence diagrams](06-mermaid-diagrams.md#8-ef-core-persistence);
- the persistence sections of the
  [production-scale deep dive](10-production-scale-dotnet-deep-dive.md).

Build next:

- PostgreSQL in `compose.yaml`;
- optimistic concurrency using a version column;
- transaction boundaries and integration tests with a real disposable database.

Learn:

- EF Core change tracking versus JPA/Hibernate sessions;
- `DbContext` lifetime and why it is normally scoped;
- LINQ expression trees versus in-memory `IEnumerable` operations;
- `AsNoTracking`, projections, split queries, N+1 queries, indexes, pooling;
- transaction isolation, write conflicts, and retry safety.

Finance addition: persist instruments, positions, market-data observations, and
risk calculation runs with immutable “as-of” timestamps.

Exit test: two concurrent updates cannot silently overwrite one another, and a
calculation can be reproduced from persisted inputs.

Use SQLite to learn the APIs, then measure and test the actual provider used by
your team: providers differ in types, SQL translation, locking, migrations, and
performance.

## Milestone 3 — market data ingestion and data quality

Build:

- CSV ingestion with `IAsyncEnumerable<T>`;
- an external market-data HTTP adapter using `HttpClientFactory`;
- typed clients, timeouts, retries, and circuit breaking;
- trading-calendar and missing-data policies;
- validation/quarantine tables and lineage metadata.

Learn:

- streaming instead of loading large files into memory;
- `HttpClient` socket lifetime;
- resilience only around safe/repeatable operations;
- bounded parallelism, `Channel<T>`, cancellation, and backpressure.

Finance addition: simple returns, log returns, adjusted prices, corporate
actions, FX conversion, and synchronized observation windows.

Exit test: a bad observation is explainable and quarantined; it cannot silently
change a risk result.

## Milestone 4 — asynchronous risk jobs

Build:

- `POST /risk-jobs` returning `202 Accepted`;
- `BackgroundService` for local development;
- a production message-broker adapter;
- idempotency keys;
- transactional outbox and consumer inbox;
- job status and retry/dead-letter handling.

Learn:

- at-least-once delivery and why “exactly once” is usually an application
  illusion;
- idempotent consumers, correlation IDs, eventual consistency;
- graceful shutdown, poison messages, and deployment compatibility.

Finance addition: calculations for large portfolios split by risk-factor or
scenario batches, followed by deterministic aggregation.

Exit test: submitting the same idempotency key twice creates one logical job,
and redelivery cannot duplicate the published result.

## Milestone 5 — expand the risk engine

Add in this order:

1. parametric/delta-normal VaR with a covariance matrix;
2. component and marginal VaR;
3. named stress scenarios and reverse stress testing;
4. VaR backtesting and exception counts;
5. option pricing, delta/gamma/vega, and full revaluation;
6. fixed-income duration, convexity, and PV01/DV01;
7. counterparty exposure and introductory credit metrics.

Learn:

- matrix operations, numerical precision, deterministic reproducibility;
- strategy/policy patterns without “interface for everything”;
- versioning models and calculation methodology;
- golden datasets and property-based tests;
- model validation separate from ordinary software testing.

Exit test: every report states methodology, input snapshot, units, confidence,
horizon, valuation time, and model version.

## Milestone 6 — production API and security

Build:

- OAuth 2.0/OIDC authentication;
- policy-based authorization for trader, risk manager, and auditor;
- tenant or desk-level data isolation;
- secret management and key rotation;
- pagination, filtering, API versioning, idempotency, and request-size limits;
- audit trails that do not leak sensitive position data into logs.

Learn:

- authentication versus authorization;
- claims and policies;
- OWASP API risks, TLS, CORS, CSRF boundaries, safe serialization;
- backward-compatible API and event evolution.

Exit test: authorization is tested at the HTTP boundary and at sensitive use
cases; logs contain identifiers but no portfolio payloads.

## Milestone 7 — observability, performance, and operations

Build:

- OpenTelemetry traces, metrics, and structured logs;
- domain metrics for job latency, scenario throughput, failures, and staleness;
- dashboards, service-level objectives, and alerts;
- caching with explicit freshness and invalidation rules;
- BenchmarkDotNet microbenchmarks plus realistic load tests;
- container resource limits, readiness, graceful shutdown, and deployment
  manifests.

Learn:

- GC generations, allocations, pooling, `Span<T>`/`Memory<T>` only after
  profiling;
- thread-pool starvation and sync-over-async;
- cardinality limits in metrics;
- scaling stateless compute separately from storage;
- deployment rollback and database compatibility.

Exit test: you can locate a slow risk job from an HTTP trace through its worker
and database calls, and you have measured rather than guessed at the bottleneck.

## Milestone 8 — architecture at scale

Keep the modular monolith until evidence supports extraction. Then evaluate:

- ownership and bounded contexts;
- independent scaling/deployment needs;
- data ownership and consistency requirements;
- operational cost and failure modes;
- event contracts and schema governance.

Likely bounded contexts are Portfolio, Market Data, Pricing, Risk Calculation,
Limits, and Reporting. They do not automatically need to be microservices.

Exit test: you can defend either keeping a module in-process or extracting it
with concrete throughput, ownership, release, and reliability evidence.

The [production-scale .NET deep dive](10-production-scale-dotnet-deep-dive.md)
connects persistence, jobs, messaging, security, observability, performance,
and deployment into one evolution path.

## Topics to ask your new team in week one

- Which .NET target and SDK feature band are supported?
- Do they use controllers or minimal APIs? EF Core, Dapper, or both?
- What are their nullable, analyzer, formatting, and warning policies?
- Which test framework, mocking library, and integration-test approach?
- How are configuration and secrets delivered in each environment?
- What are the logging, tracing, metrics, and correlation conventions?
- What messaging guarantees and idempotency rules exist?
- How are migrations deployed and rolled back?
- Which risk methodology, confidence level, horizon, quantile convention, and
  pricing source are authoritative?
- Who owns model validation and sign-off?
