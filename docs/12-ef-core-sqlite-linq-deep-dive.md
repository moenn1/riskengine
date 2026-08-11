# EF Core, SQLite, repositories, and LINQ deep dive

This chapter explains the complete persistence slice, not just the commands
needed to run it. Read it beside the files under
`TradingRisk.Infrastructure/Persistence`; the comments in those files mark the
same concepts at the point where they matter.

The implementation deliberately combines two goals:

1. it is a small database that works locally without installing a server; and
2. it demonstrates patterns that transfer to a large .NET service using SQL
   Server or PostgreSQL.

SQLite satisfies the first goal. EF Core, an Application-owned port, scoped
units of work, explicit mappings, migrations, provider-backed tests, and
translated LINQ satisfy the second.

See also:

- [architecture and file guide](02-architecture-and-file-guide.md);
- [Java-to-C# guide](01-java-to-csharp-dotnet.md);
- [all Mermaid diagrams](06-mermaid-diagrams.md), especially diagrams 8 and 9;
- [risk metrics](03-risk-metrics.md).

## 1. The slice from HTTP to disk

For a write, the call path is:

```text
POST /api/v1/portfolios
  -> PortfoliosController
  -> CreatePortfolioHandler
  -> IPortfolioRepository                  Application port
  -> SqlitePortfolioRepository             Infrastructure adapter
  -> RiskDbContext + EF change tracker
  -> Microsoft.EntityFrameworkCore.Sqlite
  -> App_Data/riskengine.db
```

For a search, it is:

```text
GET /api/v1/portfolios?baseCurrency=USD&page=1&pageSize=20
  -> PortfolioSearchRequest                HTTP/query-string contract
  -> SearchPortfoliosQuery                 Application message
  -> SearchPortfoliosHandler               validation and normalization
  -> IPortfolioQueries                     Application read port
  -> SqlitePortfolioRepository             composes IQueryable
  -> EF Core                               translates expression tree
  -> parameterized SQLite SQL
  -> domain objects -> response DTO -> JSON
```

The database is not called from the controller. HTTP, use-case policy, domain
rules, and SQL translation each have a separate home.

## 2. What EF Core is—and is not

EF Core is Microsoft's object-relational mapper for .NET. It provides:

- a model describing .NET types and relational tables;
- `DbContext`, a short-lived unit of work and change tracker;
- LINQ-to-provider translation;
- parameterized database commands;
- relationship materialization;
- schema migrations;
- provider packages for different databases.

It does not make relational behavior disappear. Indexes, unique constraints,
transactions, locking, query plans, round trips, provider limitations, and
schema deployment remain engineering concerns.

### Java/Spring comparison

| Java/Spring/JPA | .NET/EF Core | Important difference |
|---|---|---|
| JPA specification | EF Core API | EF Core is not a JPA implementation or specification shared by vendors. |
| Hibernate | EF Core runtime | Both track entities and translate object-oriented queries, but their APIs and semantics differ. |
| `EntityManager` | `DbContext` | Both are short-lived units of work; neither should be shared concurrently. |
| persistence context | change tracker | Both maintain identity and change state for loaded/tracked entities. |
| `@Entity`, `@Table` | entity type + Fluent configuration | EF also supports attributes, but this project keeps persistence metadata outside Domain. |
| Spring Data `JpaRepository` | no required equivalent | EF exposes `DbSet<T>`; teams often add focused repositories/ports rather than a generic CRUD abstraction. |
| JPQL / Criteria API | LINQ over `IQueryable<T>` | LINQ is normal typed C# syntax captured as an expression tree. |
| `JOIN FETCH` / entity graph | `Include` / `ThenInclude` | Both request related data; query shape and collection-loading costs still matter. |
| `@Transactional` | `SaveChanges` transaction or explicit `Database.BeginTransaction` | ASP.NET Core has no implicit Spring-style annotation interceptor by default. |
| Flyway/Liquibase | EF Core migrations | Large teams may still use SQL-first migration tools or generated reviewed scripts. |
| `Pageable` / `Page<T>` | `OrderBy`, `Skip`, `Take` + an application page DTO | EF provides operators; this project owns its public paging contract. |
| `@Transactional(readOnly=true)` hint | `AsNoTracking()` for tracking behavior | `AsNoTracking` avoids tracking; it does not by itself open a read-only database transaction. |

## 3. Dependencies: NuGet versus Maven/Gradle

The Infrastructure project names its packages in
`TradingRisk.Infrastructure.csproj`:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>
        runtime; build; native; contentfiles; analyzers; buildtransitive
    </IncludeAssets>
</PackageReference>
```

Versions are in the repository-level `Directory.Packages.props`:

```xml
<PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite"
                Version="10.0.10" />
```

This is NuGet Central Package Management. It resembles a Maven parent
`dependencyManagement` section or a Gradle version catalog:

- `PackageReference` means this project consumes the package;
- `PackageVersion` chooses the shared version;
- `ProjectReference` points to another project in this repository;
- restore resolves the complete transitive graph into `obj/project.assets.json`.

`Microsoft.EntityFrameworkCore.Sqlite` supplies EF's SQLite provider.
`SQLitePCLRaw.bundle_e_sqlite3` supplies the native SQLite engine. The direct
2.1.12 reference intentionally raises an older vulnerable transitive minimum.
Warnings are errors in this repository, so a vulnerable dependency cannot pass
restore unnoticed.

`Microsoft.EntityFrameworkCore.Design` is used by `dotnet ef` while designing
the model and migrations. `PrivateAssets=all` means that this tooling dependency
does not flow to a project that consumes Infrastructure. This is closest to a
build/tool-only dependency, although Maven scopes and NuGet asset metadata are
not exact equivalents.

The API also references the EF design package because `dotnet ef` builds and
loads the startup project to discover hosting configuration. It references the
EF health-check package so `/health` can test `RiskDbContext` connectivity.

## 4. SQLite and the connection string

The default configuration is in `src/TradingRisk.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "RiskDatabase": "Data Source=App_Data/riskengine.db;Foreign Keys=True;Default Timeout=5;Pooling=True"
  }
}
```

The important parts are:

- `Data Source`: database file location;
- `Foreign Keys=True`: enables foreign-key enforcement for connections;
- `Default Timeout=5`: how long a command waits when the file is locked;
- `Pooling=True`: reuses provider connections.

`Program.cs` reads it through:

```csharp
var connection = builder.Configuration
    .GetConnectionString("RiskDatabase");
