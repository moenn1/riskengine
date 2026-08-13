# Deep dive: evolving the learning slice into a large-scale .NET system

The current application is deliberately small and synchronous at its outer
boundary. It now has durable local SQLite portfolio storage. This chapter
explains what changes when that learning foundation becomes a shared,
asynchronous, observable, secure, and horizontally scaled platform.

Some features are now implemented as deliberately local learning versions:
JWT policies, a bounded in-process risk-job channel, a background worker, and
job polling. Durable messaging, distributed job state, external identity, and
production observability remain exercises. This chapter distinguishes the two.

## 1. First understand the current operating model

One API process currently contains:

```text
Kestrel
  -> ASP.NET Core middleware/controllers
  -> scoped handlers
  -> scoped EF Core repository + RiskDbContext
  -> embedded SQLite file
  -> singleton stateless risk calculator
```

Properties:

- portfolio data survives a process restart in the local SQLite file;
- each process/container still has unrelated state unless it shares the same
  deliberately mounted file, which is not a horizontal-scaling architecture;
- risk calculation uses request CPU and must finish before the response;
- Development/Testing JWT authentication and reader/operator authorization exist;
- health verifies SQLite connectivity but not every query or dependency;
- logs are local console output unless the host collects them;
- no calculation input/result is durably reproducible.

That is appropriate for learning one vertical slice and relational APIs. Each
next step should replace a boundary without moving business rules outward.

## 2. Persistence with EF Core: the JPA/Hibernate comparison

Entity Framework Core is the common .NET ORM. It is not a line-for-line port of
JPA/Hibernate.

| JPA/Hibernate | EF Core |
|---|---|
| `EntityManager` / Hibernate `Session` | `DbContext` |
| persistence context | change tracker |
| `@Entity` mappings | conventions, Data Annotations, or `IEntityTypeConfiguration<T>` |
| JPQL/Criteria | LINQ translated from expression trees |
| Flyway/Liquibase or schema generation | EF Core migrations or separate migration tool |
| optimistic `@Version` | concurrency token / row version |
| eager/lazy fetch and entity graphs | `Include`, projection, explicit/lazy loading |
| repository abstraction often from Spring Data | `DbSet<T>` plus application-owned repository/query adapters |

### `DbContext` is a unit of work

A context tracks loaded/attached entities and writes changes through
`SaveChangesAsync`.

Normal web registration:

```csharp
builder.Services.AddDbContext<RiskDbContext>(options =>
    options.UseNpgsql(connectionString));
```

`AddDbContext` normally registers the context as scoped. One HTTP request can
therefore share one unit of work.

Important rules:

- `DbContext` is not thread-safe;
- do not register it as singleton;
- do not run concurrent queries on one context;
- await an operation before using that context again;
- dispose it with the scope;
- do not keep tracked entities indefinitely.

A background worker has no automatic HTTP request scope. Create a scope for one
job:

```csharp
await using var scope = serviceScopeFactory.CreateAsyncScope();
var handler = scope.ServiceProvider
    .GetRequiredService<CalculateRiskJobHandler>();
await handler.HandleAsync(job, stoppingToken);
```

### Domain model versus persistence model

Two valid broad strategies are:

1. map Domain objects directly with EF Core configuration;
2. use persistence records/entities and map them to Domain.

Direct mapping removes duplication but may force persistence construction and
change tracking concerns onto carefully encapsulated domain objects.
Separate persistence models protect Domain but add mapping and can drift.

Choose deliberately. This project uses separate persistence entities and an
explicit mapper; compare that implementation with direct rich-domain mapping
before choosing a team convention. The complete implemented slice is explained
in [the EF/SQLite/LINQ deep dive](12-ef-core-sqlite-linq-deep-dive.md).

### Fluent entity configuration

Prefer explicit configuration for database-specific details:

```csharp
public sealed class PositionConfiguration
    : IEntityTypeConfiguration<PositionRow>
{
    public void Configure(EntityTypeBuilder<PositionRow> builder)
    {
        builder.HasKey(position => position.Id);
        builder.Property(position => position.Price)
            .HasPrecision(20, 8);
        builder.HasIndex(position => new
            {
                position.PortfolioId,
                position.InstrumentId
            })
            .IsUnique();
    }
}
```

This keeps column precision, indexes, keys, and relational constraints out of
the API request model.

