# Build progress
Next: S-20 — Digital downloads

Run rules: `store/sessions/Cloud-build-plan.md` §2, §4b (test phase), §4c (plan 14 coverage); decisions in
[DECISIONS.md](DECISIONS.md). Branch `claude/funny-wright-yum3nm`, one draft PR "Store backend — full build + tests".

## Baseline (STEP 0, 30 Sep 2026)

| Check | Result |
|---|---|
| Toolchain | dotnet SDK 10.0.112, PostgreSQL 16.13 (cluster had to be restarted: stale pid), Docker CLI without daemon → `STORE_TEST_DB=external` |
| DMS reference | cloned read-only into the session scratchpad (`lotfi029/DMS`, depth 1) |
| `dotnet build Ta3lom.Store.slnx -warnaserror` | ✅ 0 warnings, 0 errors |
| `dotnet test --solution Ta3lom.Store.slnx` | ✅ 169 passed (Architecture, Domain, Application, Api.Integration) |
| Idempotent migration script applied twice to `ta3lom_store_dev` | ✅ no error (54 relations in `store`, 2 migrations in history) — new helper `tools/scripts/dev-db-migrate.sh` |
| `create-admin` stub | ✅ builds and runs; prints the S-05 notice, exit code 2 |

## Sessions

| Session | Status | Commits | Endpoints # | Jobs / effects / outbox | Smoke | Notes |
|---|---|---|---|---|---|---|
| STEP 0 | done | — | — | — | — | baseline above |
| S-RUN run mode | done | S-RUN | — | — | — | state files, run mode, review files copied |
| S-02b DMS alignment | done | 2868161 | — | — | ✅ health, 404 problem, /docs, seed (tools/smoke/S-02b.http) | 172 tests green; no migration needed; OpenAPI unchanged |
| S-03a-lite CI | done | e96bd7d | — | — | n/a (workflow file; runs on push) | `.github/workflows/ci.yml` accepted by the push |
| S-04 customer OTP | done | S-04 | 16–20 | PruneOtp | ✅ tools/smoke/S-04.http | 192 tests green; Build + Done-when ticked below |
| S-05 staff identity | done | S-05 | 47–52, 123–128 | — | ✅ tools/smoke/S-05.http | 204 tests green; actor bug found and fixed in smoke |
| S-08 public catalogue | done | S-08 | 1–7, 9 | — | ✅ tools/smoke/S-08.http | seed catalogue served (15 products), ETag/304 |
| S-09 admin catalogue | done | S-09 | 54–77, 118, 119 | — | ✅ tools/smoke/S-09.http | 230 tests green; offer/cause UTC bug + money-setting wire bug found and fixed in smoke |
| S-13 cart | done | S-13 | 10–15 | ExpireGuestCarts | ✅ tools/smoke/S-13.http | 262 tests green; S-04 merge hook filled (RUL-17); dedication owned JSON |
| S-15 checkout | done | S-15 | 40–42 | — | ✅ tools/smoke/S-15.http | 286 tests green; last-copy race: one 201, one 409 |
| S-16 settlement core | done | S-16 | 87 + dev trigger | ExpireUnpaidOrders; DonateLineEffect + ledger | ✅ tools/smoke/S-16.http | 290 tests green; integrity checks 0 rows; doc 15 updated |
| P0-3 Arabic PDF spike | done | P0-3 | — | — | spikes/P0-3 → 2 PDFs | GO: shaping, table, QR ok; LRI/PDI isolates for phones/English; store/spikes/P0-3-arabic-pdf.md |
| S-17 outbox, documents | done | S-17 | 88 | OutboxDispatcher; OrderPaid, RenderDocuments, FinanceAlert, DedicationCard (stub), DocumentsResendRequested, lifecycle no-ops | ✅ tools/smoke/S-17.http | 294 tests green; retry after provider outage verified; path-resolution bug fixed |
| S-19 enrolment + LMS | done | S-19 | 29 | CourseEnrolmentEffect; LmsEnrol, LmsRevoke | ✅ tools/smoke/S-19.http | 296 tests green; first invoice PDF; dispatcher crash fixed |
| P0-4 watermark spike | done | P0-4 | — | — | spikes/P0-4 → 301-page book stamped | GO: QuestPDF stamp as PdfSharp form XObject ~0.2 s/300 pages; PdfSharp text cannot shape Arabic; store/spikes/P0-4-watermark.md |
| S-20 digital downloads | todo | | 32, 46, 94 | DigitalBookEffect | | |
| S-21 shipments, stock, admin orders | todo | | 78–80, 85, 86, 95–99 | PrintBookEffect; ShipmentShipped/Delivered | | integrity check |
| S-40 LMS progress sync | todo | | — | LmsProgressSync; SponsorUnitCompleted | | |
| S-41 low stock alert | todo | | — | LowStockAlert | | |
| S-23 units and pooling | todo | | — | SponsorUnitsEffect, PoolContributionEffect | | integrity check |
| S-24 beneficiary register | todo | | 100–108 | — | | |
| S-25 allocation | todo | | 109–114 | AllocationAlerts, LapseUnits; SponsorUnitAllocated, AllocationAlert/Escalation, BeneficiaryInvite, UnitLapsed, LmsRevoke | | integrity check |
| S-26 dedication cards, cause updates | todo | | 45, 81–84, 93 | DedicationCard; CauseUpdatePublished | | |
| S-42 close campaigns, sweep | todo | | 71 (sweep), 136, 137 | CloseCampaigns; PoolSwept | | integrity check |
| S-43 zakat ledger, vouchers | todo | | 115, 116 | — | | |
| S-31 `/v1/me` | todo | | 21–28, 30, 31, 33–39 | — | | |
| S-33 dashboard, reports, admin ops | todo | | 53, 117, 129–133 | — | | |
| S-35 templates, retention, integrity | todo | | 120–122 | IntegrityChecks, Retention, PruneIdempotencyKeys | | |
| S-45 sponsor digest | todo | | — | SponsorDigest | | |
| Close-out 7.1–7.3 | todo | | | | | |
| H-1 code-side hardening | todo | | | | | |