```

`GetConnectionString("RiskDatabase")` is shorthand for the hierarchical key
`ConnectionStrings:RiskDatabase`. The environment-variable override is:

```bash
ConnectionStrings__RiskDatabase="Data Source=/absolute/path/riskengine.db" \
  dotnet run --project src/TradingRisk.Api
```

ASP.NET Core uses `__` in environment variables where JSON uses `:`. Spring's
relaxed binding solves a similar configuration problem, but the exact naming
rules differ.

### Why resolve the path ourselves?

A relative SQLite path normally depends on the process working directory. IDE,
CLI, test, publish, and container working directories can differ.
`DatabaseInitialization.ResolveConnectionString` parses the string with
`SqliteConnectionStringBuilder`, anchors relative `Data Source` values to the
host content root, creates the exact parent directory, and reconstructs the
connection string. The database therefore consistently lives at:

```text
src/TradingRisk.Api/App_Data/riskengine.db
```

Absolute paths and `:memory:` are left alone. Parsing is safer than splitting a
connection string manually because quoted/escaped values have defined provider
syntax.

The database files are ignored by Git. The schema definition belongs in
migrations; personal runtime data does not belong in source control.

## 5. Registration, scopes, and one unit of work

`Program.cs` calls the Infrastructure extension method:

```csharp
builder.Services.AddTradingRiskPersistence(
    riskDatabaseConnection,
    builder.Environment.ContentRootPath);
```

The method registers:

```csharp
services.AddDbContext<RiskDbContext>(options =>
    options.UseSqlite(resolvedConnectionString));

