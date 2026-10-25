# 03: Store API rebuild plan, R5-Core (one session)

**Repo:** `lotfi029/ta3lom_store_api` · base `main` @ `ec3365b` · **branch `rebuild/r5-core`** · one PR
**Inputs:**

- [01-api-business-requirements.md](01-api-business-requirements.md) (BR-A01…A13, D-01…D-14, NFR);
- [02-api-erd.md](02-api-erd.md).

**Prompt:** [04-api-session-prompt.md](04-api-session-prompt.md)
**Out of scope (next session, R5-Payments):** gateway, webhook, `payment*`, settlement, receipts, totals, refunds, reconciliation, the expiry job, finance reports.

---

## 0. Strategy

1. **The store isn't live**, so we **rebuild on the existing platform skeleton** instead of editing feature by feature. Delete all feature code and both migrations, keep the proven platform code, then build the four modules again against the new ERD with one fresh `InitialSchema`.
2. **Port, don't rewrite**, code the reviews found sound: OTP hashing, refresh-token rotation, TOTP, the outbox, the unit of work, media processing, Arabic search, the idempotency filter, problem details, security headers and rate limiting. Fix every review finding listed in §6 while porting.
3. **The repo keeps code only.** Specs for the session live in `.session/` **only on the branch** and are deleted in the last phase, before the PR is marked ready.

---

## 1. Phase P0: preparation (owner, before the session)

| # | Action |
|---|---|
| 0.1 | Discard the uncommitted dead-code clean-up on your PC: `git restore . && git clean -n` (check the list), then `git clean -f`. The rebuild deletes that code anyway |
| 0.2 | **Archive the repo docs to your Docs folder**, because they're about to be deleted from the repo. Copy `store/`, `PROGRESS.md`, `DECISIONS.md`, `db/reference/`, `tools/smoke/` and `spikes/` to `D:\Freelance\ELAMS\Docs\store\archive\api-repo-2026-10-25\` |
| 0.3 | Create the branch and the session folder:<br>`git checkout main && git pull`<br>`git checkout -b rebuild/r5-core`<br>`mkdir .session`<br>copy **01-api-business-requirements.md**, **02-api-erd.md** and **03-api-rebuild-plan.md** into `.session/`<br>`git add .session && git commit -m "session specs (removed before merge)"`<br>`git push -u origin rebuild/r5-core` |
| 0.4 | Open a Claude Code **cloud session** on that branch (.NET 10 SDK and Docker available, like the 30 Sep run) and paste the prompt from [04](04-api-session-prompt.md) |

---

## 2. Phase P1: clean the repo (code only)

**Delete:**

| Path | Why / follow-up |
|---|---|
| `spikes/` | Finished experiments |
| `openapi/` | Snapshot file. Delete `tests/…/Contract/OpenApiSnapshotTests.cs`. OpenAPI stays available at runtime in Development (`/openapi/v1.json` + Scalar) |
| `deploy/` | Only `env/store.env.example`. Move the variable list into README §Configuration |
| `store/` | All docs (archived in P0.2). Remove the `None Include="../../store/*.md"` links from `ArchitectureTests.csproj`, plus the tests that read them (`RuleCoverageTests`, `ImplementedRules.txt`) |
| `db/` | Reference DDL. Move `003_integrity_checks.sql` into `src/Ta3lom.Store.Infrastructure/Resources/Integrity/` (rewritten for the new schema in P3). Remove the `None Include="../../db/reference/**"` from `IntegrationTests.csproj`, plus `SchemaParityTests` / `StoreSchema.cs` |
| `tools/` | `.http` smoke files, scripts and the load script |
| `PROGRESS.md`, `DECISIONS.md` | History (archived). The session tracks progress in `.session/PROGRESS.md` |
| `artifacts/` (if present), `**/bin`, `**/obj` | Untracked, already in `.gitignore` |

**Keep:**

