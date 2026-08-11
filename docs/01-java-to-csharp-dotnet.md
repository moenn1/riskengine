# Java/Spring Boot to C#/.NET map

Use this as a translation guide, not as a claim that every pair behaves
identically.

## Language and runtime

| Java | C#/.NET | Important difference |
|---|---|---|
| JDK + JVM | .NET SDK + CLR/CoreCLR | The SDK builds; the runtime executes. Apps can be framework-dependent or self-contained. |
| Maven/Gradle | MSBuild + NuGet + `dotnet` CLI | A `.csproj` is both project definition and build input. Shared properties can live in `Directory.Build.props`. |
| JAR | assembly (`.dll`) | A .NET assembly contains IL plus metadata and is loaded by the runtime. |
| `package` | `namespace` | A namespace organizes type names; it does not imply a matching directory or Java-style package visibility. |
| `import` | `using` | A `using` directive shortens names; it does not add a dependency. Project/package references do that. |
| JavaBean getter/setter | property | `position.Price` compiles to accessor methods but is first-class C# syntax. |
| `record` | `record` / `record struct` | Both favor value semantics, but C# records can be classes or structs and can still contain behavior. |
| primitive/value and reference types | value types and reference types | C# structs are copied by value; avoid large mutable structs. |
| `final` | `sealed`, `readonly`, or `const` | The correct keyword depends on whether you are restricting inheritance, mutation, or compile-time values. |
| `Optional<T>` / annotations | nullable reference types (`T?`) | Nullable reference analysis is mainly compile-time metadata; validate untrusted runtime input anyway. |
| checked exceptions | no checked exceptions | Exceptions are not part of a method's enforced signature. Document and model expected failures deliberately. |
| try-with-resources | `using` / `await using` | The resource implements `IDisposable` or `IAsyncDisposable`. |
| Stream API | LINQ | `IEnumerable<T>` pipelines are usually deferred. Database LINQ may build expression trees and translate to SQL. |
| `CompletableFuture<T>` | `Task<T>` / `ValueTask<T>` | `await` composes asynchronous operations; it does not imply a dedicated thread. |
| virtual methods by default | non-virtual methods by default | C# requires `virtual`/`abstract` and `override`; this affects proxies and mocking. |
| annotations | attributes | Attributes are metadata in square brackets, such as `[ApiController]`. |
| lambdas / functional interfaces | lambdas / delegates | `Func<T, TResult>` and `Action<T>` are common delegate types. |
| static utility method | static method or extension method | Extension methods enable call syntax on a type without modifying it. |
| switch / `instanceof` patterns | switch expressions and patterns | C# uses patterns heavily for concise, exhaustive branching. |

Continue from the language map into:

- [build, dependencies, startup, and packaging](05-build-dependencies-startup-and-packaging.md);
- [architecture and domain design](07-architecture-and-domain-design-deep-dive.md);
- [ASP.NET Core HTTP/runtime behavior](08-aspnet-core-web-api-deep-dive.md);
- [.NET and risk testing](09-testing-dotnet-and-risk-deep-dive.md); and
- [production-scale .NET engineering](10-production-scale-dotnet-deep-dive.md).

## Reading a C# source file

Compare a small service in both languages.

### Java

```java
package com.acme.risk.application;

import com.acme.risk.domain.Portfolio;
import java.util.UUID;

public final class PortfolioService {
    private final PortfolioRepository repository;

    public PortfolioService(PortfolioRepository repository) {
        this.repository = repository;
    }

    public Portfolio find(UUID id) {
        return repository.findById(id)
            .orElseThrow(() -> new PortfolioNotFoundException(id));
    }
}
```

### C#

```csharp
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Application.Portfolios;

public sealed class PortfolioService(IPortfolioRepository repository)
{
    public async Task<Portfolio> FindAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var portfolio = await repository.GetByIdAsync(
            new PortfolioId(id),
            cancellationToken);

        return portfolio
            ?? throw new PortfolioNotFoundException(id);
    }
}
```

Read the C# version from top to bottom:

- `using` is comparable to `import`; it makes a name available but does not add
  a build dependency.