### Decimal precision is a schema decision

C# `decimal` does not tell the database which precision and scale to use.
Define:

```text
decimal(precision, scale)
```

for price, quantity, return, and reported currency fields based on governed
ranges. Test values at both positive and negative limits. Silent rounding or
overflow in risk data is not a cosmetic issue.

### Migrations

A migration is a versioned schema change generated from model differences and
then reviewed:

```bash
dotnet ef migrations add InitialRiskSchema \
  --project src/TradingRisk.Infrastructure \
  --startup-project src/TradingRisk.Api

dotnet ef database update \
  --project src/TradingRisk.Infrastructure \
  --startup-project src/TradingRisk.Api
```

Do not assume the application should mutate production schema at startup.
Large teams often run migrations as a separate controlled deployment step with:

- backup/rollback or roll-forward strategy;
- lock and duration analysis;
- compatibility with old and new application versions;
- review of indexes and table rewrites;
- least-privilege credentials.

Use expand/contract changes for rolling deployments:

1. add backward-compatible schema;
2. deploy code that understands both forms;
3. backfill;
4. switch reads/writes;
5. remove obsolete schema after old versions are gone.

## 3. LINQ to objects versus LINQ to a database

These expressions look similar:

```csharp
IEnumerable<Portfolio> inMemory = ...;
IQueryable<PortfolioRow> database = ...;
```

For `IEnumerable<T>`, LINQ operators execute .NET delegates in the process.
For `IQueryable<T>`, operators build expression trees that the provider tries
to translate to SQL.

Consequences:

- not every C# method translates;
- database null/collation/time semantics may differ;
- calling `ToListAsync` executes the query;
- calling `AsEnumerable` switches later work to in-memory execution;
- accidental early materialization can load far too much data;
- enumerating can trigger a round trip.

Prefer projection:

```csharp
var summaries = await dbContext.Portfolios
    .AsNoTracking()
    .Where(portfolio => portfolio.DeskId == deskId)
    .Select(portfolio => new PortfolioSummaryDto(
        portfolio.Id,
        portfolio.Name,
        portfolio.Positions.Sum(position => position.Quantity * position.Price)))
    .ToListAsync(cancellationToken);
```

This asks SQL for only required columns and avoids tracking read-only results.

### N+1 query problem

If code loads 100 portfolios, then lazily loads positions for each, it can issue
101 database queries. Detect this with SQL logging/telemetry and integration
tests. Fix with a projection, deliberate include, or specialized query—not by
assuming all eager loading is always safe.

### Indexes and query shape

ORM code does not remove database engineering. Examine:

- generated SQL;
- execution plans;
- scanned rows;
- indexes and their write cost;
- sort/pagination strategy;
- lock duration;
- round trips;
- result size.

Offset pagination becomes expensive and unstable for deep pages. Keyset/cursor
pagination is often better when there is a stable ordered key.

## 4. Transactions and optimistic concurrency

### Transaction boundary

One application command should make its required state changes atomically when
possible:

```text
load portfolio
validate expected version
apply update
save portfolio
write outbox event
commit
```

Do not hold a database transaction open while calling a slow external HTTP
service. That couples database locks to network availability.

### Optimistic concurrency

A version column detects lost updates:

```text
UPDATE portfolios
SET ..., version = 8
WHERE id = @id AND version = 7
```

If zero rows are updated, somebody else changed the row. Decide whether to:

- return conflict and ask the caller to retry with fresh state;
- automatically retry a commutative/idempotent change;
- merge under explicit domain rules.

Blindly retrying can overwrite a trader's decision. Concurrency resolution is a
business rule, not only an ORM setting.

### Isolation and calculation snapshots

A risk calculation may read positions and market data from many rows. Ask:

- Must every row represent the same as-of snapshot?
- Can positions change during the read?
- Is snapshot isolation enabled?
- Is an immutable input snapshot stored before calculation?
- How is market-data version joined to portfolio version?

Reproducibility often favors explicitly materializing/versioning the input
snapshot rather than relying on one long database transaction.

## 5. Async, concurrency, and parallelism

These are distinct:

- **asynchrony**: avoid blocking a thread while waiting;
- **concurrency**: multiple operations make progress in overlapping time;
- **parallelism**: work runs simultaneously on multiple cores.

Database and HTTP operations should normally be asynchronous:

