# Deep dive: dependencies, build, startup, and packaging

This chapter answers the practical questions a Java/Spring developer usually
has during the first weeks on a .NET team:

- What is the equivalent of `pom.xml` or `build.gradle.kts`?
- What is actually meant by a “dependency” in C#?
- How does one project reference another?
- Where do package versions live?
- What do restore, build, test, pack, and publish each do?
- What files are produced?
- Where is `main`, and how does ASP.NET Core start?
- How does explicit .NET DI compare with Spring scanning and auto-configuration?
- What is deployed, and how is it run?

All .NET examples below come from this repository.

Related chapters continue beyond build/startup into
[architecture and domain design](07-architecture-and-domain-design-deep-dive.md),
[ASP.NET Core HTTP/runtime behavior](08-aspnet-core-web-api-deep-dive.md),
[testing](09-testing-dotnet-and-risk-deep-dive.md), and
[production-scale engineering](10-production-scale-dotnet-deep-dive.md).

## 1. Translation table

| Java/Spring concept | .NET concept in this repository |
|---|---|
| JDK | .NET SDK |
| JVM/JRE | .NET runtime/CoreCLR |
| Java language version | C# language version |
| `pom.xml` / `build.gradle.kts` | SDK-style `.csproj` plus shared `.props` files |
| Maven multi-module reactor / Gradle multi-project build | `.slnx` solution containing multiple `.csproj` projects |
| parent POM build settings / convention plugin | `Directory.Build.props` |
| Maven `<dependencyManagement>` / Gradle version catalog or platform | `Directory.Packages.props` Central Package Management |
| Maven Central artifact | NuGet package |
| local Maven repository | global NuGet packages cache |
| JAR | .NET assembly, normally `.dll` |
| Spring Boot executable/fat JAR | .NET publish directory or container image |
| `java -jar app.jar` | `dotnet TradingRisk.Api.dll` for framework-dependent deployment |
| `src/main/java` | `.cs` files under the project directory, included implicitly |
| `src/test/java` | a separate test `.csproj` by common convention |
| classpath | compile/runtime asset graph resolved by NuGet and MSBuild |
| `@SpringBootApplication` | top-level `Program.cs` using `WebApplication` |
| `ApplicationContext` | built service provider plus host/application services |
| embedded Tomcat/Jetty/Undertow | Kestrel |
| servlet filter chain | ASP.NET Core middleware pipeline |
| `@Service` discovery | explicit `IServiceCollection` registration |
| `@ConfigurationProperties` | options pattern: `IOptions<T>` |
| Actuator health endpoint | ASP.NET Core health checks |
| `mvn package` / `gradle build` | no single exact equivalent; normally `dotnet build` plus `dotnet test`, then `dotnet publish` |

The terms do not map perfectly. In particular, a .NET **build** is not a Maven
`package`: `dotnet build` compiles but does not run tests and is not normally
the final deployment layout.

## 2. Repository, solution, project, namespace, and assembly

These are different layers:

```text
repository directory
└── RiskEngine.slnx                  groups projects for tools/build commands
    ├── TradingRisk.Domain.csproj    one project -> one main assembly
    ├── TradingRisk.Application.csproj
    ├── TradingRisk.Infrastructure.csproj
    ├── TradingRisk.Api.csproj
    └── TradingRisk.Tests.csproj
```

### Solution

`RiskEngine.slnx` tells Rider, Visual Studio, MSBuild, and `dotnet` which
projects belong together:

```xml
<Solution>
  <Project Path="src/TradingRisk.Api/TradingRisk.Api.csproj" />
  <Project Path="src/TradingRisk.Application/TradingRisk.Application.csproj" />
  <Project Path="src/TradingRisk.Domain/TradingRisk.Domain.csproj" />
  <Project Path="src/TradingRisk.Infrastructure/TradingRisk.Infrastructure.csproj" />
  <Project Path="tests/TradingRisk.Tests/TradingRisk.Tests.csproj" />
</Solution>
```

A solution is an orchestration/tooling file. It is not compiled into the
application and is not deployed. This resembles a Maven aggregator POM or the
root of a Gradle multi-project build.

### Project

A `.csproj` is an MSBuild project. It controls:

- project SDK and output type;
- target framework and compiler settings;
- project-to-project references;
- external NuGet dependencies;
- content copied to build/publish output;
- generated code, analyzers, and custom build targets.

By default, a class-library project produces one main assembly. For example,
`TradingRisk.Domain.csproj` produces `TradingRisk.Domain.dll`.

### Namespace

```csharp
namespace TradingRisk.Domain.Portfolios;
```

A namespace organizes type names. It does not:

- add a dependency;
- have to match the directory path;
- create a deployment unit;
- provide Java package-private visibility.

`internal` provides assembly-level visibility. A C# folder is mostly an
organization convention unless build rules explicitly give it meaning.

### Assembly

