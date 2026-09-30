# Cloud Build Plan — the whole backend in one continuous run

**Date:** 30 September 2026 · **Owner:** Lotfi · **Main plan:** [14 — Backend implementation plan](../14-backend-implementation-plan.md)
**Architecture reference:** [lotfi029/DMS](https://github.com/lotfi029/DMS) (commit `d291c4a`), mapped in [Architecture-alignment-DMS.md](Architecture-alignment-DMS.md)
**Replaces:** `Part-1-autonomous-build.md` and `Continue-current-plan.md` (both superseded)

This file tells a cloud Claude Code session how to build every remaining session of plan 14. It is written so the session **never waits for you**. Plan 14 stays the source for *what* each session builds. This file sets *how* the work runs.

---

## 0. Repository scan (main at `26a0c15`, 30 Sep 2026)

### 0.1 Git history

| PR | Content | State |
|---|---|---|
| #1 | S-01 — solution skeleton + CLAUDE.md | merged |
| #2, #3 | reference SQL in `db/reference/` (#2 was the wrong branch, #3 is the fix) | #3 merged |
| #4 | S-02 — persistence: 49 tables, `InitialSchema` migration, seed, schema-parity test | merged |
| #5 | S-02 decisions: `payment_event` column grant (migration `GrantPaymentEventColumnUpdate`), jsonb rule | merged |

The cloud session was stopped before it started S-02b. The branches are `main` and `chore/db-reference` (stale, can be deleted).

### 0.2 What exists on `main`

| Area | Contents |
|---|---|
| Projects | `Ta3lom.Store.{Api, Application, Contracts, Domain, Infrastructure}` + 5 test projects (`Domain.Tests`, `Application.Tests`, `Api.IntegrationTests`, `ArchitectureTests`, `Testing`) |
| Domain | `Common/` (Entity, AggregateRoot, Money, Guard, DomainException, IDomainEvent) and state-only entities by module: Auditing, Carts, Catalog (15), Documents, Fulfilment, Giving (15), Identity, Media, Notifications, Ordering, Payments, Settings |
| Application | **Only abstractions**: Messaging, Result, ErrorCodes, Ports, PagedResult, Logging and Validating decorators. **No feature code yet** |
| Infrastructure | `StoreDbContext`, one configuration per table, converters (Money, ProtectedString, SnakeCaseEnum), audit interceptor, 2 migrations, SeedRunner, read models for the 4 views, health check, SystemClock |
| Api | Program, health endpoints, ProblemDetails, CORS, rate limiter, security headers, Serilog redaction, `create-admin` CLI stub, `IV1EndpointContributor` |
| Tooling | `tools/scripts/` (migrate-script, regenerate-data-dictionary, regenerate-schema-sql, scratch-db), `deploy/env/store.env.example`, `openapi/v1.json` |
| Docs | `store/` = copies of docs 01–17, adr/, database/, and `sessions/S-01-plan-review.md` only |

### 0.3 Gaps this plan must close before the run starts

| # | Gap | Fix |
|---|---|---|
| G1 | `store/sessions/Architecture-alignment-DMS.md` and this file are **not in the repo**. The cloud session cannot read the claude.ai project | Preparation step P1 copies them in |
| G2 | `store/14-backend-implementation-plan.md` in the repo lacks the r3 note | P1 copies the current plan 14 |
| G3 | CLAUDE.md requires test-first, "stop and ask", staging deploys, and asking before any new package. All four would stop the run | Session **S-RUN** (§3) rewrites these CLAUDE.md sections for the run |
| G4 | Packages needed later are missing: Mapster, JwtBearer, QuestPDF, PdfSharp, QRCoder, Scriban, Otp.NET, MailKit (optional), Http.Resilience, EF Core in Application (read queries) | Pre-approved list, §2.4 |
| G5 | The code still uses S-01's shape (Contracts project, `IV1EndpointContributor`, Common folders) | S-02b refactor, first in the run |
| G6 | Your local clone `D:\Freelance\ELAMS\repos\StoreApi` is on the stale `chore/db-reference` branch | P1 starts with `git checkout main; git pull` |

---

## 1. Preparation (you, before the run)

### P1 — Put the plan files in the repo (5 minutes, PowerShell)

```powershell
cd D:\Freelance\ELAMS\repos\StoreApi
git checkout main
git pull --ff-only                      # brings S-02 and PR #5 down
git branch -D chore/db-reference        # stale local branch
git push origin --delete chore/db-reference

git checkout -b docs/cloud-build-plan
$d = "D:\Freelance\ELAMS\Docs\store"
Copy-Item "$d\sessions\Cloud-build-plan.md","$d\sessions\Architecture-alignment-DMS.md" store\sessions\
Copy-Item "$d\14-backend-implementation-plan.md" store\
git status --short                      # only store/... must be listed
git add store
git commit -m "Add cloud build plan and DMS architecture alignment"
git push -u origin docs/cloud-build-plan
```

Open the PR on GitHub and **merge it**. On `main`, check that `store/sessions/` holds 3 files.

### P2 — Cloud environment settings (claude.ai → Claude Code → environment)

| Setting | Value | Why |
|---|---|---|
| Repository | `lotfi029/ta3lom_store_api`, with push access | The session commits and pushes after every S-nn |
| Network | Allow at least: `github.com`, `api.nuget.org`, `www.nuget.org`, `globalcdn.nuget.org`, Ubuntu apt mirrors (for `dotnet-sdk-10.0` and PostgreSQL) | Restore, build, tests |
| Setup script (if the environment offers one) | The script below | Every new session starts ready, so a resume needs no preparation |
| Permission mode | Auto-accept edits and commands, if offered | A permission prompt pauses the run |
| Secrets | **None needed.** Don't add any | The run uses sinks and fakes only |

Setup script:

```bash
#!/usr/bin/env bash
set -e
sudo apt-get update -y
sudo apt-get install -y dotnet-sdk-10.0 postgresql
sudo pg_ctlcluster 16 main start || sudo service postgresql start
sudo -u postgres psql -c "ALTER USER postgres PASSWORD 'postgres';"
sudo -u postgres psql -c "CREATE DATABASE ta3lom_store_dev;" || true
echo 'export STORE_TEST_DB=external' >> ~/.bashrc
echo 'export STORE_TEST_DB_CONNECTION="Host=localhost;Username=postgres;Password=postgres"' >> ~/.bashrc
echo 'export ConnectionStrings__Store="Host=localhost;Database=ta3lom_store_dev;Username=postgres;Password=postgres"' >> ~/.bashrc
dotnet tool restore || true
```

The password is for a throw-away database inside the container. It is not a secret and is never committed.

### P3 — Run the preparation prompt (§6.1)

It checks the environment and ends with `READY`, `CI-ONLY` or `BLOCKED`. Start the run only on `READY`.

---

## 2. Run rules (they override CLAUDE.md for the run)

### 2.1 What "never stops" means

- The session **never ends its turn to report, ask or wait.** The only reports are commits, `PROGRESS.md` and `DECISIONS.md`.
- There are **no stop conditions.** Each situation that used to stop the work has a default instead (§2.2).
- The one thing a session can't control is its own lifetime (context, time or platform limits). To cover that:
  - every S-nn ends with a commit and a push;
  - `PROGRESS.md` always names the next step;
  - a fresh session continues from the resume prompt (§6.3) without any other context.

### 2.2 Defaults instead of stopping

| Situation | What the session does |
|---|---|
| Docs disagree or a rule is missing | Pick by precedence **03 > 11 > database/ > 09 > 14**. Log it in `DECISIONS.md` (date, S-nn, docs, choice, reason). Continue |
| An open business decision (Q-xx) | Use the default in §2.3. Log it |
| A package is needed | Use it if it's on the §2.4 list. Otherwise pick the smallest well-known MIT/Apache package, log it, and mark it `REVIEW` |
| The build or existing tests can't be made green after 3 honest attempts | Revert the session's last failing change set. Mark the session `blocked` in `PROGRESS.md` with the error. Move to the next session whose dependencies are done (§4). Come back to blocked sessions at the end |
| A change would edit a merged migration | Never do it. Add a new migration instead |
| A change would delete data outside the dev database | Never do it. Log the need as `REVIEW` |
| A secret or live credential seems needed | Use the sink/fake/config placeholder. Log it as `REVIEW` |
| A session runs over its estimate | Continue. Note it in `PROGRESS.md` |
| A PR can't be merged by the session (no permission) | Don't wait. Keep committing on the same branch (§2.6) |
| A push of `.github/workflows/ci.yml` is refused (token lacks workflow scope) | Keep the file at `tools/ci/ci.yml` instead, log it as `REVIEW` (you move it later). Continue |

### 2.3 Fixed defaults for open decisions

| Topic | Default |
|---|---|
| LMS (Q-05) | `InternalAccessLmsGateway` |
| SMS provider | `SinkSmsSender` (dev log and `/dev/sms`). A real provider class sits behind config with empty keys |
| Email | SMTP via config (Mailpit host in dev); `SmtpEmailSender` |
| Payments | Out of scope (plan 14 §0): `NullPaymentInitiator` + `POST /dev/orders/{ref}/confirm-payment` (Development/Staging only) |
| VAT (Q-02) | Setting `vat_enabled = false` (seeded); code supports both |
| E-invoicing | `NoopEInvoicingGateway` |
| File storage | `LocalFileStorage` under a configurable root |
| QuestPDF licence | Community licence, set in code, logged in `DECISIONS.md` for you to confirm |
| Refund policy (Q-07) | Not built here (payment service) |

### 2.4 Pre-approved packages (add to `Directory.Packages.props` when first needed)

| Package | First session | Project |
|---|---|---|
| `Microsoft.EntityFrameworkCore` (already pinned) | S-02b | Application (read queries via `IApplicationDbContext`) |
| `Mapster`, `Mapster.DependencyInjection` | S-02b | Application |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | S-04 | Api |
| `Otp.NET` | S-05 | Infrastructure |
| `QRCoder` | S-05 (TOTP QR) | Infrastructure |
| `Microsoft.Extensions.Http.Resilience` | S-17 | Infrastructure |
| `Scriban` | S-17 | Infrastructure |
| `QuestPDF` | S-17 | Infrastructure |
| `MailKit` (only if `System.Net.Mail` is not enough) | S-17 | Infrastructure |
| `PdfSharp` | S-20 | Infrastructure |
| `CsvHelper` (only if a hand-written CSV is not enough) | S-24 / S-33 | Infrastructure |

Still banned: MediatR, AutoMapper, FluentAssertions, any payment SDK.

### 2.5 Tests during the run

You decided: **no new tests until the whole plan is built.**

| Kept | Deferred to the test phase |
|---|---|
| `dotnet build -warnaserror` and `dotnet format --verify-no-changes` on every S-nn | Every `[Rule]` test (plan 14 "Tests" lists) |
| All existing tests stay green (S-01/S-02 suites, schema parity, error-message completeness, log redaction) | The role × endpoint policy-matrix test (S-05+) |
| Existing tests are **adapted** when a refactor moves types (S-02b); no new coverage is added | Concurrency, property-based, PDF snapshot tests |
| `openapi/v1.json` regenerated with `UPDATE_OPENAPI_SNAPSHOT=1` on every S-nn that changes endpoints | Integration tests per endpoint |
| `ImplementedRules.txt` stays **empty** (the RUL coverage test then has nothing to demand) | Filling `ImplementedRules.txt` |

To make up for the missing tests, every S-nn runs a **smoke check** instead (§2.7).

### 2.6 Git flow

- Work on the branch the cloud session gives you (e.g. `claude/…`). Don't open a new branch per session.
- **One commit set per S-nn.** Commit messages start with `S-nn —`. Push after every S-nn.
- **One PR per phase** (Phase 1 … Phase 6, §4). Open it when the phase's first session starts and mark it ready when the phase ends.
  - If the session can merge, it squash-merges when the build and tests are green, then continues from the updated `main`.
  - If it can't merge, it keeps going on the same branch; you merge later.
- Never force-push, rebase shared history, or delete branches.
- New migrations are named by intent (`AddCartDedicationJson`, `AddOtpIndexes`). Merged migrations are never edited. After a migration, update `db/reference/001_store_schema.sql` by hand until schema parity passes.

### 2.7 Smoke check (end of every S-nn)

1. `dotnet build Ta3lom.Store.slnx -warnaserror` · `dotnet format --verify-no-changes` · `dotnet test --solution Ta3lom.Store.slnx` (local PG16, `STORE_TEST_DB=external`).
2. Apply migrations to `ta3lom_store_dev` with the idempotent script (`tools/scripts/migrate-script.sh`, then `psql -f`). Run the seed.
3. `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Ta3lom.Store.Api`, then call **every new endpoint once** with `curl`:
   - the success path;
   - one expected business error.
   Use the dev trigger for anything that needs a paid order. Save the calls in `tools/smoke/S-nn.http` (plain `.http` file; this is not a test project).
4. After **S-16, S-21, S-23, S-25 and S-42**, also run `psql -f db/reference/003_integrity_checks.sql` on the dev DB. Every query must return 0 rows. If one doesn't, fix it before the next session.
5. Update `PROGRESS.md` and push.

### 2.8 State files (repo root, created by S-RUN)

`PROGRESS.md`:

```markdown
# Build progress
Next: S-nn — <step>                     ← always correct; the resume prompt reads this line
| Session | Status (todo/doing/done/blocked) | Commits | Endpoints # | Jobs | Smoke | Notes |
## Part 1 checklist
- [ ] 130 endpoints of plan 14 §13.2 mapped and answering
- [ ] 13 jobs of §13.3 registered (all except ReconcilePayments)
- [ ] Every settlement effect of §13.4 registered (5 line effects + gift/donate counters)
- [ ] Integrity checks 0 rows after the full smoke scenario
- [ ] No session left blocked
```

`DECISIONS.md`: `| Date | Session | Topic | Docs | Choice | Reason | REVIEW? |`

---

## 3. Architecture (DMS, with the store exceptions)

The full mapping is in [Architecture-alignment-DMS.md](Architecture-alignment-DMS.md). The session reads it first and clones DMS read-only as the example.

### 3.1 Projects and dependencies

`SharedKernel ← Domain ← Application ← Infrastructure ← Api` (Api also references Application). The `Ta3lom.Store.` prefix stays. `Contracts` is merged into `Application/DTOs`.

### 3.2 Where a use case lives (template for every session)

| Piece | Path | Example (S-13 Cart) |
|---|---|---|
| Entity / aggregate behaviour | `Domain/Entities/<Module>/` | `Cart.AddLine(...)` |
| Enum | `Domain/Enums/` | `CartLineKind` |
| Error catalogue | `Domain/Errors/<Aggregate>Errors.cs`; codes = **11 §1.6 values** | `CartErrors.CampaignEnded => Error.Conflict("cart.campaign_ended", …)` |
| Repository port | `Domain/IRepositories/I<Aggregate>Repository.cs` | `ICartRepository` |
| Domain service | `Domain/Services/` | `ICartLinePolicy` |
| Command + handler (one file) | `Application/Features/<Module>/Commands/<UseCase>/<UseCase>Command.cs` | `AddCartLineCommand` + `internal sealed AddCartLineCommandHandler` |
| Validator | same folder, `<UseCase>CommandValidator.cs` | `AddCartLineCommandValidator` |
| Query + handler | `Application/Features/<Module>/Queries/<UseCase>/<UseCase>Query.cs` (reads via `IApplicationDbContext`, `AsNoTracking`, projects to DTOs) | `GetCartQuery` |
| Request/response DTOs | `Application/DTOs/<Module>/` | `AddCartLineRequest`, `CartResponse` |
| Mapping | `Application/Mapping/<Module>MappingConfig : IRegister` (explicit; money → `"350.00"` strings, enums → DB strings) | `CartMappingConfig` |
| Port for external services | `Application/Interfaces/` | `ISmsSender` |
| Repository / service implementation | `Infrastructure/Persistence/Repositories/`, `Infrastructure/Services/<Area>/` | `CartRepository` |
| Job | `Infrastructure/Services/Jobs/<Name>Job.cs` (Quartz) | `ExpireGuestCartsJob` |
| Endpoints | `Api/Endpoints/<Module>Endpoints.cs : IEndpoint` — bind → handler → `ToHttp()` / `ToProblem()` | `CartEndpoints` (`/v1/cart`) |
| Error texts | `Api/Resources/ErrorMessages.resx` + `.en.resx` (every new code) | `cart.campaign_ended` ar/en |

### 3.3 Store rules kept over DMS (K)

1. No startup migrations (`Database.Migrate` stays banned).
2. No EF types in Domain. `IUnitOfWork` = `SaveChangesAsync` + `ExecuteInTransactionAsync(Func<CancellationToken, Task>)`.
3. Error body = 11 §1.6 problem format with `code`.
4. `Entity` has no `IsActive`/`CreatedAt`, and the clock is `IClock` only.
5. No catch-all in handlers.
6. All 5 test projects are kept.
7. Mapster mappings are explicit for money and enums.
8. The audit interceptor is kept.
9. Row locks (`FOR UPDATE`, `SKIP LOCKED`) only in Infrastructure repositories.

---

## 4. Build order (the run)

Estimates are plan 14's human-days (about 45.5 in total). They are only a guide to size; the run doesn't stop when one is exceeded.

### Phase 0 — run set-up and architecture

| # | Session | What to build | Est |
|---|---|---|---|
| 0.1 | **S-RUN** | Create `PROGRESS.md` and `DECISIONS.md`. In CLAUDE.md, add a **"Run mode"** section at the top saying that until the Part 1 checklist is ticked, these rules override the file:<br>• test-first → no new tests (§2.5);<br>• "stop and ask" → defaults (§2.2);<br>• staging DoD → smoke check (§2.7);<br>• "ask before dependency" → the §2.4 list.<br>Commit `S-RUN — run mode` | 0.1 |
| 0.2 | **S-02b** | DMS alignment, exactly as Architecture-alignment-DMS.md §6 (SharedKernel; kind-first Domain; Features/DTOs/Mapping/Interfaces in Application; Repositories + UnitOfWork + `IApplicationDbContext`; `IEndpoint` modules replacing `IV1EndpointContributor`; Contracts merged; `DependencyInjection` chain `AddApi → AddInfrastructure → AddApplication → AddDomain`). Behaviour unchanged. **Existing tests adapted, all green.** Rewrite CLAUDE.md's architecture section to "DMS + (K) exceptions" | 1.75 |
| 0.3 | **S-03a-lite** | `.github/workflows/ci.yml`: build `-warnaserror`, format, existing tests on Testcontainers `postgres:18` (Docker exists on GitHub runners), `dotnet list package --vulnerable`. No Dockerfile, no deploy (hosting is later) | 0.5 |

**Phase 0 PR:** "Phase 0 — run mode, DMS alignment, CI".

### Phase 1 — identity (plan 14 §6)

| # | Session | Modules (DMS) | Endpoints | Jobs | Est |
|---|---|---|---|---|---|
| 1.1 | **S-04** Customer OTP identity | `Features/CustomerAuth` (RequestOtp, VerifyOtp with the cart-merge hook, RefreshSession, Logout, LogoutAll); `OtpErrors`, `SessionErrors`; `ICustomerRepository`, `IOtpChallengeRepository`, `IRefreshTokenRepository`; `IOtpHasher`, `IJwtIssuer`, `ISmsSender` (+ `SinkSmsSender`, `/dev/sms`); `CustomerAuthEndpoints` `/v1/auth`; cookie `t3s_rt`; rate policies `otp-mobile`, `otp-ip` | #16–#20 | `PruneOtp` | 2.0 |
| 1.2 | **S-05** Staff identity and roles | `Features/StaffAuth` (Login, EnrolTotp, VerifyTotp, Refresh, Logout), `Features/Staff` (CRUD, reset password/TOTP, unlock); `Domain/Constants/{Roles, Policies}`; `StorePolicies` with **segregation of duties** (RUL-62); `ICurrentActor` from claims; finish the `create-admin` CLI | #47–#52, #123–#128 | — | 1.75 |

**Phase 1 PR:** "Phase 1 — identity".

### Phase 2 — catalogue (§7)

| # | Session | Modules | Endpoints | Est |
|---|---|---|---|---|
| 2.1 | **S-08** Public catalogue | `Features/Catalog/Queries/*` (Home, ListProducts incl. `fields=slug`, GetBySlug, LiveFigures, Updates, GiftFeed, Config); Arabic search with `ar_normalize`; ETag/304 and cache headers; `CatalogEndpoints` | #1–#7, #9 | 2.0 |
| 2.2 | **S-09** Admin catalogue and settings | `Features/AdminCatalog/{Products, Courses, Books, Causes, Offers, Categories, Goals}`, `Features/Media`, `Features/Settings` (typed `setting.value` → owned JSON); `IFileStorage` + `LocalFileStorage`; audit via `ICurrentActor` | #54–#77, #118, #119 | 2.5 |

**Phase 2 PR:** "Phase 2 — catalogue".

### Phase 3 — cart, checkout, settlement, documents (§8)

| # | Session | Modules | Endpoints | Jobs / effects | Est |
|---|---|---|---|---|---|
| 3.1 | **S-13** Cart | `Cart` aggregate (RUL-11…19, 21); `cart_line.dedication` → owned JSON; guest cookie `t3s_cart`; fill the S-04 merge hook; `CartEndpoints` | #10–#15 | `ExpireGuestCarts` | 1.5 |
| 3.2 | **S-15** Checkout | `Features/Checkout` (Quote, PlaceOrder); `VatCalculator`; reference `T3-YY-NNNNNN`; conditional stock reservation; `Idempotency-Key` filter on `idempotency_key`; status token; `orders.shipping_address` → owned JSON; `IPaymentInitiator` + `NullPaymentInitiator` | #40–#42 | — | 2.25 |
| 3.3 | **S-16** Settlement core ★ | `IOrderSettlement` (15 §2) + `SettleOrder` orchestrator; `ILineSettlementEffect` registry; goal counters, donor count, zakat `in`, dedication and order-paid outbox rows, document number reservation; `Order.MarkPaid` / `Expire` (RUL-29/30/35); `settlement.effect_missing` guard; `DevEndpoints` with `POST /dev/orders/{ref}/confirm-payment`; admin cancel | #87 + dev trigger | `ExpireUnpaidOrders`; gift/donate effects | 2.0 |
| 3.4 | **S-17** Outbox, notifications, documents | `OutboxDispatcher` (SKIP LOCKED, backoff, poison → alert); handlers `OrderPaid`, `DedicationCard` (stub), `RenderDocuments`, `FinanceAlert`; Scriban templates; `notification` once-only; `SmtpEmailSender`; `DocumentNumberAllocator`; QuestPDF invoice + donation receipt; resend | #88 | `OutboxDispatcher` | 2.5 |

Integrity check after S-16. **Phase 3 PR:** "Phase 3 — cart to settlement".

### Phase 4 — fulfilment (§9)

| # | Session | Modules | Endpoints | Jobs / effects | Est |
|---|---|---|---|---|---|
| 4.1 | **S-19** Enrolment + LMS | `ILmsGateway` + `InternalAccessLmsGateway`; `CourseEnrolmentEffect`; `pending_sync` retry via outbox; launch URL | #29 | effect | 1.5 |
| 4.2 | **S-20** Digital downloads | `DigitalBookEffect`; signed links; `PdfSharpWatermarker`; `download_log`; staff reset | #32, #46, #94 | effect | 1.5 |
| 4.3 | **S-21** Shipments, stock, admin orders | `PrintBookEffect`; shipment state machine; packing slip; stock list/movements/adjust; admin order search/detail; fulfilment roll-up (RUL-40); `shipment.address` → owned JSON | #78–#80, #85, #86, #95–#99 | effect | 2.5 |
| 4.4 | **S-40** LMS progress sync | Hourly job; the sponsored-unit hook stays a no-op until S-25 | — | `LmsProgressSync` | 1.0 |
| 4.5 | **S-41** Low stock alert | Daily 08:00 Riyadh, once per format per day | — | `LowStockAlert` | 0.5 |

Integrity check after S-21. **Phase 4 PR:** "Phase 4 — fulfilment".

### Phase 5 — giving (§10)

| # | Session | Modules | Endpoints | Jobs / effects | Est |
|---|---|---|---|---|---|
| 5.1 | **S-23** Units and pooling ★ | `SponsorUnitsEffect`; `PoolContributionEffect` with the FIFO conversion loop (database/01 §4.5), `unit_funding`, zakat propagation; remove the `settlement.effect_missing` guard | — | 2 effects | 1.5 |
| 5.2 | **S-24** Beneficiary register | Encrypted PII, `mobile_hash`, code sequence, eligibility transitions, masked DTOs, audited reveal, CSV import with dry-run, template, invite | #100–#108 | — | 1.5 |
| 5.3 | **S-25** Allocation ★ | Backlog, FIFO list, candidates; all-or-nothing `AllocateUnits` with `FOR UPDATE SKIP LOCKED`; customer created at allocation; zakat `out`; batch view; manual complete; fill the S-40 unit hook | #109–#114 | `AllocationAlerts`, `LapseUnits` | 2.75 |
| 5.4 | **S-26** Dedication cards, cause updates | Finish the `DedicationCard` handler; public card (no amount, `noindex`); resend; cause updates with notify-once | #45, #81–#84, #93 | — | 1.5 |
| 5.5 | **S-42** Close campaigns, sweep, corrections | `CloseCampaigns` job and offer-close sweep as `goal_adjustment` pairs; zakat never swept; finance corrections | #71 (sweep), #136, #137 | `CloseCampaigns` | 1.0 |
| 5.6 | **S-43** Zakat ledger, vouchers | `v_zakat_balance` view endpoint; vouchers rejected above balance | #115, #116 | — | 0.75 |

Integrity checks after S-23, S-25 and S-42. **Phase 5 PR:** "Phase 5 — giving".

### Phase 6 — account, reporting, operations (§11)

| # | Session | Modules | Endpoints | Jobs | Est |
|---|---|---|---|---|---|
| 6.1 | **S-31** `/v1/me` | Profile, addresses, enrolments, certificate, books/downloads, giving summary/detail (group and region only), orders, documents, export, delete/anonymise | #21–#28, #30, #31, #33–#39 | — | 2.0 |
| 6.2 | **S-33** Dashboard, reports, admin ops | Dashboard, 5 reports as JSON + CSV (UTF-8 BOM), audit query, customer lookup/block | #53, #117, #129–#133 | — | 1.5 |
| 6.3 | **S-35** Templates, retention, integrity | Template admin with Scriban validation; jobs; debounced web rebuild trigger (config URL, no secret) | #120–#122 | `IntegrityChecks`, `Retention`, `PruneIdempotencyKeys` | 1.0 |
| 6.4 | **S-45** Sponsor digest | Monthly, idempotent, no beneficiary identity | — | `SponsorDigest` | 1.0 |

**Phase 6 PR:** "Phase 6 — account and operations".

### Phase 7 — close-out (still in the run)

| # | Step | Content |
|---|---|---|
| 7.1 | **Blocked sessions** | Retry every session marked `blocked`, with what later sessions added |
| 7.2 | **Full smoke scenario** | From an empty dev DB: seed → guest cart (mixed prototype cart) → OTP sign-in → checkout → dev trigger → documents → enrolment/download/shipment → sponsorship pooling → allocation → digest dry-run. Then run the integrity checks: 0 rows |
| 7.3 | **Coverage audit** | Compare the mapped routes with 11 §12. All 130 endpoints must answer (not 404/405). Check the 13 jobs and every settlement effect |
| 7.4 | **Final report** | Write `store/sessions/Part-1-report.md`: phases and PRs, endpoints covered, `DECISIONS.md` summary with every `REVIEW` item, known gaps, and the proposed test order for the test phase (S-16, S-23, S-25 first, then the RUL list of plan 14 §13.1) |

**The run ends** when the Part 1 checklist in `PROGRESS.md` is fully ticked and the report is pushed.

### Dependency guard (for skipping a blocked session)

| Session | Needs | Session | Needs |
|---|---|---|---|
| S-04, S-05, S-08 | S-02b | S-19, S-20, S-21 | S-16 |
| S-09 | S-05, S-08 | S-40 | S-19 |
| S-13 | S-04, S-08 | S-41 | S-21 |
| S-15 | S-13 | S-23 | S-16 |
| S-16 | S-15 | S-24 | S-05 |
| S-17 | S-16 | S-25 | S-19, S-23, S-24 |
| S-26, S-33, S-35 | S-17 | S-42 | S-23 |
| S-31 | S-17 (+ effects) | S-43, S-45 | S-25 |

---

## 5. After the run (not part of it)

| Phase | Content |
|---|---|
| **Test phase** | Every RUL test (plan 14 "Tests" per session), the policy matrix, concurrency and property-based tests for S-16/S-23/S-25, endpoint integration tests, `ImplementedRules.txt` filled. Starts from `Part-1-report.md` |
| **Hosting** | S-03 deploy half (Dockerfile, compose, Caddy, `deploy.yml`, VPS), hardening, UAT, cutover (S-37…S-39) |
| **Payment service** | Doc 15 |

---

## 6. Prompts

### 6.1 Preparation prompt (first message of a new cloud session; skip if the setup script ran and you're resuming)

```text
Prepare this cloud session for the ta3lom store API build. Don't write application code in this step.
1. Report versions of git, dotnet and psql, and whether Docker is available.
2. If dotnet 10 is missing: sudo apt-get install -y dotnet-sdk-10.0. If PostgreSQL isn't running:
   start it (pg_ctlcluster 16 main start), set the postgres password to "postgres" (local throw-away DB),
   create database ta3lom_store_dev, and export STORE_TEST_DB=external and
   STORE_TEST_DB_CONNECTION="Host=localhost;Username=postgres;Password=postgres".
3. Clone lotfi029/ta3lom_store_api (main). If access fails, report what access is needed; never ask me
   for a token in the chat.
4. Check that store/sessions/Cloud-build-plan.md and store/sessions/Architecture-alignment-DMS.md exist.
5. Clone https://github.com/lotfi029/DMS read-only into a scratch folder.
6. Run: dotnet build Ta3lom.Store.slnx -warnaserror, then dotnet test --solution Ta3lom.Store.slnx.
   Report the test count and result.
7. Answer as a table: item | ready / blocked | what I must do. End with one line:
   READY (steps 2–6 succeeded), or BLOCKED and the smallest list of actions for me.
Never try to bypass the network allowlist.
```

### 6.2 Start prompt (paste once, after READY)

```text
Build the whole ta3lom store backend now, following store/sessions/Cloud-build-plan.md in this repo.
Plan 14 (store/14-backend-implementation-plan.md) says WHAT each session builds; Cloud-build-plan.md
says HOW this run works and overrides CLAUDE.md where they differ. The architecture is my DMS repo
(https://github.com/lotfi029/DMS, clone read-only) as mapped in store/sessions/Architecture-alignment-DMS.md,
keeping the store (K) exceptions.

Rules for this run:
- Never end your turn to report, ask or wait. Work session after session until the Part 1 checklist in
  PROGRESS.md is ticked and store/sessions/Part-1-report.md is pushed. There are no stop conditions:
  use the defaults in §2.2–§2.4 and log every choice in DECISIONS.md.
- No new tests (§2.5). Keep every existing test, the architecture tests and schema parity green; adapt
  existing tests only when a refactor moves types. Keep ImplementedRules.txt empty.
- No hosting: no Dockerfile, no deploy, no VPS. CI only as in S-03a-lite.
- No payment code (plan 14 §0): NullPaymentInitiator + the dev confirm-payment trigger.
- Never edit a merged migration, never delete data outside the dev DB, never add secrets.
- Git: work on this session's branch, one commit set per S-nn ("S-nn — title"), push after each, one PR
  per phase (§2.6). If you can merge, squash-merge when green; if not, continue on the same branch.
- End every S-nn with the smoke check (§2.7) and an updated PROGRESS.md whose first line names the next
  step. If your context gets compacted, re-read PROGRESS.md, DECISIONS.md and §2 of the plan, then continue.

Order: S-RUN → S-02b → S-03a-lite → S-04 → S-05 → S-08 → S-09 → S-13 → S-15 → S-16 → S-17 → S-19 →
S-20 → S-21 → S-40 → S-41 → S-23 → S-24 → S-25 → S-26 → S-42 → S-43 → S-31 → S-33 → S-35 → S-45 →
close-out (§4 Phase 7). For each session, read its plan 14 section, the RULs, the 11 endpoints and the
database/01 sections it names, then build it layer by layer (Domain → Application → Infrastructure → Api)
in the DMS layout of §3.2.

Start now with S-RUN.
```

### 6.3 Resume prompt (any new session after an interruption)

```text
Resume the ta3lom store backend run. Read CLAUDE.md, store/sessions/Cloud-build-plan.md (§2 run rules),
PROGRESS.md and DECISIONS.md on the working branch (or main if the branch was merged). Continue from the
"Next:" line of PROGRESS.md with the same rules: never stop to ask, no new tests, no hosting, no payment
code, one commit set per S-nn, smoke check after each, PROGRESS.md always current. Don't redo sessions
marked done.
```

---

## 7. What you do during the run

| When | Action |
|---|---|
| Before | P1 (merge the docs PR), P2 (environment), P3 (preparation prompt → READY) |
| Start | Paste §6.2 |
| If the session ends | Open a new session on the same environment and paste §6.3 (§6.1 first if the environment has no setup script) |
| Every few days (optional) | Read `PROGRESS.md` and `DECISIONS.md` on the branch; merge phase PRs if the session can't; reply with corrections to any `REVIEW` item. The run doesn't wait for you |
| At the end | Read `Part-1-report.md`; then start the test phase |
