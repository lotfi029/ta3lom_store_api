# 06 — Store Non-Functional Requirements

**Revision:** S1 draft r1 · 29 September 2026 · reconciled with the r1 canonical changes

Every target here is measurable and has a stated verification method, as in the main-site
[NFR document](../requirements/04-non-functional-requirements.md). Main-site IDs (`NFR-01` …
`NFR-35`) keep their meaning. Where a store requirement reuses a main-site target, it cites it
instead of restating a different number.

Priority: **M** Must · **S** Should · **C** Could. Load and capacity figures assume the production
topology of ADR-S03: one Hostinger VPS KVM 2 (2 vCPU, 8 GB RAM, NVMe) running `caddy`, `store-api`,
`postgres:18` and `backup` under Docker Compose.

---

## Performance

### NFR-S01 — Catalogue API response time *(M)* · FR-S02, FR-S04–S07
`GET /v1/catalog/...` (listings, product detail, live figures from `v_goal`): 95th percentile
**≤ 200 ms**, 99th **≤ 500 ms**, at 50 concurrent users. Same target as main-site NFR-05.

**Verified by:** k6 scenario `catalog.js` against staging with the seed catalogue, run in CI nightly and before each release.

### NFR-S02 — Cart API response time *(M)* · FR-S16–S20
`/v1/cart/...` reads and writes, including `GET /v1/cart/summary` used by the header badge on
every ta3lom.sa page: p95 **≤ 250 ms**, p99 **≤ 600 ms** at 50 concurrent users.

**Verified by:** k6 scenario `cart.js`.

### NFR-S03 — Checkout response time *(M)* · FR-S31, FR-S32
`POST /v1/checkout` (validation, repricing, order creation and stock reservation in one
transaction, then the gateway call to create the hosted payment):

| Measure | Target |
|---|---|
| Server time excluding the gateway call | p95 ≤ 400 ms |
| End to end including the gateway call | p95 ≤ 1.5 s |

**Verified by:** k6 scenario `checkout.js` with the gateway adapter in sandbox mode; OpenTelemetry span `checkout.create_order` vs `gateway.create_payment` (NFR-S45).

### NFR-S04 — Settlement transaction *(M)* · FR-S34, RUL-39
The settlement transaction (database design §4.4), from the `payment_event` insert to `COMMIT`,
completes in **< 500 ms at p95** for an order of up to 20 lines and 100 sponsored units, including
pooling and document numbering.

**Verified by:** integration test with Testcontainers PostgreSQL 18 asserting the elapsed time over 200 runs; production histogram `store.settlement.duration` (NFR-S45).

### NFR-S05 — Webhook acknowledgement *(M)* · FR-S34, RUL-33
`POST /v1/payments/webhooks/{provider}` returns a status within **1 s at p95** and **3 s at
p99**, so the gateway does not time out and retry needlessly.

**Verified by:** k6 scenario `webhooks.js`; production histogram on the endpoint.

### NFR-S06 — Prerendered store pages meet the main-site budgets *(M)* · FR-S01–S09, ADR-002
Store home, catalogue and product routes (`RenderMode.Prerender`) meet the main-site targets
unchanged:

| Main-site NFR | Target on every prerendered store route |
|---|---|
| NFR-01 | Page transfer ≤ 400 KB |
| NFR-02 | LCP ≤ 2.0 s, INP ≤ 200 ms, CLS ≤ 0.05 (mobile, Slow 4G) |
| NFR-04 | Initial JavaScript ≤ 150 KB gzipped |
| NFR-06 | TTFB ≤ 400 ms |

Live figures (price, stock, goal, donor count) fetched at runtime (FR-S07) reserve their space so
they do not add layout shift. Product slugs for prerendering come from the main site's existing
`scripts/routes.mjs` → `routes.txt` mechanism, extended to call `GET /v1/catalog/products?fields=slug`.
If the store API is unreachable at build time the build **fails**, so a stale or empty product list
is never shipped.

**Verified by:** `angular.json` budgets (build fails); CI test that the build fails when the slug endpoint is blocked; Lighthouse CI on `/ar/store`, `/ar/store/courses`, `/ar/store/p/digital-teacher`, `/ar/store/p/water-for-schools` and their `/en` counterparts on every pull request.

### NFR-S07 — Client-rendered store routes *(S)* · FR-S18, FR-S30, FR-S35
Cart, checkout, thank-you, account and login (`RenderMode.Client`): LCP **≤ 2.5 s** and INP **≤ 200 ms**
on the mobile profile, Slow 4G. The store feature is lazy-loaded; its route chunks add **≤ 120 KB**
gzipped to the pages that use them and nothing to main-site pages except the cart badge
(≤ 5 KB).

**Verified by:** Lighthouse CI (authenticated flow with a test customer); `angular.json` budgets for the lazy chunks.

---

## Capacity

### NFR-S08 — Peak campaign load *(M)* · BR-S04
The system sustains a Ramadan-campaign peak without errors:

| Load | Duration | Pass criteria |
|---|---|---|
| **200 concurrent checkouts** (each: add lines, OTP stub, `POST /v1/checkout`) | 10 min | Error rate < 0.5 %; NFR-S03 targets at most doubled |
| **50 webhooks/s burst** | 60 s | Every event stored in `payment_event`; none lost; p99 acknowledgement ≤ 3 s |
| 500 concurrent catalogue readers alongside | 10 min | NFR-S01 targets at most doubled |

**Verified by:** k6 scenario `ramadan-peak.js` on staging sized like production, before launch and before each Ramadan; after the run, all integrity checks (NFR-S57) return zero rows.