An assembly is a compiled `.dll` or `.exe` containing Common Intermediate
Language (CIL), metadata, type definitions, and references. CoreCLR loads
assemblies at runtime and normally JIT-compiles methods to native machine code.

A Java analogy is a JAR of `.class` files, but a .NET assembly has richer
runtime metadata and normally maps directly to one project.

## 3. Anatomy of an SDK-style project

The Domain project is intentionally minimal:

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

It still builds because the SDK and repository-level files provide defaults.

### `Project Sdk`

```xml
<Project Sdk="Microsoft.NET.Sdk">
```

selects the build SDK. It implicitly imports MSBuild `.props` and `.targets`
files that define standard compilation behavior.

The API uses:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
```

The Web SDK builds on the base SDK and adds web defaults, including the ASP.NET
Core shared framework reference, web content handling, and publish behavior.
This is closer to applying Spring Boot/Java build plugins than adding an
ordinary runtime library.

### Properties

Properties are scalar build values:

```xml
<PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

This repository puts those shared values in `Directory.Build.props`. MSBuild
automatically searches parent directories for that file and imports it into
each project.

Important distinction:

- `TargetFramework=net10.0` selects the API surface/runtime contract.
- `LangVersion=14.0` selects C# syntax/compiler rules.
- `global.json` selects which installed SDK runs the build.

Those are related, but not the same setting.

### Items

Items are collections of build inputs:

```xml
<ItemGroup>
    <ProjectReference Include="../TradingRisk.Domain/TradingRisk.Domain.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
</ItemGroup>
```

MSBuild uses item names such as `Compile`, `Content`, `None`,
`ProjectReference`, and `PackageReference`.

You do not see every `.cs` file listed. The SDK implicitly includes:

```text
**/*.cs
```

under the project directory, excluding conventional build-output folders. This
is why adding a C# file under `src/TradingRisk.Domain` is normally enough; there
is no need to update the `.csproj`.

## 4. Four meanings of “dependency”

New .NET developers often mix up four separate mechanisms.

## 4.1 Source-name dependency: `using`

```csharp
using TradingRisk.Domain.Portfolios;
```

This is comparable to:

```java
import com.acme.risk.domain.Portfolio;
```

It only shortens a type name in source. It does not download a package or make
another project visible. Removing a `using` may be possible if you fully qualify
the type:

```csharp
TradingRisk.Domain.Portfolios.Portfolio portfolio;
```

The underlying assembly/project reference is still required.

## 4.2 Project-to-project dependency: `ProjectReference`

Application uses Domain types, so its `.csproj` declares:

```xml
<ItemGroup>
    <ProjectReference Include="../TradingRisk.Domain/TradingRisk.Domain.csproj" />
</ItemGroup>
```

Consequences:

1. MSBuild knows Domain must be restored/built before Application.
2. Application compiles against Domain's public types.
3. Domain's output is available when an application consuming Application is
   built/published.
4. Changing Domain can cause dependent projects to rebuild.

The approximate Maven multi-module form is:

```xml
<dependency>
    <groupId>com.acme.risk</groupId>
    <artifactId>trading-risk-domain</artifactId>
    <version>${project.version}</version>
</dependency>
```

with both modules included by a parent/aggregator POM.

The Gradle equivalent is:

```kotlin
dependencies {
    implementation(project(":trading-risk-domain"))
}
```

A `ProjectReference` uses source projects in the same build. You do not publish
Domain to NuGet merely so Application in the same repository can consume it.

## 4.3 External dependency: `PackageReference`

The API consumes packages:

```xml
<ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Microsoft.OpenApi" />
</ItemGroup>
```

The equivalent Maven declaration would include group, artifact, and version:

```xml
<dependency>
    <groupId>org.example</groupId>
    <artifactId>some-library</artifactId>
    <version>1.2.3</version>
</dependency>
```

Gradle Kotlin DSL:

```kotlin
dependencies {
    implementation("org.example:some-library:1.2.3")
    testImplementation("org.junit.jupiter:junit-jupiter:...")
}
```

NuGet package IDs are globally named strings; they do not have separate Maven
`groupId` and `artifactId` fields.

Restore resolves direct and transitive dependencies, chooses compatible assets
for the target framework/runtime, downloads missing packages to the NuGet
cache, and writes the resolved graph to:

```text
obj/project.assets.json
```

That generated file is excellent for diagnosing “which package supplied this
assembly?” problems.

Development-only package assets can be prevented from flowing to consumers:

```xml
<PackageReference Include="Some.Analyzer">
    <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Other advanced metadata includes `IncludeAssets` and `ExcludeAssets`. These
control compile, runtime, analyzer, native, content, and build assets. They are
similar in intent—not identical—to Maven scopes, optional dependencies, and
Gradle configurations.

## 4.4 Runtime object dependency: DI registration

This constructor:

```csharp
public sealed class CalculatePortfolioRiskHandler(
    IPortfolioRepository repository,
    IRiskCalculator riskCalculator,
    TimeProvider timeProvider)
