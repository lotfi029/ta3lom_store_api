# Build report — store backend, full cloud run

Branch `claude/funny-wright-yum3nm`, draft PR lotfi029/ta3lom_store_api#8 "Store backend — full build + tests".
Run rules: `store/sessions/Cloud-build-plan.md` §2, §4b, §4c; state in [PROGRESS.md](../../PROGRESS.md), every
choice in [DECISIONS.md](../../DECISIONS.md). Nothing was merged by the session; the PR is the whole run.

**Result:** every plan 14 session from S-04 to S-45 is built, and the close-out, H-1 and the test phase T-1…T-9 are
done. No session is blocked. All 130 endpoints answer. All 12 scheduled jobs and the outbox dispatcher, the 6
settlement effects and 23 outbox handlers are registered. Integrity checks return 0 rows after the full scenario
from an empty database. 54/54 backend rules have `[Rule]` tests. 803 tests are green.
Coverage: Domain 100 %, Application 85.9 %.

---

## 1. Sessions and commits

| Phase | Session | Commit | Endpoints (plan 14 §13.2 #) | Jobs / effects / outbox |
|---|---|---|---|---|
| 0 | S-RUN run mode | c00e900 | — | — |
| 0 | S-02b DMS alignment (refactor only) | 2868161 | — | — |
| 0 | S-03a-lite CI | e96bd7d | — | `.github/workflows/ci.yml` |
| 1 | S-04 customer OTP | f4a8ed8 | 16–20 | PruneOtp |
| 1 | S-05 staff identity | f7256e1 | 47–52, 123–128 | `create-admin` CLI |
| 2 | S-08 public catalogue | f1b1ad9 | 1–7, 9 | — |
| 2 | S-09 admin catalogue + settings | 59e045a | 54–77, 118, 119 | — |
| 3 | S-13 cart | cb83b97 | 10–15 | ExpireGuestCarts |
| 3 | S-15 checkout | 8e9ee12 | 40–42 | — |
| 3 | S-16 settlement core ★ | a95604f | 87 + `/dev/orders/{ref}/confirm-payment` | ExpireUnpaidOrders; DonateLineEffect |
| 3 | P0-3 Arabic PDF spike | df8ecbc | — | GO (`store/spikes/P0-3-arabic-pdf.md`) |
| 3 | S-17 outbox + documents | 7105977 | 88 | OutboxDispatcher; OrderPaid, RenderDocuments, FinanceAlert, lifecycle |
| 4 | S-19 enrolment + LMS | 6714ee8 | 29 | CourseEnrolmentEffect; LmsEnrol, LmsRevoke |
| 4 | P0-4 watermark spike | 47b7455 | — | GO (`store/spikes/P0-4-watermark.md`) |
| 4 | S-20 digital downloads | 4f34188 | 32, 46, 94 | DigitalBookEffect |
| 4 | S-21 shipments, stock, admin orders | 48c7581 | 78–80, 85, 86, 95–99 | PrintBookEffect; ShipmentShipped/Delivered |
| 4 | S-40 LMS progress | 8872dba | — | LmsProgressSync |
| 4 | S-41 low stock | 5d044f1 | — | LowStockAlert job + message |
| 5 | S-23 units + pooling ★ | e39acf3 | — | SponsorUnitsEffect, PoolContributionEffect |
| 5 | S-24 beneficiaries | c468bbf | 100–108 | — |
| 5 | S-25 allocation ★ | 4dba317 | 109–114 | AllocationAlerts, LapseUnits; 6 allocation handlers |
| 5 | S-26 cards + cause updates | 3e47e9b | 45, 81–84, 93 | DedicationCard, CauseUpdatePublished |
| 5 | S-42 close, sweep, corrections | 7b52e3b | 71 (sweep), 136, 137 | CloseCampaigns; PoolSwept |
| 5 | S-43 zakat ledger | 7a76334 | 115, 116 | — |
| 6 | S-31 `/v1/me` | 860cfed | 21–28, 30, 31, 33–39 | — |
| 6 | S-33 dashboard, reports, ops | fd080ee | 53, 117, 129–133 | — |
| 6 | S-35 templates, retention, integrity | 9f3e91c | 120–122 | IntegrityChecks, Retention, PruneIdempotencyKeys; CatalogChanged |
| 6 | S-45 sponsor digest | 21581ea | — | SponsorDigest job + message |
| 7 | Close-out 7.1–7.3 | 004f8d2, 1f0ddd6 | route audit 130/130 | `tools/smoke/e2e.sh` (68 checks from an empty DB) |
| 7 | H-1 code-side hardening | c6a9cd2 | — | `store/sessions/H-1-hardening.md` |
| T | T-1 test infrastructure | bfe55a8 | | |
| T | T-2 settlement | c9b2c94 | | |
| T | T-3/T-4 pooling + allocation | 4358224 | | |
| T | T-5 rule tests (7 commits) | d2acdd3 … eeba7d3 | | |
| T | T-6 policy matrix | 6eeda41 | | |
| T | T-7 endpoint contracts | 8c34038 | | |
| T | T-8 documents and outputs | e9f0bb6 | | |
| T | T-9 coverage | fddfffc, 1bdb0ee, (this commit) | | |

Milestone demos (plan 14 §2, run locally, `tools/smoke/M2.http` … `M7.http`): M2–M7 all passed. Details are in
PROGRESS.md.

## 2. Endpoint, job, effect and outbox coverage

| Area | Built | Evidence |
|---|---|---|
| Endpoints | 130/130 of §13.2: 128 routes of 11 §12 + 2 health. #43, #44, #89–#92 belong to the payment service (doc 15) | Close-out route audit; T-7 `The_OpenAPI_document_has_every_endpoint_of_11_section_12_that_this_backend_owns`; `openapi/v1.json` |
| `/dev/*` | `confirm-payment`, `/dev/sms`, `/dev/mail`, `/dev/lms/progress` (Development/Staging only) | T-7 `Dev_routes_are_not_mapped_in_production` (404) |
| Jobs | 12 scheduled + OutboxDispatcher (all of §13.3 except ReconcilePayments, which belongs to the payment service) | `Infrastructure/Services/Jobs/*`; each a thin shell calling one Application command |
| Settlement effects | CourseEnrolment, DigitalBook, PrintBook, SponsorUnits, PoolContribution, DonateLine | `Application/Features/Settlement/Effects/`; `settlement.effect_missing` guard |
| Outbox | 23 handlers: the 17 types of 09 §4.9 except RefundProcessed (payment service), + OrderPlaced, OrderCancelled, OrderExpired, DocumentsResendRequested, CatalogChanged, SponsorDigest | `Application/Features/Outbox/Handlers/` |

## 3. Tests

| Project | Tests | What |
|---|---|---|
| Domain.Tests | 368 | aggregates, value objects, domain services, FsCheck properties (pooling, RUL-47 allocation policy, goal math) |
| Application.Tests | 9 | `Result` and `ValidationError`, handler decorators (validation before the handler, logging without payload), DI wiring |
| Api.IntegrationTests | 396 | scenario classes on real PostgreSQL: settlement, giving, rule tests per RUL, policy matrix (560 calls), endpoint sweeps, documents (PDF/QR), account/staff/admin flows |
| ArchitectureTests | 30 | layer rules, money never floating point, only settlement marks paid (IL scan), rule coverage vs ImplementedRules.txt, schema parity |
| **Total** | **803** | all green locally (PostgreSQL 16) and in CI (postgres:18) |

Scenario classes (Api.IntegrationTests/Scenarios) run on `ScenarioFactory`. Each class gets its own migrated and
seeded database, a FakeClock, SMS and e-mail sinks, the outbox drained on demand, minted customer and staff tokens,
and settlement through the dev trigger.

Coverage (line rate, merged over Domain.Tests + Application.Tests + Api.IntegrationTests; method in DECISIONS T-9):

| Package | Lines covered | Rate | Threshold |
|---|---|---|---|
| Domain | 2136 / 2136 | **100.0 %** | ≥ 90 % ✅ |
| Application | 5513 / 6420 | **85.9 %** | ≥ 80 % ✅ |
| SharedKernel | 84 / 93 | 90.3 % | — |
| Infrastructure | 16136 / 16639 | 97.0 % (includes migrations) | — |
| Api | 2340 / 2697 | 86.8 % | — |

How to reproduce: `dotnet test --project <tests project> -- --coverage --coverage-output-format cobertura
--coverage-output coverage.cobertura.xml`, run for each of the three projects, then merge the reports per line.
ArchitectureTests are left out of coverage runs because instrumentation adds references that the layer test
rejects. The normal run keeps them green.

### RUL coverage

`tests/Ta3lom.Store.ArchitectureTests/ImplementedRules.txt` lists all 54 backend rules of plan 14 §13.1, and
`RuleCoverageTests` checks that each one has a `[Rule]` test. These rules belong to the payment service and are
excluded: RUL-32, 33, 34, 36, 37, 38, 56, 57, 58.

RUL-01 … RUL-31 · RUL-35 · RUL-39 … RUL-55 · RUL-59 … RUL-63.

### Bugs found by tests

| Test | Rule | Fix |
|---|---|---|
| `OperationsTests.An_invalid_template_is_rejected_at_save`: a template with an unclosed `{{` was saved | RUL-63 / 11 §9.18 | a34012d. `ScribanTemplateRenderer.Check` walks the lexer tokens and refuses the save with 422 `template.render_failed` |
| `OrderTests.Vat_on_a_gift_draft_never_reaches_the_order_total`: `Order.Place` summed VAT over every draft while lines store gift VAT as 0 (no live order affected; checkout sends 0) | RUL-27 | 1bdb0ee. `VatTotal` sums buy lines only |

Test-side defects fixed during the phase (the tests had misread the scenario, not the code):
- the FIFO lapse count depended on test order (59eff65);
- the outbox drain left rows that handlers enqueued during the drain (1bdb0ee);
- the last-copy race accepts either correct loser answer, 409 `checkout.out_of_stock` (RUL-30) or 422 `checkout.lines_invalid` with `cart.out_of_stock` (RUL-24).

Bugs found during the build's smoke checks were fixed in their session commits and are listed in PROGRESS.md:
- the audit actor (S-05);
- offer/cause dates in UTC and the money-setting wire format (S-09);
- document path resolution (S-17);
- a dispatcher crash (S-19);
- the RUL-40 returned line (S-21);
- a malformed JSON body answering 500, and `create-admin` in Development (close-out).

## 4. REVIEW items (DECISIONS.md)

Each of these is a default the run chose and logged. The owner confirms or changes each one:

| Session | Topic | Choice (short) |
|---|---|---|
| S-04 | Packages | JwtBearer, Scriban (early), IdentityModel.JsonWebTokens, Extensions.Http |
| S-04 | IP hashing key | IPs hashed with the mobile-hash key and an `ip:` prefix |
| S-04 | Signing keys outside Production | typed options; an empty key falls back to its DEV ONLY value (Production refuses both) |
| S-04 | SMS provider | sink by default; `UnifonicSmsSender` written against the REST API, keys empty |
| S-05 | Lockout window | 5 consecutive failures (password or TOTP) → 1 h |
| S-05 | One-time passwords | 16 unambiguous characters; valid until reset (no forced-change endpoint in 11) |
| S-08 | Typed settings | `StoreSettings` from `setting.value`, 60 s cache |
| S-08 | Home composition | featured = badge ≤ 4; impact figures; fixed 97 % |
| S-08 | Gift feed names | anonymous → "فاعل خير"; otherwise first name + last initial; no dedication text |
| S-09 | Media | WebP re-encode (q85, ≤ 2400 px) drops EXIF; PDFs ≤ 200 MB, magic checked |
| S-13 | Owned course | enrolment pending_sync/active/completed; unpaid buy line blocks |
| S-15 | No payment row | checkout inserts no `payment`; `NullPaymentInitiator` returns nulls |
| S-16 | Goal counters | atomic upsert per product per order |
| P0-3 | PDF stack | QuestPDF Community + QRCoder; IBM Plex Sans Arabic, Noto Naskh fallback |
| S-17 | Seller identity | `Store:Documents` placeholders |
| S-19 | Internal LMS | `InternalAccessLmsGateway`, signed 10-min launch URL |
| S-20 | Reset reason | required, audited as a diff, not stored in a column |
| S-40 | LMS progress | only `/dev/lms/progress` feeds it until an LMS is chosen (OP-3) |
| S-24 | Duplicate mobile race | two concurrent creates → the second gets 500 (unique index) |
| S-25 | Escalation mailbox | `Store:Email:ManagerAlerts`, falls back to StaffAlerts |
| S-43 | Voucher purpose/date | no columns; `at` = noon Riyadh, purpose in `audit_log` |
| S-31 | Recipient data on delete | shipping recipient anonymised; sent dedications stay |
| S-31 | Access token after delete | not revoked (≤ 15 min); refresh tokens revoked |
| S-31 | Certificate target | 302 to the storefront certificate page |
| S-35 | Beneficiary PII retention | placeholder name, mobile/hash/notes nulled after 2 years inactive |
| S-45 | Digest channel/texts | e-mail, SMS fallback; texts built in code |
| H-1 | OTP daily global cap | 2000/Riyadh day → 429 + one FinanceAlert |
| H-1 | `orders.ip_hash` | not added (not in DDL) |
| H-1 | 50 simultaneous checkouts | p95 0.79 s on a shared 4-vCPU box, 0 errors; re-measure on the VPS |

## 5. Known gaps

- **Payment service** (plan 14 §0, doc 15): no gateway, webhook, reconciliation, refund or `payment` rows. The
  only way an order becomes paid is the dev trigger → `IOrderSettlement`.
- **Hosting** (Plan-14-coverage-audit §3, deferred by the owner):
  - VPS, Docker, Caddy TLS, Tailscale (P0-2);
  - Dockerfile, compose files, Caddyfile, `deploy.yml`, `rollback.sh`, GHCR, Trivy (S-03);
  - staging verification of every "Done when" (replaced by local smoke, `tools/smoke/*.http`);
  - ZAP baseline, restore drill, UAT, cutover (§12);
  - the §15 launch items that need servers: backups, monitoring, SMS sender ID, SPF/DKIM/DMARC, staff TOTP
    enrolment, opening goal seeds;
  - `store_app` DB role grants (database/03 §3).
- **Open owner decisions** that affect behaviour:
  - OP-3: LMS and certificates (the internal LMS adapter reports no progress in Production);
  - OP-4: captcha / proof-of-work on OTP;
  - Q-05: the certificate target.
- **Environment limits in this run:**
  - PostgreSQL 16 locally; the integration tests use a `uuidv7()` shim, while CI runs on `postgres:18` (Testcontainers);
  - no Docker daemon;
  - load numbers were taken on a shared 4-vCPU box.
- **S-24 duplicate-mobile race** answers 500 instead of 409 (REVIEW above; would need EF exception mapping
  outside Application, K-i).

## 6. Plan 14 coverage

| Plan 14 section | State | Where |
|---|---|---|
| §0 Scope r2 (no payments) | done | NullPaymentInitiator, dev trigger; `SettlementPathTests` (only `OrderSettlement` calls `Order.MarkPaid`) |
| §1 Outcome and scope | done | this report |
| §2 Milestones M2–M7 | done locally; M1/M8 hosting | `tools/smoke/M2.http` … `M7.http` |
| §3 Order of work | followed | PROGRESS.md session table |
| §4.1 One session = one PR | adapted: one PR for the run (Run-plan-review) | DECISIONS S-RUN |
| §4.2–4.3 Running sessions, fakes first | done | SMS/e-mail sinks, internal LMS, Noop e-invoicing, local file storage |
| §4.4 Quality gates | done | CI workflow (build `-warnaserror`, format, tests on postgres:18, vulnerable packages) |
| §4.5 Definition of done | done, staging → local smoke | per-session checklists in PROGRESS.md |
| §4.6 Human review focus | done | §7 review guide below |
| §5 Phase 0 spikes | P0-3, P0-4 done; P0-2 deferred (hosting) | `store/spikes/` |
| §6 S-01, S-02 | on `main` before the run | — |
| §6 S-03 | S-03a-lite done (CI); containers/deploy deferred | `.github/workflows/ci.yml` |
| §6 S-04, S-05 | done + tested | IdentityRulesTests, AccountAndStaffFlowsTests, PolicyMatrixTests |
| §7 S-08, S-09 | done + tested | CatalogueRulesTests, AdminFlowsTests, EndpointContractTests |
| §8 S-13, S-15, S-16, S-17 | done + tested | CartRulesTests, CheckoutRulesTests, SettlementTests, DocumentsTests |
| §9 S-19, S-20, S-21, S-40, S-41 | done + tested | FulfilmentAndGivingRulesTests, OperationsTests, AdminFlowsTests |
| §10 S-23 … S-26, S-42, S-43 | done + tested | PoolingTests (FsCheck), AllocationPolicyTests, GivingScenarioTests |
| §11 S-31, S-33, S-35, S-45 | done + tested | OperationsTests, AccountAndStaffFlowsTests, AdminFlowsTests |
| §12 Phase 7 | code share done (H-1); ZAP, restore drill, UAT, cutover deferred | `store/sessions/H-1-hardening.md`, `tools/load/load-smoke.py` |
| §13.1 Rules | 54/54 tested | ImplementedRules.txt + RuleCoverageTests |
| §13.2 Endpoints | 130/130 | route audit, OpenAPI test |
| §13.3 Jobs | 12 + dispatcher | JobScheduling.cs |
| §13.4 Effects | 6/6 | Settlement/Effects |
| §14 Risks | mitigations in code (idempotency, locks, integrity checks, outbox) | — |
| §15 Launch checklist | code items ticked (rule coverage, policy matrix, `/dev` 404, `/docs` off in Production); server items deferred | §5 above |

## 7. Review guide — read these line by line

**Settlement (money becomes paid):**
- `src/Ta3lom.Store.Application/Features/Settlement/OrderSettlement.cs`: one transaction, `FOR UPDATE`, repeat =
  no-op, amount check → `payment_review`, effect registry.
- `src/Ta3lom.Store.Application/Features/Settlement/SettlementLedger.cs` and `Effects/*.cs`.
- `src/Ta3lom.Store.Infrastructure/Persistence/Repositories/OrderRepositories.cs`: row locks, document numbering.
- `src/Ta3lom.Store.Domain/Entities/Ordering/Order.cs`: the state machine; `MarkPaid` is called only from
  settlement.
- Tests: `SettlementTests`, `SettlementPathTests`.

**Pooling:**
- `src/Ta3lom.Store.Application/Features/Settlement/Effects/PoolContributionEffect.cs`,
  `SponsorUnitsEffect.cs`.
- `src/Ta3lom.Store.Domain/Entities/Giving/SponsorshipPool.cs`, `PoolContribution.cs`.
- `src/Ta3lom.Store.Application/Features/Giving/PoolSweeper.cs`, `ZakatLedger.cs`.
- `src/Ta3lom.Store.Infrastructure/Persistence/Repositories/GivingRepositories.cs`: locks.
- Tests: `Domain.Tests/Giving/PoolingTests.cs` (FsCheck), `GivingScenarioTests`.

**Allocation:**
- `src/Ta3lom.Store.Domain/Services/AllocationPolicy.cs` (RUL-47).
- `src/Ta3lom.Store.Application/Features/Allocation/Commands/AllocateUnitsCommand.cs`: SKIP LOCKED FIFO,
  all-or-nothing.
- `Features/Outbox/Handlers/AllocationHandlers.cs`.
- Tests: `AllocationPolicyTests`, `GivingScenarioTests`.

**Auth:**
- `Features/CustomerAuth/Commands/*`, `Features/StaffAuth/Commands/*`.
- `Infrastructure/Services/Security/{OtpHasher, JwtTokenIssuer, RefreshTokenCodec}.cs`.
- `Api/Extensions/{AuthCookies, OriginCheckFilter, RateLimitingSetup}.cs`.
- `Domain/Constants/Policies.cs` (SoD).
- Tests: `IdentityRulesTests`, `PolicyMatrixTests`, `AccountAndStaffFlowsTests`.

**PII:**
- `Infrastructure/Services/Security/PiiProtector.cs`.
- `Features/Beneficiaries/*`: masked DTOs, audited reveal.
- `Api/Extensions/RedactingEnricher.cs`.
- `Features/Account/MeCommands.cs`: delete = anonymise.
- Retention (`Features/AdminOps/Commands/OperationsJobs.cs`).
- Tests: `OtpLoggingTests`, `DocumentsTests` (donor DTO reflection).

**Money:**
- `src/Ta3lom.Store.Domain/ValueObjects/Money.cs`.
- `Features/Checkout/CheckoutPricing.cs` (VAT, rounding AwayFromZero).
- `Application/Mapping/*` (money as `"350.00"` strings).
- `Api/Extensions/IdempotencyFilter.cs`.
- Guards: `BannedSymbols.Money.txt`, the `No_money_as_floating_point` architecture test.
