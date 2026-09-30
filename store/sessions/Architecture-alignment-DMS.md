# Architecture Alignment — Store API follows the DMS architecture

**Date:** 30 September 2026 · **Reference repo:** [lotfi029/DMS](https://github.com/lotfi029/DMS) (read at commit `d291c4a`)
**Applies to:** `lotfi029/ta3lom_store_api` from S-02b onward · **Decision owner:** Lotfi

You asked for the store API to use the same architecture as DMS. This document maps DMS onto the
store, and lists what is adopted as-is. In the handful of places where a DMS pattern would break a
store requirement (API contract, payments safety, deployment), it keeps the store rule and says why.
Each of those is marked **(K)** and can be overridden.

---

## 1. The DMS architecture in one page

| Project | Holds | References |
|---|---|---|
| **SharedKernel** | `Entity` (Guid v7 id, domain events, equality), `Result` / `Result<T>` (`IsSuccess`, `IsFailure`, implicit conversions), `Error(Code, Description, Status)` with factories (`BadRequest`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`), `ValidationError`, `IDomainEvent`, `IDomainEventHandler`, `IAuditable` | — |
| **Domain** | `Entities/`, `Enums/`, `Constants/`, `Errors/` (one static class per aggregate, e.g. `ClientErrors.NotFound`), `IRepositories/` (`IGenericRepository<T>`, specific repositories, `IUnitOfWork`), `Services/` (domain services + interfaces), `ReadModels/`, `DependancyInjection` | SharedKernel |
| **Application** | `Abstractions/Messaging` (`ICommand`, `ICommand<T>`, `IQuery<T>`, `ICommandHandler<>`/`<,>`, `IQueryHandler<,>` with `HandleAsync(x, ct)`), `Abstractions/Behaviors` (Validation + Logging decorators), `Abstractions/Data` (`IApplicationDbContext`), `Abstractions/Pagination`, `Abstractions/Logging`, `DTOs/<Module>/` (requests, responses, request validators), `Features/<Module>/Commands\|Queries/<UseCase>/` (command/query **and** handler in one file, plus validator), `Interfaces/` (application services), `Mapping/` (Mapster `IRegister`), `DependancyInjection` (Scrutor scan + `Decorate`) | Domain |
| **Infrastructure** | `Persistence/` (`ApplicationDbContext`, `Configurations/`, `Interceptors/`, `Repositories/` incl. `GenericRepository`, `UnitOfWork`, `Seeders/`), `Services/` (implementations, auth/JWT, permissions), `DependancyInjection` | Application |
| **API** | `Endpoints/` (one `IEndpoint` class per module, auto-registered by `AddEndpoints` / `MapEndpoints`), `Extensions/`, `Infrastructure/` (`CustomResults.ToProblem`, `GlobalExceptionHandler`, OpenAPI transformers), `DependancyInjection` (`AddApi`), `Program.cs` chaining `AddApi → AddInfrastructure → AddApplication → AddDomain` | Infrastructure |

The traits that define it: **kind-first folders** (Entities / Errors / DTOs / Features), **command
and handler in the same file**, **static error catalogues per aggregate**, **repositories plus a unit
of work**, **`IEndpoint` modules**, **Mapster** for entity→response mapping, and **`GlobalUsings.cs`**
per project.

---

## 2. What the store adopts as-is

| DMS element | In the store |
|---|---|
| 5 projects incl. **SharedKernel** | `Ta3lom.Store.SharedKernel`, `.Domain`, `.Application`, `.Infrastructure`, `.Api`. The `Ta3lom.Store.` prefix stays (already in the repo). **`Ta3lom.Store.Contracts` is removed** (see §3-g) |
| SharedKernel `Entity`, `Result`, `Error`, `ValidationError`, `IDomainEvent`, `IAuditable` | Moved there from `Domain/Common` and `Application/Abstractions`, with DMS's API (`IsFailure`, `Error.NotFound(...)`, `Result.Success(...)`). See §3-d/e for two adjustments |
| Domain kind-first folders | `Domain/Entities/<Module>/`, `Domain/Enums/`, `Domain/Errors/` (`CartErrors`, `CheckoutErrors`, `AllocationErrors` …), `Domain/Constants/`, `Domain/IRepositories/`, `Domain/Services/`, `Domain/ReadModels/` (the 4 views) |
| Messaging interfaces | `ICommand`, `ICommand<T>`, `IQuery<T>`, `ICommandHandler<TCommand>` and `<TCommand,TResponse>`, `IQueryHandler<,>`, `HandleAsync(x, CancellationToken ct = default)` |
| Decorators | `ValidationDecorator` and `LoggingDecorator` nested-class style, registered with Scrutor `Decorate` |
| Features layout | `Application/Features/<Module>/Commands/<UseCase>/<UseCase>Command.cs` (record + `internal sealed` handler in the same file) and `<UseCase>CommandValidator.cs`; queries the same under `Queries/` |
| DTOs | `Application/DTOs/<Module>/` for request and response records and request validators |
| Application interfaces | `Application/Interfaces/` for services implemented in Infrastructure (`ISmsSender`, `IEmailSender`, `ILmsGateway`, `IFileStorage`, `IPdfRenderer`, `IOrderSettlement`, `IPaymentInitiator` …) |
| `IApplicationDbContext` | In `Application/Abstractions/Data`, used by **queries** (projections to response DTOs) — see §3-c for the one restriction |
| Repositories + unit of work | `IGenericRepository<T>` + specific repositories (`ICartRepository`, `IOrderRepository`, `ISponsorshipPoolRepository` …) in `Domain/IRepositories`, implementations in `Infrastructure/Persistence/Repositories`. **Commands** go through repositories + `IUnitOfWork` |
| Domain services | `Domain/Services/` (`IPoolingService`, `IAllocationPolicy`, `IRefundPolicy` …) |
| Mapster | `Application/Mapping/*MappingConfig : IRegister` for entity → response |
| Infrastructure layout | `Persistence/{StoreDbContext, Configurations, Interceptors, Repositories, Seeders}`, `Services/…` |
| API endpoints | One `IEndpoint` class per module (`CatalogEndpoints`, `CartEndpoints`, `AdminProductEndpoints` …), auto-registered by `AddEndpoints(assembly)` and mapped with `MapEndpoints(v1Group)`. This **replaces** S-01's `IV1EndpointContributor`; the test probe becomes a test-only `IEndpoint` |
| DI chaining | `AddApi(config) → AddInfrastructure(config) → AddApplication() → AddDomain()` |
| `GlobalUsings.cs` | One per project |

---

## 3. Where the store keeps its own rule (K) — and why

| # | DMS pattern | Store rule kept | Reason |
|---|---|---|---|
| a | `app.ApplyMigrations()` runs `Database.Migrate()` at startup | **(K) Migrations only from the deploy pipeline** (idempotent script as `store_owner`); `Migrate` stays a banned API | Blue/green deploys run two containers at once; a startup migration races and needs DDL rights on the app user (database/03 §2, 09 §12) |
| b | Domain references EF/Identity (`IUnitOfWork` returns `IDbContextTransaction`, `IGenericRepository.ExecuteUpdateAsync` takes `UpdateSettersBuilder<T>`) | **(K) Domain stays free of EF.** `IUnitOfWork` exposes `SaveChangesAsync` and `ExecuteInTransactionAsync(Func<CancellationToken, Task>)`; `ExecuteUpdateAsync` lives only on Infrastructure repositories. Only BCL + SharedKernel in Domain | Settlement and allocation run inside Npgsql's retrying execution strategy; manual `BeginTransaction` + try/catch (DMS handlers) breaks under retries (database/01 §4.4). Also keeps the Domain testable without EF |
| c | `IApplicationDbContext` exposes `DbSet<>` to Application | **Adopted, restricted:** Application may reference `Microsoft.EntityFrameworkCore` (abstractions only) for **read** queries. No Npgsql, no raw SQL, no `FromSql`, no `SaveChanges` from query handlers. Row locks (`FOR UPDATE`, `SKIP LOCKED`) live in Infrastructure repositories | Keeps DMS's productive query style for 60+ read endpoints, while the locking logic stays where it can be tested against PostgreSQL |
| d | `Error(Code, Description, Status)`; `ToProblem()` returns `{ errors: { code: [description] } }` | **Adopted shape, (K) contract output.** Error codes are the **11 §1.6 values** (`CartErrors.CampaignEnded` → `"cart.campaign_ended"`, 409). `ToProblem()` keeps S-01's format: `type`, `title`/`detail` localised ar/en, `status`, **`code`**, `traceId`, plus `errors` only for validation | The Angular client switches on `code` (11 §1.6); changing the error body breaks the contract |
| e | `Entity` sets `IsActive = true` and `CreatedAt = DateTime.UtcNow` | **(K) `Entity` = Id (v7, supplied) + domain events + equality only.** `created_at` / `updated_at` are set by the save interceptor from `IClock` for `IAuditable` entities; `is_active` exists only where the schema has it | Most store tables have no `is_active` (e.g. `order_line`); `DateTime.UtcNow` is banned (time comes from `IClock`); the schema-parity test would fail |
| f | Domain registers its services via `AddDomain()` (needs DI abstractions in Domain) | **Adopted, (K) with one tweak:** `AddDomain()` lives in Domain but only references `Microsoft.Extensions.DependencyInjection.Abstractions`. Nothing else is allowed | Minimal, keeps DMS's chaining |
| g | Request/response DTOs in `Application/DTOs`, no Contracts project | **Adopted.** `Ta3lom.Store.Contracts` is merged into `Application/DTOs/<Module>`; `PagedResponse<T>` becomes `Application/Abstractions/Pagination/PagedResult<T>`, but its **JSON is `{ items, total, page, pageSize }`** (11 §1.4) | This reverses S-01 review finding 1 (Application ↛ Contracts), because your DMS architecture places DTOs in Application. The architecture test is replaced by "Api must not define DTOs; Application DTOs must not reference Domain entities" |
| h | Mapster with implicit mapping | **Adopted, (K) explicit for money and enums.** Every entity→response pair is registered in an `IRegister`; money maps via `Money.ToWire()` (string `"350.00"`), enums to their DB strings; a test calls `TypeAdapterConfig.Compile()` and fails on unmapped members. AutoMapper stays banned | Money must never pass through float, and enum wire values must match the DDL |
| i | Handlers wrap work in manual `BeginTransaction` / `try / catch → return XErrors.CreatedFailed` | **(K) No catch-all in handlers.** Business failures return `Error`; unexpected exceptions go to the global handler (500 `server.error`); transactions via `IUnitOfWork.ExecuteInTransactionAsync` | A catch-all hides bugs and turns a payment-critical failure into a misleading 400 |
| j | No test projects | **(K) Keep all 5 test projects** (Domain, Application, Api.Integration, Architecture, Testing) and every S-01/S-02 gate | Plan 14 DoD and the RUL coverage rule depend on them |
| k | `DependancyInjection` (spelling) | **Use `DependencyInjection`** (correct spelling), same role and chaining | Cosmetic; avoids a permanent typo in public types |
| l | Explicit `IAuditService.LogActionAsync(...)` calls in handlers | **(K) Keep the S-02 save interceptor** writing `audit_log` for staff changes; an `IAuditService` may be added later for read/audit events if you want DMS parity | The interceptor can't be forgotten; explicit calls can |

---

## 4. Target layout (after S-02b)

```
src/
├── Ta3lom.Store.SharedKernel/     Entity, Result, Result<T>, Error, ValidationError, IDomainEvent, IDomainEventHandler, IAuditable
├── Ta3lom.Store.Domain/
│   ├── Entities/<Module>/         Product, CourseDetail … (49 tables, from S-02)
│   ├── Enums/                     39 enums (from S-02)
│   ├── Errors/                    CatalogErrors, CartErrors, CheckoutErrors, … (codes from 11 §1.6)
│   ├── Constants/                 Roles, Policies, SettingKeys, TemplateKeys
│   ├── IRepositories/             IGenericRepository<T>, IUnitOfWork, ICartRepository, …
│   ├── Services/                  domain services (+ interfaces)
│   ├── ReadModels/                v_goal, v_sales_daily, v_allocation_backlog, v_zakat_balance
│   ├── ValueObjects/              Money, OrderReference
│   └── DependencyInjection.cs
├── Ta3lom.Store.Application/
│   ├── Abstractions/{Messaging, Behaviors, Data, Pagination, Logging, Time}
│   ├── DTOs/<Module>/             requests, responses, request validators
│   ├── Features/<Module>/{Commands,Queries}/<UseCase>/
│   ├── Interfaces/                ports implemented in Infrastructure
│   ├── Mapping/                   Mapster IRegister configs
│   └── DependencyInjection.cs
├── Ta3lom.Store.Infrastructure/
│   ├── Persistence/{StoreDbContext, Configurations, Interceptors, Repositories, Seeders, Migrations}
│   ├── Services/                  SMS, email, LMS, storage, PDF, jobs, security
│   └── DependencyInjection.cs
└── Ta3lom.Store.Api/
    ├── Endpoints/                 IEndpoint per module
    ├── Extensions/                EndpointExtensions, ResultExtensions, Cors, Policies
    ├── Infrastructure/            CustomResults (11 format), GlobalExceptionHandler, OpenAPI transformers
    ├── Resources/                 ErrorMessages.resx / .en.resx
    ├── DependencyInjection.cs
    └── Program.cs
tests/                              unchanged (5 projects)
```

Dependency rule: `SharedKernel ← Domain ← Application ← Infrastructure ← Api`, plus Api → Application
for handlers and DTOs. Enforced by ArchitectureTests.

---

## 5. How to get there

| Step | What | When |
|---|---|---|
| 1 | Finish S-02 as planned (PR #4 decisions 1–6), **replace decision 7** with the message in §6 | Now |
| 2 | Merge PR #4 | After your PostgreSQL 18 run |
| 3 | **S-02b — Align with DMS architecture** (refactor only, no new features): new SharedKernel, move types, Domain kind-first folders, Errors catalogues seeded with the 11 §1.6 codes used so far, IRepositories + UnitOfWork + GenericRepository, IApplicationDbContext, Contracts → Application/DTOs, IEndpoint modules, Mapster wiring + compile test, updated architecture tests and CLAUDE.md "Architecture lock (DMS)" | Before S-03, **~1.5–2 dev-days** |
| 4 | Update docs: 09 §1–§3 r3 note pointing here; 17 kick-off rules | With S-02b |

Now is the cheapest moment: only the skeleton and persistence exist, so no feature code has to move twice.

---

## 6. Messages to paste into the cloud session

**Replace item 7 of the PR #4 message with:**

```text
7. Architecture: this repo must follow the same architecture as my DMS repo
   (https://github.com/lotfi029/DMS). Don't restructure in PR #4. Finish PR #4 with decisions 1–6 only.
   The alignment is a separate refactor PR, S-02b, right after PR #4 merges. The mapping and the few
   store rules that override DMS are in the claude.ai project file
   store/sessions/Architecture-alignment-DMS.md (I'll give you the S-02b prompt).
```

**S-02b prompt (after PR #4 is merged):**

```text
Session S-02b — Align the store API with my DMS architecture (refactor only, no new features).
Reference architecture: https://github.com/lotfi029/DMS (clone it read-only and study SharedKernel,
Domain, Application, Infrastructure, API). The mapping, the adopted patterns (§2) and the store rules
that override DMS (§3, items marked K) are in store/sessions/Architecture-alignment-DMS.md in this repo.
1. Read CLAUDE.md and that document. Confirm in 10 lines what moves where, then continue.
2. Create Ta3lom.Store.SharedKernel with DMS's Entity/Result/Error/ValidationError/IDomainEvent/
   IDomainEventHandler/IAuditable API, adjusted per §3-d and §3-e.
3. Restructure Domain (Entities/<Module>, Enums, Errors, Constants, IRepositories, Services,
   ReadModels, ValueObjects), Application (Abstractions/{Messaging,Behaviors,Data,Pagination,Logging},
   DTOs, Features, Interfaces, Mapping), Infrastructure (Persistence/{…,Repositories,Seeders}, Services),
   Api (Endpoints as IEndpoint modules, Extensions, Infrastructure/CustomResults+GlobalExceptionHandler).
4. Merge Ta3lom.Store.Contracts into Application/DTOs; PagedResult JSON stays {items,total,page,pageSize}.
5. Add IGenericRepository<T>, IUnitOfWork (SaveChangesAsync + ExecuteInTransactionAsync, no EF types
   in Domain), GenericRepository, UnitOfWork, IApplicationDbContext (read queries only).
6. Wire Mapster with explicit IRegister configs and a TypeAdapterConfig.Compile() test.
7. Keep every §3 (K) rule: no startup migrations, no EF in Domain, 11 §1.6 codes and problem format,
   IClock only, no catch-all in handlers, all 5 test projects.
8. Update ArchitectureTests to the new dependency rule and folder conventions; rewrite CLAUDE.md
   "Architecture lock" to point at DMS + the (K) exceptions; keep the schema-parity and all S-01/S-02
   tests green (behaviour must not change).
9. Full gate (build -warnaserror, format, all tests on local PG16), then PR "S-02b — Align with DMS
   architecture" with a before/after tree in the description.
```