```

declares object dependencies, but says nothing about which implementations to
create. `Program.cs` supplies the runtime mapping:

```csharp
builder.Services.AddSingleton<
    IPortfolioRepository,
    InMemoryPortfolioRepository>();

builder.Services.AddSingleton<
    IRiskCalculator,
    HistoricalSimulationRiskCalculator>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CalculatePortfolioRiskHandler>();
```

There are therefore two different graphs:

```text
compile-time project graph:
Api -> Application -> Domain
Api -> Infrastructure -> Application + Domain

runtime object graph for one controller:
PortfoliosController
  -> CalculatePortfolioRiskHandler
      -> IPortfolioRepository -> InMemoryPortfolioRepository
      -> IRiskCalculator -> HistoricalSimulationRiskCalculator
      -> TimeProvider -> TimeProvider.System
```

A project reference makes types available to the compiler. A DI registration
tells the container how to create objects at runtime. Many errors come from
fixing one graph while forgetting the other.

## 5. Central package versions

The project files declare which packages they use. Versions are centralized:

```xml
<Project>
    <PropertyGroup>
        <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    </PropertyGroup>
    <ItemGroup>
        <PackageVersion
            Include="Microsoft.AspNetCore.OpenApi"
            Version="10.0.10" />
        <PackageVersion
            Include="Microsoft.OpenApi"
            Version="2.11.0" />
        <PackageVersion
            Include="xunit.v3.mtp-v2"
            Version="3.2.2" />
    </ItemGroup>
</Project>
```

With Central Package Management:

- `Directory.Packages.props` owns versions.
- `.csproj` files own usage and asset metadata.
- putting `Version="..."` on an ordinary `PackageReference` is an error.

This separation is similar to:

- Maven dependency management/BOM controlling versions while child modules
  declare dependencies;
- Gradle platforms or version catalogs controlling coordinates while individual
  projects declare `implementation`/`testImplementation`.

The direct `Microsoft.OpenApi` reference is intentional. The ASP.NET package's
minimum transitive version had a high-severity advisory, so the application
directly pins a patched compatible 2.x release. This demonstrates a general
rule: inspect the resolved graph, not only the top-level dependencies.

Useful inspection commands:

```bash
dotnet list RiskEngine.slnx package
dotnet list RiskEngine.slnx package --include-transitive
dotnet list RiskEngine.slnx package --vulnerable --include-transitive
```

Java equivalents:

```bash
mvn dependency:tree
./gradlew dependencies
./gradlew dependencyInsight --dependency some-library
```

## 6. Dependency direction in this solution

Allowed compile-time references:

| Project | May reference | Why |
|---|---|---|
| Domain | nothing in the solution | Business model and math stay independent. |
| Application | Domain | Use cases coordinate domain objects and define ports. |
| Infrastructure | Application and Domain | Adapters implement Application ports using Domain types. |
| API | Application, Domain, Infrastructure | Composition root and HTTP adapter wire the whole process. |
| Tests | all projects | Tests intentionally exercise boundaries at several levels. |

Forbidden examples:

- Domain referencing ASP.NET `HttpContext`;
- Domain referencing EF Core `DbContext`;
- Application returning `IActionResult`;
- Infrastructure deciding HTTP status codes;
- a controller implementing VaR math.

The compiler enforces project direction because a project cannot name types from
an assembly it does not reference.

## 7. Restore, build, test, run, pack, and publish

These commands have different deliverables.

## 7.1 Restore

```bash
dotnet restore RiskEngine.slnx
```

Restore:

- evaluates project and package references;
- reads NuGet sources/configuration;
- resolves versions and target-framework assets;
- downloads missing packages to the user package cache;
- writes `obj/project.assets.json` and generated NuGet props/targets.

It does not compile source.

Most commands perform implicit restore. CI often restores explicitly once, then
uses `--no-restore` so later steps cannot change the dependency graph:

```bash
dotnet restore RiskEngine.slnx
dotnet build RiskEngine.slnx -c Release --no-restore
```

Approximate Java equivalents are Maven/Gradle dependency resolution performed
as part of a task. There is no universally used separate Maven “restore”
command.

## 7.2 Build

```bash
dotnet build RiskEngine.slnx --configuration Release
```

Build:

- restores implicitly unless `--no-restore`;
- orders projects using `ProjectReference`;
- invokes the C# compiler and analyzers;
- produces assemblies and runtime metadata under `bin/`;
- puts intermediate/generated artifacts under `obj/`.

It does **not** run the test suite.

This differs from:

- `mvn package`, which executes earlier lifecycle phases including unit tests;
- `gradle build`, which normally depends on `check` and `assemble`.

The closer explicit sequence is:

```bash
dotnet build RiskEngine.slnx -c Release
dotnet test RiskEngine.slnx -c Release --no-build
```

`Debug` and `Release` are MSBuild configurations. They control build properties
such as optimization and symbols. They are not the same concept as Spring
profiles, which select runtime configuration/beans.

## 7.3 Test

```bash
dotnet test RiskEngine.slnx --configuration Release
```

Unless `--no-build` is used, `dotnet test` can restore/build before executing.
This repository selects Microsoft Testing Platform in `global.json`; the test
project uses xUnit v3.

Java comparisons:

```bash
mvn test
./gradlew test
```

## 7.4 Run from source

```bash
dotnet run \
  --project src/TradingRisk.Api \
  --launch-profile http
