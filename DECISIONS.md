# Decisions log

Precedence when docs disagree: 03 > 11 > database/ > 09 > 14 (Cloud-build-plan §2.2). `REVIEW` marks an item
the owner should confirm.

| Date | Session | Topic | Docs | Choice | Reason | REVIEW? |
|---|---|---|---|---|---|---|
| 2026-09-30 | S-RUN | Governing docs for the run | Cloud-build-plan, Run-plan-review, Plan-14-coverage-audit | The approved plan + Run-plan-review changes (one draft PR, no merges, effects in Application, thin jobs, typed keys, test phase T-1…T-9) + coverage-audit addendum apply on top of Cloud-build-plan. The two review files were copied alone from `origin/docs/cloud-build-plan`; that branch is otherwise stale and is never merged or used as a base | Owner's approval message | |
| 2026-09-30 | S-RUN | Final report name | Cloud-build-plan §4 Phase 7 vs Run-plan-review §3 | `store/sessions/Build-report.md` replaces `Part-1-report.md`; the Part 1 checklist in PROGRESS.md is still ticked before the test phase | Run-plan-review is later and approved | |
| 2026-09-30 | S-RUN | Endpoints #29 and #32 | plan 14 S-31 text vs §13.2 | #29 is built in S-19, #32 in S-20; S-31 re-uses them | §13.2 is the map that adds up to 130 | |
| 2026-09-30 | S-RUN | Endpoint #71 | plan 14 S-09 / S-42 | S-09 builds the close without the sweep; S-42 adds the pool sweep to the same endpoint | Plan 14 §13.2 counts #71 once | |
| 2026-09-30 | S-RUN | `settlement.effect_missing` | plan 14 §13.4 vs 11 §1.6 | Added to 11 §1.6 as an internal code (500-class, never returned for a correct deployment) in S-16 | Every emitted code must be in 11 §1.6 (ErrorCodeCatalogueTests) | |
| 2026-09-30 | S-RUN | Payment-only checkout codes | 11 §1.6 vs plan 14 §0 | With `NullPaymentInitiator`, S-15 never emits `checkout.gateway_unavailable` or `checkout.payment_method_not_allowed`; the codes stay in 11 for the payment service | Payments are out of scope | |
| 2026-09-30 | S-RUN | `/dev/*` endpoints | plan 14 §4.3 vs 11 (silent) | A note in 11 documents `/dev/*` (`/dev/sms`, `/dev/mail`, `/dev/orders/{ref}/confirm-payment`) as Development/Staging-only and outside the contract | 11 must describe every mapped route | |
| 2026-09-30 | S-RUN | Job count | plan 14 §1 / M7 / §13.3 | 12 scheduled Quartz jobs + the OutboxDispatcher (= "13 jobs"); ReconcilePayments excluded | §13.3 lists 12 + dispatcher | |
| 2026-09-30 | STEP 0 | Dev DB migration helper | CLAUDE.md commands | New `tools/scripts/dev-db-migrate.sh`: adds the `uuidv7()` shim on PostgreSQL < 18 and applies `artifacts/migrate.sql` | The dev DB is PG16; the shim is never in a migration | |
| 2026-09-30 | S-02b | `AlignDmsNamespaces` migration | Run-plan-review §2 #5 | **Not added**: `dotnet ef migrations has-pending-model-changes` reports no changes after the entity namespaces moved (EF keys entity types by CLR name only in designer metadata); merged migration designer files untouched | Tooling decides; merged migrations are never edited | |
| 2026-09-30 | S-02b | `Error` shape | DMS `Error(Code, Description, int? Status)` vs S-01 `ErrorKind` | SharedKernel `Error(Code, Description, int Status, Extensions?)` with factories (`BadRequest`, `NotFound`, `Conflict`, `Unprocessable`, `Gone`, `Locked`, `TooLarge`, `RateLimited` …) and `ValidationError`; `Error.None` on success like DMS. The Api writes `status` directly; the 11 §1.6 body (code, localised title/detail, traceId) is unchanged (K-d) | DMS parity, and 11 §1.6 needs 413/415/423/502/503 which the old enum lacked | |
| 2026-09-30 | S-02b | `Entity` vs `AggregateRoot` | Architecture-alignment §2 | Both kept in SharedKernel: `Entity` = supplied v7 Id + equality; `AggregateRoot : Entity` holds the domain events | Moving events onto every entity would add a field to 48 mapped types for no use; behaviour unchanged | |
| 2026-09-30 | S-02b | Error code catalogue location | 09 §3.2 (Application/Abstractions) vs DMS (Domain/Errors) | `Domain/Errors/ErrorCodes.cs` (flat constants the catalogue/resx tests reflect on) + per-aggregate `XErrors` classes built from it (`GeneralErrors` first) | DMS layout; one list keeps the 11 §1.6 and resx checks simple | |
| 2026-09-30 | S-02b | Enum wire names | S-02 converter | `Domain/Enums/EnumWire` holds the snake_case/`[EnumMember]` mapping; the EF converter and Mapster configs both use it | JSON enum values must equal the DDL values (K-h) | |
| 2026-09-30 | S-02b | Mapster compile check | Architecture-alignment §6 step 6 vs "no new tests" | `TypeAdapterConfig.Compile()` runs at start-up in Development; the test is added in T-7 | Run-plan-review §2 #6 | |
| 2026-09-30 | S-02b | Architecture tests | LayerTests (S-01) | Adapted to the DMS rule: SharedKernel test, Domain may use only DI abstractions (for `AddDomain`), Application may use EF Core abstractions but not Npgsql/Relational, "Contracts sealed" → "Application DTOs sealed and entity-free" + "Api defines no DTOs", inward-reference theory with SharedKernel | The refactor moved types; same intent, new layers (3 more facts, no new behavioural coverage) | |
| 2026-09-30 | S-02b | Mapster version | §2.4 pre-approved | `Mapster` + `Mapster.DependencyInjection` 10.0.13 (DMS uses 10.0.7) | Latest stable, MIT | |

## Bugs found by tests

| Test | Rule | Fix commit |
|---|---|---|