## Milestones (plan 14 §2 demos, local)

| Milestone | Smoke file | Result |
|---|---|---|
| M2 Identity | tools/smoke/M2.http | ✅ passed (OTP sink sign-in, rotation, reuse revokes family, staff TOTP, 403s) |
| M3 Catalogue | tools/smoke/M3.http | ✅ passed (store_manager created/published course, book print+digital, cause; public ETags; archive → 404) |
| M4 First settled order | tools/smoke/M4.http | ✅ passed for the gift path (receipt PDF, SMS, e-mail, repeat, review); invoice PDF re-checked in M5 |
| M5 Fulfilment | tools/smoke/M5.http | |
| M6 Giving | tools/smoke/M6.http | |
| M7 Operations-complete | tools/smoke/M7.http | |

## Part 1 checklist
- [ ] 130 endpoints of plan 14 §13.2 mapped and answering
- [ ] 13 jobs of §13.3 registered (12 scheduled + OutboxDispatcher; all except ReconcilePayments)
- [ ] Every settlement effect of §13.4 registered (5 line effects + gift/donate counters)
- [ ] 17 outbox message types of 09 §4.9 handled (all except RefundProcessed)
- [ ] Integrity checks 0 rows after the full smoke scenario
- [x] Spikes P0-3 and P0-4 written up in store/spikes/
- [ ] H-1 code-side hardening done
- [ ] No session left blocked

## Test phase

| Step | Status | Commits | Notes |
|---|---|---|---|
| T-1 test infrastructure | todo | | |
| T-2 settlement | todo | | |
| T-3 pooling | todo | | |
| T-4 allocation | todo | | |
| T-5 RUL tests + ImplementedRules.txt | todo | | |
| T-6 policy matrix | todo | | |
| T-7 endpoint integration | todo | | |
| T-8 documents and outputs | todo | | |
| T-9 coverage + Build-report.md | todo | | |

- [ ] T-1…T-9 done
- [ ] All tests green locally (and CI green)
- [ ] Coverage Domain ≥ 90 %, Application ≥ 80 %
- [ ] store/sessions/Build-report.md pushed, PR marked ready

## Per-session checklists