```

`dotnet run` restores/builds when necessary, then starts the project output. It
is a development command comparable to:

```bash
mvn spring-boot:run
./gradlew bootRun
```

It is not normally how a production process is launched.

Arguments after `--` go to the application rather than the `dotnet` command:

```bash
dotnet run --project src/TradingRisk.Api -- --SomeSetting=value
```

## 7.5 Pack

```bash
dotnet pack path/to/MyLibrary.csproj -c Release
```

`pack` creates a `.nupkg` for other projects to consume from a NuGet feed. It is
mainly a library distribution operation, comparable to producing/publishing a
library JAR. It is **not** the ordinary deployment command for an ASP.NET Core
service.

The rough Maven flow is `mvn package` then `install`/`deploy`; the exact mapping
is imperfect because Maven's lifecycle combines more stages.

## 7.6 Publish

```bash
dotnet publish src/TradingRisk.Api/TradingRisk.Api.csproj \
  --configuration Release \
  --output artifacts/publish
```

Publish creates a deployment closure:

- application and referenced project assemblies;
- required package assemblies;
- `.deps.json` describing runtime dependencies;
- `.runtimeconfig.json` describing target runtime/framework;
- application configuration/content files;
- browser assets from the Web SDK's `wwwroot` convention;
- platform executable and/or runtime files depending on deployment mode.

Publishing is the closest .NET equivalent to producing a runnable Spring Boot
archive, although the physical layout is normally a directory rather than one
fat JAR.

## 8. What appears under `bin` and `obj`

Typical API build output:

```text
src/TradingRisk.Api/bin/Release/net10.0/
├── TradingRisk.Api.dll
├── TradingRisk.Api.pdb
├── TradingRisk.Api.deps.json
├── TradingRisk.Api.runtimeconfig.json
├── TradingRisk.Application.dll
├── TradingRisk.Domain.dll
├── TradingRisk.Infrastructure.dll
├── package dependency assemblies
├── appsettings.json
├── appsettings.Development.json
└── wwwroot/
    ├── index.html
    ├── css/site.css
    └── js/app.js
```

Meanings:

- `.dll`: compiled managed assembly.
- `.pdb`: portable debug symbols used for source line information.
- `.deps.json`: dependency graph used by the host.
- `.runtimeconfig.json`: runtime/framework and runtime options.
- appsettings files: content copied for runtime configuration.
- `wwwroot`: public HTML/CSS/JavaScript content tracked by the Web SDK and
  served by ASP.NET Core's static-file pipeline.

`obj/` contains intermediate and generated files:

```text
obj/
├── project.assets.json
├── *.nuget.g.props
├── *.nuget.g.targets
├── generated assembly metadata
└── configuration-specific compiler intermediates
```

Never make manual edits under `bin/` or `obj/`; a clean/rebuild can replace
them. Source control ignores both.

## 9. Packaging and deployment models

## 9.1 Spring Boot executable JAR

A common Maven flow:

```bash
mvn clean package
java -jar target/trading-risk.jar
```

The Spring Boot Maven/Gradle plugin repackages the application with nested
dependencies and a launcher. The JAR normally relies on a compatible JVM
installed in the runtime environment.

## 9.2 Framework-dependent .NET deployment

Default publish:

```bash
dotnet publish src/TradingRisk.Api/TradingRisk.Api.csproj \
  -c Release \
  -o artifacts/framework-dependent

dotnet artifacts/framework-dependent/TradingRisk.Api.dll
```

The target machine/container needs a compatible ASP.NET Core runtime. Advantages:

- smaller application output;
- runtime can be patched independently;
- many apps can share the installed runtime.

The final Docker stage in this repository uses:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
```

so the runtime is supplied by the base image.

## 9.3 Self-contained .NET deployment

```bash
dotnet publish src/TradingRisk.Api/TradingRisk.Api.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -o artifacts/linux-x64
```

This includes the .NET runtime. Advantages and costs:

- no separately installed runtime is required;
- output is larger;
- publish is OS/architecture specific;
- you own rebuilding/redeploying runtime security patches.

Common Runtime Identifiers (RIDs) include `linux-x64`, `linux-arm64`,
`win-x64`, and `osx-arm64`.

## 9.4 Single-file, trimming, ReadyToRun, and Native AOT

These are separate choices, not synonyms:

- Single-file bundles output for distribution.
- Trimming removes code considered unused.
- ReadyToRun precompiles some code to reduce startup JIT work.
- Native AOT compiles ahead of time with stricter dynamic-code/reflection
  constraints.