### NFR-S09 — Hot-row contention *(M)* · RUL-39, RUL-45
Concurrent settlements on the same product (`goal_progress`, `sponsorship_pool`) and the same
document series (`document_counter`) lose no update: serialization failures are retried
(Npgsql `EnableRetryOnFailure`) and no request fails because of them at 50 settlements/s on one
cause.

**Verified by:** integration test firing 500 parallel settlements for `water-for-schools`; `goal_progress.raised_amount` equals the sum of lines, invoice and receipt numbers are gapless.

### NFR-S10 — No oversell, no double grant *(M)* · RUL-13, RUL-30, RUL-47
Under concurrency: print stock is never negative or over-reserved (`ck_format_reserve`); no
customer has two live enrolments in a course (`ux_enrolment_live`); no sponsored unit is allocated
twice (`FOR UPDATE SKIP LOCKED`).

**Verified by:** concurrency integration tests (two checkouts for the last copy; two officers allocating the same backlog; duplicate webhook and reconciliation racing on one order).

### NFR-S11 — Year-1 data volume *(S)*
With 50,000 orders, 150,000 lines and 20,000 units loaded (database design §5), NFR-S01 to NFR-S04
still hold and no query in the top 10 of `pg_stat_statements` exceeds 50 ms mean.

**Verified by:** load test on staging after generating data with `database/store/tools` fake-data script.

---

## Availability and recovery

### NFR-S12 — Availability *(S)*
**99.5 %** monthly for `store-api.ta3lom.sa`, excluding announced maintenance (about 3.6 hours per
month). One VPS makes a higher target dishonest.

**Verified by:** external uptime monitor polling `/health/ready` every minute and a synthetic check of `GET /v1/catalog/...`; monthly report.

### NFR-S13 — Recovery objectives *(M)* · 03-database-operations §4
**RPO ≤ 15 minutes** (WAL archiving with pgBackRest) and **RTO ≤ 4 hours**. Stricter than the
main site's RPO 24 h because the store holds money.

**Verified by:** monthly restore drill on staging: restore to a point in time, run the integrity checks (NFR-S57) and the integration suite, record the time taken. The Data Protection key ring is restored with it and a beneficiary name decrypts.

### NFR-S14 — Graceful degradation *(M)* · NFR-20
If the store API is down: prerendered store pages still render their content and show the buy
box as unavailable; main-site pages hide the cart badge; no blank page or raw error. If the SMS
or email provider is down, payments still settle (RUL-63). If the LMS is down, enrolments are
`pending_sync` (RUL-42). Payments are never lost: the gateway retries webhooks, and reconciliation (RUL-38,
every 5 minutes for payments `initiated` more than 5 minutes ago) brings a captured order to `paid`
within **about 10 minutes** even if every webhook is lost.

**Verified by:** Playwright tests with the API host blocked; integration tests with the SMS, email and LMS adapters returning 503; integration test with webhooks suppressed asserting `paid` within 10 minutes of capture (fake clock driving `ReconcilePayments`).

### NFR-S15 — Notification latency *(S)* · FR-S80
95 % of outbox messages are processed within **60 s** of the business transaction's commit.
Backlog alert when more than 500 messages are pending or the oldest is older than 30 minutes.
A retried message never sends the same SMS or email twice: `notification.outbox_message_id` with
the unique index `ux_notification_once (outbox_message_id, channel, recipient)`. The OTP SMS is the
one message sent directly, outside the outbox (RUL-63).

**Verified by:** metric `store.outbox.lag_seconds` (NFR-S45); alert rule tested on staging; integration test that kills the dispatcher after sending and before marking processed, then asserts one notification.

### NFR-S16 — Deployment interruption *(S)* · 06-deployment-architecture
A normal release (idempotent migration script, then container swap) makes the API unavailable
for **≤ 30 s**. Migrations never run at application start-up.

**Verified by:** uptime monitor during staging deploys; deploy job duration logged.

---

## Security

### NFR-S17 — OWASP ASVS Level 2 *(M)*
The store API and the store feature of `ta3lom-web` meet **OWASP ASVS Level 2** (version current
at build start, 5.0). Non-applicable controls are listed with a reason.

**Verified by:** ASVS checklist maintained in the repository and reviewed before launch; the external penetration test (NFR-S28) maps its findings to ASVS.

### NFR-S18 — PCI DSS scope SAQ-A *(M)* · RUL-32, ADR-S05
Card entry happens only on the gateway's hosted page or SDK. No PAN, CVV or track data is received,
logged or stored. The store's pages that start a payment load only scripts from the approved CSP
list.

**Verified by:** code review; log search for 13–19-digit card-like numbers on staging after a full payment test (zero hits); annual SAQ-A self-assessment signed by the association.

### NFR-S19 — Webhook integrity *(M)* · FR-S34, RUL-33, RUL-34
Every webhook signature is verified (HMAC or provider method) with a constant-time comparison
before any processing. Invalid signature → **401**, event stored with `signature_valid = false`
and logged. Replay of a valid event → **200** with no state change. Events older than the
provider's tolerance window are rejected where the provider supplies a timestamp.

**Verified by:** integration tests: valid, invalid, replayed and stale events.

### NFR-S20 — Transport and headers *(M)* · NFR-12
TLS **1.2 minimum, 1.3 preferred**, terminated by Caddy with automatic certificates. On the API:
HSTS `max-age=31536000; includeSubDomains`, `X-Content-Type-Options: nosniff`,
`X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`,
`Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` on JSON responses. The
ta3lom.sa CSP adds `store-api.ta3lom.sa` to `connect-src` and only the chosen gateway's hosts to
`script-src`/`frame-src`. CORS allows exactly `https://ta3lom.sa` and `https://www.ta3lom.sa`
with credentials.

**Verified by:** SSL Labs grade A or better and an external header scan grade A at launch and quarterly; integration test asserting headers and CORS rejection of other origins.

