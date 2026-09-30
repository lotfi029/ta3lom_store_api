# 17 — Claude Code Prompts: Building the Backend (Clean Architecture, latest .NET)

**Revision:** 30 September 2026
**Uses:** [14 — Backend implementation plan](14-backend-implementation-plan.md) (sessions S-01 … S-45, payments excluded) · [09](09-backend-dotnet.md) · [11](11-api-contract.md) · [03](03-business-rules.md) · [database/](database/01-database-design.md)

Three prompts:

1. **Kick-off prompt** — paste once at the start of the first session. It writes the repo's `CLAUDE.md` so every later session inherits the rules.
2. **Session prompt** — paste at the start of every session, filling in the session ID.
3. **Review prompt** — paste at the end of a session, before you merge.

---

## Version decision: .NET 11

As of 30 September 2026:

| | .NET 10 | **.NET 11** |
|---|---|---|
| Status | LTS, GA since Nov 2025 (10.0.12) | **RC1 (8 Sep 2026) with go-live support**; GA planned for November 2026 |
| Language | C# 14 | **C# 15** |
| Support end | Nov 2028 | Nov 2028 (STS releases are now supported for 24 months) |

**Use .NET 11.** Launch is about 26 weeks away, so GA lands early in the build. Support ends at the
same date as .NET 10, and RC1 is licensed for production. Pin the RC SDK in `global.json` and move to GA
the week it ships. Where the docs say *.NET 10 / C# 14 / EF Core 10*, read *.NET 11 / C# 15 / EF Core 11*.

**Current state (S-01 review, 30 Sep 2026): build on .NET 10 LTS for now.** The cloud container can
install only the .NET 10 SDK (Ubuntu archive); the .NET 11 download host is blocked. The move to
.NET 11 happens at GA as **its own PR**: it changes the TFM, `global.json`, and the package majors
(EF Core, Npgsql EF Core provider, Microsoft.AspNetCore.OpenApi, Microsoft.AspNetCore.Mvc.Testing and the
other `10.0.x` Microsoft packages). `LangVersion` is not pinned, so C# follows the TFM, and `global.json`
uses `rollForward: latestFeature`.

**Fallback rule:** if a required library (Npgsql EF Core provider, QuestPDF, Quartz.NET, Scalar)
does not yet support .NET 11 during RC, keep the library at its latest stable version (packages built
for `net10.0` or `netstandard2.x` run on .NET 11). If that fails, stop and report. Do not downgrade the
target framework silently.

