# CLAUDE.md — ta3lom store API

.NET backend of the ta3lom.sa donations store: catalogue of donation projects and categories, donation cart, donor
sign-in by SMS code, checkout into `pending_payment` orders, donor account, staff admin. Read this file first.

## Scope

- In: catalogue, media, cart, checkout (orders in `pending_payment`, `payment: null`), order status, donor account (export,
  delete = anonymise), staff auth (password + TOTP), admin operations, outbox, jobs.
- Out until **R5-Payments**: payment gateway, webhook, payment tables, settlement (`paid`), receipts/PDF, giving totals,
  reconciliation, unpaid-order expiry, refunds, finance reports. **Keep `IPaymentInitiator` absent**: no payment port,
  client or table is added before that release.
- Business rules D-01 … D-14 are never invented or "fixed" in code. A rule that is missing or ambiguous → stop and ask.

## Platform

- .NET 10 (`net10.0`), ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, PostgreSQL 18, `Ta3lom.Store.slnx`.
- Central package versions (`Directory.Packages.props`), nullable, warnings as errors, analyzers on. Ask before adding a
  dependency.
- Idioms: primary constructors, collection expressions, records, file-scoped namespaces, `TimeProvider` behind `IClock`,
  `Guid.CreateVersion7()`, `TypedResults`, built-in OpenAPI + Scalar (Development only), built-in rate limiter.
- Tests: xUnit v3 on Microsoft.Testing.Platform, Shouldly, Testcontainers `postgres:18`. No MediatR, AutoMapper or
  FluentAssertions.

## Architecture (enforced by `tests/Ta3lom.Store.ArchitectureTests`)

| Project | References | Never |
|---|---|---|
| `SharedKernel` | BCL only | anything else |
| `Domain` | SharedKernel (+ DI abstractions for `AddDomain`) | EF Core, ASP.NET Core, outer projects |
| `Application` | Domain, SharedKernel, FluentValidation, Mapster, Scrutor, EF Core abstractions (read queries) | Npgsql, EF Relational, raw SQL, ASP.NET Core |
| `Infrastructure` | Application, Domain, SharedKernel | Api, the ASP.NET Core web stack |
| `Api` | all (composition root) | business logic, DTO types |

| Piece | Path |
|---|---|
| Entities and aggregates | `Domain/Entities/<Module>/` · enums `Domain/Enums/` · `Money`, `Slug`, `SaudiMobile` in `Domain/ValueObjects/` |
| Errors | `Domain/Errors/ErrorCodes.cs` + `Domain/Errors/<Area>Errors.cs` |
| Repository ports, unit of work | `Domain/IRepositories/` (no EF types) |
| Commands, queries, handlers, validators | `Application/Features/<Module>/` |
| DTOs | `Application/DTOs/<Module>/` (sealed records; money as `"50.00"` strings, enums as wire strings) |
| Ports for external services | `Application/Interfaces/` |
| Outbox handlers | `Application/Features/Outbox/Handlers/` |
| Repositories, row locks, raw SQL | `Infrastructure/Persistence/Repositories/`, `Infrastructure/Services/<Area>/` |
| Jobs | `Infrastructure/Services/Jobs/<Name>Job.cs` — thin shell over one Application command |
| Endpoints | `Api/Endpoints/<Module>Endpoints.cs : IEndpoint` on `/v1`; `/dev/*` via `IDevEndpoint` (Development only) |
| Error texts | `Api/Resources/ErrorMessages.resx` (ar) + `ErrorMessages.en.resx` |

## Conventions

- Endpoints: bind → one handler → `ToHttp()`. No logic, no DbContext. Every route declares a policy or `AllowAnonymous`
  (the fallback policy denies anything else). Admin routes go into the policy matrix automatically; keep it green.
- Handlers return `Result` / `Result<T>`; failures are `Error(Code, Description, Status)` with a code from `ErrorCodes`.
  Aggregates return `Result` for business outcomes and throw `DomainException` only for broken invariants (→ 409).
- Aggregates protect invariants: private setters, methods named after the business action (`Cart.AddOptionLine`,
  `Order.Cancel`). One transaction per use case through `IUnitOfWork.ExecuteInTransactionAsync`; outbox rows are written
  in that transaction; no external call inside it.
