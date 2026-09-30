# S-01 Plan Review — Kick-off + Solution Skeleton

**Date:** 30 September 2026 · **Reviewed:** the cloud session's plan "Kick-off + S-01 — Solution skeleton (ta3lom store API)"
**Checked against:** 17 §1 (kick-off prompt), 14 §6 S-01, 09 §1–§3, §5.1, §5.14, §8, §10–§12, 11 §1 and §10, 08 §3

**Verdict: good plan. Approve after the 11 corrections below.** The structure, scope, test-first approach and verification steps match plan 14. The corrections fix one architecture contradiction, four behaviours that would fail or drift from the API contract, and several smaller gaps. The revised plan (§3) is ready to paste back into the cloud session.

---

## 1. Findings

| # | Severity | Finding | Correction |
|---|---|---|---|
| 1 | **High** | **Application references Contracts.** The plan follows 09 §2, but the kick-off prompt (17 §1) you asked for requires strict Clean Architecture: *Application references Domain only*. Contracts are HTTP DTOs, part of the presentation layer. The plan contradicts the prompt it is executing | Application → Domain only. Handlers return Application result records; Api maps them to Contracts (static mappers, no AutoMapper). `ErrorCodes` moves to `Application/Abstractions`. The architecture test forbids `Ta3lom.Store.Contracts` in Application. **09 §2 updated (r2).** Cost: about 2–3 dev-days of mapping over the whole project |
| 2 | **High** | **CSP breaks `/docs`.** `default-src 'none'` is applied everywhere and only `frame-ancestors` is relaxed for `/docs`. Scalar needs scripts, styles and fetch, so the docs page will be blank and the "/docs loads" check fails | Skip the CSP header (and X-Frame-Options) for `/docs` and `/openapi` outside Production. Everything else keeps `default-src 'none'; frame-ancestors 'none'` |
| 3 | **High** | **CORS test has no endpoint to hit.** CORS is applied to the `/v1` group only, but S-01 has **no** `/v1` endpoint, so the preflight tests cannot pass (the same applies to the rate-limiter test) | Tests register a probe endpoint in the `/v1` group via `WebApplicationFactory.WithWebHostBuilder` (`/v1/__test/ping`, rate policy `public`). No probe route in production code |
| 4 | Medium | **`Cache-Control: no-store` list is incomplete.** 11 §1 requires it on everything with a cookie or bearer, including `/v1/cart/**` and `/v1/orders/{ref}/status` | Paths: `/v1/me`, `/v1/auth`, `/v1/admin`, `/v1/checkout`, `/v1/cart`, `/v1/orders` |
| 5 | Medium | **ProblemDetails details missing from the plan** (11 §1.3 and §1.6): the `type` URI, localised `title`/`detail` by `Accept-Language` (default `ar`), and `traceId` format | `type = {PublicBaseUrl}/problems/{code}`; `title`/`detail` from `ErrorMessages.ar.resx` / `.en.resx` keyed by code; `traceId = Activity.Current.TraceId` (32 hex, W3C), not `HttpContext.TraceIdentifier` |
| 6 | Medium | **Health JSON shape** is not the one in 11 §10 | `/health/live` → `{ "status": "Healthy" }` only. `/health/ready` → `{ status, totalDurationMs, checks: [ { name, status, durationMs, data? } ] }`, never exception text |
| 7 | Medium | **`RuleAttribute` lives in ArchitectureTests**, so Domain.Tests would have to reference ArchitectureTests (which references every src project) | New `tests/Ta3lom.Store.Testing` (RuleAttribute, FakeClock, test builders) referenced by all test projects. ArchitectureTests references the other test assemblies to scan `[Rule]` |
| 8 | Medium | **Staging CORS origin missing.** 08 §3.1 lists `https://staging.ta3lom.sa` for the staging API | `Store:Cors:Origins` per environment: prod = ta3lom.sa + www; staging = staging.ta3lom.sa; Development adds `http://localhost:4200` |
| 9 | Medium | **Middleware order not stated.** HSTS and the scheme depend on forwarded headers (TLS ends at Caddy) | Order: ForwardedHeaders → ExceptionHandler → StatusCodePages → HSTS (non-Dev) → SecurityHeaders → Serilog request logging → Routing → CORS → RateLimiter → endpoints. `KnownNetworks` cleared and set from config (Docker subnet) |
| 10 | Low | **"Two-line change" to .NET 11 is optimistic.** TFM, `global.json` **and** the package majors change (EF Core, Npgsql, OpenApi). `LangVersion 14` pinned would be a third line | Do not set `LangVersion` (the TFM default is correct). `global.json`: `rollForward: latestFeature` (works with 10.0.104 here and 10.0.2xx on Windows/VS). The .NET 11 move is its own PR at GA. **17 updated.** |
| 11 | Low | **Exposed headers.** 11 §1.5 returns `Idempotent-Replayed`, which is not in the exposed list | Exposed: `ETag, Retry-After, Location, Idempotent-Replayed`. **11 §1.2 and 08 §3.1 updated** |