- A file-scoped `namespace` applies to the rest of the file without braces.
- `sealed` prevents inheritance, like Java `final` on a class.
- `(IPortfolioRepository repository)` after the class name is a C# primary
  constructor. The parameter is available throughout the class.
- `Task<Portfolio>` is an asynchronous eventual result.
- The `Async` suffix is a .NET naming convention, not a compiler requirement.
- `CancellationToken` carries cooperative cancellation from the HTTP request.
- `var` still has a compile-time static type; it is not JavaScript-style
  dynamic typing.
- `??` returns the right side only when the left side is null.
- A semicolon ends statements, while braces define the method/class scope just
  as in Java.

## Classes, records, and structs

C# gives you more type-shape choices than ordinary Java application code.

### Class

```csharp
public sealed class Portfolio
{
    private readonly List<Position> _positions = [];

    public string Name { get; }

    public IReadOnlyList<Position> Positions => _positions;
}
```

- A class is a reference type. Variables hold a reference to an object.
- Ordinary classes use identity equality unless equality is overridden.
- `_positions` is a field. The underscore is a common private-field convention.
- `Name` is a getter-only property, not a public field.
- `Positions => _positions` is an expression-bodied getter.
- Exposing `IReadOnlyList<T>` communicates intended use, but you must still avoid
  leaking a mutable list instance that a caller can cast and modify.

### Record class

```csharp
public sealed record PositionDto(
    string InstrumentId,
    decimal Quantity,
    decimal Price,
    decimal MarketValue);
```

This is closest to a Java record:

```java
public record PositionDto(
    String instrumentId,
    BigDecimal quantity,
    BigDecimal price,
    BigDecimal marketValue) {}
```

A C# record class is still a reference type, but the compiler generates
value-based equality, deconstruction, a useful `ToString`, and `with`
copy-expression support. “Record” does not mean “DTO only”; a record can contain
validation and behavior.

### Record struct

```csharp
public readonly record struct PortfolioId(Guid Value);
```

This is a small value type with generated value equality. It is useful for
strong IDs because:

```csharp
void LoadPortfolio(PortfolioId id) { }
void LoadTrade(TradeId id) { }
```

cannot be called with the wrong ID type even if both wrap `Guid`. Java often
uses a small record wrapper for the same semantic protection, but the Java
record remains a reference type.

Use structs for small, immutable values. Large or mutable structs are easy to
copy accidentally and are usually a poor domain-model choice.

## Properties, fields, `init`, and `required`

Java typically exposes state through explicit methods:

```java
public final class RiskOptions {
    private int maxScenarioCount;

    public int getMaxScenarioCount() {
        return maxScenarioCount;
    }

    public void setMaxScenarioCount(int value) {
        maxScenarioCount = value;
    }
}
```

C# properties make the accessors part of the language:

```csharp
public sealed class RiskOptions
{
    public int MaxScenarioCount { get; set; } = 1_000;
}
```

Common forms:

```csharp
public string Name { get; }                 // constructor/factory sets it
public string Code { get; private set; }    // type can set it; callers cannot
public string Region { get; init; } = "EU"; // only during initialization
public required string Desk { get; init; }  // caller must initialize it
public decimal Value => Quantity * Price;   // calculated property
```

`required` is primarily a compile-time initialization contract. JSON, reflection,
or older callers can still violate assumptions, so domain validation remains
necessary.

## Constructors and dependency injection syntax

Traditional C# constructor injection looks almost identical to Java:

```csharp
public sealed class RiskService
{
    private readonly IRiskCalculator _calculator;

    public RiskService(IRiskCalculator calculator)
    {
        _calculator = calculator;
    }
}
```

The primary-constructor form used by this project is shorter:

```csharp
public sealed class RiskService(IRiskCalculator calculator)
{
    public RiskReport Calculate(Portfolio portfolio)
    {
        return calculator.Calculate(portfolio, scenarios, 0.99m);
    }
}
```

The syntax only declares a constructor dependency. It does **not** register the
class in the .NET DI container. Registration is a separate runtime step:

```csharp
builder.Services.AddSingleton<IRiskCalculator, HistoricalSimulationRiskCalculator>();
builder.Services.AddScoped<RiskService>();
```