Do not enable them merely because they sound faster. Reflection-heavy
frameworks, serializers, plugins, and dynamic loading need compatibility work.
Measure startup, memory, image size, throughput, and operational cost first.

## 10. How a Spring Boot application starts

Typical Java entry point:

```java
@SpringBootApplication
public class TradingRiskApplication {
    public static void main(String[] args) {
        SpringApplication.run(TradingRiskApplication.class, args);
    }
}
```

Conceptual startup:

1. The JVM loads the main class and calls `main`.
2. `SpringApplication` determines application type and prepares the environment.
3. Configuration/property sources and profiles are resolved.
4. An `ApplicationContext` is created and refreshed.
5. `@SpringBootApplication` enables configuration, component scanning, and
   auto-configuration.
6. Component scanning finds `@Controller`, `@Service`, `@Repository`,
   `@Configuration`, and other components in the package tree.
7. Auto-configuration contributes beans based on classpath contents,
   configuration, and missing/present beans.
8. Bean definitions are resolved and singleton beans are instantiated.
9. Spring MVC, filters, exception handlers, and the embedded server are wired.
10. Tomcat/Jetty/Undertow starts listening.
11. Lifecycle runners/events complete and the process waits for shutdown.

This is intentionally high level; Spring provides many extension points within
these stages.

## 11. How this ASP.NET Core application starts

The actual entry point begins:

```csharp
var builder = WebApplication.CreateBuilder(args);
```

C# top-level statements are compiled into a generated `Program.Main`. The
visible:

```csharp
public partial class Program;
```

at the bottom supplies a public type that `WebApplicationFactory<Program>` can
use in integration tests; it is not a second entry point.

### Stage 1: create the builder

`WebApplication.CreateBuilder(args)` prepares:

- the Generic Host;
- configuration providers;
- logging;
- the dependency-registration collection;
- Kestrel/server defaults;
- content root and environment information.

Default configuration includes appsettings files, environment variables, and
command-line arguments. Later providers override earlier providers for the same
key.

At this point `builder.Services` is an `IServiceCollection`: a mutable list of
service descriptors, not the final object container.

### Stage 2: register framework services

```csharp
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();
```

Each `Add...` method is an extension method that adds multiple related service
descriptors. This resembles Spring auto-configuration in effect, but the
application explicitly opts into each subsystem.

`AddControllers` discovers controller/action metadata and registers MVC
infrastructure. It does not scan the whole assembly for arbitrary `@Service`
equivalents.

### Stage 3: bind and validate configuration

```csharp
builder.Services
    .AddOptions<RiskApiOptions>()
    .Bind(builder.Configuration.GetSection(RiskApiOptions.SectionName))
    .Validate(
        options => options.MaxScenarioCount is > 0 and <= 10_000,
        "RiskApi:MaxScenarioCount must be between 1 and 10,000.")
    .ValidateOnStart();
```

This corresponds roughly to Spring `@ConfigurationProperties` plus validation.
`ValidateOnStart` makes bad configuration fail startup instead of waiting until
the first request resolves the options.

### Stage 4: register application services and implementations

```csharp
builder.Services.AddSingleton<
    IPortfolioRepository,
    InMemoryPortfolioRepository>();
builder.Services.AddSingleton<
    IRiskCalculator,
    HistoricalSimulationRiskCalculator>();
builder.Services.AddScoped<CreatePortfolioHandler>();
builder.Services.AddScoped<GetPortfolioHandler>();
builder.Services.AddScoped<CalculatePortfolioRiskHandler>();
```

This is the explicit equivalent of Spring bean definitions/component discovery.
The generic parameters mean:

```text
when a constructor asks for IPortfolioRepository,
create/return InMemoryPortfolioRepository according to singleton lifetime
```

### Stage 5: build the application

```csharp
var app = builder.Build();
```

Build:

- creates the service provider from descriptors;
- constructs the host/application infrastructure;
- finalizes configuration needed for request-pipeline setup;
- enables scope validation in appropriate environments.

Service registrations should be complete before this call. Afterward, code
configures how requests flow.

This is conceptually related to refreshing/building a Spring
`ApplicationContext`, although the lifecycle and extension mechanisms differ.

### Stage 6: configure middleware

```csharp
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();
```

Middleware order is execution order for the inbound request and reverse order
for the outbound response. Middleware may:

- inspect/modify `HttpContext`;
- call the next component;
- short-circuit and produce a response.

Approximate Java comparisons:

| ASP.NET Core | Spring/Servlet |
|---|---|
| middleware | servlet filter |
| MVC resource/action/result filter | Spring MVC interceptor/filter, depending on purpose |
| `IExceptionHandler` | `@ControllerAdvice` / exception resolver |
| endpoint routing | handler mapping |

