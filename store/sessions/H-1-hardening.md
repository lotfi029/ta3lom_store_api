# H-1 — code-side hardening (close-out, 30 Sep 2026)

Cloud-build-plan §4c item 10 and plan 14 §12/§15, local only (no staging, no VPS). Everything below was run on the cloud
container: 4 vCPU, 15 GB, PostgreSQL 16 on the same machine, API from a Release build unless stated.

## 1. STRIDE walk (08 §6.2)

| Area | Threat | Control in code | Result |
|---|---|---|---|
| Checkout | S: order as someone else | `POST /v1/checkout` needs the customer bearer (OTP-verified, RUL-25/60); `PlaceOrderCommand` refuses a blocked customer (`auth.account_blocked`); `VerifyOtp` and `RefreshSession` refuse blocked accounts (RUL-61) | ✅ |
| | S: double submit | `Idempotency-Key` filter + `idempotency_key` (24 h, `PruneIdempotencyKeys`) | ✅ |
| | T: client price edits | server reprices every line, `expectedGrandTotal` checked (RUL-15/24), money `numeric(12,2)` + CHECKs | ✅ |
| | R: "I never ordered" | order, lines and snapshots; `audit_log.ip_hash`, `otp_challenge.ip_hash`, `customer.last_login_at` | ✅ — the DDL has no `orders.ip_hash` (08 mentions one); database/ wins, logged |
| | I: status by guessing refs | `GET /v1/orders/{ref}/status` needs the bearer or the HMAC status token (`IOrderStatusTokens`) | ✅ |
| | D: stock hoarding | 60-min expiry job, cart limits (RUL-18), `checkout` rate policy | ✅ |
| | E: draft/archived product, owned course | server validation (RUL-02/24), duplicate-course check (RUL-13) | ✅ |
| Webhook | all | payment service (doc 15); here only the dev trigger, 404 in Production | n/a — verified 404 |
| OTP | S: interception | 5-min codes, 5 attempts, HMAC + pepper, OTP SMS sent directly (never stored) | ✅ |
| | I: mobile enumeration | uniform `202` for any valid mobile | ✅ |
| | D: SMS pumping | `+9665` only, 3/mobile/15 min, 10/IP/hour, **new: store-wide daily cap `OtpDailyGlobalCap` (2000/Riyadh day) → 429 + one `FinanceAlert otp_daily_cap` per day**; Turnstile/proof-of-work is open point OP-4 | ✅ fixed (cap was missing) |
| | E: invite hijack | customer row created at allocation, invite carries no credential (RUL-47e/52) | ✅ |
| Admin | S: stolen password | mandatory TOTP, lockout, 10-min access token, `t3s_srt` refresh | ✅ |
| | T: unauthorised edits | role policies on every admin route (anonymous probe: 100/100 admin + me routes → 401; multipart ones 401 with a multipart body) | ✅ |
| | R: disputed staff action | `audit_log` with actor email + masked diff (refunds n/a, allocations, vouchers, reveals, templates, settings) | ✅ |
| | I: PII browsing | masked DTOs; reveal = `allocation_officer` + audit (FR-S52) | ✅ |
| | D: admin hammering | `admin` sliding limit per subject/IP — **8 admin groups added after S-09 had none; now all admin groups carry it** | ✅ fixed |
| | E: role escalation | only `admin` edits roles; SoD: admin cannot allocate / reveal (e2e: admin allocate → 403) | ✅ |
| PII | I: DB dump | beneficiary name/mobile/notes and TOTP secrets through Data Protection (`PiiProtector`, `ProtectedStringConverter`); `mobile_hash` keyed | ✅ (backup encryption = hosting) |
| | I: logs | `RedactingEnricher`; no `EnableSensitiveDataLogging`; a Debug-level e2e log and an Information-level load log contain 0 unmasked mobiles; `Authorization`/`Set-Cookie` values `***` | ✅ |
| | E: app DB role | `store_app` grants are a hosting item (database/03 §3) | deferred (hosting) |