Spring commonly discovers `@Service` automatically. ASP.NET Core normally uses
explicit registration; this distinction is explained in detail in the deep
dive.

## Methods, named arguments, and optional arguments

```csharp
public RiskReport Calculate(
    Portfolio portfolio,
    decimal confidenceLevel = 0.99m)
{
    ArgumentNullException.ThrowIfNull(portfolio);
    return CalculateCore(portfolio, confidenceLevel);
}

var report = Calculate(
    portfolio: portfolio,
    confidenceLevel: 0.975m);
```

- The type precedes a parameter name, as in Java.
- A default value makes an argument optional.
- Named arguments can improve readability and allow different call-site order.
- `0.99m` is a `decimal` literal; without `m`, `0.99` is a `double`.
- C# has no Java-style method-level `throws` declaration.
- Prefer optional arguments for stable local APIs; adding values to public
  contracts can create versioning ambiguity.

Expression-bodied methods are useful when the body is genuinely one expression:

```csharp
public decimal MarketValue() => Quantity * Price;
```

## Collections and collection expressions

```csharp
Position[] array = [first, second];
List<Position> mutable = [first, second];
IReadOnlyList<Position> readOnly = mutable.AsReadOnly();
Dictionary<string, decimal> returns = new()
{
    ["AAPL"] = -0.02m,
    ["MSFT"] = 0.01m
};
```

Approximate Java equivalents are arrays, `ArrayList`, unmodifiable list views,
and `HashMap`. Important interfaces:

- `IEnumerable<T>`: can be enumerated; often lazy.
- `ICollection<T>`: count plus mutation contract.
- `IReadOnlyCollection<T>`: enumeration and count, no mutation methods.
- `IList<T>` / `IReadOnlyList<T>`: indexed access.
- `Dictionary<TKey,TValue>` / `IReadOnlyDictionary<TKey,TValue>`.

`IReadOnlyList<T>` prevents mutation through that interface; it does not make
the underlying object immutable. This project copies inputs before exposing
read-only views.

## LINQ versus Java Streams

Java:

```java
BigDecimal total = positions.stream()
    .filter(position -> position.quantity().signum() > 0)
    .map(Position::marketValue)
    .reduce(BigDecimal.ZERO, BigDecimal::add);
```

C#:

```csharp
var total = positions
    .Where(position => position.Quantity > 0m)
    .Select(position => position.MarketValue)
    .Sum();
```

Useful mappings:

| Java Stream | LINQ |
|---|---|
| `filter` | `Where` |
| `map` | `Select` |
| `flatMap` | `SelectMany` |
| `sorted` | `OrderBy` / `OrderByDescending` |
| `findFirst` | `FirstOrDefault` / `First` |
| `anyMatch` | `Any` |
| `allMatch` | `All` |
| `collect(toList())` | `ToList()` / `ToArray()` |
| `reduce` | `Aggregate` or a specialized operator such as `Sum` |

Both can be lazy, but a crucial .NET distinction is:

- `IEnumerable<T>` executes normal .NET delegates, usually in memory.
- `IQueryable<T>` captures expression trees that a provider such as EF Core may
  translate into SQL.

Calling `ToList()` materializes now. Re-enumerating a lazy sequence can rerun
work or another database query.

## Nullability

With nullable reference types enabled:

```csharp
string nonNullable = "USD";
string? maybeCurrency = FindCurrency();

if (maybeCurrency is not null)
{
    Console.WriteLine(maybeCurrency.Length);
}

var code = maybeCurrency?.Trim().ToUpperInvariant() ?? "USD";
```

- `string` means “intended not to be null.”
- `string?` means “may be null.”
- `?.` stops and returns null when the receiver is null.
- `??` supplies a fallback.
- `!` suppresses a compiler warning; it does not perform a runtime check and
  should not be used to hide uncertain logic.
- Flow analysis learns from guards and pattern matches.

Java `Optional<T>` is a runtime container. C# nullable reference annotations are
mostly a compile-time analysis system. Neither replaces input validation.

Value types use `Nullable<T>`, written with the same syntax:

```csharp
decimal? optionalConfidence = null;
```

## Pattern matching and switch expressions

