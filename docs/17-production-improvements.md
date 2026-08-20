# Production-oriented improvements

This change set adds a few small improvements that are useful in a real service
without pretending that the learning adapters are production infrastructure.

## Changes in this commit

- `.gitattributes` standardizes line endings and gives Git useful language-aware
  diffs.
- The CI workflow verifies formatting and collects cross-platform coverage data.
- API rate limiting is partitioned by authenticated user, or by client IP for an
  anonymous request. One noisy client no longer consumes the global quota for all
  callers.
- The empty, machine-local `.vscode/launch.json` was removed from the repository
  workspace.

The rate limiter is still process-local. In a multi-replica deployment, each
replica would have its own counter. Replace it with a distributed limiter backed
by a shared store when horizontal scaling becomes a requirement.

## Recommended next increments

1. Add a PostgreSQL adapter and run migrations as a deployment step.
2. Add a durable broker adapter with an outbox, idempotency keys, retries, and a
   dead-letter queue.
3. Add OpenTelemetry traces and metrics for HTTP requests, database calls, and
   risk-job queue latency.
4. Add golden-master and property-based tests for risk calculations, then add
   load tests for concurrent portfolio calculations.
5. Move demo credentials to ASP.NET Core Identity or an external OIDC provider
   before exposing the application beyond a local learning environment.