### NFR-S21 — Tokens and sessions *(M)* · ADR-S04, RUL-60, RUL-62
Access token lifetimes: customers 15 min, staff 10 min; kept in memory (Angular signal), never
`localStorage`. Refresh tokens rotate with reuse detection revoking the family, in HttpOnly Secure SameSite=Lax
cookies: customers `t3s_rt`, `Path=/v1/auth`, 60 days; staff `t3s_srt`, `Path=/v1/admin/auth`,
8 h (staff auth endpoints under `/v1/admin/auth/*`). Separate JWT audiences `store-customer` and
`store-staff`; a customer token is rejected on `/v1/admin/...`.

**Verified by:** integration tests for expiry, rotation, reuse detection, audience mismatch; ESLint rule forbidding `localStorage` for tokens in `ta3lom-web`.

### NFR-S22 — OTP abuse limits *(M)* · RUL-60
Per mobile: 3 codes / 15 min; per IP: 10 codes / hour; 5 wrong attempts per code; code valid
5 min, stored only as HMAC-SHA256 with a server pepper. The OTP SMS is sent directly within the
request, never through `outbox_message`, so the plain code is stored nowhere (RUL-63). Responses do
not reveal whether a mobile has an account; a `blocked` customer cannot sign in (RUL-61). An alert fires when OTP SMS volume in one hour exceeds 3 × the trailing 7-day hourly
average (SMS-pumping fraud).

**Verified by:** integration tests asserting 429 at each threshold and that no table or log contains the plain code; alert rule tested on staging.

### NFR-S23 — Rate limiting *(M)* · NFR-14
ASP.NET Core rate limiting, partitioned by client IP (hashed) and, when signed in, by subject:

| Scope | Limit |
|---|---|
| Global per IP | 300 requests/min |
| `POST /v1/checkout` per customer | 10/min |
| Cart writes per cart | 60/min |
| Staff login per IP | 5/min (as NFR-14) |
| `GET /v1/cards/{token}` per IP | 30/min |
| Webhooks | Not rate-limited by IP (gateway source ranges allow-listed where the provider publishes them) |

Excess returns **429** with `Retry-After` and a ProblemDetails body.

**Verified by:** integration tests asserting 429 at each threshold.

### NFR-S24 — Staff authentication *(M)* · FR-S70, RUL-62
Password hashing PBKDF2 (ASP.NET Core `PasswordHasher` v3, ≥ 600,000 iterations) or Argon2id;
minimum 12 characters checked against a common-password list; TOTP mandatory before any admin
action; lockout 5 failures in 15 min → 1 hour (as main-site FR-53); uniform error responses.

**Verified by:** integration tests; pen test (NFR-S28).

### NFR-S25 — Input validation and error shape *(M)* · NFR-15
Every request DTO is validated with FluentValidation; unknown JSON properties are rejected with
400 (`UnmappedMemberHandling.Disallow`); every string has a maximum length; money is accepted
only as a string decimal with at most 2 decimals. Errors use ProblemDetails (RFC 9457); stack
traces and SQL never appear in responses.

**Verified by:** contract tests per endpoint with unknown fields, over-long strings and malformed money.

### NFR-S26 — Authorisation and object access *(M)* · FR-S71
Every staff endpoint checks the role server-side (RUL-62 matrix). **Segregation of duties:** `admin`
does not implicitly refund, allocate or reveal beneficiary PII; those need `finance` or
`allocation_officer` in `staff_user.roles` explicitly. Beneficiary PII is decrypted only for
`allocation_officer`, and each reveal is audited. Customer endpoints return **404**, not 403, for
another customer's order, document, download or enrolment. `GET /v1/orders/{ref}/status` requires
the customer's bearer token or the per-order status token (HMAC of the order id) returned by
`POST /v1/checkout`; the reference alone never reveals an order.

**Verified by:** authorisation test matrix (role × endpoint, including `admin` alone → 403 on refund, allocation and PII reveal) generated from the endpoint registry; IDOR tests with two customers; status endpoint tested with no token, a wrong token and a valid token.

### NFR-S27 — Secrets handling *(M)* · NFR-16
No secret in Git: connection strings, gateway keys, webhook secrets, SMS credentials, JWT signing
keys and the OTP pepper come from environment or Docker secret files on the VPS. Data Protection
keys are persisted to the files volume and backed up (03-database-operations §4). Secrets are
rotated at launch and after any staff departure with access.

**Verified by:** GitHub secret scanning and push protection on `ta3lom-store-api`; gitleaks in CI; pre-launch rotation checklist.

### NFR-S28 — Penetration test before launch *(M)*
An external penetration test of the store API, the store and admin routes of ta3lom.sa, and the
VPS, covering OWASP Top 10, payment and webhook flows, OTP abuse and IDOR. Launch requires **zero
open critical or high findings**; mediums have a dated fix plan.

**Verified by:** pen-test report and retest letter filed with the release.

### NFR-S29 — Dependency and image hygiene *(M)* · NFR-17
CI fails on known vulnerabilities of **high or critical** severity in NuGet packages
(`dotnet list package --vulnerable --include-transitive`), npm packages (`npm audit`) and the
container image (Trivy). Dependabot pull requests are reviewed weekly. Base images are the
chiselled .NET runtime and the official `postgres:18`, pinned by digest.

**Verified by:** CI jobs; weekly review log.

### NFR-S30 — Unguessable public tokens *(M)* · RUL-22, RUL-41
Dedication `card_token` and cart ids have at least 128 bits of randomness; signed download URLs
expire after 10 minutes and are bound to the grant. Card pages are `noindex` and never show
amounts.

**Verified by:** unit tests on token generation; integration test that an expired or tampered URL returns 403.

---

