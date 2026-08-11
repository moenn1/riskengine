# Deep dive: architecture, domain modeling, and application flow

This chapter explains why the solution is divided into Domain, Application,
Infrastructure, and API projects, what belongs in each one, and how that maps to
a large Java/Spring codebase.

The architecture is intentionally more important than the number of projects.
The goal is to keep business decisions testable and independent of delivery and
storage technology.

## 1. Start with the dependency rule

Open the
[component dependency diagram](06-mermaid-diagrams.md#1-compile-time-component-dependencies) while
reading this section.

The production dependency graph is:

```text
TradingRisk.Api ──────────────┐
    │                         │
    v                         v
TradingRisk.Application <- TradingRisk.Infrastructure
    │                         │
    └──────────> TradingRisk.Domain
```

An arrow means “the project at the tail can name public types from the project
at the arrowhead.” It is a compile-time relationship created by
`<ProjectReference>`.

The central rule is:

> Dependencies point toward business policy, not toward technical mechanisms.

Domain does not know:

- whether input came from HTTP, a message, a CLI, or a scheduled job;
- whether portfolios live in PostgreSQL, memory, or another service;
- whether JSON is serialized with ASP.NET Core;
- which DI container, logger, or configuration system is used.

This is the Dependency Inversion Principle at module scale. High-level policy
defines the abstraction it needs; a lower-level adapter implements it.

## 2. Java package boundaries versus .NET project boundaries

A common Spring Boot layout is:

```text
com.acme.risk
├── api
├── application
├── domain
└── infrastructure
```

If all packages are in one Maven module, `domain` can still import
`infrastructure` unless an architecture test or team convention prevents it.

This repository makes each major boundary a `.csproj`. If Domain does not have
a `ProjectReference` to Infrastructure, code in Domain cannot compile when it
tries to use `SqlitePortfolioRepository` or `RiskDbContext`. The compiler becomes an
architecture guard.

This does not mean “one project per folder” is always best. Project boundaries
have costs:

- more build graph nodes;
- more references to maintain;
- public APIs are required across assembly boundaries;
- cyclic project references are rejected;
- too many tiny projects make navigation and builds harder.

Use a project boundary when you want a meaningful dependency boundary,
independent testing or packaging, or a clear ownership seam. Use a folder or
namespace for ordinary organization.

## 3. The four projects

## 3.1 Domain: business truth

Domain contains the concepts and rules that would remain if HTTP, the database,
and ASP.NET Core disappeared:

```text
Portfolio
Position
PortfolioId
InstrumentId
Currency
HistoricalScenario
IRiskCalculator
HistoricalSimulationRiskCalculator
RiskReport
```

Its current project file is empty except for the SDK declaration because it
inherits shared compiler settings and uses no external package:

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

That emptiness is a useful signal. The risk math can run in an API, worker,
console tool, test, or notebook adapter without pulling in a web framework.

### Spring comparison

This resembles a plain Java domain module containing no Spring annotations.
It avoids:

```java
@Component
@Entity
@ConfigurationProperties
@JsonProperty
```

on the domain types. Framework annotations are convenient, but attaching every
framework role to the same class often couples persistence shape, JSON shape,
validation shape, and business behavior.

### What belongs here

- invariants that must always hold;
- value objects and aggregates;
- calculations and domain policies;
- domain-specific exceptions;
- interfaces for interchangeable domain strategies, when genuine alternatives
  exist.

### What does not belong here

- HTTP status codes and route strings;
- database queries, connection strings, or EF Core mappings;
- JSON attributes;
- logging of request or infrastructure details;
- environment-specific configuration;
- orchestration that is specific to a use case.

## 3.2 Application: use cases and ports

Application describes what users or systems can ask the software to do:

- create a portfolio;
- get a portfolio;
- calculate portfolio risk.

A handler coordinates a use case:

```csharp
public sealed class CalculatePortfolioRiskHandler(
    IPortfolioRepository repository,
    IRiskCalculator riskCalculator,
    TimeProvider timeProvider)
{
    public async Task<RiskReportDto> HandleAsync(
        CalculatePortfolioRiskQuery query,
        CancellationToken cancellationToken)
    {
        // validate input
        // load the aggregate
        // create domain values
        // invoke domain policy
        // map the result
    }
}
```

Application decides the order of operations, but delegates business
calculations to Domain and storage mechanics to a port.

### Spring comparison

This is closest to an application service:

```java
@Service
public final class CalculatePortfolioRiskHandler {
    private final PortfolioRepository repository;
    private final RiskCalculator riskCalculator;
    private final Clock clock;

    // constructor

    public RiskReportDto handle(
            CalculatePortfolioRiskQuery query,
            CancellationTokenLike token) {
        // ...
    }
}
```

The C# class has no `[Service]` attribute. It becomes a service because
`Program.cs` explicitly registers it:

```csharp
builder.Services.AddScoped<CalculatePortfolioRiskHandler>();
```

The difference is discovery, not purpose.

### Input and output ports

Hexagonal Architecture uses “port” for an interface at an application
boundary:

- an input port exposes a use case to an adapter;
- an output port describes something the use case needs from the outside.

The handler itself acts as a simple input port. `IPortfolioRepository` is an
output port:

```csharp
public interface IPortfolioRepository
{
    Task AddAsync(Portfolio portfolio, CancellationToken cancellationToken);

    Task<Portfolio?> GetByIdAsync(
        PortfolioId portfolioId,
        CancellationToken cancellationToken);
}
```

Application owns this interface because Application is the consumer that
defines the capability it needs. Infrastructure depends inward and implements
that contract.

This is the inversion:

```text
runtime calls:       Handler ──calls──> Repository object
compile-time types:  Infrastructure ──implements──> Application interface
```

Runtime call direction and compile-time dependency direction are not always the
same.

## 3.3 Infrastructure: technical adapters

Infrastructure contains implementations using a particular technology. The
production adapter is:

```csharp
public sealed class SqlitePortfolioRepository(RiskDbContext dbContext)
    : IPortfolioRepository, IPortfolioQueries
```

The original adapter remains as a focused Application-test fake:

```csharp
public sealed class InMemoryPortfolioRepository : IPortfolioRepository
```

The application handler should not change merely because storage changes.
Reality is more nuanced: a production persistence requirement may reveal that
the port is incomplete, too chatty, or has the wrong transaction semantics. It
is acceptable to evolve the interface. The purpose is controlled coupling, not
pretending infrastructure never influences design.

### Spring comparison

Spring often combines the port and adapter through Spring Data:

```java
public interface PortfolioJpaRepository
        extends JpaRepository<PortfolioEntity, UUID> {
}
```

That is productive for CRUD, but the interface inherits persistence concepts.
In this solution the Application port stays technology-neutral, and an adapter
can use EF Core, Dapper, HTTP, or memory internally.

### Why Infrastructure references Domain

The EF adapter reconstructs `Portfolio` objects through Domain factories. It
therefore needs the Domain assembly. It also implements Application interfaces,
so it needs Application. Those are the two `<ProjectReference>` entries in its
project file.

## 3.4 API: inbound adapter and composition root

API has two roles.

First, it is an HTTP adapter:

- match routes;
- bind and validate HTTP input;
- map contracts to application messages;
- select HTTP status codes;
- serialize output;
- convert failures to Problem Details.

Second, it is the composition root:

- choose `SqlitePortfolioRepository` for the repository/query ports;
- configure scoped `RiskDbContext` with the SQLite connection;
- choose `HistoricalSimulationRiskCalculator` for `IRiskCalculator`;
- choose object lifetimes;
- bind configuration;
- order middleware;
- start the process.

Because composition needs to see interfaces and concrete implementations, API
references Application, Domain, and Infrastructure. That is expected at the
outermost boundary.

### Why controllers stay thin

The controller is responsible for HTTP policy, not domain policy. For example,
the maximum number of scenarios is currently an API resource-protection rule:

```csharp
if (request.Scenarios.Count > options.Value.MaxScenarioCount)
{
    throw new RequestValidationException(...);
}
```

The confidence interval and non-empty-scenario rules are also checked in
Application/Domain because those rules remain meaningful if the same use case
is invoked by a message consumer.

If a controller calculated VaR directly:

- the calculation would be coupled to HTTP;
- a background worker would duplicate it;
- unit tests would need controller concerns;
- transaction and authorization rules would become scattered.

## 4. Clean Architecture, Hexagonal Architecture, and DDD

These terms overlap, but they are not synonyms.

### Clean Architecture

Clean Architecture emphasizes concentric policy boundaries and inward
dependencies. Details such as UI, databases, and frameworks stay outside.

### Hexagonal Architecture

Hexagonal or Ports-and-Adapters Architecture emphasizes interaction boundaries:
the application exposes and consumes ports; adapters connect HTTP, storage,
messaging, or other systems.

### Domain-Driven Design

DDD is about modeling a business domain with a shared language, explicit
boundaries, and behavior-rich models. Tactical patterns include value objects,
entities, aggregates, repositories, and domain services. Strategic patterns
include bounded contexts and context maps.

This repository borrows useful pieces from all three. It is not proof that
every enterprise system needs exactly four assemblies.

## 5. Domain model vocabulary in this project

Use the [domain model diagram](06-mermaid-diagrams.md#6-domain-model) with this
section.

## 5.1 Value object

A value object is identified by its value rather than an independent lifecycle.
Examples:

```csharp
public readonly record struct PortfolioId(Guid Value);
```

```csharp
public readonly record struct Currency
{
    public string Code { get; }
    public static Currency From(string? code) { ... }
}
```

Two `Currency` values containing `USD` are equal. There is no reason to track
which physical USD object was created first.

Benefits:

- invalid raw values are rejected at creation;
- equality follows the represented value;
- method signatures express meaning;
- `PortfolioId` cannot be silently confused with an unrelated `Guid`;
- normalization occurs in one place.

Java equivalents include a `record` with a compact constructor or a final class
with value-based `equals`/`hashCode`.

### Why a factory and private constructor

`Currency.From(" usd ")` normalizes to `USD`. A public constructor accepting any
string would allow invalid state. The private constructor means all callers
must pass through validation.

This is sometimes called “make invalid states unrepresentable.” It is an
aspiration, not an absolute: deserializers, reflection, persistence tools, or
default value types can introduce subtleties. For example, the default value of
a struct can bypass its factory, so public APIs must still be designed
carefully.

## 5.2 Entity

An entity has identity across time even if its attributes change. `Portfolio`
has a `PortfolioId`, so conceptually it is an entity.

The current portfolio is immutable, but immutability does not stop it being an
entity. A later update operation could return a new portfolio snapshot with the
same ID and a higher version.

## 5.3 Aggregate and aggregate root

An aggregate is a consistency boundary. The root is the only object external
code uses to modify the aggregate.

`Portfolio` owns positions and enforces:

- a name exists and is at most 100 characters;
- base currency is valid;
- at least one position exists;
- no duplicate instrument exists.

The API cannot reach into an exposed `List<Position>` and add a duplicate,
because `Positions` is an `IReadOnlyList<Position>` backed by a copied
read-only collection.

For persistence, an aggregate often defines a transaction boundary: one
transaction should preserve its invariants. Do not make an aggregate enormous
merely because objects are connected. A portfolio with millions of lots may
need a different model and consistency strategy.

## 5.4 Domain service

`HistoricalSimulationRiskCalculator` represents domain behavior that uses a
portfolio and many scenarios but does not naturally belong to one of those
objects:

```csharp
public sealed class HistoricalSimulationRiskCalculator : IRiskCalculator
```

It is:

- pure for the same input;
- stateless;
- independent of storage and HTTP;
- synchronous because it performs in-memory CPU work.

That makes it safe to register as a singleton.

Avoid turning every calculation into a service. Behavior that naturally
maintains one object's invariant often belongs on that object.

## 5.5 Repository

A repository presents aggregate persistence in domain-oriented terms. This
port stores and retrieves whole `Portfolio` aggregates:

```csharp
Task AddAsync(Portfolio portfolio, CancellationToken cancellationToken);
Task<Portfolio?> GetByIdAsync(PortfolioId id, CancellationToken cancellationToken);
```

It does not expose SQL, `DbSet`, or `IQueryable`. Exposing `IQueryable` across
the boundary would let outer query-provider behavior leak into Application and
make performance unpredictable.

For rich read screens, a separate query service can return projections without
forcing every read through an aggregate repository.

## 6. Commands, queries, handlers, DTOs, and contracts

These names describe different jobs.

### Command

`CreatePortfolioCommand` requests a state change:

```csharp
public sealed record CreatePortfolioCommand(
    string? Name,
    string? BaseCurrency,
    IReadOnlyCollection<CreatePositionInput>? Positions);
```

### Query

`CalculatePortfolioRiskQuery` asks for a calculation result. It currently does
not persist a risk run:

```csharp
public sealed record CalculatePortfolioRiskQuery(...);
```

“Command/query” here is a naming and responsibility convention. The project
does not need a mediator package to apply it.

### Handler

A handler provides one entry point for a use case. Constructor dependencies
make requirements explicit and testable.

### HTTP contract

`CalculateRiskRequest` describes JSON accepted at the API boundary. It carries
Data Annotations used by ASP.NET Core.

### Application DTO

`RiskReportDto` is a transport-neutral output from Application. The API can
serialize it, while another adapter could map it to an event.

### Domain object

`RiskReport` expresses the result in validated domain terms.

The mapping is deliberate:

```text
JSON
  -> CalculateRiskRequest       API contract
  -> CalculatePortfolioRiskQuery
  -> HistoricalScenario        Domain value
  -> RiskReport                Domain result
  -> RiskReportDto             Application output
  -> JSON
```

This appears repetitive, but each boundary can evolve for different reasons:

- the HTTP schema may be versioned;
- use-case input may come from HTTP or messaging;
- the domain may add behavior that should not be serialized;
- persistence may use a different schema;
- output can hide internal fields.

For a tiny CRUD service, fewer models may be reasonable. Add separation where
change, security, or invariants justify it.

## 7. Validation has layers

“Validate once at the edge” is insufficient because each layer protects a
different promise.

### API shape validation

Examples:

- required JSON members;
- string length;
- parsable GUID/date/decimal;
- request collection size.

These are HTTP contract and resource-protection concerns.

### Application use-case validation

Examples:

- a portfolio ID must not be empty;
- scenarios are required for this calculation;
- scenario dates must be unique;
- the confidence level supported by this use case starts at 50%.

These remain true if the handler is called outside HTTP.

### Domain invariant validation

Examples:

- zero quantity is not a position;
- a return cannot be less than -100%;
- every scenario needs each portfolio instrument;
- a portfolio has no duplicate instruments.

No caller should be able to create a valid domain object that violates these
rules.

Duplicated-looking checks can be correct when they protect different
boundaries. The important thing is to avoid inconsistent rules and provide one
authoritative domain invariant.

## 8. Immutability and thread safety

The in-memory fake may be accessed concurrently by tests or a future local
composition. Two choices make that adapter safe:

1. the dictionary is a `ConcurrentDictionary`;
2. stored `Portfolio` aggregates are immutable snapshots.

A thread-safe collection only protects its own internal operations. It would
not make a mutable `Portfolio.Positions` list safe. Conversely, immutable
values alone would not make concurrent writes to a regular `Dictionary` safe.

### Copying input collections

`Portfolio.Create` converts the input enumeration to an array, and the
constructor creates its own array before wrapping it:

```csharp
_positions = new ReadOnlyCollection<Position>(positions.ToArray());
```

Without a defensive copy, a caller could keep a reference to the original list
and mutate the portfolio indirectly.

`IReadOnlyList<T>` describes what operations are exposed through that
reference; it does not prove the underlying collection can never change.
Ownership and copying still matter.

The production EF adapter takes a different approach: repository and
`RiskDbContext` are scoped per request. A context is stateful and not
thread-safe, so callers must await one operation before using that instance
again. Scoped lifetime avoids sharing it between concurrent requests.

## 9. Transactions and consistency with persistence

The SQLite EF adapter now answers the first learning-level questions:

- creating a portfolio writes the aggregate across portfolio and position
  tables;
- one `SaveChangesAsync` is the transaction boundary for that aggregate;
- no-tracking reads reconstruct the domain aggregate through explicit mapping.

The next production questions remain:

- How are concurrent changes detected?
- Should calculation read a consistent position/market-data snapshot?
- Is the result persisted with its exact input versions?

A normal scoped flow might become:

```text
HTTP request scope
  -> scoped handler
  -> scoped repository
  -> scoped RiskDbContext
  -> transaction / SaveChangesAsync
```

Do not put `DbContext` in Domain. Infrastructure maps between persistence
representation and domain representation.

### Optimistic concurrency

Add a version token to detect lost updates:

```text
client read version 7
client submits update expected version 7
database updates only where id = ... and version = 7
0 affected rows => somebody else changed it
```

The API can translate that application conflict to HTTP `409 Conflict`.

### Reproducible risk snapshots

A production risk result should reference immutable versions of:

- positions/trades;
- prices and market data;
- calendars and mappings;
- model/methodology;
- configuration;
- code or deployment.

“Recalculate portfolio 123” is not reproducible if portfolio 123 and its
market data mutate after the run.

## 10. When to add abstractions

Interfaces are valuable at boundaries and where policies genuinely vary. They
also have costs.

Good current abstractions:

- `IPortfolioRepository`: the mechanism will change from memory to a database;
- `IRiskCalculator`: risk methodology is a genuine strategy;
- `TimeProvider`: time is nondeterministic and tests need control.

Probably unnecessary today:

- `IPortfolioDtoMapper` with one trivial implementation;
- `IPositionFactory` around one pure factory;
- one interface for every handler merely to satisfy a rule.

A useful question is: “Which change, test seam, or ownership boundary does this
abstraction protect?” If there is no concrete answer, keep the code direct.

## 11. Modular monolith versus microservices

This solution is one process and one deployment. It is modular because its
compile-time boundaries are explicit.

Benefits:

- local calls and transactions remain simple;
- one debugger can follow the request;
- deployment and observability are cheaper;
- refactoring contracts is easier;
- no distributed failure is introduced prematurely.

Extract a module only when evidence justifies an independent service, such as:

- a different team owns and releases it;
- it needs materially different scaling;
- its availability boundary must be isolated;
- its data and consistency boundary is genuinely independent;
- regulatory or security isolation requires it.

Extraction adds network latency, partial failure, retries, idempotency,
contract/version governance, distributed tracing, deployment coordination, and
eventual consistency. A folder structure alone does not pay those costs.

Likely future bounded contexts are Portfolio, Market Data, Pricing, Risk
Calculation, Limits, and Reporting. “Bounded context” describes a model and
language boundary; it does not automatically mean “microservice.”

## 12. Trace one complete risk request

Use the
[risk sequence diagram](06-mermaid-diagrams.md#3-risk-request-sequence), then follow
these files:

1. `Contracts/PortfolioRequests.cs` defines accepted JSON.
2. `PortfoliosController.CalculateRiskAsync` applies API policy and maps input.
3. `CalculatePortfolioRiskHandler.HandleAsync` validates the use case.
4. `IPortfolioRepository.GetByIdAsync` loads the aggregate through a port.
5. `HistoricalScenario.Create` validates scenario data.
6. `HistoricalSimulationRiskCalculator.Calculate` performs risk math.
7. the handler maps `RiskReport` to `RiskReportDto`.
8. the controller returns `Ok(result)`.
9. ASP.NET Core serializes the body and writes HTTP 200.

For the failure path:

1. repository returns `null`;
2. handler throws `PortfolioNotFoundException`;
3. exception unwinds through controller and middleware;
4. `ApiExceptionHandler` maps it to status 404;
5. `IProblemDetailsService` writes a stable JSON error shape.

If you can explain why each step belongs in its layer, you understand the
architecture better than someone who merely recognizes its folder names.

## 13. Architecture review checklist

For every new feature, ask:

- Which rule is HTTP-specific, use-case-specific, or a domain invariant?
- Which project owns the interface?
- Does any inner project import a framework or outer project unnecessarily?
- Is the aggregate boundary small enough for consistent updates?
- Are DTOs exposing internal or persistence-only fields?
- Is mutable state shared across requests?
- Does an async boundary represent real I/O?
- Where is the transaction boundary?
- Can the exact risk result be reproduced?
- Would another adapter duplicate business logic?
- Is a new interface protecting a real variation or only adding indirection?
- Is a proposed service boundary justified by ownership and operational needs?

## Official references

- [Common web application architectures](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [Dependency injection in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0)
- [Spring beans and dependency injection](https://docs.spring.io/spring-boot/reference/using/spring-beans-and-dependency-injection.html)
