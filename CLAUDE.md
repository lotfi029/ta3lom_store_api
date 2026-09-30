# CLAUDE.md — ta3lom store API

## Run mode (active until `store/sessions/Build-report.md` is pushed)

The continuous cloud run is governed by `store/sessions/Cloud-build-plan.md` (§2 run rules, §4b test phase,
§4c plan 14 coverage). While it runs, these rules **override** the rest of this file:

- **Tests:** no new tests until the build and close-out are finished (§2.5); existing tests, architecture tests and
  schema parity stay green and are only adapted when a refactor moves types. Then the test phase T-1…T-9 (§4b)
  writes every RUL, policy-matrix and endpoint test and fills `ImplementedRules.txt`.
- **"Stop and ask" → defaults:** docs conflicts and open decisions use the defaults of §2.2–§2.3 and are logged in
  `DECISIONS.md` (precedence 03 > 11 > database/ > 09 > 14).
- **Staging DoD → local smoke check** (§2.7): gate, dev-DB migration, every new endpoint called once with curl
  (`tools/smoke/S-nn.http`), integrity checks after S-16, S-21, S-23, S-25, S-42.
- **"Ask before adding a dependency" → pre-approved list** (§2.4 + §4c item 5). Anything else: the smallest
  well-known MIT/Apache package, logged as `REVIEW`.
- **Git:** one draft PR for the whole run, nothing merged by the session, state in `PROGRESS.md` (first line
  `Next:`) and `DECISIONS.md`.

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
5. `store/database/01-database-design.md` and the reference DDL in `db/reference/` — the master copy of the
   reference SQL lives in this repo (from S-02).
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

## Architecture — DMS + store (K) exceptions, enforced by `tests/Ta3lom.Store.ArchitectureTests`

The layout follows the owner's DMS repo (github.com/lotfi029/DMS) as mapped in
`store/sessions/Architecture-alignment-DMS.md`; the (K) rules there override DMS where they differ.

| Project | References | Never |
|---|---|---|
| `Ta3lom.Store.SharedKernel` | BCL only | anything else |
| `Ta3lom.Store.Domain` | SharedKernel (+ DI abstractions for `AddDomain` only) | EF Core (K-b), ASP.NET Core, any outer project |
| `Ta3lom.Store.Application` | Domain, SharedKernel (+ FluentValidation, Mapster, Scrutor, EF Core **abstractions** for read queries, K-c) | Npgsql, EF Relational, raw SQL, ASP.NET Core, adapter SDKs |
| `Ta3lom.Store.Infrastructure` | Application, Domain, SharedKernel | Api, the ASP.NET Core web stack |
| `Ta3lom.Store.Api` | all (composition root) | business logic, DTO types |

Where a use case lives (Cloud-build-plan §3.2):

| Piece | Path |
|---|---|
| Entity / aggregate behaviour | `Domain/Entities/<Module>/` · enums `Domain/Enums/` · `Money` in `Domain/ValueObjects/` |
| Error catalogue | `Domain/Errors/ErrorCodes.cs` (flat list of 11 §1.6 codes) + `Domain/Errors/<Aggregate>Errors.cs` |
| Repository port / unit of work | `Domain/IRepositories/` (`IGenericRepository<T>`, `IUnitOfWork` = `SaveChangesAsync` + `ExecuteInTransactionAsync`, no EF types) |
| Command / query + handler (one file) + validator | `Application/Features/<Module>/{Commands,Queries}/<UseCase>/` |
| Request / response DTOs | `Application/DTOs/<Module>/` (sealed records; money as `"350.00"` strings, enums as DDL strings) |
| Mapping | `Application/Mapping/<Module>MappingConfig : IRegister` (Mapster, explicit for money and enums) |
| Ports for external services | `Application/Interfaces/` |
| Settlement effects | `Application/Features/Settlement/Effects/` |
| Repository / service implementations | `Infrastructure/Persistence/Repositories/`, `Infrastructure/Services/<Area>/` (row locks only here) |
| Jobs | `Infrastructure/Services/Jobs/<Name>Job.cs` — thin shell calling one Application command |
| Endpoints | `Api/Endpoints/<Module>Endpoints.cs : IEndpoint` (mapped on `/v1`); `/dev/*` via `IDevEndpoint` |
| Error texts | `Api/Resources/ErrorMessages.resx` + `.en.resx` |

- Handlers return SharedKernel `Result` / `Result<T>`; failures are `Error(Code, Description, Status)` whose
  code is an 11 §1.6 value. `CustomResults.ToHttp()`/`ToProblem()` keep the 11 §1.6 problem body (K-d).
- Endpoints: bind → call one handler → `ToHttp()`. No logic, no DbContext. Test-only routes are an
  `IEndpoint` registered by the test factory; no probe route in production code.
- No catch-all in handlers (K-i); transactions only through `IUnitOfWork.ExecuteInTransactionAsync` (retrying
  execution strategy). No startup migrations (K-a). `created_at`/time only from `IClock` (K-e).
- Aggregates protect invariants; private setters; state changes through methods named after the business
  action (`Order.MarkPaid`, `Cart.AddLine`). One transaction per use case; outbox rows in that transaction;
  no external call inside a transaction.