- **Code:** `src/`, `tests/`.
- **Solution and build config:** `Ta3lom.Store.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.config/dotnet-tools.json`.
- **Analyzers and repo settings:** `BannedSymbols*.txt`, `.editorconfig`, `.gitattributes`, `.gitignore`.
- **CI:** `.github/workflows/ci.yml`.
- **`README.md`:** rewritten, short (run, configure, test).
- **`CLAUDE.md`:** rewritten in P8: architecture rules and conventions only, **no links to docs**.

**Gate P1:** `dotnet build -warnaserror` passes (tests may still fail until P2).

---

## 3. Phase P2: strip to the platform skeleton

### 3.1 Keep (port and adjust)

| Layer | Keep |
|---|---|
| SharedKernel | `Entity`, `AggregateRoot`, `Result`, `Error` (+ the factories in use), `ValidationError`, `Guard`, `IDomainEvent`, `Unit`, `DomainException` |
| Domain | `ValueObjects/Money`, `Errors/ErrorCodes` (trimmed), `Constants/{Policies,Roles,SessionPolicy}`, `IRepositories/{IGenericRepository,IUnitOfWork}`; entities `Customer`, `OtpChallenge`, `RefreshToken`, `StaffUser`, `MediaAsset`, `Setting`, `AuditLogEntry`, `OutboxMessage`, `MessageTemplate`, `Notification`, `Category` (adapt to the ERD) |
| Application | `Abstractions/*` (CQRS interfaces, Logging and Validation decorators, `IClock`, `IApplicationDbContext`), `DependencyInjection` (Scrutor scan), Mapster base, `Interfaces/*` for SMS, email, files, tokens, OTP hasher, idempotency, outbox, current actor, web links; features `CustomerAuth`, `StaffAuth`, `Staff`, `Media`, `Settings`, `Notifications`, `Outbox` (OTP and alert handlers only) |
| Infrastructure | `StoreDbContext`, `UnitOfWork`, interceptors (audit, outbox), `GenericRepository`, identity repositories, `Services/{Security,Sms,Email,Files,Health,Idempotency,Outbox,Settings,Templates,Time,Catalog(search)}`; jobs `PruneOtp`, `PruneIdempotencyKeys`, `ExpireGuestCarts`, `Retention`, `IntegrityChecks` |
| Api | `Program`, `DependencyInjection`, `Extensions/*` (CORS, cookies, cart cookie, idempotency, origin check, logging, rate limits, security headers, forwarded headers, public files, HTTP caching), `Infrastructure/*` (problem details, global exception handler, error messages + `.resx`), `Cli/CreateAdminCommand`, endpoints `Health`, `CustomerAuth`, `StaffAuth`, `Staff`, `Dev` (trimmed) |
| Tests | `Ta3lom.Store.Testing` (`FakeClock`, `RuleAttribute`), integration infrastructure (Testcontainers fixture, scenario host, builders: adapt), architecture `LayerTests`, `DependencyPolicyTests`, `DomainModelTests`, `ErrorCodeCatalogueTests` |

### 3.2 Delete

- **Domain:** everything else (catalogue products, carts, ordering, documents, fulfilment, giving, payments).
- **Features:** `Account` (rewrite in P6), `AdminCatalog`, `AdminOps`, `Allocation`, `Beneficiaries`, `Cart`, `Catalog`, `Checkout`, `Dev` (rewrite), `Documents`, `Downloads`, `Enrolments`, `Fulfilment`, `Giving`, `Orders`, `Settlement`.
- **Infrastructure services:** `Documents` (QuestPDF + fonts: they come back in R5-Payments for receipts, so delete them now and re-add then), `Lms`, `Payments`, `SiteRebuild` (re-added in P5), and every job except those kept above.
- **Persistence:** all migrations and all configurations for deleted entities.
- **Tests:** every feature test project folder (re-created per module).
- **Packages:** remove the unused ones from `Directory.Packages.props` (e.g. QuestPDF, PdfSharp).

