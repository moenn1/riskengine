# PlantUML architecture and runtime diagrams

The diagrams in `docs/diagrams` are source files, not screenshots. You can edit
them beside the code, review changes as text, and regenerate SVG or PNG output.
They intentionally use only standard PlantUML syntax and do not download remote
themes or diagram libraries.

## Diagram index

| Diagram | Question it answers |
|---|---|
| [Component dependencies](diagrams/01-component-dependencies.puml) | Which `.csproj` may know which other project, and in what direction? |
| [Startup comparison](diagrams/02-startup-comparison.puml) | What happens between `main`/`Program.cs` and the server accepting requests in Spring Boot and ASP.NET Core? |
| [Risk request sequence](diagrams/03-risk-request-sequence.puml) | Which runtime component handles a risk request, in which order, and where do errors become HTTP responses? |
| [Build and publish](diagrams/04-build-and-publish.puml) | How do source, restore state, build output, tests, and publish output relate? |
| [DI lifetimes](diagrams/05-di-lifetimes.puml) | Which objects are shared between requests and which belong to one request scope? |
| [Domain model](diagrams/06-domain-model.puml) | How do the portfolio, positions, scenarios, calculator, and report relate? |

## 1. Compile-time component dependencies

Open [01-component-dependencies.puml](diagrams/01-component-dependencies.puml).

A solid arrow means the source project has a compile-time dependency on the
target. In MSBuild, that relationship is normally a `<ProjectReference>`.
The diagram points inward toward stable business code:

```text
API ───────> Application ───────> Domain
 │                 ^
 └──> Infrastructure ────────────┘
```

The important reading is not merely “API calls Application.” The compiler
allows API to name Application types because API references Application.
Domain has no reverse reference, so Domain cannot accidentally use controllers,
repositories, configuration, or ASP.NET Core.

The dashed test arrows mean tests depend on production projects for different
test scopes. Those dependencies do not make production projects depend on
tests.

## 2. Startup comparison

Open [02-startup-comparison.puml](diagrams/02-startup-comparison.puml).

Both platforms perform the same broad jobs:

1. enter user code;
2. collect configuration and logging;
3. describe available application services;
4. construct a dependency container;
5. create an HTTP server and request pipeline; and
6. listen until shutdown.

The visible style differs. Spring Boot commonly starts with
`SpringApplication.run` and then discovers much of the object graph through
component scanning, configuration classes, and auto-configuration. This
project registers application services explicitly in `builder.Services`.

`builder.Build()` does not start listening. It creates the configured
`WebApplication` and root service provider. Middleware and endpoints are then
added to that application. `app.Run()` starts Kestrel and blocks until
shutdown.

## 3. Risk request sequence

Open [03-risk-request-sequence.puml](diagrams/03-risk-request-sequence.puml).

Read a sequence diagram from top to bottom:

- horizontal arrows are calls or returned values;
- vertical lifelines are runtime participants;
- the loop repeats for every historical observation;
- the `alt` block shows successful and not-found paths.

This diagram also shows the boundary rule. The controller understands HTTP
contracts. The handler understands a use case and repository port. The
calculator understands portfolio and risk-domain types. A missing portfolio is
an Application exception until the outer exception handler maps it to HTTP 404.

The cancellation token travels from the HTTP request into asynchronous
repository work. The pure, CPU-bound calculator is synchronous; adding `async`
would not make its arithmetic parallel or faster.

## 4. Build and publish pipeline

Open [04-build-and-publish.puml](diagrams/04-build-and-publish.puml).

There are three output concepts to keep separate:

- `obj/` contains intermediate and generated build state, including NuGet's
  resolved asset graph.
- `bin/` contains compiled output for a project and configuration.
- the publish directory is an intentionally assembled deployment layout.

Restore resolves packages and project graphs. Build compiles. Test runs test
assemblies. Publish gathers the application, dependencies, runtime metadata,
configuration, and content needed by the chosen deployment mode.

Commands can trigger earlier stages implicitly. For a transparent CI pipeline,
this repository restores once, builds with `--no-restore`, and tests with
`--no-build`. That makes failures easier to attribute to one stage.

## 5. Dependency-injection lifetimes

Open [05-di-lifetimes.puml](diagrams/05-di-lifetimes.puml).

The root service provider owns singletons until process shutdown. ASP.NET Core
creates a scope for each HTTP request. Resolving a scoped handler twice inside
the same request returns the same scoped instance; a different request gets a
different handler.

Both request handlers use the same repository because this learning adapter is
a singleton. That is what lets an in-memory portfolio created by one request be
read by another. Its state must therefore be thread-safe. The calculator is
also singleton because it is stateless.

When the repository is replaced by EF Core, the usual arrangement is a scoped
`DbContext` and a scoped repository. Never inject a scoped object into a
singleton: the singleton would retain an object whose intended request lifetime
has ended. This is the .NET version of a scope-lifetime mismatch in Spring.

## 6. Domain model

Open [06-domain-model.puml](diagrams/06-domain-model.puml).

A filled diamond represents ownership/composition. A portfolio owns an
immutable snapshot of one or more positions. A risk report owns its scenario
results. A hollow triangle with a dashed line means
`HistoricalSimulationRiskCalculator` implements `IRiskCalculator`.

The model uses small value types for IDs and currency so values with different
meanings cannot be mixed merely because both happen to be a `Guid` or `string`.
Factory methods validate before an object can exist. That corresponds more
closely to Java records/value objects with static factories than to mutable
JPA entities.

The diagram is deliberately a conceptual view. It omits private fields,
constructors, every report property, and DTOs so that domain relationships stay
readable. The C# source remains the exact specification.

## Render the diagrams

### Rider

Install a PlantUML-compatible plugin through **Settings → Plugins**, then open a
`.puml` file and use the plugin's preview. Plugin names and rendering backends
can change between Rider versions, so follow the selected plugin's setup page
if it requests Graphviz or a PlantUML server.

### PlantUML command line

With a `plantuml` executable on your path:

```bash
plantuml -checkonly docs/diagrams/*.puml
plantuml -tsvg docs/diagrams/*.puml
plantuml -tpng docs/diagrams/*.puml
```

With a downloaded PlantUML JAR:

```bash
java -jar plantuml.jar -checkonly docs/diagrams/*.puml
java -jar plantuml.jar -tsvg docs/diagrams/*.puml
```

SVG is usually the best format for documentation because text and arrows stay
sharp when zoomed. Generated images are intentionally not committed: the
`.puml` files are the reviewable source of truth.

## Modify a diagram safely

Use a stable alias when a label contains spaces:

```plantuml
participant "CalculatePortfolioRiskHandler" as Handler
database "IPortfolioRepository\n(InMemory adapter)" as Repository

Handler -> Repository : GetByIdAsync(id, token)
Repository --> Handler : Portfolio or null
```

The visible label can change without changing all later references. Use `->`
for a call or dependency and `-->` for a returned value in sequence diagrams.
For class diagrams, `*--` means composition and `..|>` means implements.

When architecture changes, update the corresponding diagram in the same
commit. For example, adding an EF Core adapter should update the component and
DI-lifetime diagrams; adding a message-driven risk job should update the
runtime sequence rather than pretending it is still an HTTP-only flow.