services.AddScoped<SqlitePortfolioRepository>();
services.AddScoped<IPortfolioRepository>(provider =>
    provider.GetRequiredService<SqlitePortfolioRepository>());
services.AddScoped<IPortfolioQueries>(provider =>
    provider.GetRequiredService<SqlitePortfolioRepository>());
```

`AddDbContext<T>` registers a scoped context by default. ASP.NET Core creates a
scope for each request, so one normal request receives one `RiskDbContext` and
disposes it when the request ends.

The concrete repository is registered once per scope. Both interfaces resolve
that same concrete instance. If both handlers were resolved during one request,
they would share one repository and one context—not two unrelated units of
work.

This registration:

```csharp
services.AddScoped<IPortfolioRepository, SqlitePortfolioRepository>();
services.AddScoped<IPortfolioQueries, SqlitePortfolioRepository>();
```

would normally create a different `SqlitePortfolioRepository` for each service
descriptor. The factory form deliberately aliases both ports to the concrete
scoped registration.

### Why not singleton?

`DbContext` is stateful and not thread-safe. It tracks entity instances and
represents a unit of work. A singleton context would mix unrelated requests,
grow its change tracker indefinitely, create concurrency races, and hold stale
state. Do not run parallel EF operations on one context either.

This is the same reason an application should not keep one JPA
`EntityManager`/persistence context for the whole JVM.

## 6. `RiskDbContext` syntax

The declaration:

```csharp
public sealed class RiskDbContext(DbContextOptions<RiskDbContext> options)
    : DbContext(options)
```

uses a C# primary constructor. The generated constructor accepts strongly typed
options and passes them to the base `DbContext` constructor. A Java-style
expanded mental model is:

```java
final class RiskEntityManagerWrapper extends SomeBaseContext {
    RiskEntityManagerWrapper(ContextOptions<RiskDbContext> options) {
        super(options);
    }
}
```

The real C# type exposes:

```csharp
internal DbSet<PortfolioEntity> Portfolios => Set<PortfolioEntity>();
```

This is an expression-bodied read-only property. `DbSet<T>` is the entry point
for querying and tracking one entity type. `internal` means only Infrastructure
can access it; Application cannot accidentally construct EF queries.

`OnModelCreating` calls:

```csharp
modelBuilder.ApplyConfigurationsFromAssembly(
    typeof(RiskDbContext).Assembly);