**Gate P2:** a green build and green architecture tests on an **empty model** (no migration yet).

---

## 4. Phase P3: data model + `InitialSchema`

1. Create the entities, enums and EF configurations exactly as in [02-api-erd.md](02-api-erd.md) §2: 19 tables, `order_ref_seq`, `v_catalog_stats`.
2. Add the extensions and functions in the migration: `citext`, `pg_trgm`, `unaccent` (if used), and `ar_normalize()` (port the SQL function from the archived DDL); `uuidv7()` isn't needed because ids are generated in .NET.
3. Add one migration, `InitialSchema`. Add a test that **applies the migration on `postgres:18`** and asserts that `dotnet ef migrations has-pending-model-changes` reports none. This replaces schema parity.
4. **Seeders** (idempotent):
   - settings (ERD §2.4) and the OTP SMS templates (ar/en);
   - **Development only:** 3 categories, 6 projects with options (shaped like the dsenaiah examples), 1 admin (`CreateAdminCommand`).
5. Rewrite the integrity checks for the new schema:
   - `order.total_amount = Σ lines`;
   - `cart_line` and `order_line` target CHECKs hold;
   - no published project without an active option or an open amount.

**Gate P3:** the migration applies cleanly, the seed runs twice without errors, and the domain tests for the entity invariants pass.

---

## 5. Phases P4–P7: modules

**Every module:**

- **Code shape:** domain behaviour lives in the aggregates; one file per command or query with its handler and validator; DTOs as sealed records (money as strings); Mapster configs; thin endpoints.
- **Errors:** each error is added to the catalogue and to both `.resx` files.
- **Policy-matrix row:** every admin route gets a row in the policy-matrix test, which checks each role and anonymous.
- **Tests:** unit tests for the rules (tagged `[Rule("D-xx")]`) plus integration scenarios.

### P4: Auth (BR-A09, A11, A13 · D-10…D-14)

| # | Endpoint | Notes |
|---|---|---|
| 1 | `POST /v1/auth/otp/request` | Advisory lock per mobile hash. Per-mobile, per-IP and global caps; alert at 50 % of the global cap. Same response whether the account exists or not. `Retry-After` header on 429 |
| 2 | `POST /v1/auth/otp/verify` | Creates the customer + **PDPL consent** on first sign-in. Merges the guest cart. Returns an access token + refresh cookie |
| 3 | `POST /v1/auth/refresh` | Rotation, **absolute family expiry**, reuse revokes the family, 409 on a race |
| 4 | `POST /v1/auth/logout`, `POST /v1/auth/logout-all` | |
| 5 | `POST /v1/admin/auth/login` | Checks the password before the lock state (no enumeration); a 15-min failure window; audits the outcome |
| 6 | `POST /v1/admin/auth/totp/enrol`, `/totp/verify` | `totp_last_step` replay guard; the MFA token is single-use (jti); re-enrolling is allowed until verified |
| 7 | `POST /v1/admin/auth/password` | **New.** Change password (old + new). Required when `must_change_password` is set; the access token carries a `pwd_change` claim that limits the staff member to this route |
| 8 | `POST /v1/admin/auth/refresh`, `/logout`, `GET /v1/admin/me` | |
| 9 | `GET/POST/PUT /v1/admin/staff`, `POST …/{id}/reset-password` · `reset-totp` · `unlock` | **Last-admin guard**; roles `admin`, `store_manager`, `finance`, `viewer` |

Security events written to `audit_log`: `staff.login_succeeded`, `staff.login_failed`, `staff.locked`, `staff.totp_failed`, `token.reuse_detected`, `customer.blocked`.

### P5: Catalogue (BR-A01…A06 · D-05…D-08)