```csharp
await dbContext.Portfolios
    .SingleOrDefaultAsync(..., cancellationToken);
```

Historical risk arithmetic is CPU-bound. `async` does not accelerate it.

### Do not wrap server CPU work in `Task.Run` by default

```csharp
await Task.Run(() => calculator.Calculate(...));
```

This moves work to another thread-pool thread but the server still consumes CPU
and the request still waits. Under load it can worsen scheduling and memory.

Better choices:

- keep small CPU work synchronous in the request;
- optimize and bound it based on measurements;
- queue large durable work to worker processes;
- partition calculations intentionally when parallelism helps.

### Parallel scenario calculation

Before using `Parallel.ForEach` or PLINQ:

- benchmark realistic portfolios/scenario counts;
- ensure every input/output object is thread-safe;
- use deterministic aggregation;
- limit degree of parallelism;
- account for multiple concurrent requests;
- avoid oversubscribing container CPU limits;
- propagate cancellation.

A calculation that uses all cores per request can collapse throughput when many
requests arrive.

## 6. Turning risk calculations into jobs

Large calculations should usually become asynchronous resources:

```http
POST /api/v1/risk-jobs
Idempotency-Key: ...

HTTP/1.1 202 Accepted
Location: /api/v1/risk-jobs/{jobId}
```

States:

```text
Queued -> Running -> Succeeded
                  -> Failed
                  -> Cancelled
```

Persist the job and input snapshot before acknowledging the request. The client
polls or receives a notification.

### Local learning version: bounded `Channel<T>`

`Channel<T>` supports an in-process producer/consumer queue. Make it bounded:

```csharp
Channel.CreateBounded<RiskJob>(new BoundedChannelOptions(capacity)
{
    FullMode = BoundedChannelFullMode.Wait
});
```

A bound provides backpressure. An unbounded queue can accept work faster than
the worker completes it until memory is exhausted.

A `BackgroundService` reads until shutdown using its `stoppingToken`.

The repository's `IRiskJobBroker`/`InMemoryRiskJobBroker` demonstrates this local
version at `POST /api/v1/risk-jobs` and `GET /api/v1/risk-jobs/{jobId}`.

Limitations of an in-process channel:

- queued work is lost on process failure/restart;
- another replica cannot see the queue;
- deployments interrupt work;
- it is not a durable broker.

Use it to learn the worker abstraction, then put a durable adapter behind a
port.

## 7. Message delivery, idempotency, outbox, and inbox

Most brokers provide at-least-once delivery in practical failure scenarios.
The same message can be processed more than once.

### Idempotency

The same logical request should produce one logical job/result:

```text
unique(tenant, idempotency_key)
request_hash
job_id
stored response/status
```

If the key is reused:

- same request hash: return the existing outcome;
- different request hash: reject as conflict.

Do not keep idempotency only in process memory.

### Transactional outbox

The dual-write problem:

```text
database commit succeeds
message publish fails
```

or:

```text
message publish succeeds
database commit fails
```

Write the state change and an outbox record in one database transaction. A
publisher later sends pending outbox records and marks them dispatched.
Publishing may still repeat, so consumers remain idempotent.

### Consumer inbox

A consumer records a message ID/result in its own durable store. If redelivered,
it detects prior completion. The inbox update and business change should share
the appropriate local transaction.

“Exactly once” is usually achieved as exactly-once **business effect** through
idempotency, not through believing packets cannot repeat.

### Message contract evolution

Events outlive deployments. Prefer:

- explicit schema and version rules;
- additive backward-compatible fields;
- consumers tolerant of unknown fields;
- immutable event meaning;
- contract tests;
- replay strategy;
- no internal CLR type names as the wire contract.

## 8. External HTTP with `IHttpClientFactory`

A future market-data adapter can use a typed client:

```csharp
builder.Services
    .AddHttpClient<IMarketDataClient, MarketDataClient>(client =>
    {
        client.BaseAddress = marketDataUri;
        client.Timeout = TimeSpan.FromSeconds(5);
    });
```

`IHttpClientFactory` centralizes client configuration and manages underlying
handler lifetimes. Avoid creating/disposing a new raw `HttpClient` per call;
connection pooling and DNS lifetime need deliberate management.

### Timeout budgets

Define:

- connect timeout;
- per-attempt timeout;
- total request budget;
- retry delay budget;
- caller cancellation.