```

EF scans this assembly for `IEntityTypeConfiguration<T>` implementations. This
is focused model-configuration discovery, not broad runtime component scanning
like Spring's `@ComponentScan`.

## 7. Domain objects versus persistence entities

The Domain `Portfolio` and `Position` types enforce creation rules, expose
value objects, and prefer immutable state. EF persistence entities are mutable
classes with scalar foreign keys and navigation properties.

Why duplicate the shape?

- EF mechanics do not leak into Domain;
- the Domain project has no provider package;
- table shape can change independently of an API/domain shape;
- database-generated position keys are not invented as domain concepts;
- reads re-enter validated factories;
- a future PostgreSQL or event-store adapter can use the same Application port.

This is a deliberate architectural choice, not an EF requirement. EF can map
rich domain models directly, and small CRUD systems often do. Separate models
cost mapping code, so use them when the boundary value exceeds that cost.

`PortfolioEntityMapper.ToEntity` projects an aggregate into a tracked graph.
`ToDomain` orders positions for deterministic reconstruction and calls
`Position.Create` and `Portfolio.Create`. If stored data violates current
domain rules, the load fails at the boundary instead of producing an invalid
object.

In Java terms, the EF entities play the role of package-private JPA entities,
while Domain objects resemble immutable business records/value objects. MapStruct
could generate some Java mapping; here the small mapping is explicit C#.

## 8. Fluent relational mapping

`PortfolioEntityConfiguration` and `PositionEntityConfiguration` implement:

```csharp
IEntityTypeConfiguration<PortfolioEntity>
```

The builder calls mean:

| Fluent call | Relational meaning | Typical JPA equivalent |
|---|---|---|
| `ToTable("Portfolios")` | table name | `@Table(name="Portfolios")` |
| `HasKey(x => x.Id)` | primary key | `@Id` |
| `ValueGeneratedNever()` | application supplies ID | no `@GeneratedValue` |
| `HasMaxLength(100)` | max-length model/schema facet | `@Column(length=100)` |
| `IsRequired()` | non-nullable relationship/property | `nullable=false` plus type constraints |
| `HasIndex(x => x.Name)` | database index | `@Table(indexes=...)` or migration DDL |
| `HasPrecision(38, 18)` | decimal precision contract | `precision=38, scale=18` |
| `HasMany(...).WithOne(...)` | one-to-many relationship | `@OneToMany` / `@ManyToOne` |
| `HasForeignKey(...)` | explicit FK property | `@JoinColumn` |
| `OnDelete(Cascade)` | dependent rows deleted with parent | database cascade/cascade semantics |
| `IsUnique()` | unique index | unique constraint/index |

The unique `(PortfolioId, InstrumentId)` index repeats an important Domain
invariant at the database boundary. Domain validation protects normal creation;
the constraint protects against races, bugs, manual SQL, and other writers.
Defense in depth matters most for money and position data.

Indexes support the demonstrated name, currency, and instrument filters. An
index is not automatically useful merely because it exists: `Contains` often
translates to a pattern with a leading wildcard, which may prevent an ordinary
B-tree index from seeking. Inspect real query plans and workload statistics
before tuning production indexes.

## 9. Migrations: executable schema history

The repository contains a local tool manifest at `.config/dotnet-tools.json`.
It pins `dotnet-ef` so every developer and CI uses the same major/tool version:

```bash
dotnet tool restore
dotnet tool run dotnet-ef --version
```

The initial migration has three generated artifacts:

- `..._InitialSqlitePersistence.cs`: forward `Up` and reverse `Down` operations;
- `...Designer.cs`: metadata for this migration;
- `RiskDbContextModelSnapshot.cs`: EF's latest model snapshot used to calculate
  the next difference.

The generated SQL creates `Portfolios`, `Positions`, the foreign key, indexes,
and EF's `__EFMigrationsHistory` bookkeeping table.

To add a model change:

```bash
dotnet tool restore

dotnet tool run dotnet-ef migrations add AddRiskRunHistory \
  --project src/TradingRisk.Infrastructure \
  --startup-project src/TradingRisk.Api

dotnet tool run dotnet-ef migrations has-pending-model-changes \
  --project src/TradingRisk.Infrastructure \
  --startup-project src/TradingRisk.Api

dotnet test RiskEngine.slnx -c Release
```

Review generated migration code as carefully as handwritten code. Renaming a
property can look like drop-and-add and lose data unless the migration is
corrected to rename. Test both an empty database and an upgrade containing
representative data.

### Design-time factory

`RiskDbContextFactory` implements
`IDesignTimeDbContextFactory<RiskDbContext>`. Tooling can create the context
without running the web server. This makes migration generation predictable
and documents a design-time fallback connection.

### Startup migration versus production deployment

This learning app calls:

```csharp
await app.Services.MigrateTradingRiskDatabaseAsync();
```

after `builder.Build()` and before `app.Run()`. The extension creates a scope,
resolves the scoped context, checks/logs pending migrations, and calls
`MigrateAsync`.

That convenience is good for a single local process. In a replicated service,
multiple instances can start together, database permissions should be
separated, and migration duration can exceed startup probes. Prefer a reviewed
idempotent SQL script or EF migration bundle in the deployment pipeline, then
start application replicas with schema-compatible permissions.

## 10. The write repository method

```csharp
dbContext.Portfolios.Add(PortfolioEntityMapper.ToEntity(portfolio));
await dbContext.SaveChangesAsync(cancellationToken);
```

`Add` does not immediately execute an insert. It marks the portfolio entity and
reachable new positions as `Added` in the change tracker. `SaveChangesAsync`:

1. detects tracked changes;
2. creates parameterized insert commands;
3. uses a transaction when several statements must be atomic;
4. sends commands through the provider;
5. accepts successful tracked changes.

The cancellation token can cancel asynchronous database waiting. Cancellation
is cooperative; do not assume it rolls back unrelated external effects.

`SaveChangesAsync` is similar to flushing a JPA persistence context, though the
exact flush timing and transaction integration differ. This method saves
immediately because one handler represents one small use case. A more complex
use case might coordinate repositories behind an explicit application unit of
work/transaction.

## 11. Reading an aggregate

The lookup uses:

```csharp
var entity = await dbContext.Portfolios
    .AsNoTracking()
    .Include(portfolio => portfolio.Positions)
    .SingleOrDefaultAsync(
        portfolio => portfolio.Id == portfolioId.Value,
        cancellationToken);