## Privacy (Saudi PDPL)

### NFR-S31 — Notice and consent *(M)*
A bilingual privacy notice is published before launch and linked from checkout and the OTP
screen. Consent time is stored in `customer.pdpl_consent_at` at first sign-in. Marketing consent is
separate, off by default (`marketing_opt_in = false`), and changeable in the profile. No analytics
or advertising cookies by default (as NFR-32); the store adds only the functional cookies
`t3s_rt` and `t3s_cart`.

**Verified by:** checklist review; Playwright test that a new session has only those two cookies from the store.

### NFR-S32 — Data minimisation *(M)* · 01 §6
Only the fields in the DDL are collected. Donors never see beneficiary identities: public and
customer endpoints expose group, region and status only (RUL-50). IP addresses are stored only as
salted SHA-256 hashes (`ip_hash`), as NFR-31. Logs mask mobiles (`05•••••567`) and never contain
OTP codes, tokens or full names of beneficiaries.

**Verified by:** API response schema review; automated log scan on staging for unmasked mobiles.

### NFR-S33 — Encryption *(M)*
Beneficiary PII (`full_name_enc`, `mobile_enc`, `guardian_name_enc`) and `staff_user.totp_secret_enc`
are encrypted with ASP.NET Core Data Protection before reaching the database. VPS disk encryption
at rest. Backups are encrypted (`age`) before leaving the server. All traffic is TLS (NFR-S20).

**Verified by:** integration test reading the raw column (ciphertext only); restore drill decrypts a record (NFR-S13).

### NFR-S34 — Data subject rights *(M)* · FR-S27, RUL-61
Export (`GET /v1/me/export`) and deletion (`DELETE /v1/me`, anonymisation) are self-service and
complete immediately. Requests received by other channels are honoured within **30 days** (as
NFR-30). Financial records are retained with identity anonymised.

**Verified by:** integration tests for export content and the anonymisation effects in 03-database-operations §7.

### NFR-S35 — Retention enforced in code *(M)*
The retention table in 03-database-operations §7 is enforced by the `Retention`, `PruneOtp`,
`PruneIdempotencyKeys` and `ExpireGuestCarts` jobs: `otp_challenge` 24 h, guest carts 30 days idle, `notification` 12 months,
processed `outbox_message` 30 days, `idempotency_key` 24 h (`PruneIdempotencyKeys`, hourly), `payment_event.payload` 18 months, `download_log` 12 months,
beneficiary PII active + 2 years, `audit_log` 5 years, financial records 10 years.

**Verified by:** integration tests with a fake `IClock` advancing past each boundary.

### NFR-S36 — Breach response *(M)*
A written incident runbook covers detection, containment, evidence preservation, and notification
of the regulator (SDAIA) **within 72 hours** of becoming aware of a personal-data breach, and of
affected individuals where required. **The 72-hour period and the notification duties are to be
confirmed by the association's legal adviser.**

**Verified by:** runbook reviewed and a tabletop exercise held before launch.

### NFR-S37 — Data residency *(M)* · Q-08
Hosting location of the database, backups and files complies with the association's answer to
Q-08. Until Q-08 closes, no production personal data is loaded.

**Verified by:** Q-08 sign-off recorded in 01 §7 before Phase 3.

---

## E-commerce law (Saudi E-Commerce Law and implementing regulations)

### NFR-S38 — Seller disclosures *(M)* · 01 §6
Visible in the store footer and on checkout, in both languages: the association's name, licence
number, national address, contact channels (`store@ta3lom.sa`, phone), **commercial registration
or equivalent licence**, and the **VAT number** when registered (Q-02). Product pages show the
total price in SAR including VAT where applicable; the cart shows shipping before checkout.

**Verified by:** legal checklist signed by the association before launch; Playwright assertion that the footer and checkout show the licence number and, when `vat_enabled`, the VAT number.

### NFR-S39 — Policies before purchase *(M)* · RUL-57, Q-07
The refund and return policy, terms and the privacy notice are linked on product pages and
checkout, and must be accepted (checkbox) before payment. The acceptance time is stored in
`orders.terms_accepted_at` (NOT NULL) as evidence. Physical goods show the **7-day
withdrawal right** (RUL-57: full refund including shipping before shipment; within 7 days of
delivery, unused, shipping not refunded unless faulty). Exceptions (digital book once
downloaded, course over 20 % progress, gifts after 48 hours) are stated before purchase.

**Verified by:** Playwright test that checkout cannot submit without acceptance; integration test that the API rejects a checkout without acceptance and stores `terms_accepted_at`; content review against Q-07 wording.

### NFR-S40 — Order confirmation record *(M)*
After payment the customer receives the order reference, the lines, totals, and the invoice or
donation receipt by SMS and email (RUL-63), and can download them later (FR-S26).

**Verified by:** end-to-end test on staging with the sandbox gateway.

---

## Accessibility

### NFR-S41 — WCAG 2.2 Level AA *(M)* · NFR-07
Every store route in both locales, including cart, checkout, OTP entry, thank-you, account, card
page and the admin store area. Same tools as the main site: axe-core in CI failing on any
violation, and one manual pass with NVDA (Windows) and VoiceOver (iOS) before launch covering the
full purchase, sponsorship and dedication journeys.

**Verified by:** `npm run a11y` in CI; manual test report.

