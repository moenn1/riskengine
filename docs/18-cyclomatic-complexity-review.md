# Cyclomatic complexity review

This review covers every non-generated C# file under `src/`. Files under
`obj/`, generated EF migrations, records/contracts, and interfaces were treated
as generated or declarative code rather than complexity hotspots.

## Method

The review used two passes:

1. A repository-wide structural scan for `if`, `switch`, loops, `catch`,
   conditional expressions, and boolean branch operators.
2. Manual method-level inspection of every file with multiple branches, with
   special attention to domain calculations, application handlers, persistence
   queries, authentication, and middleware.

Cyclomatic complexity is a signal, not a quality verdict. A branch that enforces
a domain invariant is often clearer than a generic rules engine. The practical
target used here is to keep individual business methods roughly below 10, and
to split methods when they combine unrelated responsibilities.

## Findings

| Area | Finding | Decision |
|---|---|---|
| `HistoricalSimulationRiskCalculator.Calculate` | Guards and statistical stages are linear and easy to follow. | Keep together because the method describes one calculation pipeline. |
| `CalculatePortfolioRiskHandler.HandleAsync` | Previously mixed validation, loading, mapping, and DTO construction. | Refactored into `ValidateQuery`, `MapScenarios`, and `MapReport`; the entry point now orchestrates one path. |
| `SqlitePortfolioRepository.SearchAsync` | Four optional filters are independent query predicates. | Keep the branches; extracting each predicate would obscure the LINQ-to-SQL shape. |
| `CredentialValidator` | Lockout, fake-hash fallback, and hash-format validation have separate guards. | Keep separate for security reviewability; each method is small. |
| `Program.cs` | Several environment/configuration branches appear in generated top-level `Main`. | Keep startup wiring explicit; moving it into a large abstraction would reduce, not improve, readability. |
| Domain value objects | Validation branches protect invariants at construction boundaries. | Keep close to the invariant and covered by focused tests. |

No remaining method was identified as a high-complexity refactoring target. The
next complexity review should run after adding durable messaging or market-data
ingestion, where retry/state-machine code can grow quickly.

## Guardrails for future work

- Keep controllers and handlers orchestration-only.
- Extract validation when it is reused or when it hides the request flow.
- Prefer named policy methods over nested boolean expressions.
- Keep provider-specific LINQ predicates together so the generated SQL remains
  visible to reviewers.
- Add tests before simplifying financial formulas; a shorter method is not an
  improvement if it changes the model.
- If a method approaches 10 independent decision points, split by responsibility
  and add focused tests for each branch.