Copied from plan 14 (Build / Tests / Done when) plus Plan-14-coverage-audit §2.2 when each session starts.
Build and Done-when boxes are ticked in the session; Tests boxes in the test phase.

### S-04 — Customer identity (OTP)

Build
- [x] Domain: `Customer` (create by mobile, block/unblock, anonymise stub), `OtpChallenge` (attempts, expiry, consume), `RefreshToken` (family, rotate, revoke)
- [x] Application: `RequestOtp`, `VerifyOtp` (creates the customer on first login; cart-merge hook `ICartMergeHook`, no-op until S-13), `RefreshSession`, `Logout`, `LogoutAll`; Saudi mobile `05XXXXXXXX` → E.164 (`SaudiMobile`)
- [x] Infrastructure: `OtpHasher` (HMAC-SHA256 + pepper, constant-time); `JwtTokenIssuer` (`store-customer`, 15 min); refresh rotation 60 d with reuse detection → family revoked; `SinkSmsSender` + `UnifonicSmsSender` behind `Store:Sms:Provider`; `PruneOtp` job (hourly, Riyadh)
- [x] Api: `/v1/auth/*` group; cookie `t3s_rt` (HttpOnly, Secure, SameSite=Lax, `Path=/v1/auth`); rate policies `otp-request` (10/h per IP) and `otp-verify`, `auth-refresh`; per-mobile 3/15 min and per-IP 10/h in the handler (DB-backed); Origin check on cookie endpoints
- [x] Audit §2.2: E.164 validator; Origin check; blocked customer can't verify (RUL-61); OTP never logged (sink logs masked mobile + length only)
- [x] Endpoints #16–#20 mapped; `/dev/sms` (Development/Staging)

Tests (test phase)
- [ ] RUL-60: expiry, 5 wrong attempts, per-mobile and per-IP limits, hash not reversible, code never logged
- [ ] RUL-61: blocked customer cannot verify
- [ ] Refresh reuse revokes the family
- [ ] Cookie has the exact attributes
- [ ] OTP SMS is sent directly, not via the outbox (RUL-63 exception)

Done when (local smoke)
- [x] Sign-in works with the sink (request → /dev/sms → verify → session)
- [x] The 4th code within 15 minutes returns `otp.rate_limited` (429, `retryAfterSeconds`)
- [x] Replaying an old refresh token logs out every session of that customer (`auth.refresh_reused`, current cookie then 401)

### S-05 — Staff identity and authorisation