### NFR-S42 — Store-specific accessibility checks *(M)* · NFR-08, NFR-10, NFR-11
| Check | Target |
|---|---|
| Goal bars | `role="progressbar"` with `aria-valuenow` and a text equivalent ("61,250 من 90,000") |
| Quantity steppers, presets, segmented controls | Labelled buttons, 44 × 44 px targets, keyboard operable |
| OTP input | `autocomplete="one-time-code"`, `inputmode="numeric"`, errors announced |
| Form errors (checkout, dedication) | Linked to fields with `aria-describedby`; focus moves to the first error |
| Price changes and cart updates | Announced through a polite live region |
| Motion | Accordion auto-advance, count-up and bar animations off under `prefers-reduced-motion` |
| Headings | Exactly one `h1` per route |
| Hosted payment page | Gateway chosen in Q-01 is checked for keyboard and screen-reader use; findings recorded |

**Verified by:** axe-core plus Playwright keyboard-only test of each journey.

---

## Internationalisation and RTL

### NFR-S43 — Arabic-first, RTL correct *(M)* · NFR-09, BR-S12
Two builds (`/ar`, `/en`) via `@angular/localize`; logical CSS only (lint rule); `pick()` fallback
for English (RUL-03). Mobiles, references (`T3-26-004188`), SKUs and amounts are isolated with
`dir="ltr"`. Numbers use Latin digits as in the prototype. Dates are Gregorian in Asia/Riyadh.
Arabic search normalises both the index and the query with `ar_normalize()` (used by
`product.search_vector` and `ix_product_trgm`).

**Verified by:** Playwright screenshot comparison of every store route in both directions; ESLint RTL rule; search tests for ة/ه, ى/ي, أ/إ/ا variants.

### NFR-S44 — Bilingual messages and documents *(M)* · RUL-59, RUL-63
Every `message_template` key exists for `ar` and `en`; messages go in the order's `locale`.
Invoices and donation receipts are bilingual PDFs with correct Arabic shaping and bidirectional
text.

**Verified by:** startup check failing when a template key lacks a locale; snapshot tests of rendered PDFs (text extraction compared with expected Arabic strings).

---

## Observability

### NFR-S45 — Logs, traces and metrics *(M)* · NFR-22
- **Logs:** Serilog JSON to stdout, one correlation id per request (W3C `traceparent`, returned as
  `X-Correlation-Id`) on every line, carried into outbox messages and jobs. Redacted:
  `authorization`, `cookie`, `t3s_rt`, `t3s_cart`, OTP codes, `card_token`, webhook secrets;
  mobiles masked.
- **Traces:** OpenTelemetry for ASP.NET Core, HttpClient (gateway, SMS, LMS), EF Core/Npgsql,
  Quartz jobs and the outbox dispatcher, exported over OTLP.
- **Metrics:** request rate/latency/errors per endpoint group; `store.settlement.duration`;
  `store.outbox.lag_seconds`; `store.payments.review_count`; job success/failure; SMS sent/failed.

**Verified by:** integration test asserting the correlation id appears in logs of a checkout and its outbox message; log redaction test.

### NFR-S46 — Alerts *(M)*
Alerts go to the staff alert channel and the operator's email:

| Alert | Condition |
|---|---|
| API down | `/health/ready` fails 3 consecutive minutes |
| Error rate | 5xx > 2 % of requests over 5 minutes |
| Webhook failures | Any webhook 5xx, or > 5 invalid signatures in 10 minutes |
| Payment review | Any order enters `payment_review` |
| Reconciliation | `ReconcilePayments` (every 5 min) fails twice in a row, or payments `initiated` > 15 min exist |
| Outbox backlog | > 500 pending or oldest > 30 minutes |
| SMS failures | > 10 % failed in 15 minutes, or OTP volume anomaly (NFR-S22) |
| Integrity check | Any nightly check returns a row (NFR-S57) |
| Backups | Any backup or WAL archive failure; last successful dump older than 7 h |
| Disk / DB | Disk > 80 %; connections > 80 % of `max_connections` |
| Certificates | TLS certificate expires in < 14 days |
| Allocation SLA | Units awaiting > 30 days (RUL-49) |
| Low stock | `LowStockAlert` (08:00): available ≤ `book_format.low_stock_threshold`, else `low_stock_default_threshold` (20) |
| Duplicate enrolment | Settlement marks a course line for refund (RUL-13) |
| Late capture | Capture on an `expired`/`cancelled` order (RUL-35) |

**Verified by:** each alert fired once on staging and acknowledged, recorded in the launch checklist.

### NFR-S47 — Health endpoints *(M)* · FR-S83
`/health/live` (process up) and `/health/ready` (PostgreSQL reachable, outbox lag < 30 min)
respond in ≤ 200 ms and expose no internal detail publicly.

**Verified by:** integration tests; uptime monitor.

---

## Maintainability

### NFR-S48 — Test coverage *(M)*
Line coverage: **`Ta3lom.Store.Domain` ≥ 90 %**, **`Ta3lom.Store.Application` ≥ 80 %**. Every rule
`RUL-01` … `RUL-63` that the code enforces has at least one test named with its ID. Endpoints are
covered by `Ta3lom.Store.Api.IntegrationTests` (WebApplicationFactory + Testcontainers
PostgreSQL 18). The store feature in `ta3lom-web` has Vitest unit tests for the cart store, buy
box calculations and interceptors, and Playwright tests for the journeys in 05.

**Verified by:** coverlet thresholds failing the CI build; a CI script that lists RUL IDs without a test.

### NFR-S49 — Architecture rules *(M)* · ADR-S08
`Ta3lom.Store.ArchitectureTests` (NetArchTest) enforce:
- `Domain` references no other project and no EF Core, ASP.NET Core or HTTP types;
- `Application` references `Domain` only (and abstractions), never `Infrastructure` or `Api`;
- `Contracts` has no dependencies on other projects;
- modules do not reference another module's internal types (only its public application interfaces);
- no reference to MediatR; handlers implement `ICommandHandler<TCommand,TResult>`/query equivalents;
- `double`/`float` are not used for money types.

**Verified by:** architecture test project in CI.

