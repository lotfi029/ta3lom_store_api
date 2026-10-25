# R5-Core — progress log

Specs: `.session/01-api-business-requirements.md`, `02-api-erd.md`, `03-api-rebuild-plan.md` (precedence 01 > 02 > 03 > CLAUDE.md).
Approved plan: v3 (owner review R-1…R-12 applied).

## Phase status

| Phase | Status | Build | Format | Tests | Notes |
|---|---|---|---|---|---|
| P1 clean repo | done | green | clean | not run (old suite references deleted features; P2 replaces it) | |
| P2 strip | done | green | clean | Domain, Application, Architecture green; integration suite cannot run until P3 (no migration → fixture seed fails), as the plan's P2 gate allows | |
| P3 model + InitialSchema | done | green | clean | 337 passed / 0 failed (Domain 118) on postgres:18 | migration Up/Down/Up checked by hand; `has-pending-model-changes`: none |
| P4 auth | done | green | clean | 377 passed / 0 failed | donor OTP + sessions, staff password/TOTP/forced change, staff admin |
| P5 catalogue | done | green | clean | 403 passed / 0 failed | public catalogue, admin categories/projects/options, media, ETag/output cache, site rebuild |
| P6 donations | pending | | | | |
| P7 admin ops | pending | | | | |
| P8 hardening | pending | | | | |
| P9 gates | pending | | | | |

## Decisions

Lines marked **REVIEW** are not covered by the specs (or deviate from them) and need the owner's look.

- **REVIEW** Branch: work happens on the session branch `claude/youthful-feynman-yr7h4h` (the environment is bound to it);
  `rebuild/r5-core` was not created (plan P0 was not run). Owner-confirmed (R-12).
- **REVIEW** Packages: the baseline build failed on NU1903 (High) advisories for transitive
  `System.Security.Cryptography.Xml/Pkcs`. Patch bumps: Microsoft.* 10.0.12 → 10.0.13, MailKit 4.18.1 → 4.18.2.
- Compile error left by the owner's dead-code clean-up (commit 83595b5, `AuthErrors.Forbidden`) fixed with
  `GeneralErrors.Forbidden` (owner-confirmed).
- P1: `db/reference/003_integrity_checks.sql` moved to `src/Ta3lom.Store.Infrastructure/Resources/Integrity/` (rewritten in P3).
  Removed tests that read deleted folders: OpenApiSnapshotTests, RuleCoverageTests + ImplementedRules.txt, SchemaParityTests,
  ReferenceScriptsTests. The env-variable list of `deploy/env/store.env.example` is kept in `.session/` for README §Configuration (P8).
- Tests run against Testcontainers `postgres:18` (pulled through `mirror.gcr.io`, Docker Hub was rate-limited);
  `TESTCONTAINERS_RYUK_DISABLED=true` is a shell variable of this session only, never committed (R-11).
- **REVIEW** OTP SMS is sent directly after the challenge commits (only `code_hash` is stored, the plain code is never
  persisted). The outbox carries `StaffAlert`, `CatalogChanged`, `OrderPlaced`, `OrderCancelled` (ERD lists `OtpRequested`
  only as an example type).
- **REVIEW** MFA token single use: the ERD has no column for it, so used `jti`s live behind an `IUsedTokenStore` port
  (IMemoryCache implementation) until the 5-minute token expires. Needs a shared store if the API ever runs as >1 instance (R-6).
- Coverage is measured on the merged Cobertura report of all test projects.
- The cloud environment presets `STORE_TEST_DB=external`; test runs unset it (`env -u STORE_TEST_DB`) so they use
  Testcontainers `postgres:18` as specified.
- P2: the operations path is kept in `Application/Features/Operations` (IntegrityChecks, PruneIdempotencyKeys, Retention,
  plus PruneOtp and ExpireGuestCarts as set-based deletes through `IOperationsMaintenance`, so the jobs work without the
  Cart aggregate) (R-1).
- P2: `UploadMedia`, `KeyOptions`, `SkiaImageProcessor`, `MediaAsset`, `Notification`, CORS and `/dev` were brought to their
  final shape while touching them (magic-byte sniff, 40 MP limit, orphan-file cleanup; DEV/missing keys refused outside
  Development; `/dev` Development only; allow-list store + store-staging, localhost in Development).
- P2: the beneficiary PII protector is gone; the hashing key is now `Store:Hashing:KeyBase64` (IPs, recipient hashes).
  `Store:Downloads:*` and `Store:Pii:*` are no longer read.