Build
- [x] `StaffUser`: password hash, lockout (5 failures → 1 h), TOTP secret encrypted with Data Protection, roles array
- [x] `StaffLogin` → MFA token (store-staff-mfa, 5 min); `EnrolTotp` (otpauth URI + base32); `VerifyTotp` → access (store-staff, 10 min) + refresh 8 h in `t3s_srt` (`Path=/v1/admin/auth`)
- [x] Policies of 11 §1.2 with segregation of duties (`staff:refund`, `staff:allocate`, `staff:reveal-pii` not satisfied by admin) — `Domain/Constants/Policies.StaffRoles`
- [x] Staff CRUD, reset password / TOTP, unlock (#123–#128)
- [x] `ICurrentActor` from claims feeding `audit_log.actor`
- [x] `create-admin` CLI (one-time password, TOTP enrolment forced at first login)
- [x] Audit §2.2: lockout per FR-53; placeholder allocate route for the policy matrix → built with the matrix in T-6

Tests (test phase)
- [ ] RUL-62 policy matrix (every endpoint group × every role → 200/403), extended by later sessions
- [ ] Lockout; TOTP window ±1 step; staff and customer tokens not interchangeable (audience)

Done when (local smoke)
- [x] `create-admin` works (dev DB)
- [x] A `viewer` gets 403 on every write (staff admin routes; later sessions' writes join the matrix in T-6)
- [ ] An `admin` without `allocation_officer` gets 403 on the allocate route → verified when #112 exists (S-25)

### S-08 — Public catalogue API

Build
- [x] Domain: `Product.IsPublished`, `SponsorshipOffer.IsOpen(now, funded)`, `CauseDetail.IsOpen(now, raised)` (RUL-09/10), `GoalMath.Percent` floor (RUL-08), `DonorDisplay` (RUL-23)
- [x] Application: `GetHome`, `ListProducts` (type, category, sponsorable, q, sort, page) + `ListProductSlugs` (`fields=slug`), `GetProductBySlug`, `GetLiveFigures`, `GetProductUpdates`, `GetGiftFeed` (anonymised), `GetCatalogConfig` (presets, min gift, dedication templates, VAT flag)
- [x] Infrastructure: `AsNoTracking` projections to DTOs; search SQL `search_vector @@ plainto_tsquery('simple', ar_normalize(q))` + trigram fallback; `StoreSettingsProvider` (typed, 60 s cache)
- [x] Api: `/v1/catalog/*`; ETag + Cache-Control (content 60 s + SWR 300, live 15 s); `If-None-Match` → 304; output caching keyed by URL
- [x] Audit §2.2: gift feed anonymised; config endpoint; locale-free output cache; tashkeel-insensitive search

Tests (test phase)
- [ ] RUL-02 (draft invisible, archived only in history), RUL-08 (floor and cap), RUL-09 (ended campaign flagged), RUL-23 (feed never shows a name when anonymous)
- [ ] Arabic search: مكتبة for مكتبه, حكايات with tashkeel; ETag round trip; the seed catalogue returns 15 products

Done when (local smoke)
- [x] The seed catalogue is served (15 products, dev DB)
- [x] p95 < 150 ms for the product endpoint (local: 2–4 ms cached; k6 load smoke in H-1)
- [x] S-10/S-11 (frontend) can start against the API (OpenAPI regenerated)

### S-09 — Admin catalogue API

Build
- [x] Product create/update with the type lock after the first order (RUL-01, `product.type_locked`); duplicate slug → 409
- [x] Course detail, curriculum replace, book detail; formats: print stock only via movements (created at 0), digital needs a private `book_file` (RUL-05)
- [x] Cause detail: unit amount ↔ label pairing, one general fund (RUL-07); goal seed (`goal_progress.seed_*`, audited)
- [x] Publish/archive with validation (Arabic title/summary, cover + alt, price or active formats, cause target, offer/cause dates) → 422 `{missing:[…]}` (RUL-03/04); preview of drafts
- [x] Sponsorship offers: one active per product (RUL-06), dates, `beneficiary_group` key, no zakat on offers, close → `is_active=false` (sweep in S-42)
- [x] Categories; media upload (allow-list, size limits, image alt required, `book_file` private, EXIF stripped by WebP re-encode) through `IFileStorage` (`LocalFileStorage`)
- [x] Settings read/update with typed validation per key (RUL §M); money as `"25.00"` strings; `IStoreSettings` invalidated + catalogue output cache evicted on update
- [x] Every mutation audited through `ICurrentActor`; staff policies `staff-catalogue` / `staff-admin` / `staff-read`
- [x] Audit §2.2: `StoreSettings` covers every key (addendum 8); `LocalFileStorage` + `/files/public`; SkiaSharp re-encode

Tests (test phase)
- [ ] RUL-01, RUL-03, RUL-04, RUL-05, RUL-06, RUL-07, RUL-10 (`close_on_target` in `IsOpen`)
- [ ] Audit rows for publish, price change, seed change; policy-matrix rows for #54–#77, #118, #119

Done when (local smoke)
- [x] A `store_manager` creates, publishes and archives one course, one book (print + digital) and one cause (M3)
- [x] Public endpoints show them within 60 s (output cache evicted on write; shown at once)

### S-13 — Cart

Build
- [x] `Cart` aggregate: `AddLine` (RUL-12 kind × mode × product matrix, RUL-13 owned / in-cart / unpaid-order course, RUL-14 merge, RUL-18 50 lines / `cart_max_total`, RUL-19 intent, RUL-21 dedication, RUL-30 print stock), `UpdateQuantity`, `RemoveLine`, `Clear`, `MergeInto`, `AssignTo`
- [x] `CartLineRules` shared with checkout (S-15): line maximum, indicative price (RUL-15), `issues` per line
- [x] Guest cookie `t3s_cart` (Data Protection sealed, HttpOnly, Secure, Lax, `/v1`, 30 days) issued lazily and refreshed on change
- [x] S-04 hook filled: VerifyOtp merges the guest cart (owned course dropped with a notice), cookie deleted (owner fix 3)
- [x] `ExpireGuestCarts` job (03:00 Riyadh) → `DeleteExpiredGuestCarts` command (RUL-16)
- [x] Summary endpoint for the header badge (count + total, no auth required)
- [x] `cart_line.dedication` owned JSON (CLAUDE.md jsonb rule); row locks on the cart row in `CartRepository` only
- [x] Audit §2.2: badge endpoint; `MergeInto` (RUL-17); owned course dropped on merge with a notice

Tests (test phase)
- [ ] One test per RUL-11 … RUL-19 and RUL-21; print qty 21 rejected; digital qty forced to 1; `amount` line never merges; a dedicated line never merges; zakat on a non-eligible cause → `cart.zakat_not_allowed`; campaign ended → `cart.campaign_ended`; the merge drops an owned course with a notice

Done when (local smoke)
- [x] A guest adds the prototype's mixed cart, signs in and keeps it (merge verified, cookie removed)
- [x] FR-S12: a 100 SAR share of a 350 SAR seat → one `amount` line; 5 seats → a `units` line of 1,750 SAR

### S-15 — Checkout and order creation

Build
- [x] `CheckoutQuote` reprices from the catalogue, per-line `ok`/`changed`/`invalid` + `priceChanged` (RUL-15, RUL-24)
- [x] `PlaceOrder` re-validates everything (RUL-24) incl. a course in an unpaid order (RUL-13); `acceptTerms` → `terms_accepted_at`
- [x] Shipping address only with print lines (RUL-26); `VatCalculator` (RUL-27); shipping and totals (RUL-31, `ck_order_total`)
- [x] Reference `T3-YY-NNNNNN` from `order_ref_seq` (RUL-28); lines and dedications snapshotted; `shipping_address` owned JSON
- [x] Print stock reserved with `FOR UPDATE` + conditional `UPDATE … WHERE available ≥ q` inside the transaction (RUL-30)
- [x] `payment_due_at` = placed + `order_payment_timeout_min`; cart → `converted`
- [x] After commit `IPaymentInitiator.StartAsync` (doc 15 §3) → `NullPaymentInitiator` (no redirect)
- [x] `Idempotency-Key` filter on `idempotency_key` (09 §5.16); per-order status token (HMAC) for #42
- [x] `GET /v1/orders/{ref}/status` by bearer or status token, never by reference alone
- [x] Audit §2.2: `acceptTerms` → `terms_accepted_at`; address only with print; totals; `payment_due_at`; cart converted; status never by reference alone

Tests (test phase)
- [ ] RUL-24 matrix (unpublished, ended, closed offer, stock out, limit, owned course); RUL-27 350 @ 15 % → 45.65 / 304.35; RUL-28 format
- [ ] RUL-30 two concurrent checkouts for the last copy → exactly one succeeds; RUL-31 `ck_order_total` never violated
- [ ] Same `Idempotency-Key` + same body → same response; different body → 422; status without a token → 401/404

Done when (local smoke)
- [x] The prototype cart becomes order `T3-26-001000` in `pending_payment`, with stock reserved and a status token

### S-16 — Settlement core and payment hand-off

Build
- [x] `IOrderSettlement` + `PaymentConfirmation` + `SettlementOutcome` (doc 15 §2) in `Application/Features/Settlement`; `OrderSettlement` = database/01 §4.4 without the inbox step
- [x] `ILineSettlementEffect` registry (Scrutor) + `DonateLineEffect` (RUL-53); `settlement.effect_missing` guard with `FinanceAlert`
- [x] Order state change, goal counters, donor count, zakat ledger `in` (RUL-20), dedication and order-paid outbox rows, document number reservation (RUL-59)
- [x] `Order.MarkPaid` (RUL-29 table, RUL-35 amount + late capture → `payment_review` + `FinanceAlert`); `Order.Expire` with cart restore and stock release (RUL-29/30)
- [x] Row-lock helpers in Infrastructure repositories; retrying execution strategy (UnitOfWork); `IPaymentInitiator` + `NullPaymentInitiator` (S-15)
- [x] `CancelOrder` (#87, staff:finance, from `pending_payment`/`payment_failed` only, stock released); `ExpireUnpaidOrders` job
- [x] Dev trigger `POST /dev/orders/{ref}/confirm-payment` (+ `/dev/jobs`)
- [x] Audit §2.2: staff cancel; expiry restores the cart; retry on serialization failure; `FinanceAlert`; doc 15 updated to the code

Tests (test phase)
- [ ] Same confirmation 5× → one state change and one set of effects; two confirmations in parallel → one settlement
- [ ] Amount mismatch → `payment_review`, no effects; confirmation after expiry → `payment_review`
- [ ] Expiry releases stock and restores the lines; duplicate course race → no second enrolment, line flagged, `FinanceAlert` (S-19)
- [ ] Donate with zakat → `zakat_ledger in`; goal counters once per product per order; architecture test: only `IOrderSettlement` sets `paid`

Done when (local smoke)
- [x] The dev trigger settles an order → `paid`/`fulfilled`, with effects and outbox messages
- [x] Repeating the trigger changes nothing; a wrong amount parks the order in `payment_review`
- [x] Killing the API mid-settlement leaves no partial state (single transaction on the retrying strategy; verified by design, crash test in T-2)
- [x] The contract in 15 §2 matches the code (15 §2.3a added)

### S-17 — Outbox, notifications and documents

Build
- [x] `OutboxDispatcher` (hosted, `FOR UPDATE SKIP LOCKED`, batch 20, 5-min lease, backoff up to 6 h, dead letter after 24 h → staff alert); nudged after commits
- [x] Handlers `OrderPaid`, `DedicationCard` (stub until S-26), `RenderDocuments`, `FinanceAlert` (+ resend, lifecycle no-ops)
- [x] `ScribanTemplateRenderer` + `message_template` lookup with ar fallback (S-04) used for every message
- [x] `notification` rows with `outbox_message_id`; `ux_notification_once` prevents re-sends; failed rows retried
- [x] `SmtpEmailSender` (MailKit) by config; `SinkEmailSender` + `GET /dev/mail` in Development (Mailpit replaced)
- [x] `DocumentNumberAllocator` (S-16, gapless, row lock until commit)
- [x] QuestPDF invoice (simplified tax invoice with the TLV QR when `vat_enabled`) and donation receipt (zakat separately, licence no., real donor name)
- [x] PDFs as private `media_asset` rows; admin resend documents (#88)
- [x] Audit §4c: `SinkEmailSender` + `/dev/mail`, `SmtpEmailSender`, `IEInvoicingGateway` + `NoopEInvoicingGateway`

Tests (test phase)
- [ ] RUL-59: mixed order → one INV + one DR; numbers gapless under 20 parallel settlements
- [ ] Outbox retry does not duplicate an SMS; template fallback to Arabic; the QR decodes to the five TLV fields; PDF text snapshot

Done when (local smoke)
- [x] An order settled through the dev trigger produces its PDF(s), the SMS lands in the sink and the e-mail in the mail sink (invoice path: M5)
- [x] Stopping the SMS provider → messages retry and are delivered after it recovers

### S-19 — Course enrolment and LMS adapter

Build
- [x] `ILmsGateway` (enrol, revoke, launch URL, progress) + `InternalAccessLmsGateway` (default, Q-05)
- [x] `CourseEnrolmentEffect : ILineSettlementEffect` (enrolment source `purchase`, outbox `LmsEnrol`); duplicate → no enrolment + `FinanceAlert`
- [x] `pending_sync` on LMS failure, retried through the outbox; `LmsEnrol`/`LmsRevoke` handlers
- [x] `POST /v1/me/enrolments/{id}/launch` (#29) returns a short-lived launch URL
- [x] Audit §2.2: short-lived launch URL; `LmsEnrol` retries through the dispatcher

Tests (test phase)
- [ ] RUL-42: lifetime vs `access_days`; LMS down → `pending_sync` → recovers; never a second enrolment (`ux_enrolment_live`)

Done when (local smoke)
- [x] A paid course shows as active and launches
- [x] With the LMS adapter forced to fail, it becomes active after recovery