| Public | Admin |
|---|---|
| `GET /v1/catalog/home`: featured projects, categories with an active project count, amount presets | `GET/POST /v1/admin/categories`, `PUT …/{id}` (with `version`), `PUT /v1/admin/categories/order` |
| `GET /v1/catalog/categories` | `GET /v1/admin/projects` (filters: status, category, q; paged) |
| `GET /v1/catalog/projects?q&category&sort&page&pageSize`: filters **in SQL**, LIKE escaped, an unknown category returns an empty page | `POST /v1/admin/projects`, `GET/PUT …/{id}` (`version` → 409) |
| `GET /v1/catalog/projects/{slug}`: if the slug is old, returns `301`-style `{ redirectTo }` | `PUT /v1/admin/projects/{id}/options` (replaces the list; options already used in an order are deactivated, not deleted) |
| `GET /v1/catalog/projects?fields=slug` (prerender); unknown `fields` → 400 | `POST …/{id}/publish` (D-06 → 422 `project.publish_incomplete` + `missing[]`), `POST …/{id}/archive`, `GET …/{id}/preview` |
| `GET /v1/catalog/config`: minimum gift, presets, limits | `POST/GET /v1/admin/media`, `PATCH …/{id}` (alt text); pixel limit, magic-byte sniffing, temp-file cleanup |

**Caching:**

- **ETag:** the hash of the response body.
- **Output cache:** varies by known query keys only, and is evicted by the `catalog` tag on every write to a category, project or option.

**Site rebuild:** a `CatalogChanged` outbox message (debounced **only on unclaimed rows**) triggers a GitHub `repository_dispatch` to the web repo. Re-add the `SiteRebuild` service from the old code. The token comes from configuration.

### P6: Donations (BR-A07, A08, A10 · D-01…D-05, D-09, D-14)

| # | Endpoint | Notes |
|---|---|---|
| 1 | `GET /v1/cart`, `GET /v1/cart/summary` | Guest cart cookie (HttpOnly) or the donor's cart. The response carries indicative amounts and per-line `problems[]` (unpublished project, inactive option, below the minimum) |
| 2 | `POST /v1/cart/lines` | Body: `{ projectId, optionId, quantity }`, `{ projectId, amount }` or `{ categoryId, amount }`. Option lines merge |
| 3 | `PATCH /v1/cart/lines/{id}`, `DELETE /v1/cart/lines/{id}`, `DELETE /v1/cart` | Limits D-03, D-04 |
| 4 | `POST /v1/checkout/quote` | Re-prices from the database; returns the lines, total and problems |
| 5 | `POST /v1/checkout` | `Idempotency-Key` required. The scope is the customer. **A 409 while in progress keeps the key.** Creates `orders` + `order_line` snapshots in `pending_payment`, `payment_due_at = now + 60 min`, clears the cart, queues `OrderPlaced`. Response: `{ reference, status, totalAmount, statusToken, payment: null }` (filled in by R5-Payments) |
| 6 | `GET /v1/orders/{reference}/status?t=` | Donor or token holder |
| 7 | `GET/PATCH/DELETE /v1/me`, `GET /v1/me/export` | Delete = anonymise (D-14). The status must be `active` for every write |
| 8 | `GET /v1/me/donations`, `GET /v1/me/donations/{reference}` | Orders + lines (receipts arrive next session) |

### P7: Admin operations (BR-A12, A13)

| Endpoint | Roles |
|---|---|
| `GET /v1/admin/dashboard`: counts (published projects, categories, donors, pending orders today and this week, top projects by **pending** amount). Paid totals arrive in R5-Payments | all staff |
| `GET /v1/admin/customers`, `GET …/{id}`, `POST …/{id}/block`, `/unblock` (revokes tokens) | read: all staff · block: admin, store_manager |
| `GET /v1/admin/orders` (search by reference, mobile, status, date), `GET …/{reference}`, `POST …/{reference}/cancel` (pending only) | read: all staff · cancel: admin, finance |
| `GET /v1/admin/settings`, `PUT …/{key}` (typed allow-list) | admin |
| `GET /v1/admin/templates`, `PUT …/{key}/{channel}/{locale}`, `POST …/test` | admin, store_manager |
| `GET /v1/admin/audit` | admin |
| Dev (Development only): `GET /dev/sms`, `GET /dev/mail`, `POST /dev/jobs/{name}` | — |