```csharp
var description = value switch
{
    null => "missing",
    decimal amount and < 0m => "loss",
    decimal => "non-negative amount",
    Portfolio { Positions.Count: 0 } => "empty portfolio",
    Portfolio portfolio => $"portfolio {portfolio.Name}",
    _ => "unknown"
};
```

The expression returns a value, and patterns can test type, value, relational
conditions, and object properties. Modern Java has comparable switch and
`instanceof` pattern features, but C# codebases tend to use this syntax
extensively.

## Exceptions, cleanup, and cancellation

Java try-with-resources:

```java
try (InputStream stream = client.open()) {
    return stream.readAllBytes();
}
```

C# synchronous disposal:

```csharp
using var stream = client.Open();
return Read(stream);
```

C# asynchronous disposal:

```csharp
await using var connection = await dataSource.OpenConnectionAsync(
    cancellationToken);
```

`using` compiles to `try/finally`-style cleanup around `IDisposable`.
`await using` calls `IAsyncDisposable.DisposeAsync`.

Cancellation is explicit and cooperative:

```csharp
public async Task<Portfolio?> FindAsync(
    PortfolioId id,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    return await repository.GetByIdAsync(id, cancellationToken);
}
```

Do not catch `OperationCanceledException` and convert it into a generic 500
unless your boundary has a deliberate cancellation policy.

## Attributes versus annotations

Java:

```java
@RestController
@RequestMapping("/api/v1/portfolios")
public final class PortfolioController { }
```

C#:

```csharp
[ApiController]
[Route("api/v1/portfolios")]
public sealed class PortfoliosController : ControllerBase { }
```

Both attach metadata. Frameworks inspect that metadata through reflection or
generated code. Attribute targets can be explicit:

```csharp
[property: Required]
[method: Obsolete]
[assembly: CLSCompliant(true)]
```

ASP.NET Core validation on positional record request contracts expects metadata
on constructor parameters, which is why this project uses `[Required]` directly
on those parameters rather than `[property: Required]`.

## Generics, delegates, and extension methods

Generic constraint:

```csharp
public static T RequireValue<T>(T? value)
    where T : class
{
    return value ?? throw new ArgumentNullException(nameof(value));
}
```

Delegate values:

```csharp
Func<Position, bool> isLong = position => position.Quantity > 0m;
Action<string> write = Console.WriteLine;
```

Extension method:

```csharp
public static class StringExtensions
{
    public static bool IsBlank(this string? value)
    {
        return string.IsNullOrWhiteSpace(value);
    }
}

if (name.IsBlank()) { }
```

An extension method is still a static method. The `this` modifier on the first
parameter enables instance-like call syntax. Java normally uses a static helper
or library utility instead.

## Visibility and naming

| C# | Java comparison |
|---|---|
| `public` | `public` |
| `private` | `private` |
| `protected` | `protected`, with different package semantics |
| `internal` | visible inside the assembly; closest intent is package/module internal |
| no modifier on a class member | normally `private`, unlike Java package-private |
| `sealed class` | `final class` |
| `abstract class` | `abstract class` |
| `virtual` + `override` | Java methods are usually overridable unless `final`; C# requires opt-in |

Conventions:

- Types, public members, methods, properties: `PascalCase`.
- Parameters and local variables: `camelCase`.
- Private fields: commonly `_camelCase`.
- Interfaces: normally `I` prefix, such as `IPortfolioRepository`.
- Async methods returning `Task`/`ValueTask`: normally `Async` suffix.
- Constants: usually `PascalCase`, not Java's `UPPER_SNAKE_CASE`.

## Finance-sensitive type choices

| Need | Prefer | Why |
|---|---|---|
| Currency amounts and exact decimal inputs | `decimal` | Base-10, 128-bit representation avoids many binary floating-point surprises. It is fixed precision, not Java's arbitrary-precision `BigDecimal`. |
| Statistical/transcendental math | `double` | `Math.Sqrt`, logarithms, distributions, and numerical libraries generally use IEEE-754 doubles. |
| Identifier | strong record struct around `Guid` or string | Prevents mixing unrelated IDs at compile time. |
| Calendar date | `DateOnly` | Avoids inventing a time and zone for a date-only observation. |
| Instant | `DateTimeOffset` or `Instant` from a chosen time library | An offset makes the instant unambiguous; agree on UTC storage. |
| Testable current time | `TimeProvider` | Avoids static `DateTime.UtcNow` in business workflows. |