### Doc conflicts reported by the session — resolved

| Conflict | Resolution (docs updated 30 Sep) |
|---|---|
| Staging host `staging-store-api` (11, 09 k6) vs `store-api-staging` (08, 12, 14, 16, ADR-S03) | **`store-api-staging.ta3lom.sa`**: 08 is the DNS/infra source and 5 docs agree. 11 §1.1 and the 09 k6 script are corrected |
| CORS exposed headers, 08 vs 11 | 11 wins, plus `Idempotent-Replayed` (finding 11). 08 corrected |
| Platform .NET 10 vs .NET 11 | .NET 10 LTS now (environment limit), .NET 11 at GA in its own PR. 17 records this |

### Confirmed correct (no change)

- Scope: CLAUDE.md + S-01 only; S-02 waits for the reference SQL files.
- Session branch name overrides plan 14 naming; the PR title starts with `S-01`.
- Housekeeping: `.vs/` untracked, empty `StoreApi.slnx` replaced, README rewritten in UTF-8.
- Money exactly per 09 §5.1 and tests (both signs of AwayFromZero, ar-SA culture), `[Rule("RUL-31")]`.
- Banned APIs (time, `Migrate`; double/single in Domain + Contracts), no MediatR/AutoMapper/FluentAssertions.
- `TryDecorate` instead of `Decorate` while no handler exists (correct, Scrutor throws otherwise).
- `DomainException` → 409 with its code; anything else → 500 `server.error` (09 §5.14, 11 §1.6).
- `ImplementedRules.txt`, empty in S-01, growing per session (plan 14 §4.4 / §13.1).
- The OpenAPI snapshot test, the deliberate-break verification and the curl checks.
- S-02 prerequisites (reference SQL in `db/reference/`, `STORE_TEST_DB=external` on local PostgreSQL 16 + `uuidv7()` shim here; Testcontainers `postgres:18` in CI).

---

## 2. Doc changes made in this review (copied into the repo's `store/` in the S-01 PR)

| File | Change |
|---|---|
| `09-backend-dotnet.md` §2 | r2 note: Application does not reference Contracts; table row updated; the LayerTests sketch forbids `Ta3lom.Store.Contracts` in Application |
| `09-backend-dotnet.md` §3.2 | ErrorCodes location → `Application/Abstractions/ErrorCodes.cs` |
| `09-backend-dotnet.md` §10.6 | k6 staging URL → `store-api-staging.ta3lom.sa` |
| `11-api-contract.md` §1.1 | Staging URL → `https://store-api-staging.ta3lom.sa/v1` |
| `11-api-contract.md` §1.2 | Exposed headers + `Idempotent-Replayed` |
| `08-solution-architecture.md` §3.1 | Exposed headers aligned with 11 |
| `17-claude-code-prompts.md` | Environment note: .NET 10 now, .NET 11 at GA as its own PR |

The cloud session could not read the project copies, so it applied the table above itself. Replace
these files with the project versions if the wording differs.

---

## 3. Approved additions (after r2)

- **A1 Probe mechanism:** `Api/Endpoints/IV1EndpointContributor { void Map(RouteGroupBuilder v1); }`.
  Program maps every registered contributor onto the `/v1` group (none registered in production).
  Integration tests register `PingContributor` (`/v1/__test/ping`, `RequireRateLimiting("public")`) via
  `ConfigureTestServices`. Rate-limit numbers come from `Store:RateLimits:*` so tests can use tiny limits.
  The OpenAPI snapshot uses the factory without the probe.
- **A2 Localisation:** `[assembly: NeutralResourcesLanguage("ar")]` in Api; `UseRequestLocalization`
  with supported cultures ar, en, default ar, Accept-Language provider only (after ExceptionHandler,
  before endpoints). `ErrorMessagesCompletenessTests`: every ErrorCodes constant has a non-empty entry in
  `ErrorMessages.resx` and `ErrorMessages.en.resx`.
- **A3 Logging:** keep the "payload never logged" test for `LoggingCommandHandler`; add
  `LogRedactionTests` with a Serilog in-memory sink: Authorization, Cookie, Set-Cookie and
  `*password*`/`*secret*`/`*token*` properties are redacted in request logs.
- Also: HSTS tested in Production with `X-Forwarded-Proto: https` from a known proxy; Degraded → 200 and
  Unhealthy → 503 tested with throw-away checks tagged `ready` registered in the test only.

---

## 4. What is still needed for S-02

1. Push `database/store/` (001_store_schema.sql, 002_store_seed.sql, 003_integrity_checks.sql, tests/smoke_scenario.sql, tools/) into the repo as `db/reference/`.
2. Confirm integration tests in the cloud run against local PostgreSQL 16 (`STORE_TEST_DB=external` + `uuidv7()` shim), with Testcontainers `postgres:18` as the CI path.
