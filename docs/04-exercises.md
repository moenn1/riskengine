# Hands-on exercises

Work in order. Each exercise has a finance outcome and a .NET outcome.
Use the [production-scale .NET deep dive](10-production-scale-dotnet-deep-dive.md)
for the architecture and operational trade-offs behind exercises 2–10.

## 1. Add portfolio mutation safely

Implement `POST /api/v1/portfolios/{id}/positions`.

Requirements:

- reject a duplicate instrument or define an explicit netting rule;
- do not expose the internal mutable collection;
- add an optimistic version number to the portfolio;
- return `409 Conflict` on a stale expected version;
- unit-test long, short, duplicate, and concurrent-change cases.

Think about: should `Portfolio` stay immutable and return a new version, or
become a carefully encapsulated mutable aggregate?

## 2. Replace process memory with PostgreSQL and EF Core

Implement a second `IPortfolioRepository` adapter.

Requirements:

- migrations, unique constraints, indexes, and decimal column precision;
- `DbContext` scoped lifetime;
- cancellation on every database call;
- a transaction around aggregate writes;
- integration tests against a real temporary PostgreSQL instance;
- no EF Core types in Domain or Application.

Measure query counts and inspect generated SQL. Demonstrate the difference
between `IEnumerable<T>` and `IQueryable<T>`.

## 3. Ingest price history

Implement CSV ingestion and calculate simple returns rather than accepting
returns in the risk request.

Requirements:

- stream rows with `IAsyncEnumerable<T>`;
- reject duplicate instrument/date observations;
- store source, received-at, effective-at, and quality status;
- handle adjusted versus unadjusted closes explicitly;
- align calendars and fail or apply a documented missing-data policy.

Test a stock split and show why raw close-to-close returns would be wrong.

## 4. Turn calculations into jobs

Change risk calculation to return `202 Accepted` and a job URL.

Requirements:

- bounded in-memory `Channel<T>` first;
- `BackgroundService` consumer;
- idempotency key and deterministic request hash;
- states: queued, running, succeeded, failed, cancelled;
- graceful shutdown and cancellation;
- later replace the channel with a broker without changing the use case.

Write a test that deliberately redelivers a message and proves one logical
result is published.

## 5. Add parametric VaR

Implement a second `IRiskCalculator` strategy using:

\[
VaR_c = z_c \sigma_P
\]

where portfolio variance comes from exposure weights and a covariance matrix.

Requirements:

- validate matrix dimensions, symmetry, and positive-semidefinite behavior;
- make methodology an explicit request/report field;
- compare parametric and historical results on normal and fat-tailed data;
- property test that multiplying all exposures by \(k>0\) scales VaR by \(k\).

Do not hard-code a z-score table without documenting its source and interpolation
policy.

## 6. Add backtesting

Store daily VaR forecasts and subsequent hypothetical P&L.

Requirements:

- identify exceptions where loss exceeds prior VaR;
- prevent look-ahead bias;
- retain the exact model/input version used by each forecast;
- build a time-series endpoint with pagination;
- add green/amber/red classification only after researching the exact framework
  your team uses.

## 7. Price a European option

Add a European option instrument and Black–Scholes pricing as a learning model.

Requirements:

- separate contract terms from market data;
- compute price and delta/gamma/vega;
- compare delta-only, delta-gamma, and full revaluation under scenarios;
- define day-count and time-zone rules;
- golden-test against an independently verified dataset.

This is where the current linear engine's limitation becomes visible.

## 8. Add resilience correctly

Create a typed `HttpClient` market-data adapter.

Requirements:

- explicit connect/request timeout budgets;
- retries only for transient and idempotent operations;
- jittered backoff, circuit breaker, and concurrency limit;
- propagate cancellation;
- log endpoint and duration, never secrets or full market-data payloads;
- simulate latency, `429`, `500`, bad JSON, and partial data.

Explain why retrying a non-idempotent order submission would be dangerous.

## 9. Add authentication and authorization

Use a local OIDC provider or your team's identity platform.

Requirements:

- read-only and risk-calculation policies;
- desk-level portfolio authorization;
- test `401` versus `403`;
- audit actor, action, resource, result, and correlation ID;
- never trust a desk ID supplied only in a request body.

## 10. Observe and load-test it

Add OpenTelemetry and a load-test scenario.

Requirements:

- trace HTTP → handler → database → worker;
- counters for jobs and failures;
- histograms for end-to-end latency and scenario throughput;
- avoid portfolio IDs as metric labels;
- define an SLO before an alert;
- use a profiler and allocation data before applying `Span<T>` or pooling.

Record a short architecture decision for every optimization: evidence,
alternative, measured result, and readability cost.

## Review checklist for every exercise

- Is the business rule in Domain/Application rather than the controller?
- Are units, signs, time horizon, currency, and as-of time explicit?
- Is cancellation propagated?
- Is external input validated at the boundary and again where a domain invariant
  matters?
- Does async code avoid blocking?
- Are lifetimes correct and thread safety understood?
- Are failures observable without leaking sensitive data?
- Are unit, integration, and contract tests at the appropriate boundaries?
- Can the calculation be reproduced?
- Is the simplest deployable design still sufficient?