Do not assume a servlet filter and middleware have identical lifecycle or DI
rules. Conventional middleware is constructed for application lifetime, so a
scoped service should not be captured in its constructor.

### Stage 7: map endpoints

```csharp
app.MapHealthChecks("/health");
app.MapControllers().RequireRateLimiting("api");
```

Mapping creates endpoint definitions. `[Route]`, `[HttpPost]`, and similar
attributes on controllers provide route/action metadata.

`AddControllers()` registers MVC services; `MapControllers()` exposes the
attribute-routed actions as endpoints. Both are needed.

### Stage 8: run

```csharp
app.Run();
```

The host starts, Kestrel begins listening, startup validation/lifetime callbacks
run, and the call blocks until graceful shutdown. The process receives
termination, stops accepting work, runs shutdown logic, and exits.

Kestrel can be internet-facing, but production deployments commonly put it
behind a load balancer, ingress, or reverse proxy.

## 12. Spring scanning versus explicit .NET registration

Spring:

```java
@Service
public final class CalculatePortfolioRiskHandler {
    private final PortfolioRepository repository;
    private final RiskCalculator calculator;

    public CalculatePortfolioRiskHandler(
        PortfolioRepository repository,
        RiskCalculator calculator) {
        this.repository = repository;
        this.calculator = calculator;
    }
}
```

Component scanning registers it because `@Service` is in the scanned package
tree.

.NET:

```csharp
public sealed class CalculatePortfolioRiskHandler(
    IPortfolioRepository repository,
    IRiskCalculator calculator,
    TimeProvider timeProvider);
```

The class has no DI attribute. `Program.cs` explicitly registers it:

```csharp
builder.Services.AddScoped<CalculatePortfolioRiskHandler>();
```

Benefits of explicit registration:

- the composition root shows runtime choices in one place;
- ordinary libraries need no container annotations;
- accidentally moving a class does not change scanning behavior;
- replacing an adapter is visible.

Costs:

- forgetting a registration causes runtime resolution failure;
- large composition roots need organization, usually feature-specific
  `Add...` extension methods;
- the built-in container intentionally has fewer advanced conventions than
  mature Spring ecosystems.

Some .NET teams use scanning libraries or source generators. Learn your team's
convention, but understand the explicit model first.

## 13. DI lifetimes compared

| .NET lifetime | Typical Spring comparison | Created | Disposed |
|---|---|---|---|
| Singleton | default singleton bean | once per root service provider/process | host shutdown |
| Scoped | request-scoped bean for web use | once per HTTP request scope | request end |
| Transient | prototype-like intent | every resolution | when owning scope/provider disposes it, subject to registration/resolution pattern |

Examples from this project:

```csharp
AddSingleton<IPortfolioRepository, InMemoryPortfolioRepository>();
AddSingleton<IRiskCalculator, HistoricalSimulationRiskCalculator>();
AddScoped<CalculatePortfolioRiskHandler>();
```

Why:

- Repository is singleton so in-memory data survives between requests. It uses
  a concurrent collection and stores immutable aggregates.
- Calculator is stateless and thread-safe.
- Handler is per request and is ready to depend on a future scoped EF Core
  `DbContext`.

### Captive dependency error

This is unsafe:

```csharp
builder.Services.AddScoped<RiskDbContext>();
builder.Services.AddSingleton<RiskCache>();

public sealed class RiskCache(RiskDbContext dbContext);
```

The singleton would retain a request-scoped context after its intended lifetime.
Fix the design/lifetime; do not simply create scopes everywhere to silence the
container.

### Resolution

When ASP.NET Core creates `PortfoliosController`, the container recursively
resolves constructor parameters. If:

- a type is not registered;
- multiple registrations are ambiguous for the expected usage;
- a circular constructor graph exists;
- a scoped service is illegally captured by a singleton under scope validation;

resolution/startup fails. Read the full exception; it normally contains the
dependency chain.

## 14. Configuration and profiles/environments

Spring example:

```yaml
risk-api:
  default-confidence-level: 0.99
  max-scenario-count: 1000
```

```java
@ConfigurationProperties(prefix = "risk-api")
public record RiskApiProperties(
    BigDecimal defaultConfidenceLevel,
    int maxScenarioCount) {}
```

.NET `appsettings.json`:

```json
{
  "RiskApi": {
    "DefaultConfidenceLevel": 0.99,
    "MaxScenarioCount": 1000
  }
}
```

C# options:

```csharp
public sealed class RiskApiOptions
{
    public const string SectionName = "RiskApi";
    public decimal DefaultConfidenceLevel { get; init; } = 0.99m;
    public int MaxScenarioCount { get; init; } = 1_000;
}
```

Injection:

```csharp
public PortfoliosController(IOptions<RiskApiOptions> options)
{
    var limit = options.Value.MaxScenarioCount;
}
```

Common .NET options interfaces:

- `IOptions<T>`: stable value, singleton-compatible, no named/reload behavior.
- `IOptionsSnapshot<T>`: scoped snapshot recomputed per request; cannot be
  injected into a singleton.
