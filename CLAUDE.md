# CLAUDE.md — ta3lom store API

.NET backend for the store of ta3lom.sa (E-Learning Association, Riyadh): courses, books, seat
sponsorships and donation products. Every session reads this file first. Work is built session by
session from plan 14; payment processing is a separate service (plan 14 §0, doc 15).

## Source of truth — reading order

All docs live in `store/` in this repo.

1. `store/14-backend-implementation-plan.md` — **the plan**: §0 scope (no payments), §4 working method,
   quality gates and definition of done, §13 coverage maps, then the section of the current session.
2. `store/03-business-rules.md` — RUL-01…RUL-63. Rules are never invented and never "fixed" in code.
3. `store/09-backend-dotnet.md` — code shape, patterns, sketches (r2: Application → Domain only).
4. `store/11-api-contract.md` — endpoints (index §12), DTOs, error codes (§1.6).
5. `store/database/01-database-design.md` and the reference DDL (`db/reference/`, from S-02).
6. `store/15-payment-service-handoff.md` — the payment boundary: only the contracts named there.

**Precedence when docs disagree:** 03 > 11 > database/ > 09. Report every conflict; never choose silently.

## Platform

- **.NET 10 LTS** (`net10.0`), ASP.NET Core Minimal APIs, EF Core 10 + Npgsql (from S-02), PostgreSQL 18.
  The move to .NET 11 happens at GA as **its own PR** (TFM, `global.json`, package majors; doc 17).
- `LangVersion` is not pinned (TFM default). `global.json` rolls forward to the latest feature band.
- Current idioms: primary constructors, collection expressions, records, file-scoped namespaces,
  `TimeProvider` behind `IClock`, `Guid.CreateVersion7()`, `TypedResults`, built-in OpenAPI + Scalar,
  built-in rate limiter.
- Central package management (`Directory.Packages.props`), nullable, warnings as errors, analyzers on,
  `Ta3lom.Store.slnx`. Tests run on Microsoft.Testing.Platform (xUnit v3).

## Clean Architecture — enforced by `tests/Ta3lom.Store.ArchitectureTests`

| Project | References | Never |
|---|---|---|
| `Ta3lom.Store.Domain` | BCL only | EF Core, `Microsoft.Extensions.*`, any other project |
| `Ta3lom.Store.Application` | **Domain only** (+ FluentValidation, Logging/DI abstractions, Scrutor) | **Contracts**, EF Core, Npgsql, ASP.NET Core, adapter SDKs |
| `Ta3lom.Store.Infrastructure` | Application, Domain | Api, Contracts, ASP.NET Core |
| `Ta3lom.Store.Contracts` | BCL only; sealed records | Domain, Application |
| `Ta3lom.Store.Api` | all (composition root) | business logic |

- Handlers return Application result records (`Result<T>`, `PagedResult<T>`); the Api maps them to
  Contracts with static mappers in `Api/Mapping/` (no AutoMapper). Error codes live in
  `Application/Abstractions/ErrorCodes.cs`.
- Endpoints: bind → call one handler → `ToHttp()`. No logic, no DbContext. Test-only routes go through
  `IV1EndpointContributor` registered in tests; no probe route in production code.
- Aggregates protect invariants; private setters; state changes through methods named after the business
  action (`Order.MarkPaid`, `Cart.AddLine`). One transaction per use case; outbox rows in that transaction;
  no external call inside a transaction.

## Non-negotiables (09 §12, adapted)

1. **03 is the source of truth.** Read the RUL before touching its code. Code vs rule → stop and ask.
2. **Never float money.** `decimal`/`Money` only, two decimals, `MidpointRounding.AwayFromZero`; JSON money
   is a string (`"350.00"`). Guards: `BannedSymbols.Money.txt` (double/float members in Domain and
   Contracts) + the `No_money_as_floating_point` architecture test (money-named declarations).
3. **Payments are out of scope.** Only `IOrderSettlement` sets an order `paid` (S-16). No gateway,
   webhook, reconciliation, retry or refund code here (doc 15).