Other findings fixed in the close-out: an unbindable request body answered 500 (now 400 `validation.failed`);
`create-admin` failed under `ASPNETCORE_ENVIRONMENT=Development`.

## 2. Vulnerable packages

`dotnet list Ta3lom.Store.slnx package --vulnerable --include-transitive` → no vulnerable package in any of the 10 projects
(CI runs the same scan and fails on High/Critical).

## 3. Production configuration

`ASPNETCORE_ENVIRONMENT=Production`, API on a scratch port:

| Check | Result |
|---|---|
| No keys configured | start refused: every missing key listed (`OptionsValidationException`) |
| A key equal to its DEV ONLY value | start refused: "`Store:Jwt:CustomerSigningKeyBase64` must not be the DEV ONLY value in Production" |
| Random 32-byte keys | starts; `/health/live`, `/health/ready` 200 |
| `/docs`, `/openapi/v1.json` | 404 |
| `/dev/jobs`, `/dev/sms`, `/dev/mail`, `POST /dev/orders/{ref}/confirm-payment` | 404 |
| Start-up seed | off by `Store__Seed__RunOnStartup=false` |

## 4. Security headers

Every response (errors included): `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`,
`X-Frame-Options: DENY`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, **new**
`Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()`; `Cache-Control: no-store` on every credentialed
prefix; no `Server` header. HSTS is sent by `UseHsts` in Production over HTTPS (TLS ends at Caddy).

## 5. Load smoke (`tools/load/load-smoke.py`; k6 is not installable here)

| Scenario | Result | Target |
|---|---|---|
| Catalogue, 30 threads, 3000 requests (home, list, detail) | ~960 req/s, p50 27.7 ms, **p95 62.4 ms**, p99 88.2 ms, 0 errors | NFR-S01 p95 ≤ 200 ms ✅ |
| 1 checkout | 41 ms | |
| 20 concurrent checkouts | **p95 145 ms**, 0 errors | NFR-S03 p95 ≤ 400 ms ✅ |
| 50 checkouts released at the same instant | p95 791 ms, 50/50 `pending_payment`, 0 errors | see note |
| Burst of 50 settlements through the dev trigger | all 50 settled in 1.07 s, p95 1.02 s, 0 errors; integrity checks 0 rows | NFR-S04 (per transaction) |

Note: the 50-at-once numbers are CPU-bound on this box (API, PostgreSQL and the Python load client share 4 vCPU; 50 × ~30 ms
of CPU per checkout ≈ the observed queueing). The settlement burst is serialised by design on the gapless
`document_counter` row (RUL-59): `pg_stat_statements` shows the counter `UPDATE` with 280 ms mean *lock wait*, every other
statement < 1 ms. NFR-S08 (200 concurrent checkouts for 10 min) needs k6 on staging sized like production — hosting item.

## 6. Index tuning (`pg_stat_statements`)

`pg_stat_statements` enabled on the local cluster (`shared_preload_libraries`, extension in the dev DB — local only, not
in a migration). After the e2e scenario and the load runs: the top statements by total time are the document counter
(lock wait, by design) and inserts; every read is < 1 ms mean. `pg_stat_user_tables` shows sequential scans only on tables
with < 100 rows, where the planner is right to prefer them. **No index added**; re-check on staging with production-like
volumes (the reference DDL already carries the FIFO, outbox, notification and order indexes of database/01 §5).

## 7. Plan 14 §15 — code-side items

| Item | Status |
|---|---|
| `/dev/orders/{ref}/confirm-payment` 404 in production | ✅ (§3) |
| No `/dev` endpoint in production; `/docs` disabled | ✅ (§3) |
| `create-admin` works; TOTP enrolment at first login; SoD | ✅ (e2e scenario; fixed for Development) |
| Migration backward-compatible (expand/contract) | ✅ all migrations additive |
| Integrity checks 0 rows | ✅ after the e2e scenario and the load runs |
| RUL coverage test, policy matrix | test phase (T-5, T-6) |
| Payment launch gate, SMS sender ID, SPF/DKIM, backups, monitoring, rollback, settings review, goal seeds | owner / hosting / payment service |