This project calculates exact position arithmetic in `decimal`, temporarily
converts P&L observations to `double` for square roots, and converts the result
back. Production quantitative code must define precision and rounding policies
more rigorously.

## Spring Boot and ASP.NET Core

| Spring Boot | ASP.NET Core in this project |
|---|---|
| `public static void main` + `SpringApplication.run` | top-level statements and `WebApplication.CreateBuilder` in `Program.cs` |
| auto-configuration/component scan | explicit `builder.Services.Add...` registrations |
| `@RestController` | class deriving from `ControllerBase` with `[ApiController]` |
| `@RequestMapping`, `@PostMapping` | `[Route]`, `[HttpPost]` |
| request/response record | request contract record and application DTO |
| Bean Validation | Data Annotations plus domain/application validation |
| `@Service` | plain class registered with the DI container |
| `@Repository` | repository implementation registered for an interface |
| constructor injection | primary-constructor or ordinary constructor injection |
| singleton bean (default) | `AddSingleton` |
| request scope | usually `AddScoped` |
| prototype scope | `AddTransient` |
| `application.yml` / profiles | `appsettings.json`, environment variants, environment variables, other providers |
| `@ConfigurationProperties` | options pattern with `IOptions<T>` |
| servlet filter | middleware |
| controller advice / exception handler | `IExceptionHandler` and Problem Details |
| Actuator health | ASP.NET Core health checks |
| springdoc OpenAPI | `Microsoft.AspNetCore.OpenApi` |
| `RestClient`/`WebClient` | `HttpClient` created through `IHttpClientFactory` |
| `@Async` / scheduler | `BackgroundService`, queues/channels, or an external scheduler |
| Micrometer | .NET metrics APIs and OpenTelemetry |
| JPA/Hibernate | EF Core |

## Dependency injection lifetimes

The biggest early mistake is allowing a singleton to capture a scoped service.
The singleton outlives an HTTP request, while the scoped dependency (often a
future `DbContext`) does not.

- Singleton: one instance for the process. Use for immutable, thread-safe,
  stateless services such as the risk calculator and system clock.
- Scoped: one instance per HTTP request. This project uses it for handlers, the
  EF repository, and `RiskDbContext`.
- Transient: a new instance each resolution. Use for cheap, stateless objects
  when scoped identity is unnecessary.

ASP.NET Core DI is intentionally less magical than Spring. Registration occurs
in the composition root, and class libraries can remain unaware of the
container.

## Async and cancellation

Typical shape:

```csharp
public async Task<Portfolio?> GetByIdAsync(
    PortfolioId id,
    CancellationToken cancellationToken)
{
    return await dbContext.Portfolios
        .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
}
```

Rules:

- async flows “all the way”; do not call `.Result` or `.Wait()` in request code;
- pass the request `CancellationToken` to I/O;
- cancellation is cooperative, not a forced thread interruption;
- do not add `Task.Run` around naturally asynchronous database or HTTP calls;
- `ValueTask<T>` is an optimization with usage constraints, not a default
  replacement for `Task<T>`.

## LINQ traps for Java developers

- Many LINQ operators are lazy. Enumerating twice may execute twice.
- `IEnumerable<T>` runs .NET code; `IQueryable<T>` builds an expression for a
  provider such as EF Core.
- A method that works in memory might not translate to SQL.
- Materialize intentionally with `ToArrayAsync`/`ToListAsync`.
- Prefer projecting only needed columns instead of loading full entities.
- Never assume ordering without an explicit `OrderBy`.

## Errors and results

This project uses:

- domain exceptions when constructing an invalid domain object;
- application exceptions for invalid use-case inputs and missing resources;
- one HTTP exception handler that maps those to Problem Details;
- cancellation propagated rather than converted to a business error.

Expected high-volume business outcomes may deserve an explicit result type
instead of exceptions. Do not copy Spring's `ResponseStatusException` into the
domain: HTTP is an outer-layer concern.