The outer operation needs a total deadline. Three five-second attempts plus
backoff cannot fit a six-second request budget.

### Retries

Retry only failures likely to be transient and operations safe to repeat:

- timeouts/connectivity;
- selected 5xx;
- 429 with server guidance.

Do not automatically retry:

- validation/auth failures;
- deterministic bad JSON;
- every exception;
- non-idempotent operations without a key/deduplication strategy.

Use jittered backoff to avoid synchronized retry storms.

### Circuit breaking and concurrency limits

A circuit breaker stops repeatedly calling an unhealthy dependency for a
period. A concurrency limiter prevents one dependency from consuming all local
resources.

Neither replaces a fallback/data-quality policy. Stale market data must be
clearly labeled and governed; silently using it can create a plausible but
wrong risk report.

## 9. Caching with finance semantics

Before caching, specify:

- exact key, including portfolio/market/model versions;
- owner/source of truth;
- maximum age;
- invalidation event;
- behavior when stale;
- per-tenant authorization boundary;
- memory and eviction limit.

Unsafe key:

```text
risk:portfolio-123
```

Safer conceptual key:

```text
risk:{tenant}:{portfolioVersion}:{marketSnapshot}:{methodologyVersion}:{confidence}
```

Never return another tenant's cached portfolio. Do not cache a mutable domain
object and then modify it in place. Do not call data “fresh” merely because a
TTL has not expired; finance freshness depends on market and business time.

Use:

- in-process cache for replica-local optimization;
- distributed cache for shared ephemeral results;
- database/object storage for durable governed calculation artifacts.

A cache is not the system of record.

## 10. Authentication and authorization

They answer different questions:

- authentication: who/what is the caller?
- authorization: may this identity perform this action on this resource?

For an API, a common production arrangement validates access tokens issued by
an approved OAuth 2.0/OIDC identity provider.

Validate at least:

- signature;
- trusted issuer;
- intended audience;
- expiration and applicable time claims;
- token type/usage according to provider guidance.

Do not create a home-grown token format.

### 401 versus 403

- 401: acceptable authentication is missing/invalid; a challenge is issued.
- 403: identity is authenticated but not permitted.

### Policy-based authorization

Define business policies:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CalculateRisk", policy =>
        policy.RequireClaim("permission", "risk.calculate"));
```

Route-level `[Authorize(Policy = "CalculateRisk")]` is useful, but sensitive
use cases should also enforce resource authorization:

```text
Can this caller calculate this portfolio for this desk/tenant?
```

Never trust `deskId` from the body as proof of access. Resolve permitted
resources from verified identity claims and authoritative data.

### Multi-tenancy

Tenant isolation must reach:

- authorization;
- every database query and write;
- cache/message keys;
- background jobs;
- logs and audit;
- exports and backups.

Global query filters can reduce accidental omissions but should be backed by
tests and, when required, database-level controls.

## 11. Security boundary checklist

Production work should include:

- TLS and proxy trust;
- authentication and resource authorization;
- least-privilege application/database identities;
- secret rotation;
- request/body/collection limits;
- safe JSON configuration;
- dependency and container scanning;
- audit records;
- security headers/CORS policy where relevant;
- output/data classification;
- abuse and denial-of-service analysis.

CORS is a browser cross-origin policy, not authentication. CSRF risk differs
between bearer headers and automatically attached cookies; choose defenses for
the actual credential transport.

Never put full portfolios or credentials in exception responses, logs, traces,
metrics, or OpenAPI examples.

## 12. Observability: logs, metrics, and traces

### Logs

Logs describe discrete events:

```text
Risk calculation completed
portfolioId=...
jobId=...
scenarioCount=...
methodology=historical-v1
durationMs=...
```

Use stable templates/event IDs and centralized collection.

### Metrics

Metrics aggregate numeric behavior:

- request/risk-job counts;
- success/failure counts;
- queue depth;
- calculation duration histogram;
- scenarios processed;
- data-staleness age;
- worker saturation;
- process CPU, memory, GC, thread pool.

Do not use portfolio ID, job ID, instrument ID, or raw exception message as a
metric label. Unbounded label values create high-cardinality cost and can make
the metrics system unusable.

### Distributed traces

A trace connects:

```text
incoming HTTP
  -> database read
  -> message publish
  -> worker consume
  -> market-data call
  -> result write