## Non-negotiables (09 §12, adapted)

1. **03 is the source of truth.** Read the RUL before touching its code. Code vs rule → stop and ask.
2. **Never float money.** `decimal`/`Money` only, two decimals, `MidpointRounding.AwayFromZero`; JSON money
   is a string (`"350.00"`). Guards: `BannedSymbols.Money.txt` (double/float members in Domain and
   SharedKernel) + the `No_money_as_floating_point` architecture test (money-named declarations).
3. **Payments are out of scope.** Only `IOrderSettlement` sets an order `paid` (S-16). No gateway,
   webhook, reconciliation, retry or refund code here (doc 15).
4. **Settlement, pooling and allocation are one transaction each**, with the row locks of database/01 §4.
   External calls (SMS, email, LMS, PDF) happen from the outbox, after commit.
5. **No business logic in endpoints.**
6. **Every implemented RUL has a test** tagged `[Rule("RUL-nn")]` (`Ta3lom.Store.Testing.RuleAttribute`).
   Append the session's rules to `tests/Ta3lom.Store.ArchitectureTests/ImplementedRules.txt`.
7. **No MediatR, no AutoMapper, no FluentAssertions.** Plain handler interfaces, Scrutor, Shouldly.
8. **No `Database.Migrate()`** (banned). A merged migration is never edited; changes go in a new migration. Migrations ship as the idempotent script run by the deploy job;
   schema changes are expand → migrate → contract.
9. **DDL names are fixed** (`db/reference/001_store_schema.sql`, including PostgreSQL's constraint names).
   After a migration, edit 001 by hand until `SchemaParityTests` passes and regenerate the data dictionary.
   Every column keeps its SQL default, but EF properties are `ValueGeneratedNever()`: the domain sets values.
   **jsonb columns** are raw JSON strings in S-02. Each becomes a typed owned JSON type (`OwnsOne(...).ToJson()`)
   in the session that first uses it: `cart_line.dedication` → S-13, `orders.shipping_address` and
   `shipment.address` → S-15/S-21, `setting.value` → S-09. The column type stays `jsonb`.
10. **Personal data stays protected.** Beneficiary PII only through the protector, never logged, never in
    a donor DTO; reveal is audited; mobiles masked in logs. `RedactingEnricher` masks
    authorization/cookie/password/secret/token properties and headers.
11. **Idempotency wherever money moves** (checkout by `Idempotency-Key`, outbox handlers at-most-once).
12. **Use `IClock`.** `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow` are banned in `src/`.
    Business dates are Asia/Riyadh.
13. **DTOs are the API.** A change to `Application/DTOs` updates `openapi/v1.json` in the same PR
    (`UPDATE_OPENAPI_SNAPSHOT=1 dotnet test`) and 11 if needed; every new error code goes into 11 §1.6,
    `ErrorCodes` and both `ErrorMessages*.resx` files.
14. **Ask before adding a dependency.** Versions only in `Directory.Packages.props`.
15. **Secrets never in the repo or the chat.** Only `*.env.example` files are committed.

## Working method (plan 14 §4)

- One session = one branch = one PR; the PR title starts with `S-nn` (in cloud sessions, use the branch
  the session names).
- Test-first: failing `[Rule]` test per RUL, then the code. Layer by layer: Domain → Application →
  Infrastructure → Api (DTOs in Application).
- Extend the shared tests each session: `ImplementedRules.txt`, the role × endpoint policy matrix (S-05+),
  the architecture tests.
- **Stop and ask** when a rule is missing or ambiguous, docs conflict, a session will exceed 1.5× its
  estimate, or a decision belongs to the owner (versions, licensed packages, security trade-offs).

## Commands

```bash
dotnet build Ta3lom.Store.slnx -warnaserror
dotnet format Ta3lom.Store.slnx --verify-no-changes
dotnet test --solution Ta3lom.Store.slnx                                   # Testcontainers postgres:18 (needs Docker)
STORE_TEST_DB=external STORE_TEST_DB_CONNECTION="Host=localhost;Username=postgres;Password=…" \
  dotnet test --solution Ta3lom.Store.slnx                                 # a running PostgreSQL (16 gets a uuidv7() shim)
dotnet ef migrations add <Intent> -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure -o Persistence/Migrations
tools/scripts/migrate-script.sh              # artifacts/migrate.sql (idempotent; what the deploy job runs)
tools/scripts/regenerate-data-dictionary.sh  # after a migration (PG* env vars); also edit db/reference/001 by hand
UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --project tests/Ta3lom.Store.Api.IntegrationTests   # accept an OpenAPI change
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Ta3lom.Store.Api          # Scalar UI at /docs
```

## Environment notes (cloud sessions)

- Only the .NET 10 SDK is installable (`apt-get install -y dotnet-sdk-10.0`); NuGet is reachable.
- No Docker daemon → Testcontainers cannot run here. Integration tests use `STORE_TEST_DB=external`
  against the local PostgreSQL 16 (`pg_ctlcluster 16 main start`); the test fixture creates a `uuidv7()`
  shim, migrations never do. Testcontainers `postgres:18` is the default and the CI path (doc 16 §2-F).
  The schema-parity test must pass on both.

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