```

- `AsNoTracking` says this read result will not be edited and saved through the
  context. EF avoids change-tracking snapshots and identity management costs.
- `Include` loads the positions required to reconstruct the complete aggregate.
- the lambda is an expression EF translates to a predicate;
- `SingleOrDefaultAsync` returns no entity for zero rows and throws if the
  supposedly unique predicate returns more than one.
- the terminal `Async` method executes SQL.

`FindAsync` can use the change tracker and primary key efficiently, but it does
not express this required collection include as clearly. Explicit query shape
is valuable at an aggregate boundary.

Avoid lazy loading by surprise. It can turn a loop into N+1 database calls and
make mapping behavior depend on whether a context is still alive.

## 12. LINQ: `IEnumerable` versus `IQueryable`

LINQ is a family of operators, not one execution engine.

### `IEnumerable<T>`

With arrays, lists, and other in-memory sequences, lambdas compile to delegates
and the CLR executes them:

```csharp
IEnumerable<Portfolio> values = loadedPortfolios;
var names = values.Where(p => p.Positions.Count > 2)
                  .Select(p => p.Name);
```

### `IQueryable<T>`

With an EF `DbSet<T>`, operators build an expression tree:

```csharp
IQueryable<PortfolioEntity> query = dbContext.Portfolios;
query = query.Where(p => p.Positions.Count >= minimum);
```

The tree describes method calls, member access, constants, and operators as
data. The SQLite provider inspects it and translates supported nodes into SQL.
The query is deferred: assigning `query` and calling `Where` sends no command.

Terminal operators include:

- `ToArrayAsync`, `ToListAsync`;
- `SingleOrDefaultAsync`, `FirstOrDefaultAsync`;
- `CountAsync`, `AnyAsync`;
- `SumAsync`, `MaxAsync`, and other aggregates.

Calling `AsEnumerable()` switches subsequent operators to in-process LINQ. That
is sometimes intentional after a small projection, but doing it early can load
an entire table and hide a query that should have been translated.

Do not return `IQueryable` from Infrastructure through Application. That would
leak EF/provider semantics, let outer layers accidentally create expensive
queries, and make the port hard to implement using another storage system.

## 13. Building the search query

`SearchAsync` starts with:

```csharp
IQueryable<PortfolioEntity> query =
    dbContext.Portfolios.AsNoTracking();
```

Every optional filter replaces the recipe with another recipe:

```csharp
if (criteria.BaseCurrency is not null)
{
    query = query.Where(portfolio =>
        portfolio.BaseCurrency == criteria.BaseCurrency);
}
```

Variables such as `criteria.BaseCurrency` become SQL parameters rather than
being concatenated into SQL text. Parameterization is important for injection
safety and plan reuse.

### String filter

```csharp
portfolio.Name.Contains(criteria.NameContains)
```

translates to provider-specific string search (SQLite uses `instr` in current
EF translation). Database collation determines case behavior; do not assume all
providers behave identically.

### Navigation `Any`

```csharp
portfolio.Positions.Any(position =>
    position.InstrumentId == criteria.InstrumentId)