```

.NET instrumentation uses `Activity`/`ActivitySource`; metrics use
`Meter`; OpenTelemetry can collect and export those signals.

Propagate W3C trace context through HTTP and message headers. A business job ID
is still useful because traces are sampled/retained for limited periods.

### Observability is not auditing

An audit record answers who changed/calculated what, under which authority and
version, with controlled retention and tamper/access requirements. Telemetry is
optimized for system diagnosis and aggregation. Keep the concerns connected
but distinct.

## 13. Service-level objectives

Define outcomes before alerts:

```text
99.9% of accepted interactive calculations complete successfully
within the documented latency threshold over 30 days
```

For queued jobs, useful objectives may include:

- time from accepted to started;
- time from started to completed by portfolio size;
- completion rate;
- data freshness;
- reproducibility rate.

Alert on symptoms that threaten an objective, not every transient exception.
Include runbooks and ownership.

## 14. Performance engineering in .NET

### Measure in layers

1. Correctness and realistic workload.
2. End-to-end latency/throughput and resource use.
3. Trace to the slow component.
4. Profile CPU/allocations/database.
5. Benchmark a candidate optimization.
6. load test again.

Do not begin with `Span<T>`, pooling, unsafe code, or hand-written loops because
they look advanced.

### Managed memory

The .NET garbage collector reclaims unreachable managed objects. You still
control:

- allocation rate;
- object lifetime;
- retained references;
- large arrays/objects;
- unmanaged/disposable resources.

The current calculator creates:

- a scenario result array;
- an ordered losses array;
- a `double` array for volatility.

That is clear and appropriate for small inputs. At large scale, profile whether
these allocations dominate before changing the design.

Possible measured optimizations:

- avoid repeated enumeration;
- rent large temporary arrays through `ArrayPool<T>`;
- stream/batch scenario inputs;
- use spans for hot local transforms;
- vectorize numerical kernels;
- move stable data to compact representations.

Each adds ownership and correctness complexity. Pooled arrays can leak
sensitive data unless cleared/handled correctly.

### BenchmarkDotNet versus load tests

BenchmarkDotNet measures isolated code with warmup/statistical controls. It is
good for comparing risk kernels.

A load test measures the running service under concurrency, including:

- serialization;
- middleware;
- database;
- thread pool;
- GC;
- connection pools;
- CPU/container limits.

Use both for different questions.

### JIT, ReadyToRun, and Native AOT

Default framework-dependent apps JIT methods at runtime. ReadyToRun can move
some compilation to publish time at size/crossgen trade-offs. Native AOT has
stricter reflection/dynamic-code compatibility and different deployment
properties.

Do not choose AOT merely because it is newer. Measure startup, memory,
throughput, package size, and library compatibility for the deployment model.

## 15. Horizontal scaling

When two API replicas run:

```text
client -> load balancer -> API A -> SQLite file A
                     \--> API B -> SQLite file B