Sources: [.NET 11 RC1 announcement](https://devblogs.microsoft.com/dotnet/dotnet-11-rc-1/) · [InfoQ: RC1 with go-live, C# 15](https://www.infoq.com/news/2026/09/dotnet-11-rc-1-release/) · [Visual Studio Magazine: November launch](https://visualstudiomagazine.com/articles/2026/09/14/net-11-rc1-gets-go-live-support-ahead-of-november-launch.aspx) · [STS supported for 24 months](https://devblogs.microsoft.com/dotnet/dotnet-sts-releases-supported-for-24-months/) · [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) · [What's new in .NET 11](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/overview)

---

## 1. Kick-off prompt (first session only)

```text
You are the implementation engineer for the ta3lom STORE API — a .NET backend for the store of
ta3lom.sa (E-Learning Association, Riyadh): courses, books, seat sponsorships and donation products.
I review every change. Work in repos/ta3lom-store-api (create it if missing).

## Source of truth — read before writing any code
Read these documents (from Docs/store/ locally, or the claude.ai project folder store/ in the cloud):
1. 14-backend-implementation-plan.md — THE PLAN. §0 (scope: payments excluded), §4 (working method,
   quality gates, definition of done), §13 (coverage maps). You build it session by session.
2. 03-business-rules.md — RUL-01…RUL-63. Business rules are not negotiable and are never invented.
3. 09-backend-dotnet.md — code shape, patterns and code sketches.
4. 11-api-contract.md — every endpoint, DTO and error code (index in §12).
5. database/01-database-design.md and database/store/001_store_schema.sql — the schema (49 tables),
   the settlement, pooling and allocation transactions.
6. 15-payment-service-handoff.md — the payment boundary. Do NOT build gateway, webhook,
   reconciliation, retry or refund code; only the contracts named there.
If two documents disagree, 03 wins, then 11, then database/, then 09. Report every conflict you
find instead of choosing silently.

## Platform — latest .NET
- .NET 11 (SDK pinned in global.json; RC now, move to GA in November 2026), C# 15, ASP.NET Core 11
  Minimal APIs, EF Core 11 + Npgsql, PostgreSQL 18.
- Wherever the docs say .NET 10 / C# 14 / EF Core 10, use .NET 11 / C# 15 / EF Core 11.
- Use current idioms: primary constructors, collection expressions, required members, records for
  immutable data, file-scoped namespaces, TimeProvider behind IClock, Guid.CreateVersion7(),
  TypedResults, built-in OpenAPI (Microsoft.AspNetCore.OpenApi) + Scalar, the built-in rate limiter
  and HybridCache where caching is needed.
- Central package management (Directory.Packages.props), nullable enabled, warnings as errors,
  analyzers on, .slnx solution file.
- If a library lacks .NET 11 support, use its latest stable version; if it still fails, stop and report.
  Never downgrade the target framework.

## Clean Architecture — the dependency rule is enforced, not suggested
Projects (names from 09 §1):
- Ta3lom.Store.Domain — entities, aggregates, value objects (Money, OrderReference…), domain
  events, domain services, domain errors. References NOTHING (no EF Core, no ASP.NET, no NuGet
  except what the BCL provides).
- Ta3lom.Store.Application — use cases (one folder per use case: Command/Query, Handler, Validator,
  result DTOs), ports (interfaces for persistence, clock, SMS, email, LMS, file storage, PDF,
  payment hand-off), Result/Error, validation and logging decorators. References Domain only.
  No MediatR (commercial licence): plain ICommandHandler/IQueryHandler + Scrutor. FluentValidation.
- Ta3lom.Store.Infrastructure — EF Core DbContext and configurations, migrations, repositories/
  query services, outbox, Quartz jobs, adapters (SMS, SMTP, LMS, storage, QuestPDF, PdfSharp),
  Data Protection. References Application (and Domain transitively). Implements the ports.
- Ta3lom.Store.Contracts — HTTP request/response records only (the API contract in 11). No logic.
- Ta3lom.Store.Api — composition root and presentation: Program.cs, endpoint groups, auth,
  ProblemDetails, filters. Endpoints only bind → call a handler → map Result to HTTP. No business
  logic, no DbContext use in endpoints.
Rules:
- Dependencies point inward only. Domain and Application never reference Infrastructure, Api or
  EF Core types. Business rules live in Domain (invariants) and Application (orchestration), never in
  endpoints, EF configurations or SQL triggers. The database constraints in the DDL are a second line of defence.
- Aggregates protect their invariants; setters are private; state changes go through methods named
  after the business action (Order.MarkPaid, Cart.AddLine, SponsorshipPool.Add, SponsoredUnit.Allocate).
- One transaction per use case, through IUnitOfWork. Outbox messages are written in that transaction.
  No external call inside a transaction.
- Money is decimal inside a Money value object (SAR, 2 dp), strings in JSON. Never double or float.
- Time comes from IClock (TimeProvider). Never DateTime.Now or UtcNow directly.
- Enforce all of the above with Ta3lom.Store.ArchitectureTests (NetArchTest or ArchUnitNET); the
  build fails on a violation.

## Working method (from plan 14 §4)
- One session = one branch feat/s-nn-name = one PR. Session IDs are those in plan 14.
- Test-first: for each RUL in the session, write a failing test tagged [Rule("RUL-nn")], then the code.
- Done = dotnet build -warnaserror, all tests green, openapi/v1.json matches 11, migrations added
  and the reference schema regenerated, no PII in logs, the session's "Done when" list satisfied.
- Stop and ask when: a rule is missing or ambiguous, a doc conflicts, a session will exceed 1.5× its
  estimate, or a decision belongs to me (versions, packages with licences, security trade-offs).
- Never put secrets in code, config committed to git, or the chat.

## Environment (if this is a cloud session)
Run the environment check in 16-cloud-session-preparation.md §4 first. If the .NET SDK or NuGet is
blocked, tell me before writing code; we then build and test through GitHub Actions. Without Docker,
integration tests use STORE_TEST_DB=external against a local PostgreSQL (plan 16 §2-F).

## Your first task
1. Write repos/ta3lom-store-api/CLAUDE.md, containing the rules above (condensed), the
   non-negotiables of 09 §12 updated to .NET 11 and Clean Architecture, the definition of done of
   plan 14 §4.5, and the reading order. Every later session reads this file first.
2. Then execute session S-01 from plan 14 §6 exactly, with the .NET 11 platform above.
3. Finish with a summary: files created, tests, how each architecture rule is enforced, conflicts
   found in the docs, and what S-02 needs from me.
```

---

## 2. Session prompt (every later session)

Fill in the angle-bracket fields. The Build, Endpoints, Tests and Done-when details already live in
plan 14, so the prompt points to them rather than repeating them.

```text
Session <S-nn> — <title from plan 14>.

1. Read CLAUDE.md (repo rules), then plan 14 section for <S-nn> in full, then only the parts it cites:
   RUL <ids> in 03, endpoints <#numbers> in 11, <09 sections>, <database/01 sections>.
2. Confirm in 5 lines what you will build and which RUL/endpoints it covers. List any doc conflict
   or missing rule BEFORE coding. If none, continue without waiting.
3. Branch feat/<s-nn>-<name>. Work test-first, layer by layer, respecting the dependency rule:
   Domain (entities, invariants, domain tests) → Application (use cases, ports, validators,
   handler tests with fakes) → Infrastructure (EF configuration, migration, adapters, integration
   tests) → Contracts + Api (endpoints, policies, ProblemDetails codes, integration tests incl.
   401/403/400 and each business error code).
4. Extend the shared tests: the RUL coverage list, the role × endpoint policy matrix, the
   architecture tests.
5. Run: dotnet build -warnaserror && dotnet test. Regenerate openapi/v1.json and, if the model
   changed, the migration script and database/store reference schema + data dictionary.
6. Report: what changed per layer, RUL and endpoints covered, test counts, deviations from the docs
   (with the doc fix you propose), and anything I must review line by line (plan 14 §4.6).
Scope guard: nothing from other sessions, nothing from the payment service (plan 14 §0).
```

**Example — S-13 (Cart), filled in:**

```text
Session S-13 — Cart.
1. Read CLAUDE.md, then plan 14 §8 S-13, then RUL-11…RUL-19 and RUL-21 in 03, endpoints #10–#15
   in 11 (§3), 09 §4.2 and §5.9.
… (steps 2–6 unchanged)
```

---

## 3. Review prompt (end of every session, before merge)

Run it in a **fresh** session, so the reviewer doesn't share the author's assumptions.

```text
Review the pull request for session <S-nn> in repos/ta3lom-store-api against:
plan 14 (<S-nn> section and §4.5 definition of done), 03 (RUL <ids>), 11 (endpoints <#numbers>),
database/01, and CLAUDE.md.
Check, in this order, and cite file:line for every finding:
1. Clean Architecture — any inward-rule violation, business logic outside Domain/Application,
   EF Core or HTTP types leaking inward, logic in endpoints.
2. Business rules — each RUL implemented exactly as written; a test per RUL that would fail if the
   rule were removed.
3. Money, time, concurrency — decimal/Money only, IClock only, locks and transactions per database/01,
   no external call inside a transaction, idempotency where the plan requires it.
4. Security — authorisation policy per endpoint incl. segregation of duties (RUL-62), input
   validation, no PII or secrets in logs, cookie attributes, rate limits.
5. Contract — responses, status codes and error codes match 11; openapi/v1.json updated.
6. Tests and quality — coverage of edge cases listed in plan 14, analyzers clean, no dead code.
Output: a table of findings (severity, file:line, rule/doc violated, fix), then "merge / fix first".
Do not modify code.
```

---

## 4. Order of use

| Step | Prompt | When |
|---|---|---|
| 1 | Kick-off | Once: creates `CLAUDE.md` and runs S-01 |
| 2 | Session | For S-02, S-03, S-04, S-05, S-08, S-09, S-13, S-15, S-16, S-17, S-19, S-20, S-21, S-40, S-41, S-23, S-24, S-25, S-26, S-42, S-43, S-31, S-33, S-35, S-45 (the order in plan 14 §3) |
| 3 | Review | After every session, before merge |
| — | Payment service | Separate kick-off, in 16 §3 |