```

becomes a correlated `EXISTS`, conceptually:

```sql
WHERE EXISTS (
    SELECT 1
    FROM Positions AS p
    WHERE p.PortfolioId = portfolio.Id
      AND p.InstrumentId = @instrument)
```

This checks in the database; it does not load every position and call LINQ to
Objects.

### Navigation `Count`

```csharp
portfolio.Positions.Count >= criteria.MinimumPositionCount.Value
```

becomes a correlated count, conceptually:

```sql
WHERE (
    SELECT COUNT(*)
    FROM Positions AS p
    WHERE p.PortfolioId = portfolio.Id) >= @minimum
```

### Count before paging

`CountAsync` runs against the fully filtered recipe before `Skip` and `Take`.
That produces `TotalCount` for the response. It is a separate database round
trip, which is the normal cost of exact offset-pagination metadata.

### Deterministic offset pagination

```csharp
query.OrderBy(p => p.Name)
     .ThenBy(p => p.Id)
     .Skip(criteria.Offset)
     .Take(criteria.Limit)
```

SQLite translates `Take` and `Skip` to `LIMIT` and `OFFSET`. Ordering by a
unique combination is mandatory for stable pages. Ordering only by name leaves
ties nondeterministic.

Offset pagination is convenient and lets users jump to a page, but large
offsets make the database process skipped rows and concurrent inserts can shift
results. High-volume timelines commonly use keyset pagination:

```text
WHERE (Name, Id) > (lastName, lastId)
ORDER BY Name, Id
LIMIT pageSize
```

The exact LINQ predicate expands the tuple comparison into lexicographic
conditions because provider support varies.

### `Include` and `AsSplitQuery`

The page needs positions to build domain aggregates and exposure metrics.
`Include` requests them. `AsSplitQuery` asks EF to issue a portfolio query and a
related positions query instead of one join that repeats portfolio columns for
every position.

This trades one large joined result for additional round trips. It is not
universally faster. Measure with realistic row sizes and latency. The split
queries also do not automatically provide a perfectly isolated snapshot under
all concurrency; use an appropriate transaction/isolation level if that is a
business requirement.

## 14. Grouping and projection statistics

The statistics endpoint demonstrates server-side aggregation without loading
entities. The repository executes two small grouped projections:

```csharp
dbContext.Portfolios
    .GroupBy(p => p.BaseCurrency)
    .Select(g => new CurrencyCount(g.Key, g.Count()));
```

and:

```csharp
dbContext.Positions
    .GroupBy(p => p.Portfolio.BaseCurrency)
    .Select(g => new CurrencyCount(g.Key, g.Count()));