### NFR-S50 — Zero warnings *(M)* · NFR-27
.NET: `TreatWarningsAsErrors`, nullable reference types enabled, .NET analyzers at `latest-recommended`.
Angular: TypeScript `strict`, no `any` outside declaration files, ESLint clean including the
ta3lom-web CLAUDE.md rules (logical CSS, no `bypassSecurityTrust*`, token in signal, component prefix `ta-`). CI fails on
any warning.

**Verified by:** CI build logs.

### NFR-S51 — API contract discipline *(M)* · NFR-27
OpenAPI is generated from `Ta3lom.Store.Contracts`; TypeScript types in `ta3lom-web` are generated
from it, never hand-written. A breaking change to a published endpoint fails CI unless the version
changes.

**Verified by:** oasdiff breaking-change check in CI; generated types diff check.

### NFR-S52 — Migration safety *(M)* · NFR-21
Migrations are applied by an idempotent script before the container swap; destructive changes use
expand → migrate → contract across two releases; `001_store_schema.sql` and the data dictionary are
regenerated with each migration.

**Verified by:** CI applies all migrations to an empty PostgreSQL 18 and to a copy of the previous release's schema; staging rehearsal before production.

### NFR-S53 — Onboarding *(S)* · NFR-29
A developer new to `ta3lom-store-api` runs the API with seed data and the store feature of
`ta3lom-web` locally within **one hour**, using only the README (`docker compose up`).

**Verified by:** a person outside the project attempts it before handover.

### NFR-S54 — Documented decisions *(M)* · NFR-28
Every hard-to-reverse store decision has an ADR-S in the ADR-000 format.

**Verified by:** review at each phase gate.

---

## Compatibility

### NFR-S55 — Browsers *(M)* · NFR-23
Latest two versions of Chrome, Safari, Firefox and Edge; Safari on iOS 16+; Chrome on Android 10+.
Apple Pay offered only where the browser supports it.

**Verified by:** Playwright on Chromium, WebKit and Firefox; manual Apple Pay test on iOS Safari with the gateway sandbox.

### NFR-S56 — Mobile first *(M)* · NFR-24
Store screens are designed at **360 px** first; every store route works from 320 px to 2,560 px
with no horizontal scroll. At 360 px the cart summary, checkout pay button and buy-box total are
reachable without horizontal scrolling or zoom, and the product page shows a sticky total and
add button as in the prototype.

**Verified by:** Playwright at 320, 360, 375, 768, 1024, 1440 and 1920 px in both locales.

---

## Data integrity

### NFR-S57 — Nightly integrity checks *(M)* · 03-database-operations §6
The `IntegrityChecks` job at 04:00 runs every check in `003_integrity_checks.sql`; each must return
**zero rows**. Check 6 (goal counters) includes `goal_adjustment` rows, so pool sweeps
(`pool_sweep_out`/`pool_sweep_in`) and corrections reconcile (RUL-46). Any row raises an alert (NFR-S46) with the offending references.

**Verified by:** job history; the same checks run after every integration-test suite and after the load test (NFR-S08).

### NFR-S58 — Exact money *(M)* · RUL-27, RUL-31
Money is `numeric(12,2)`/`numeric(14,2)` in PostgreSQL, `decimal` in C#, string decimals in JSON.
VAT is computed per line with `round(amount × rate ÷ (100 + rate), 2)` (e.g. 350.00 at 15 % →
45.65). `grand_total = purchases_total + shipping_total + gifts_total` always holds
(`ck_order_total`).

**Verified by:** property-based tests on VAT and totals; architecture test forbidding `double` for money (NFR-S49).

### NFR-S59 — Database-enforced invariants *(M)* · database design §1
The CHECK constraints and partial unique indexes of `001_store_schema.sql` exist in every
environment and match the EF Core model.

**Verified by:** CI compares the migrated schema with `001_store_schema.sql` (schema diff = empty).

---

## Auditability

### NFR-S60 — Audit trail *(M)* · FR-S78, BR-S11
Every staff-originated change writes an `audit_log` row (actor email, action, entity, id, diff,
`ip_hash`) through the EF Core `SaveChangesInterceptor`. Security events are also audited: staff
login success/failure, lockout, refresh-token reuse, PII reveal, role changes, setting changes,
refunds. `audit_log`, `payment_event`, `zakat_ledger`, `stock_movement` and
`sponsored_unit_event` are append-only for `store_app` (INSERT, SELECT only). Retention 5 years.

**Verified by:** integration test per admin endpoint asserting one audit row; grant test that UPDATE/DELETE on append-only tables fails for `store_app`.

### NFR-S61 — Financial traceability *(M)* · BR-S11, OBJ-5
For any riyal, finance can trace `payment` → `orders` → `order_line` → `sponsored_unit` /
`pool_contribution` / `unit_funding` → `beneficiary` code, or → `zakat_ledger`, without leaving
the admin area or using a spreadsheet.

**Verified by:** acceptance demo on staging with the seed plus a scripted day of orders, performed by the finance officer (P10).

---

## Content operations

### NFR-S62 — Publish latency *(M)* · FR-S07, NFR-34
Price, stock and goal changes are live to API consumers **immediately**. Prerendered HTML reflects
them within **2 minutes** of a manual rebuild or within 24 hours automatically.

**Verified by:** the FR-S07 acceptance test.

### NFR-S63 — Staff autonomy *(M)* · BR-S10, NFR-35
The store manager (P9) publishes a new cause with a unit, target, end date and image, and posts a
delivery update with a photo, **unaided in under 15 minutes**, using only the handover guide.

**Verified by:** observed session before sign-off.

---

## Added in r1