- Concurrency: `xmin` row version + `version` in update bodies → 409 `concurrency.conflict`. Cart writes and checkout
  lock the cart row (`FOR UPDATE`); locks and SQL live in Infrastructure only.
- Lock before you modify: `CustomerRepository.LockForUpdateAsync` reloads the tracked donor, discarding unsaved changes;
  take every lock (customer → cart) before changing an entity.
- Money: `decimal`/`Money` only, `numeric(12,2)`, two decimals, `MidpointRounding.AwayFromZero`, JSON strings. No
  float or double in Domain or SharedKernel.
- Time only from `IClock` (`DateTime.Now`/`UtcNow` are banned in `src/`); business dates are Asia/Riyadh; timestamps
  are written to PostgreSQL in UTC.
- Ids: `Guid.CreateVersion7()`, set by the domain (`ValueGeneratedNever`).
- Every new error code: `ErrorCodes` + both `.resx` files (a test checks it).
- Every rule D-01 … D-14 keeps at least one test tagged `[Rule("D-xx")]`.
- Personal data: no names, mobiles, e-mails or tokens in logs (`RedactingEnricher`); donor PII never in another donor's
  response; deletion anonymises and keeps the orders.
- Idempotency where orders are created (`Idempotency-Key`, scope = the donor); outbox handlers are at-most-once per
  recipient (`notification.dedupe_key`).
- Secrets never in the repository or the chat. Non-Development environments refuse missing or DEV ONLY keys.
- Migrations: one schema history from `InitialSchema`; a merged migration is never edited; no `Database.Migrate()`;
  deployments run the idempotent script. Expand → migrate → contract for schema changes.
- Migration `Down` is never run in production (roll forward with a new migration). `InitialSchema.Down` drops the
  `citext`/`pg_trgm` extensions; every newer migration's `Down` leaves the extensions in place.
- `ux_option_label` is a DEFERRABLE unique constraint owned by raw SQL (migration `OptionLabelDeferrable`), not part of
  the EF model; keep it when touching `project_option`.

## Commands

```bash
dotnet build Ta3lom.Store.slnx -warnaserror
dotnet format Ta3lom.Store.slnx --verify-no-changes
dotnet test --solution Ta3lom.Store.slnx                       # Testcontainers postgres:18 (needs Docker)
dotnet ef migrations add <Intent> -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure -o Persistence/Migrations
dotnet ef migrations has-pending-model-changes -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure
docker compose -f compose.dev.yaml up -d --wait                  # postgres:18 on :5440
dotnet run --project src/Ta3lom.Store.Api                          # Development on http://localhost:5080, Scalar UI at /docs
OPENAPI_OUT=artifacts/openapi/store-api.v1.json dotnet test --project tests/Ta3lom.Store.Api.IntegrationTests -- --filter-class "*OpenApiDocumentTests"
```

- Deploy: `Dockerfile` (targets `api`, `migrate`) + `deploy/compose.yaml`; runbook `deploy/README.md`; secrets are key-per-file
  under `/run/secrets` (`deploy/secrets.md`), never in the repository.
- OpenAPI: CI writes `/openapi/v1.json` (OpenApiDocumentTests, `OPENAPI_OUT`) and uploads it as the artifact
  `store-api-openapi` (30 days); nothing is committed (`artifacts/` is ignored).
- Development only: `Store__Otp__Development__MaxPerMobile` / `Store__Otp__Development__MaxPerIp` raise the D-10 OTP caps
  for local end-to-end runs; every other environment refuses them at start-up.

## Quality gates (every PR)

- [ ] Layer rules and architecture tests green; endpoints bind → handler → `Result` mapping only.
- [ ] `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, all tests green on `postgres:18`.
- [ ] Line coverage: Domain ≥ 90 %, Application ≥ 80 % (CI gate).
- [ ] No High/Critical vulnerable packages.
- [ ] New endpoints: success, 400, 401/403 and a business error `code` tested; admin routes in the policy matrix.
- [ ] Migration added when the model changed; `has-pending-model-changes` reports none.
- [ ] Logs carry no PII.