```

A portfolio created on A is absent on B. Sticky sessions mask rather than solve
the problem. Move durable shared state to an appropriate database/service.

Keep request handlers stateless where practical. Shared local memory must be:

- disposable cache;
- safe under concurrency;
- correct when replicas disagree;
- bounded.

Separate scaling dimensions:

- API request traffic;
- CPU-heavy risk workers;
- market-data ingestion;
- database throughput;
- reporting/export.

Different processes can scale independently after the boundaries are real.

## 16. Graceful shutdown and deployments

On shutdown:

- stop accepting new work;
- mark instance unready;
- let in-flight requests finish within a budget;
- stop queue consumption;
- finish or checkpoint jobs;
- release leases;
- flush telemetry within limits;
- dispose scopes/resources.

Long risk jobs must not depend on a process living forever. Persist state and
design retry/resume semantics.

For rolling deployment, old and new versions overlap. Database schemas,
messages, and API clients must be compatible during that window.

Container concerns:

- run as non-root, as this Dockerfile does;
- use read-only filesystem where possible;
- set CPU/memory requests and limits;
- distinguish readiness/liveness;
- deliver secrets safely;
- pin/scan base images according to team policy;
- preserve logs outside the container;
- handle termination grace periods.

## 17. Reliability and failure-mode thinking

For each dependency ask:

| Failure | Desired behavior |
|---|---|
| database slow | timeout within budget; become unready if appropriate; no retry storm |
| market-data 429 | honor policy/`Retry-After`; bounded retry; mark data unavailable/stale |
| broker redelivery | idempotent consumer |
| worker dies mid-job | lease expires or job returns to queue |
| duplicate submission | return existing idempotent job/result |
| partial scenario batch | no partial “successful” report unless explicitly modeled |
| telemetry backend down | application continues with bounded buffering/drop policy |
| deployment during job | persisted state permits safe retry/resume |

Resilience is not “retry everything.” It is controlled behavior under known
failure modes with bounded resource use.

## 18. Reproducibility and model governance

A large risk system must make a result reconstructible:

```text
RiskRun
├── run/job ID
├── portfolio snapshot/version
├── market-data snapshot/version
├── valuation timestamp and time zone
├── methodology/model version
├── parameters: confidence, horizon, window, weighting
├── code/deployment version
├── data-quality decisions/overrides
├── output and currency/units
└── actor/authorization and timestamps
```

Avoid using “latest” as durable input. Resolve latest to immutable version IDs
before calculation.

Model changes need:

- independent review/validation;
- controlled approval;
- backtesting and benchmark data;
- limitation documentation;
- rollout/rollback plan;
- result comparability rules;
- versioned reports.

Software deploy version and model methodology version are related but not
identical.

## 19. Suggested implementation order

Do not add every enterprise technology simultaneously. A learning-friendly
sequence is:

1. **Move the implemented EF Core adapter from SQLite to PostgreSQL**
   - shared durable portfolios;
   - provider-specific migrations and integration tests;
   - optimistic concurrency and deployment-managed migrations.
2. **Market-data boundary**
   - typed `HttpClient`;
   - governed snapshot and data-quality model;
   - cancellation and resilience.
3. **Persistent risk runs**
   - exact input/output versions;
   - queryable status/history.
4. **Asynchronous jobs**
   - 202 resource model;
   - bounded Channel locally;
   - durable broker, idempotency, outbox/inbox.
5. **Security**
   - OIDC/OAuth token validation;
   - policy and resource authorization;
   - tenant/desk isolation and audit.
6. **Observability**
   - OpenTelemetry traces/metrics;
   - SLOs and load tests;
   - profile-driven optimization.
7. **Advanced finance**
   - new calculators behind explicit methodology;
   - validation, backtesting, and golden datasets.

At each step, keep the API adapter thin and protect Domain from technical
dependencies.

## 20. Production-readiness review

### Data and correctness

- Are inputs immutable/versioned and results reproducible?
- Are decimal precision, units, currency, sign, and time explicit?
- Are constraints enforced in both domain and database where appropriate?
- Are transactions and concurrent updates deliberate?
- Is data quality visible rather than silently repaired?

### Availability

- Are timeouts and retries bounded by one budget?
- Is queued work durable and idempotent?
- Are resource pools and queues bounded?
- Are liveness/readiness meaningful?
- Does graceful shutdown preserve work?

### Security

- Are issuer, audience, signature, and expiry validated?
- Is resource authorization enforced by desk/tenant?
- Are secrets and sensitive portfolio data excluded from telemetry?
- Are database and service identities least privilege?
- Are dependencies/images scanned and patched?

### Observability

- Can one job be traced across HTTP, queue, worker, database, and outbound calls?
- Are metrics low-cardinality and tied to SLOs?
- Do logs have stable events and correlation?
- Are audit records distinct and governed?

### Performance

- Are workload and limits measured?
- Are database query count/plans known?
- Are CPU, allocation, GC, thread-pool, and connection-pool constraints visible?
- Does calculation parallelism respect container and fleet capacity?

### Delivery

- Are schema/message/API changes compatible during rolling deployment?
- Is rollback or roll-forward practiced?
- Can an exact build/image be identified from a result?
- Are runbooks, owners, alerts, and incident evidence ready?

## Official references

- [EF Core `DbContext` lifetime](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)
- [EF Core efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
- [EF Core concurrency conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [`IHttpClientFactory`](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory)
- [.NET resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/)
- [.NET worker services](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers)
- [Queue service with `Channel<T>`](https://learn.microsoft.com/en-us/dotnet/core/extensions/queue-service)
- [ASP.NET Core authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/?view=aspnetcore-10.0)
- [Policy-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0)
- [.NET observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)
- [.NET garbage collection](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/)