### NFR-S64 — Idempotent commands *(M)* · FR-S32, FR-S64
`POST /v1/checkout` and admin refunds require an `Idempotency-Key` header. The key is stored in
`idempotency_key` (primary key `scope` + `key`, e.g. `checkout:{customerId}`, `refund:{staffId}`)
with the SHA-256 `request_hash`, `status_code` and `response`:

| Case | Behaviour |
|---|---|
| Same key, same body, first request finished | The stored response is returned; no second order, refund or gateway call |
| Same key, same body, first request still running | 409, client retries |
| Same key, different body | 422, nothing created |
| Missing header | 400 |

Keys are kept **24 hours** and deleted by `PruneIdempotencyKeys` (hourly). The gateway webhook
path relies on `payment_event (provider, provider_event_id)` instead (NFR-S19).

**Verified by:** integration tests for each case, including two parallel identical checkouts creating exactly one order and one gateway payment.

---

## Summary

| ID | Area | Requirement | Pri | Target | Verified by |
|---|---|---|---|---|---|
| NFR-S01 | Performance | Catalogue API | M | p95 ≤ 200 ms @ 50 users | k6 |
| NFR-S02 | Performance | Cart API | M | p95 ≤ 250 ms | k6 |
| NFR-S03 | Performance | Checkout | M | p95 ≤ 400 ms server, ≤ 1.5 s end to end | k6 + traces |
| NFR-S04 | Performance | Settlement transaction | M | p95 < 500 ms | Integration test + metric |
| NFR-S05 | Performance | Webhook acknowledgement | M | p95 ≤ 1 s, p99 ≤ 3 s | k6 |
| NFR-S06 | Performance | Prerendered pages | M | NFR-01/02/04/06 | Budgets + Lighthouse CI |
| NFR-S07 | Performance | Client-rendered routes | S | LCP ≤ 2.5 s; chunks ≤ 120 KB | Lighthouse CI + budgets |
| NFR-S08 | Capacity | Ramadan peak | M | 200 checkouts, 50 webhooks/s | k6 peak + integrity checks |
| NFR-S09 | Capacity | Hot-row contention | M | No lost update @ 50/s | Integration test |
| NFR-S10 | Capacity | No oversell / double grant | M | Zero | Concurrency tests |
| NFR-S11 | Capacity | Year-1 volume | S | Targets hold at 50k orders | Load test |
| NFR-S12 | Availability | Uptime | S | 99.5 % monthly | Uptime monitor |
| NFR-S13 | Availability | Recovery | M | RPO 15 min, RTO 4 h | Monthly restore drill |
| NFR-S14 | Availability | Degradation | M | No blank page; `paid` ≤ ~10 min without webhooks | Playwright + integration |
| NFR-S15 | Availability | Notification latency | S | 95 % ≤ 60 s; never sent twice | Metric + alert + integration |
| NFR-S16 | Availability | Deploy interruption | S | ≤ 30 s | Uptime during deploy |
| NFR-S17 | Security | OWASP ASVS | M | Level 2 | Checklist + pen test |
| NFR-S18 | Security | PCI DSS | M | SAQ-A | Review + log scan + SAQ |
| NFR-S19 | Security | Webhook integrity | M | 401 invalid, 200 replay no-op | Integration tests |
| NFR-S20 | Security | TLS and headers | M | TLS ≥ 1.2, grade A | SSL Labs + header scan |
| NFR-S21 | Security | Tokens | M | 15/10 min access; rotating refresh | Integration tests + lint |
| NFR-S22 | Security | OTP abuse | M | RUL-60 limits + anomaly alert | Integration tests |
| NFR-S23 | Security | Rate limits | M | Table in NFR-S23 | Integration tests |
| NFR-S24 | Security | Staff auth | M | TOTP, lockout, strong hash | Integration tests + pen test |
| NFR-S25 | Security | Input validation | M | 400 on unknown fields; RFC 9457 | Contract tests |
| NFR-S26 | Security | Authorisation / IDOR / SoD | M | 404 on foreign objects; admin ≠ finance/allocation | Role matrix + IDOR tests |
| NFR-S27 | Security | Secrets | M | None in Git | Secret scanning + gitleaks |
| NFR-S28 | Security | Pen test | M | Zero open critical/high | Report + retest |
| NFR-S29 | Security | Dependencies & image | M | No high/critical | CI scans |
| NFR-S30 | Security | Public tokens | M | ≥ 128 bits; 10-min URLs | Unit + integration |
| NFR-S31 | Privacy | Notice & consent | M | Consent stored; 2 functional cookies | Review + Playwright |
| NFR-S32 | Privacy | Minimisation | M | No beneficiary identity to donors | Schema review + log scan |
| NFR-S33 | Privacy | Encryption | M | PII columns encrypted | Integration + drill |
| NFR-S34 | Privacy | Data subject rights | M | Self-service; ≤ 30 days otherwise | Integration tests |
| NFR-S35 | Privacy | Retention | M | 03-ops §7 | Clock-driven tests |
| NFR-S36 | Privacy | Breach response | M | SDAIA ≤ 72 h (legal to confirm) | Runbook + tabletop |
| NFR-S37 | Privacy | Data residency | M | Per Q-08 | Q-08 sign-off |
| NFR-S38 | E-commerce | Seller disclosures | M | Identity, licence, VAT no. | Legal checklist + Playwright |
| NFR-S39 | E-commerce | Policies & withdrawal | M | Accepted before pay; 7 days physical | Playwright + review |
| NFR-S40 | E-commerce | Confirmation record | M | Reference + documents sent | E2E staging |
| NFR-S41 | Accessibility | WCAG 2.2 AA | M | Zero axe violations | axe-core + NVDA/VoiceOver |
| NFR-S42 | Accessibility | Store-specific checks | M | Table in NFR-S42 | axe + Playwright keyboard |
| NFR-S43 | i18n | Arabic-first RTL | M | No RTL defect | Screenshots + lint |
| NFR-S44 | i18n | Bilingual messages & PDFs | M | Every key in ar and en | Startup check + PDF tests |
| NFR-S45 | Observability | Logs, traces, metrics | M | Correlation id end to end | Integration test |
| NFR-S46 | Observability | Alerts | M | Table in NFR-S46 | Fired once on staging |
| NFR-S47 | Observability | Health endpoints | M | ≤ 200 ms | Integration + monitor |
| NFR-S48 | Maintainability | Coverage | M | Domain ≥ 90 %, Application ≥ 80 % | coverlet in CI |
| NFR-S49 | Maintainability | Architecture tests | M | Rules in NFR-S49 | NetArchTest |
| NFR-S50 | Maintainability | Zero warnings | M | 0 | CI |
| NFR-S51 | Maintainability | API contract | M | No unversioned breaking change | oasdiff |
| NFR-S52 | Maintainability | Migrations | M | Idempotent; expand/contract | CI + staging |
| NFR-S53 | Maintainability | Onboarding | S | ≤ 1 hour | Observed attempt |
| NFR-S54 | Maintainability | ADRs | M | Every hard-to-reverse decision | Gate review |
| NFR-S55 | Compatibility | Browsers | M | NFR-23 list | Playwright + manual Apple Pay |
| NFR-S56 | Compatibility | Mobile first | M | 360 px design; 320–2560 px | Playwright viewports |
| NFR-S57 | Data integrity | Nightly checks | M | Zero rows | Job + CI |
| NFR-S58 | Data integrity | Exact money | M | numeric/decimal/string | Property tests |
| NFR-S59 | Data integrity | DB invariants | M | Schema diff empty | CI schema compare |
| NFR-S60 | Auditability | Audit trail | M | One row per staff change | Integration + grants |
| NFR-S61 | Auditability | Financial traceability | M | Payment → beneficiary | Finance demo |
| NFR-S62 | Content ops | Publish latency | M | Live now; HTML ≤ 2 min after rebuild | FR-S07 test |
| NFR-S63 | Content ops | Staff autonomy | M | < 15 min unaided | Observed session |
| NFR-S64 | Security / integrity | Idempotent commands | M | One effect per key; 24 h | Integration tests |

