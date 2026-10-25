# R5-Core — progress log

Specs: `.session/01-api-business-requirements.md`, `02-api-erd.md`, `03-api-rebuild-plan.md` (precedence 01 > 02 > 03 > CLAUDE.md).
Approved plan: v3 (owner review R-1…R-12 applied).

## Phase status

| Phase | Status | Build | Format | Tests | Notes |
|---|---|---|---|---|---|
| P1 clean repo | done | green | clean | not run (old suite references deleted features; P2 replaces it) | |
| P2 strip | done | green | clean | Domain, Application, Architecture green; integration suite cannot run until P3 (no migration → fixture seed fails), as the plan's P2 gate allows | |
| P3 model + InitialSchema | pending | | | | |
| P4 auth | pending | | | | |
| P5 catalogue | pending | | | | |
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
- **REVIEW** `notification` rows are claimed as `failed` (= not delivered yet) before the provider call and set to `sent`
  after it; the ERD allows only `sent | failed`, so there is no `queued` state.

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