- P3: `InitialSchema` follows the R-2 order by hand edits of the generated file. The two extensions are declared unqualified
  in the model (Npgsql's snapshot drops an extension schema, which would leave a permanent pending change) and created
  `SCHEMA public` by the migration's raw SQL.
- **REVIEW** P3: the old DDL's `ar_normalize` mapped ة→ي and ى→ه (its `translate` target had 7 characters for 6). The port
  maps alef forms → ا, ة → ه, ى → ي, which is what the comment and the search need.
- P3: aggregates return `Result` for expected business outcomes (cart limits D-03/D-04, publish D-06, transitions D-07,
  cancel D-09) and throw `DomainException` only for invariant violations (bugs).
- P3: `v_catalog_stats` gives, per project and per category, the number of pending orders and their pending amount (the
  dashboard's "top projects by pending amount").
- P3: deleting a donor also anonymises the contact snapshot on their orders (name, e-mail, mobile); amounts stay (D-14).
- P3: Development demo projects avoid the words the P9 grep forbids: `print-a-title` (طباعة كتاب), `support-a-daiya`
  (كفالة داعية), `dawah-umrah`, `water-wells`, `iftar-meals`, `learner-support`.
- P4: the `pwd_change` limit is an authorization requirement on every staff policy; only `POST /v1/admin/auth/password`
  (its own policy) and logout (cookie only) stay open, and a refused call answers 403 `auth.password_change_required`.
  `GET /v1/admin/me` is closed too while the change is pending (plan: only password and logout).
- P4: staff login verifies the password before looking at the lock: a wrong password answers 401 `auth.invalid_credentials`
  whether the account is locked, unknown or inactive; the right password of a locked account answers 423. Failures while
  already locked do not extend the lock. Unknown e-mails cost one full PBKDF2 run (`SimulateVerify`), so timing is the same.
- P4: hashes with fewer than 600,000 iterations (or another PRF) still verify and are rehashed at the next sign-in.
- **REVIEW** P4: the common-password list (`Application/Features/StaffAuth/Resources/common-passwords.txt`) holds 56 entries
  of ≥ 12 characters plus a "single repeated character" rule; the owner may want a larger list.
- P4: a password change (and an admin reset) revokes every refresh family of the member; the change returns a fresh session.
- P4: a donor refresh token lives at most 30 days inside the 60-day family (expires_at = min(now + 30 d, family end)).
- P4: the 50 % OTP alert is one `StaffAlert` per Riyadh day (`otp_volume_half_cap`); at the cap requests get 429 until midnight.
- P4: package `Microsoft.Extensions.Caching.Memory` 10.0.13 added for the `IUsedTokenStore` (owner R-6).
- **REVIEW** `notification` rows are claimed as `failed` (= not delivered yet) before the provider call and set to `sent`
  after it; the ERD allows only `sent | failed`, so there is no `queued` state.
- P5: an old slug answers `200 { "redirectTo": "<slug>" }` (the web site does the 301); it stays reserved for every project.
- P5: `GET /v1/catalog/projects?fields=slug` lists every published slug (no paging); any other `fields` value is 400.
- P5: the output cache varies by `q, category, sort, page, pageSize, fields` only; any other query key shares the entry.
  Every admin write to a category, project, option, media alt text or setting evicts the `catalog` tag.
- **REVIEW** P5 site-rebuild debounce: a handled `CatalogChanged` absorbs the pending rows no dispatcher has claimed, and
  only the newest change (by occurred_at, id) triggers `repository_dispatch`; older ones finish without a call. A claimed
  row (in flight on another instance) is never absorbed. Without `Store:SiteRebuild:Token` the trigger logs (Information)
  and the message is done; Production refuses to start without `Store:SiteRebuild:Token` and `:Repository` (R-9).
- P5: `GlobalExceptionHandler` maps races that pass the handlers' checks: `DbUpdateConcurrencyException` → 409
  `concurrency.conflict`, 23505 → 409 `db.duplicate`, 23503 → 422 `db.reference_missing`.
- P5: "options used in an order are deactivated, not deleted" is covered by the domain tests now and gets its API test in
  P6 (it needs an order).

## Owner review (plan v3)

R-1 operations path kept · R-2 InitialSchema.Up order · R-3 tests read OTP from the in-process SMS sink · R-4 ICartMergeHook
port (no-op in P4, real in P6) · R-5 pwd_change claim on every staff token issue; password change revokes the family and
re-issues · R-6 IUsedTokenStore · R-7 real WebP placeholders in the dev seed · R-8 image check on category images ·
R-9 SiteRebuild no-op without a token; Production requires it · R-10 CI coverage gate · R-11 Ryuk env var not committed ·
R-12 session branch + owner archive note in the PR.

## Blocked

(none)

## Remaining for R5-Payments

Payment gateway (hosted page), webhook, `payment` / `payment_event`, settlement (`paid`), donation receipts (DR/DRC PDF +
message), giving totals, reconciliation, unpaid-order expiry job, refunds, finance reports and dashboard totals.