### Verification methods

| Method | Requirements |
|---|---|
| Build budgets and Lighthouse CI | NFR-S06, NFR-S07 |
| k6 load tests | NFR-S01–S03, NFR-S05, NFR-S08, NFR-S11 |
| Integration tests (Testcontainers) | NFR-S04, NFR-S09, NFR-S10, NFR-S14, NFR-S15, NFR-S19, NFR-S21–S26, NFR-S30, NFR-S33–S35, NFR-S45, NFR-S47, NFR-S60, NFR-S64 |
| Architecture tests, coverage, CI scans | NFR-S27, NFR-S29, NFR-S48–S52, NFR-S58, NFR-S59 |
| Playwright and axe-core | NFR-S14, NFR-S31, NFR-S38, NFR-S39, NFR-S41–S43, NFR-S55, NFR-S56 |
| External scanners, monitors, pen test | NFR-S12, NFR-S17, NFR-S18, NFR-S20, NFR-S28 |
| Drills, runbooks and observed sessions | NFR-S13, NFR-S36, NFR-S53, NFR-S61, NFR-S63 |
| Legal / association sign-off | NFR-S36–S39 |

---

## Open points

| # | Point | Status | Affects |
|---|---|---|---|
| OP-06-1 | **Breach notification.** The 72-hour SDAIA notification period and the individual-notification threshold are working assumptions. | Open (legal adviser) | NFR-S36 |
| OP-06-2 | **Commercial registration.** A licensed non-profit may not hold a CR. Legal must confirm which identifiers (NCNP licence number, unified national number, VAT number) satisfy the E-Commerce Law disclosure. The prototype footer shows "ترخيص رقم 1234" and "الرقم الضريبي 3000000000003" as placeholders. | Open (legal) | NFR-S38 |
| OP-06-3 | No record of terms acceptance on `orders`. | **Resolved — FR-S30** (`orders.terms_accepted_at timestamptz NOT NULL`) | NFR-S39 |
| OP-06-4 | Low-stock threshold had no setting. | **Resolved — RUL §M** (`low_stock_default_threshold`, `book_format.low_stock_threshold`). The **SMS-anomaly factor** (3 × trailing 7-day hourly average) in NFR-S22 is still an operational default to confirm. | NFR-S22, NFR-S46 |
| OP-06-5 | **Observability backend.** Traces and metrics are exported over OTLP; the receiving tool (self-hosted on the VPS or a hosted service) is not yet chosen, and a hosted service outside KSA interacts with Q-08. | Open | NFR-S45 |
| OP-06-6 | Reconciliation window vs. FR-S34. | **Resolved — RUL-38** (every 5 min, `initiated` > 5 min; `paid` within about 10 minutes) | NFR-S14 |
| OP-06-7 | **Peak figures.** 200 concurrent checkouts and 50 webhooks/s are planning assumptions for a Ramadan campaign; confirm with the association's past campaign data. If the VPS fails NFR-S08, the options are a larger VPS plan or moving PostgreSQL to its own VPS (ADR-S03 follow-up). | Open | NFR-S08 |
| OP-06-8 | **FR-S71 vs. RUL-62.** Resolved — FR-S71 reworded to follow RUL-62 segregation of duties. | Resolved | NFR-S26 |
| OP-06-9 | **In-flight idempotent request.** The 409 behaviour for a key whose first request is still running (NFR-S64) is not stated in the canonical docs; confirm in 11-api-contract. | Open | NFR-S64 |