```

These translate conceptually to:

```sql
SELECT BaseCurrency, COUNT(*)
FROM Portfolios
GROUP BY BaseCurrency;
```

and a `Positions`/`Portfolios` join grouped by portfolio currency. Only compact
scalar rows cross the process boundary. The repository merges the two result
sets in memory because they are already small grouped summaries.

An earlier nested `GroupBy`/navigation projection looked valid in C# but the
SQLite provider could not translate it. EF correctly threw instead of silently
loading every row. The real-provider integration test exposed the problem, and
the query was rewritten as two clearly translatable aggregates. This is an
important lesson: successful compilation proves type correctness, not SQL
translatability.

## 15. Application and HTTP query contracts

`PortfolioSearchRequest` is an API model bound from query-string values through
`[FromQuery]`. Data Annotation attributes provide early HTTP validation and
generate metadata:

```csharp
[Range(1, 100)]
public int PageSize { get; init; } = 20;
```

The controller explicitly maps it into `SearchPortfoliosQuery`. The handler
then applies transport-neutral validation, trims names, and canonicalizes
currency/instrument strings through Domain value-object factories.

The read port accepts `PortfolioSearchCriteria`, not an MVC request and not an
EF expression. It returns materialized domain portfolios plus a total. The
handler calculates exposure through Domain properties and maps public DTOs.

This separation may look verbose compared with a Spring Data derived method
such as `findByBaseCurrencyAndPositionsInstrumentId`. It buys explicit public
contracts, provider isolation, centralized input normalization, and freedom to
change storage without exposing ORM types.

Endpoints:

```text
GET /api/v1/portfolios
GET /api/v1/portfolios?name=book&baseCurrency=USD
GET /api/v1/portfolios?instrumentId=AAPL&minimumPositionCount=2&page=1&pageSize=20
GET /api/v1/portfolios/statistics/by-currency
```

## 16. SQLite decimals and finance caveats

The domain uses `decimal`, which is normally preferable to binary floating
point for quantities and prices. The configuration documents precision
`(38,18)`, but SQLite does not enforce a fixed decimal type like PostgreSQL
`numeric(38,18)` or SQL Server `decimal(38,18)`. In the generated SQLite schema,
these values use the provider's `TEXT` mapping to preserve .NET decimal values.

Consequences:

- equality can be supported through conversion;
- some decimal comparisons, ordering, or database aggregates have provider
  limitations;
- a precision declaration is not enforced by SQLite as it would be by a richer
  relational type system;
- provider-sensitive finance queries require tests against the production
  database provider.

This project computes net market value, gross exposure, P&L, VaR, expected
shortfall, and volatility in Domain after loading a bounded aggregate. It does
not ask SQLite to perform decimal portfolio arithmetic.

SQLite is excellent for learning, desktop/embedded tools, and many single-node
workloads. It is not a drop-in substitute for a highly concurrent trading-risk
service: writes serialize at the database-file level, operational replication
is different, and distributed application instances must share a server-side
database.

## 17. Health checks

Registration:

```csharp
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<RiskDbContext>("sqlite");
```

Mapping:

```csharp
app.MapHealthChecks("/health");
```

The check verifies that EF can connect, so a missing/unusable database causes
an unhealthy result instead of a misleading process-only success. It does not
prove every query, migration, or downstream dependency works. Mature services
often separate liveness (should the process be restarted?) from readiness
(should traffic be routed here?).

Spring Boot Actuator health indicators fill a similar role.

## 18. Testing the database correctly

`SqlitePortfolioRepositoryTests` creates a unique temporary SQLite file and
applies the real migration. One test writes with one context and reads with a
new context, proving data is on disk rather than only surviving in a change
tracker. Another exercises filters, paging, navigation predicates, and grouped
statistics.

`SqliteWebApplicationFactory` replaces the normal context registration with a
unique test database for each API factory. The complete HTTP test still runs:

```text
JSON -> model binding -> controller -> handler -> repository
     -> EF translation -> SQLite -> response JSON
```

It removes all relevant EF registrations before adding the test context. Merely
adding a second `DbContext` registration can leave design/configuration services
pointing at the old provider and can leak data between test hosts.

Why not EF's non-relational InMemory provider?

- it does not execute SQLite SQL;
- it cannot reveal translation failures;
- relational constraints and transactions differ;
- provider type mappings differ;
- navigation/query behavior can differ.

The existing `InMemoryPortfolioRepository` remains useful as a focused fake in
Application handler tests. Use the cheapest test double that can catch the
class of defect under test; use the real provider for persistence behavior.

## 19. Build, migrate, run, and inspect

From repository root:

```bash
dotnet --info
dotnet tool restore
dotnet restore RiskEngine.slnx
dotnet build RiskEngine.slnx -c Release --no-restore
dotnet test RiskEngine.slnx -c Release --no-build
dotnet run --project src/TradingRisk.Api
```

The app applies the checked-in migration at startup. Create a portfolio in the
UI or API, stop the process, and restart it: the portfolio remains in the
SQLite file.

Useful EF commands:

```bash
dotnet tool run dotnet-ef migrations list \
  --project src/TradingRisk.Infrastructure \
  --startup-project src/TradingRisk.Api

dotnet tool run dotnet-ef database update \
  --project src/TradingRisk.Infrastructure \
  --startup-project src/TradingRisk.Api