4. **Settlement, pooling and allocation are one transaction each**, with the row locks of database/01 §4.
   External calls (SMS, email, LMS, PDF) happen from the outbox, after commit.
5. **No business logic in endpoints.**
6. **Every implemented RUL has a test** tagged `[Rule("RUL-nn")]` (`Ta3lom.Store.Testing.RuleAttribute`).
   Append the session's rules to `tests/Ta3lom.Store.ArchitectureTests/ImplementedRules.txt`.
7. **No MediatR, no AutoMapper, no FluentAssertions.** Plain handler interfaces, Scrutor, Shouldly.
8. **No `Database.Migrate()`** (banned). Migrations ship as the idempotent script run by the deploy job;
   schema changes are expand → migrate → contract.
9. **DDL names are fixed** (reference schema). After a migration, regenerate the reference SQL and the
   data dictionary.
10. **Personal data stays protected.** Beneficiary PII only through the protector, never logged, never in
    a donor DTO; reveal is audited; mobiles masked in logs. `RedactingEnricher` masks
    authorization/cookie/password/secret/token properties and headers.
11. **Idempotency wherever money moves** (checkout by `Idempotency-Key`, outbox handlers at-most-once).
12. **Use `IClock`.** `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow` are banned in `src/`.
    Business dates are Asia/Riyadh.
13. **Contracts are the API.** A DTO change updates `openapi/v1.json` in the same PR
    (`UPDATE_OPENAPI_SNAPSHOT=1 dotnet test`) and 11 if needed; every new error code goes into 11 §1.6,
    `ErrorCodes` and both `ErrorMessages*.resx` files.
14. **Ask before adding a dependency.** Versions only in `Directory.Packages.props`.
15. **Secrets never in the repo or the chat.** Only `*.env.example` files are committed.

## Working method (plan 14 §4)

- One session = one branch = one PR; the PR title starts with `S-nn` (in cloud sessions, use the branch
  the session names).
- Test-first: failing `[Rule]` test per RUL, then the code. Layer by layer: Domain → Application →
  Infrastructure → Contracts + Api.
- Extend the shared tests each session: `ImplementedRules.txt`, the role × endpoint policy matrix (S-05+),
  the architecture tests.
- **Stop and ask** when a rule is missing or ambiguous, docs conflict, a session will exceed 1.5× its
  estimate, or a decision belongs to the owner (versions, licensed packages, security trade-offs).

## Commands

```bash
dotnet build Ta3lom.Store.slnx -warnaserror
dotnet format Ta3lom.Store.slnx --verify-no-changes
dotnet test --solution Ta3lom.Store.slnx
UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --project tests/Ta3lom.Store.Api.IntegrationTests   # accept an OpenAPI change
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Ta3lom.Store.Api          # Scalar UI at /docs
```

## Environment notes (cloud sessions)

- Only the .NET 10 SDK is installable (`apt-get install -y dotnet-sdk-10.0`); NuGet is reachable.
- No Docker daemon → Testcontainers cannot run here. From S-02, integration tests use
  `STORE_TEST_DB=external` against the local PostgreSQL 16 (with a `uuidv7()` shim); Testcontainers
  `postgres:18` is the CI path (doc 16 §2-F).

## Definition of done (plan 14 §4.5)

- [ ] Layer rules respected; endpoints bind → handler → `Result` mapping only.
- [ ] Every RUL of the session has a failing-then-passing `[Rule]` test and is listed in `ImplementedRules.txt`.
- [ ] Every endpoint has integration tests: success, 400, 401/403, and at least one business error `code`.
- [ ] Migration added if the model changed; idempotent script generated; reference DDL and data dictionary regenerated.
- [ ] `openapi/v1.json` updated and matching 11; deviations fixed in 11 in the same PR.
- [ ] Logs carry no PII (masked mobile, no names, no tokens) — log-redaction test green.
- [ ] `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, all tests green.
- [ ] Deployed to staging and the session's *Done when* verified there (from S-03).
- [ ] PR lists RUL / FR / endpoint numbers covered and open questions.