---

## 6. Phase P8: hardening (the review findings carried into the rebuild)

| From | Item | Done in |
|---|---|---|
| A-F1 | CORS / Origin allow-list = `store.ta3lom.sa`, `store-staging.ta3lom.sa`; `WebBaseUrl` = the store host | P8 |
| A-1/A-2 | `/dev` mapped in **Development only**; every environment except Development refuses DEV or missing keys | P8 |
| A-8 | Fallback authorization policy = authenticated; public routes `.AllowAnonymous()` | P8 |
| A-F2…F15 | Auth fixes | P4 |
| A-3, A-6 | OTP alerting + advisory lock | P4 |
| A-7 | Outbox: lease per row, dead-letter by attempt count, provider idempotency key | P2 (port) |
| C-2, C-3, C-N6 | Search escape, filters in SQL, unknown category | P5 |
| C-5, C-12, C-13 | Cover kind on every write, publish guard, amounts > 0 | P5 |
| C-N1, C-N5 | Rebuild debounce, notify on every write, slug history | P5 |
| C-N3, C-N4 | 23505/23503 mapping, `xmin` + `version` → 409 | P3/P5 |
| C-N7, C-N10 | Image pixel limit, temp-file and orphan cleanup | P5 |
| C-N14 | Output cache varies by known keys | P5 |
| W-1 (web) | The API keeps returning 409 `idempotency.in_progress` until it's done | P6 |

Then:

- **`README.md`** (short): run, configure (the variable list from the old `deploy/env`), test, create an admin.
- **`CLAUDE.md`:**
  - architecture table and layout;
  - conventions;
  - quality gates;
  - "payments come in R5-Payments: keep `IPaymentInitiator` absent";
  - **no doc links**.

---

## 7. Phase P9: verification and close-out

| Gate | Requirement |
|---|---|
| Build | `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, no High/Critical vulnerable packages |
| Tests | All green on `postgres:18`. Coverage: Domain ≥ 90 %, Application ≥ 80 %. Every D-01…D-14 rule has at least one `[Rule]` test |
| Policy matrix | Every admin route × 4 roles + anonymous |
| Smoke (scripted in an integration test) | Seed → guest adds an option line + an amount line + a category line → OTP sign-in (code read from the SMS sink) → cart merge → quote → checkout (with a replay using the same key) → status → staff login with TOTP → change password → cancel the order → audit rows exist |
| Repo | `git ls-files` shows **only** the keep-list paths in §2. A grep finds no `course`, `book`, `sponsor`, `zakat`, `beneficiar`, `allocation` or `shipment` in `src/` |
| Clean-up | **Delete `.session/`** in the final commit. The PR description holds the summary: phases, decisions taken (anything not covered by 01/02/03, each marked `REVIEW`), test counts and coverage, and what's left for R5-Payments |

---

## 8. Estimate and order

| Phase | Agent effort (dev-day equivalents) |
|---|---|
| P1–P2 clean + strip | 0.75 |
| P3 model + migration + seed | 1 |
| P4 auth | 1.5 |
| P5 catalogue | 2 |
| P6 donations | 1.75 |
| P7 admin ops | 1 |
| P8–P9 hardening, tests, close-out | 1.5 |
| **Total** | **≈ 9.5** (one autonomous session; checkpoints after each phase) |

**Next session, R5-Payments (≈ 6 d):**

- **Payments:** gateway adapter (after Q-01), webhook + `payment_event`, settlement (`paid` → `giving_total`).
- **Receipts:** DR receipt PDF (QuestPDF + IBM Plex Arabic) + SMS and email.
- **Jobs and refunds:** expire unpaid orders, reconcile payments, refunds + DRC.
- **Reporting:** finance reports CSV, dashboard totals, `payment_review` resolution.