dotnet tool run dotnet-ef migrations script 0 \
  --project src/TradingRisk.Infrastructure \
  --startup-project src/TradingRisk.Api
```

If the `sqlite3` shell is installed:

```bash
sqlite3 src/TradingRisk.Api/App_Data/riskengine.db '.tables'
sqlite3 src/TradingRisk.Api/App_Data/riskengine.db '.schema'
sqlite3 src/TradingRisk.Api/App_Data/riskengine.db \
  'SELECT Id, Name, BaseCurrency FROM Portfolios;'
```

Development configuration logs EF database commands at `Information`, which is
useful for learning translation. Avoid logging sensitive parameter values in
production.

The Docker image declares `/app/App_Data` as a volume and creates it with the
non-root application's ownership. Without a mounted persistent volume, a
container replacement loses its writable-layer database:

```bash
docker run --rm -p 8080:8080 \
  -v riskengine-data:/app/App_Data \
  riskengine:local
```

## 20. Moving from SQLite to PostgreSQL

The intended production-learning evolution is:

1. add `Npgsql.EntityFrameworkCore.PostgreSQL`;
2. change provider registration from `UseSqlite` to `UseNpgsql`;
3. create provider-appropriate migrations or a deliberate multi-provider
   migration strategy;
4. configure the connection through secrets/environment, never source control;
5. run repository and migration tests against a temporary PostgreSQL instance;
6. review decimal, date, collation, concurrency, and index behavior;
7. move schema application to the deployment pipeline;
8. add resilience only where retry semantics are safe and idempotent;
9. introduce transaction boundaries for multi-aggregate use cases;
10. profile generated SQL and query plans with production-like data volume.

Application and Domain should require little or no change because their ports
do not mention SQLite, EF, `DbSet`, or `IQueryable`. Infrastructure and
deployment own the provider-specific change.

## 21. Exercises that force understanding

1. Add a maximum gross-exposure filter. Decide whether it can translate using
   SQLite decimals; write a provider test before choosing database-side or
   bounded in-memory calculation.
2. Add sort keys through an enum/closed mapping. Never accept a raw column name
   and splice it into SQL.
3. Replace offset pagination with keyset pagination over `(Name, Id)` and test
   equal-name portfolios.
4. Add an optimistic concurrency token and demonstrate two conflicting edits.
   Compare EF concurrency exceptions with JPA optimistic locking.
5. Add a `RiskRunEntity` storing calculation inputs, model/version metadata,
   metrics, and timestamp in one explicit transaction.
6. Generate an idempotent migration script and explain who runs it in CI/CD.
7. Use `ToQueryString()` in a diagnostic test to study SQL, but keep assertions
   about behavior rather than brittle whitespace or alias names.
8. Run the same repository contract tests against PostgreSQL and list every
   provider difference you discover.

## 22. Review questions

- Why is `DbContext` scoped instead of singleton?
- Which line actually sends the search query to SQLite?
- Why does `Where` compile even when a provider cannot translate it?
- Why are `Any` and navigation `Count` useful LINQ demonstrations?
- Why must offset pagination have a unique ordering?
- What does `AsNoTracking` change, and what does it not change?
- Why do we map persistence entities back through Domain factories?
- Why is the unique instrument index valuable when Domain already validates?
- Why is SQLite-provider testing more useful than EF InMemory for repository
  queries?
- Why should a multi-replica production service not casually migrate at app
  startup?
- Which layer must change to adopt PostgreSQL, and which layers should not?

If you can answer those from the code rather than by memorizing this chapter,
you understand the persistence slice well enough to extend it safely.

## 23. Primary references

- [EF Core DbContext configuration and lifetime](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)
- [Microsoft EF Core SQLite provider](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/)
- [SQLite provider limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)
- [Tracking versus no-tracking queries](https://learn.microsoft.com/en-us/ef/core/querying/tracking)
- [Pagination guidance](https://learn.microsoft.com/en-us/ef/core/querying/pagination)
- [EF Core migrations overview](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [Applying migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- [SQLitePCLRaw patched package line](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3)
