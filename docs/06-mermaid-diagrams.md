# Mermaid architecture and runtime diagrams

These diagrams live directly inside Markdown `mermaid` fences, so GitHub
renders them on the documentation page without a separate diagram plugin,
server, generated image, or checked-in binary. The diagram source and its
explanation therefore change together in one reviewable file.

## Diagram index

| Diagram | Question it answers |
|---|---|
| [Component dependencies](#1-compile-time-component-dependencies) | Which `.csproj` may know which other project? |
| [Startup comparison](#2-startup-comparison) | What happens between the entry point and the HTTP server accepting requests? |
| [Risk request sequence](#3-risk-request-sequence) | Which runtime component handles a risk request and where do failures become HTTP? |
| [Build and publish](#4-build-and-publish) | How do source, restore state, build output, tests, and publish output relate? |
| [DI lifetimes](#5-dependency-injection-lifetimes) | Which objects are shared between requests and which belong to one request? |
| [Domain model](#6-domain-model) | How do portfolios, positions, scenarios, the calculator, and reports relate? |
| [Browser UI flow](#7-browser-ui-flow) | How does the static browser workbench call the same ASP.NET Core API? |

## 1. Compile-time component dependencies

```mermaid
flowchart TB
    Tests["TradingRisk.Tests<br/>Executable specifications"]
    Api["TradingRisk.Api<br/>ASP.NET Core adapter<br/>and composition root"]
    Infrastructure["TradingRisk.Infrastructure<br/>Technical adapters"]
    Application["TradingRisk.Application<br/>Use cases and ports"]
    Domain["TradingRisk.Domain<br/>Business model and risk math"]

    Api -->|"ProjectReference"| Application
    Api -->|"ProjectReference"| Infrastructure
    Api -->|"ProjectReference"| Domain
    Application -->|"ProjectReference"| Domain
    Infrastructure -->|"implements ports"| Application
    Infrastructure -->|"stores domain objects"| Domain

    Tests -. "HTTP integration tests" .-> Api
    Tests -. "handler tests" .-> Application
    Tests -. "in-memory adapter" .-> Infrastructure
    Tests -. "unit tests" .-> Domain

    classDef api fill:#dbeafe,stroke:#374151,color:#111827
    classDef application fill:#dcfce7,stroke:#374151,color:#111827
    classDef infrastructure fill:#fef3c7,stroke:#374151,color:#111827
    classDef domain fill:#fce7f3,stroke:#374151,color:#111827
    classDef tests fill:#ede9fe,stroke:#374151,color:#111827
    class Api api
    class Application application
    class Infrastructure infrastructure
    class Domain domain
    class Tests tests
```

A solid arrow means the source project has a compile-time dependency on the
target, normally created by `<ProjectReference>`.

The central rule is that dependencies point inward toward business policy.
Domain has no reverse project or NuGet dependency, so it cannot accidentally
use controllers, repositories, configuration, or ASP.NET Core. Dashed test
arrows do not make production code depend on tests.

## 2. Startup comparison

```mermaid
flowchart TB
    subgraph Spring["Java / Spring Boot"]
        direction LR
        J1["JVM starts<br/>main(String[] args)"]
        J2["SpringApplication.run(...)"]
        J3["Create ApplicationContext<br/>and Environment"]
        J4["Scan configuration,<br/>components and auto-config"]
        J5["Instantiate beans<br/>and start embedded server"]
        J6["Accept requests"]
        J1 --> J2 --> J3 --> J4 --> J5 --> J6
    end

    subgraph DotNet["C# / ASP.NET Core"]
        direction LR
        D1["Generated Main executes<br/>top-level Program.cs"]
        D2["WebApplication<br/>.CreateBuilder(args)"]
        D3["Create host,<br/>configuration and logging"]
        D4["Explicitly register services<br/>in builder.Services"]
        D5["builder.Build()<br/>creates service provider"]
        D6["Configure middleware<br/>and mapped endpoints"]
        D7["app.Run()<br/>starts Kestrel"]
        D1 --> D2 --> D3 --> D4 --> D5 --> D6 --> D7
    end

    J1 -. "entry point" .-> D1
    J3 -. "host and configuration" .-> D3
    J4 -. "dependency registration" .-> D4
    J5 -. "construct container" .-> D5
    J6 -. "HTTP server" .-> D7
```

Both platforms enter user code, collect configuration/logging, describe
services, construct a dependency container, create an HTTP server, and listen
until shutdown.

Spring Boot commonly discovers much of its graph through component scanning,
configuration, and auto-configuration. This project registers application
services explicitly in `builder.Services`.

`builder.Build()` creates the configured application and root service provider;
it does not start listening. `app.Run()` starts Kestrel and waits for shutdown.

## 3. Risk request sequence

```mermaid
sequenceDiagram
    actor Client
    participant Middleware as ASP.NET Core<br/>middleware
    participant Controller as PortfoliosController
    participant Handler as CalculatePortfolioRiskHandler
    participant Repository as IPortfolioRepository<br/>(in-memory adapter)
    participant Calculator as HistoricalSimulation<br/>RiskCalculator
    participant Errors as ApiExceptionHandler

    Client->>Middleware: POST /api/v1/portfolios/{id}/risk
    Middleware->>Controller: CalculateRisk(id, request, token)
    Controller->>Controller: Enforce request-size policy<br/>Map HTTP contract to query
    Controller->>Handler: HandleAsync(query, token)
    Handler->>Handler: Validate transport-neutral input
    Handler->>Repository: GetByIdAsync(PortfolioId, token)
    Repository-->>Handler: Portfolio or null

    alt Portfolio exists
        Handler->>Handler: Map scenario inputs<br/>to Domain value objects
        Handler->>Calculator: Calculate(portfolio, scenarios, confidence)
        loop Each historical scenario
            Calculator->>Calculator: P&L = Σ(market value × return)<br/>Loss = −P&L
        end
        Calculator->>Calculator: Order losses<br/>Compute VaR, ES and volatility
        Calculator-->>Handler: RiskReport
        Handler->>Handler: Map report to DTO<br/>Add TimeProvider timestamp
        Handler-->>Controller: RiskReportDto
        Controller-->>Middleware: 200 OK + DTO
        Middleware-->>Client: JSON risk report
    else Portfolio is missing
        Repository-->>Handler: null
        Handler--x Middleware: PortfolioNotFoundException
        Middleware->>Errors: TryHandleAsync(exception)
        Errors-->>Client: 404 Problem Details JSON
    end
```

Read sequence diagrams from top to bottom. Horizontal arrows are calls or
returned values; vertical lifelines are runtime participants; the loop repeats
for every historical observation; and the `alt` block separates success from
not-found behavior.

The controller understands HTTP, the handler understands a use case and ports,
and the calculator understands portfolio/risk types. A missing portfolio is an
Application exception until the outer handler maps it to HTTP 404.

The cancellation token travels into asynchronous repository work. The pure
CPU-bound calculator is synchronous because `async` would not make arithmetic
parallel or faster.

## 4. Build and publish

```mermaid
flowchart LR
    subgraph Inputs
        Projects[".slnx and .csproj"]
        Shared["Directory.Build.props<br/>Directory.Packages.props"]
        Source["C# source and<br/>configuration files"]
        Feed["NuGet feeds"]
    end

    Restore["dotnet restore"]
    Obj["obj/<br/>project.assets.json<br/>generated build state"]
    Build["dotnet build -c Release"]
    Bin["bin/Release/net10.0/<br/>assemblies, deps and runtime config"]
    Test["dotnet test -c Release<br/>--no-build"]
    Publish["dotnet publish -c Release<br/>--no-build -o artifacts/publish"]
    PublishDir["Publish directory<br/>application + dependencies + content"]
    Runtime["Runtime target"]

    Projects --> Restore
    Shared --> Restore
    Feed -->|"package metadata/files"| Restore
    Restore --> Obj
    Projects --> Build
    Shared --> Build
    Source --> Build
    Obj -->|"resolved dependency graph"| Build
    Build --> Bin
    Bin -->|"test assemblies"| Test
    Bin --> Publish
    Source -->|"appsettings + wwwroot"| Publish
    Publish --> PublishDir
    PublishDir -->|"dotnet TradingRisk.Api.dll<br/>or container image"| Runtime
```

Keep three output concepts separate:

- `obj/` contains intermediate/generated state, including NuGet's resolved
  asset graph;
- `bin/` contains compiled project output;
- the publish directory is an assembled deployment layout.

Restore resolves, build compiles, test executes test assemblies, and publish
gathers application assemblies, dependencies, runtime metadata, configuration,
and static browser assets.

These commands can trigger previous stages implicitly. CI restores once, then
uses `--no-restore` and `--no-build` so each failure belongs to one stage.

## 5. Dependency-injection lifetimes

```mermaid
flowchart LR
    Root["Root service provider<br/>created by builder.Build()"]

    subgraph ScopeA["Request A scope"]
        ControllerA["PortfoliosController A"]
        HandlerA["CalculatePortfolioRiskHandler A<br/>(scoped)"]
        ControllerA --> HandlerA
    end

    subgraph ScopeB["Request B scope"]
        ControllerB["PortfoliosController B"]
        HandlerB["CalculatePortfolioRiskHandler B<br/>(scoped)"]
        ControllerB --> HandlerB
    end

    subgraph Singletons["Singletons: one instance for the process"]
        Repository["IPortfolioRepository<br/>→ InMemoryPortfolioRepository"]
        Calculator["IRiskCalculator<br/>→ HistoricalSimulationRiskCalculator"]
        Clock["TimeProvider.System"]
    end

    Root -->|"creates scope"| ControllerA
    Root -->|"creates scope"| ControllerB
    Root -->|"creates once"| Repository
    Root -->|"creates once"| Calculator
    Root -->|"creates once"| Clock

    HandlerA -->|"shared instance"| Repository
    HandlerB -->|"same instance"| Repository
    HandlerA -->|"shared stateless instance"| Calculator
    HandlerB -->|"same instance"| Calculator
    HandlerA --> Clock
    HandlerB --> Clock
```

The root provider owns singletons until shutdown. ASP.NET Core creates a scope
for each request. A new scoped handler is used for each request, while both
handlers share the process-wide repository, calculator, and clock.

The repository's state must be thread-safe because it is shared. The calculator
can be shared because it has no mutable state.

Transient services, if registered, are created whenever resolved. A singleton
must not capture a scoped service because that object could outlive the request
that owns it. A future EF Core `DbContext` and repository should normally be
scoped.

## 6. Domain model

```mermaid
classDiagram
    class Portfolio {
        <<aggregate root>>
        +PortfolioId Id
        +string Name
        +Currency BaseCurrency
        +IReadOnlyList~Position~ Positions
        +decimal NetMarketValue
        +decimal GrossExposure
        +Create(...) Portfolio
    }

    class PortfolioId {
        <<readonly record struct>>
        +Guid Value
        +New() PortfolioId
    }

    class Currency {
        <<readonly record struct>>
        +string Code
        +From(code) Currency
    }

    class Position {
        <<record>>
        +InstrumentId InstrumentId
        +decimal Quantity
        +decimal Price
        +decimal MarketValue
        +Create(...) Position
    }

    class InstrumentId {
        <<readonly record struct>>
        +string Value
        +From(value) InstrumentId
    }

    class HistoricalScenario {
        +DateOnly AsOfDate
        +IReadOnlyDictionary Returns
        +Create(...) HistoricalScenario
        +ReturnFor(instrumentId) decimal
    }

    class IRiskCalculator {
        <<interface>>
        +Calculate(portfolio, scenarios, confidence) RiskReport
    }

    class HistoricalSimulationRiskCalculator {
        +Calculate(portfolio, scenarios, confidence) RiskReport
    }

    class RiskReport {
        <<record>>
        +PortfolioId PortfolioId
        +Currency Currency
        +decimal ConfidenceLevel
        +decimal ValueAtRisk
        +decimal ExpectedShortfall
        +decimal DailyPnlVolatility
        +IReadOnlyList ScenarioResults
    }

    class ScenarioResult {
        <<record>>
        +DateOnly AsOfDate
        +decimal ProfitAndLoss
        +decimal Loss
    }

    Portfolio "1" *-- "1..*" Position : owns snapshot
    Portfolio "1" *-- "1" PortfolioId
    Portfolio "1" *-- "1" Currency
    Position "1" *-- "1" InstrumentId
    HistoricalScenario "1" o-- "1..*" InstrumentId : return keys
    HistoricalSimulationRiskCalculator ..|> IRiskCalculator
    IRiskCalculator ..> Portfolio
    IRiskCalculator ..> HistoricalScenario
    IRiskCalculator ..> RiskReport
    RiskReport "1" *-- "1..*" ScenarioResult
    RiskReport "1" *-- "1" PortfolioId
    RiskReport "1" *-- "1" Currency
```

Composition (`*--`) represents ownership. A portfolio owns an immutable
snapshot of positions; a risk report owns scenario results. The implementation
arrow (`..|>`) means the historical calculator implements the strategy
interface.

Small value types prevent values with different meanings from being mixed only
because both are a raw `Guid` or `string`. Factory methods validate before an
object is created. This is closer to Java records/value objects with static
factories than to mutable JPA entities.

The diagram is conceptual: private fields, constructors, some report fields,
and DTOs are omitted so relationships remain readable. C# is the exact
specification.

## 7. Browser UI flow

```mermaid
sequenceDiagram
    actor Learner
    participant Browser as Browser workbench<br/>HTML + CSS + JavaScript
    participant Static as Static-file middleware
    participant Health as Health endpoint
    participant Controller as PortfoliosController
    participant Handler as Application handlers
    participant Repository as Portfolio repository
    participant Calculator as Risk calculator

    Learner->>Browser: Open /
    Browser->>Static: GET /index.html, /css/site.css, /js/app.js
    Static-->>Browser: Published wwwroot assets
    Browser->>Health: GET /health
    Health-->>Browser: 200 Healthy

    Learner->>Browser: Submit portfolio form
    Browser->>Controller: POST /api/v1/portfolios (JSON)
    Controller->>Handler: CreatePortfolioCommand
    Handler->>Repository: AddAsync(portfolio)
    Handler-->>Controller: PortfolioDto
    Controller-->>Browser: 201 Created + JSON
    Browser->>Browser: Render exposure summary<br/>Build instrument return matrix

    Learner->>Browser: Submit historical scenarios
    Browser->>Controller: POST /api/v1/portfolios/{id}/risk (JSON)
    Controller->>Handler: CalculatePortfolioRiskQuery
    Handler->>Repository: GetByIdAsync(id)
    Handler->>Calculator: Calculate(...)
    Calculator-->>Handler: RiskReport
    Handler-->>Controller: RiskReportDto
    Controller-->>Browser: 200 OK + JSON
    Browser->>Browser: Render metrics, P&L chart,<br/>interpretation and result table
```

The UI is a same-origin adapter. ASP.NET Core serves its static files and the
browser calls relative `/api/...` URLs, so local development needs no separate
frontend server or CORS policy.

The browser does not calculate risk. It gathers input, serializes JSON, handles
Problem Details, and renders the server's DTO. Domain and Application remain
the source of business behavior.

## How to edit Mermaid safely

GitHub renders a fenced block:

````text
```mermaid
flowchart LR
    Browser -->|"JSON over HTTP"| Api
    Api --> Application
```
````

Useful syntax:

- `flowchart LR` describes left-to-right dependencies;
- `sequenceDiagram` describes calls over time;
- `classDiagram` describes type relationships;
- `-->` is a solid dependency/call;
- `-.->` is a dashed dependency;
- `-->>` is a returned value in a sequence;
- `*--` is composition and `..|>` is interface implementation.

Use stable short identifiers such as `Application` and place the longer visible
label inside `["..."]`. When architecture changes, update the matching Mermaid
block in the same commit. GitHub's rendered preview is the normal review
surface; the fenced text remains the source of truth.