- `IOptionsMonitor<T>`: singleton-compatible current value and change
  notifications.

Environment override:

```bash
RiskApi__MaxScenarioCount=500 dotnet TradingRisk.Api.dll
```

Double underscore represents nested `:` in environment variable keys.

ASP.NET environment:

```bash
ASPNETCORE_ENVIRONMENT=Development
```

and environment-specific file:

```text
appsettings.Development.json
```

This is conceptually similar to Spring profiles, but naming, precedence, and
conditional bean behavior are different. Do not store production secrets in
committed appsettings files.

## 15. HTTP request lifecycle comparison

For:

```text
POST /api/v1/portfolios/{portfolioId}/risk
```

ASP.NET Core performs:

1. Kestrel accepts the connection and creates `HttpContext`.
2. Exception middleware wraps later components.
3. HTTPS/rate-limit middleware runs.
4. Endpoint routing selects the controller action.
5. A request DI scope is available.
6. MVC binds route and JSON values to C# parameters/contracts.
7. Data Annotations and `[ApiController]` validation run.
8. The DI container creates `PortfoliosController` and its handler dependencies.
9. The controller maps HTTP contract to Application query.
10. The handler loads Domain state through an interface.
11. The Domain calculator performs risk math.
12. Results map to an Application DTO.
13. MVC serializes the DTO to JSON.
14. Middleware unwinds in reverse order.

Spring MVC roughly follows:

1. embedded server accepts the request;
2. servlet filter chain;
3. `DispatcherServlet`;
4. handler mapping/controller selection;
5. argument resolution and conversion;
6. Bean Validation;
7. controller/service/repository/domain calls;
8. message converter serializes response;
9. exception resolvers/advice and filters/interceptors complete.

Layer mappings:

| This project | Typical Spring name |
|---|---|
| request record | request DTO |
| controller | `@RestController` |
| application handler | application service/use-case service |
| repository interface | repository port |
| infrastructure repository | `@Repository` adapter |
| domain calculator | domain service/strategy |
| `ApiExceptionHandler` | `@ControllerAdvice` |
| `System.Text.Json` MVC formatter | Jackson HTTP message converter |

## 16. Controller syntax line by line

```csharp
[ApiController]
[Route("api/v1/portfolios")]
public sealed partial class PortfoliosController(
    CalculatePortfolioRiskHandler calculateRisk,
    IOptions<RiskApiOptions> options,
    ILogger<PortfoliosController> logger) : ControllerBase
{
    [HttpPost("{portfolioId:guid}/risk")]
    public async Task<ActionResult<RiskReportDto>> CalculateRiskAsync(
        Guid portfolioId,
        CalculateRiskRequest request,
        CancellationToken cancellationToken)
    {
        var result = await calculateRisk.HandleAsync(
            query,
            cancellationToken);

        return Ok(result);
    }
}
```

- Attributes provide controller/route metadata.
- `:guid` is a route constraint.
- Primary-constructor parameters are injected.
- `ControllerBase` provides helpers such as `Ok`, `CreatedAtRoute`, and
  `Problem`.
- `Task<ActionResult<T>>` represents an asynchronous action that can return
  typed success or another HTTP result.
- MVC binds `portfolioId` from the route.
- MVC binds the complex request object from JSON by convention.
- ASP.NET supplies request cancellation.
- `await` releases the request thread while actual asynchronous I/O is pending;
  it does not make CPU-bound risk calculation parallel.

## 17. Testing comparison

xUnit:

```csharp
[Fact]
public void CalculateReturnsKnownEmpiricalRiskMetrics()
{
    var report = calculator.Calculate(portfolio, scenarios, 0.80m);
    Assert.Equal(50m, report.ValueAtRisk);
}

[Theory]
[InlineData(0)]
[InlineData(1)]
public void CalculateRejectsInvalidConfidenceLevel(double value)
{
    // ...
}
```

JUnit 5:

```java
@Test
void calculateReturnsKnownEmpiricalRiskMetrics() {
    var report = calculator.calculate(portfolio, scenarios, new BigDecimal("0.80"));
    assertEquals(new BigDecimal("50"), report.valueAtRisk());
}

@ParameterizedTest
@ValueSource(doubles = {0, 1})
void calculateRejectsInvalidConfidenceLevel(double value) {
    // ...
}
```

Integration comparison:

| Spring | ASP.NET Core |
|---|---|
| `@SpringBootTest` | `WebApplicationFactory<Program>` |
| MockMvc/WebTestClient | `HttpClient` against in-memory TestServer |
| `@MockBean`/test configuration | override registrations with test host builder |
| JUnit lifecycle | xUnit fixtures and async disposal |

Prefer pure domain tests for formulas, handler tests for orchestration, and a
smaller number of HTTP/database integration tests for framework wiring.

## 18. CI pipeline in this repository

`.github/workflows/ci.yml` deliberately separates stages:

```yaml
- name: Restore
  run: dotnet restore RiskEngine.slnx

- name: Build
  run: dotnet build RiskEngine.slnx --configuration Release --no-restore

- name: Test
  run: dotnet test RiskEngine.slnx --configuration Release --no-build
```

Why:

- Restore failure clearly means dependency resolution.
- Build failure clearly means compilation/analyzers.
- Test failure clearly means behavioral verification.
- `--no-restore` and `--no-build` prevent hidden repeated work.

A stricter production pipeline would also add formatting, package vulnerability
audit, container build/scan, integration tests, SBOM/signing, and controlled
deployment.

## 19. Docker build explained

Build stage:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
```

contains compiler, NuGet, and MSBuild.

Project files are copied before source so the expensive restore layer can be
cached:

```dockerfile
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/TradingRisk.Domain/TradingRisk.Domain.csproj src/TradingRisk.Domain/
# other project files...
RUN dotnet restore src/TradingRisk.Api/TradingRisk.Api.csproj
```

Then source is copied and published:

```dockerfile
COPY src/ src/
RUN dotnet publish src/TradingRisk.Api/TradingRisk.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish
```

Runtime stage:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "TradingRisk.Api.dll"]
```

This leaves the SDK/compiler out of the production image and runs as the
non-root app user supplied by the base image.

The Spring analogy is a multi-stage build that creates a Boot JAR with Maven or
Gradle, then copies it into a JRE runtime image and executes `java -jar`.

## 20. Common transition mistakes

### “I added a `using`; why can the compiler not find the type?”

`using` is not a dependency. Add the required `ProjectReference` or
`PackageReference`, then restore.

### “The project builds, so why did CI fail?”

`dotnet build` does not run tests. Run `dotnet test`.

### “I built the API; can I copy only its DLL?”

Usually no. Deploy the `dotnet publish` output, which contains the complete
runtime dependency closure and metadata.

### “I wrote a class with constructor injection; why can DI not create it?”

Declare the registration in `builder.Services`, unless your team deliberately
uses scanning/generated registrations.

### “Why does the singleton fail when it injects my DbContext?”

The singleton outlives the scoped context. Fix the lifetime/ownership design.

### “Why does my async endpoint still block?”

`async` helps while awaiting truly asynchronous I/O. CPU-heavy calculations
still consume CPU. Do not wrap every call in `Task.Run`; design bounded worker
execution for large risk jobs.

### “Why did environment configuration not override JSON?”

Use the correct nested key syntax, for example:

```bash
RiskApi__MaxScenarioCount=500
```

and ensure the deployment actually passes the variable to the process.

### “Why does Debug/Release not select appsettings.Development.json?”

Build configuration and hosting environment are different axes:

- `-c Release` controls compilation.
- `ASPNETCORE_ENVIRONMENT=Development` controls runtime environment.

### “Why is my code not included in the build?”

Check that the file is under the intended project directory, not merely visible
under the solution. Then inspect `.csproj` default/exclusion rules.

## 21. Commands to practice

```bash
# SDK/runtime selection
dotnet --info
dotnet --list-sdks
dotnet --list-runtimes

# Solution/project graph
dotnet sln RiskEngine.slnx list
dotnet list src/TradingRisk.Api/TradingRisk.Api.csproj reference
dotnet list RiskEngine.slnx package --include-transitive

# Clean dependency/build sequence
dotnet restore RiskEngine.slnx
dotnet build RiskEngine.slnx -c Release --no-restore
dotnet test RiskEngine.slnx -c Release --no-build

# Deployment output
dotnet publish src/TradingRisk.Api/TradingRisk.Api.csproj \
  -c Release \
  -o artifacts/publish

# Run the published framework-dependent app
dotnet artifacts/publish/TradingRisk.Api.dll
```

After running them, inspect:

```text
src/TradingRisk.Api/obj/project.assets.json
src/TradingRisk.Api/bin/Release/net10.0/
artifacts/publish/
```

Explain what each file is for without memorizing every generated detail.

## 22. Official references

- [.NET CLI commands](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet)
- [`dotnet publish`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish)
- [NuGet PackageReference](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)
- [NuGet Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
- [ASP.NET Core fundamentals](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/?view=aspnetcore-10.0)
- [ASP.NET Core dependency injection](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0)
- [ASP.NET Core options pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0)
- [Maven build lifecycle](https://maven.apache.org/guides/introduction/introduction-to-the-lifecycle.html)
- [Maven dependency mechanism](https://maven.apache.org/guides/introduction/introduction-to-dependency-mechanism.html)
- [Gradle Java plugin](https://docs.gradle.org/current/userguide/java_plugin.html)
- [Spring Boot beans and dependency injection](https://docs.spring.io/spring-boot/reference/using/spring-beans-and-dependency-injection.html)
- [Spring Boot executable archive packaging](https://docs.spring.io/spring-boot/maven-plugin/packaging.html)
