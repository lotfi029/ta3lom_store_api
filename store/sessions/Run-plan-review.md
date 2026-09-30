# Review — cloud run plan (STEP 0 → S-45 → close-out) + tests added to the run

**Date:** 30 September 2026 · **Plan reviewed:** the cloud session's plan on branch `claude/funny-wright-yum3nm`
**Verdict:** **Approve with 9 changes.** It follows Cloud-build-plan.md, the DMS mapping and plan 14 closely, and the endpoint, job and effect maps match plan 14 §13. The changes below fix real risks. Your new decision: the run also **writes the tests** after the build, still without stopping, and you review once at the end.

---

## 1. What's right (keep as is)

- STEP 0 records a baseline, and S-RUN sets up the state files before any refactor.
- S-02b is sequenced step by step with a build after each step, and the OpenAPI snapshot must stay unchanged, which proves behaviour didn't move.
- The (K) exceptions are carried into concrete choices:
  - `IUnitOfWork` without EF types;
  - `ExecuteInTransactionAsync` on the Npgsql execution strategy;
  - the 11 §1.6 problem body;
  - `Entity` without `IsActive`/`CreatedAt`;
  - explicit Mapster mapping for money and enums.
- The feature table matches plan 14 §13.2 exactly (130 endpoints; #43, #44 and #89–#92 excluded). Jobs and effects match §13.3 and §13.4.
- The known doc conflicts are listed up front, with the precedence rule, to be logged when met.
- There's a gate and a smoke check per session, with integrity checks after S-16, S-21, S-23, S-25 and S-42.

## 2. Changes required

| # | Where | Issue | Change |
|---|---|---|---|
| 1 | Git flow | **Squash-merge + `git merge origin/main`** breaks. After a squash, the branch's commits don't match main's single squash commit, so merging main back in conflicts or duplicates changes on every file touched. You also now review **at the end**, so self-merging defeats that | **Don't merge anything.** Open **one draft PR** for the whole run (`claude/funny-wright-yum3nm` → `main`), titled "Store backend — full build + tests". Keep a phase/session checklist in its description, updated per session, and mark it ready only at the end. Commits stay `S-nn — …` / `T-nn — …` so it can be reviewed session by session |
| 2 | S-16 … S-23 | Effects under `Infrastructure/Services/Settlement/Effects` put business logic (enrolment, grants, stock commit, pooling) in Infrastructure, which goes against DMS and Clean Architecture | `ILineSettlementEffect` and every effect go in **`Application/Features/Settlement/Effects/`**, working through Domain methods and repositories. Only the row-lock queries (`FOR UPDATE`, `SKIP LOCKED`, conditional `UPDATE`) stay in Infrastructure repositories (K-c) |
| 3 | Jobs | Jobs in Infrastructure risk holding logic | A Quartz job is a thin shell: it resolves and calls **one Application command** (`Features/<Module>/Commands/<JobName>/`). All logic lives in the command |
| 4 | S-04, S-05, S-15, S-20 | Signing keys aren't mentioned: JWT keys, OTP pepper, order status-token HMAC key, download-link key. The run would either hard-code them or stall | Add typed options (`Store:Jwt:*`, `Store:Otp:PepperBase64`, `Store:Tokens:StatusKeyBase64`, `Store:Downloads:LinkKeyBase64`). Use **dev-only values** in `appsettings.Development.json`, marked "DEV ONLY", and placeholders in `deploy/env/store.env.example`. Validate on start (`ValidateOnStart`); Production fails if any are missing. No real secret anywhere |
| 5 | S-02b step 5 | The **empty `AlignDmsNamespaces` migration** is probably unnecessary. A CLR namespace change produces no model operations | Run the schema-parity + `HasPendingModelChanges` check first. Add the empty migration **only if** tooling reports pending changes, and log which case applied |
| 6 | S-02b step 8 | "Mapster compile at startup instead of a test" was a workaround for the no-tests rule | Keep the startup check in Development, **and** add the `TypeAdapterConfig.Compile()` test in the test phase (T-7) |
| 7 | `/dev/*` | Only "mapped in Development/Staging" is stated | Keep that, and in the test phase add the architecture/integration test that `/dev/*` returns 404 in Production (plan 14 §4.3) |
| 8 | Subagents | Subagents building layers can drift from conventions | Allowed, but the main agent must read every subagent diff against §3.2 before the gate. A subagent never commits and never edits `PROGRESS.md` / `DECISIONS.md` |
| 9 | Stop rules | "Final summary" appears only after close-out | After close-out the run **continues into the test phase (§3)** without a summary turn. The only final summary is at the end of T-9 |

## 3. Added to the run: the test phase (after close-out, non-stop)

Order matters: the riskiest code first, then broad coverage.

| # | Step | Content |
|---|---|---|
| T-1 | Test infrastructure | Shared PostgreSQL fixture (external PG16 here, Testcontainers `postgres:18` in CI) with a reset between tests (Respawn or truncate script). `StoreApiFactory` with `FakeClock`. Readers for the SMS sink and the email sink. Helpers that mint customer and staff tokens per role. Test-data builders for products, carts and orders. A `SettleViaDevTrigger` helper |
| T-2 | **Settlement (S-16)** | Same confirmation 5× → one change; 2 parallel confirmations → one settlement; amount mismatch → `payment_review` with no effects; confirmation after expiry → review; expiry releases stock and restores cart lines; duplicate course race → no second enrolment + FinanceAlert; zakat `in`; goal counters change once; architecture test that only `IOrderSettlement` sets `paid` |
| T-3 | **Pooling (S-23)** | Property-based (FsCheck): `balance = Σ remaining` and `units × seat price = Σ converted`; the 100 + 150 + 120 example on a 350 seat → 1 unit, 20 remaining, funding split 100/150/100; parallel contributions → no lost update |
| T-4 | **Allocation (S-25)** | One test per RUL-47 (a)–(e); batch is all-or-nothing; 2 officers in parallel never share a unit; zakat unit refused for a non-zakat beneficiary; `allocation.mobile_required`; lapse re-queues at the front with the SMS in the sink |
| T-5 | RUL tests for every session | Every "Tests" list in plan 14 (S-04 … S-45), tagged `[Rule("RUL-nn")]`. Fill `ImplementedRules.txt` from plan 14 §13.1 as rules get covered, until `RuleCoverageTests` passes with the full list (payment-service RULs excluded) |
| T-6 | Policy matrix | Data-driven: every admin route × every role → expected 200/403, including segregation of duties (admin ∌ refund/allocate/reveal-pii) |
| T-7 | Endpoint integration | All 130 endpoints: success, 400, 401/403, and at least one business `code`. Plus: the Mapster `Compile()` test; OpenAPI snapshot vs 11; log redaction for the new PII (mobile, beneficiary); `/dev/*` 404 in Production |
| T-8 | Documents and outputs | PDF text extraction: reference, totals, Arabic name shaped correctly; QR decodes to the 5 TLV fields; gapless numbers under 20 parallel settlements; CSV has a BOM and intact Arabic; the donor DTO never carries beneficiary identity (reflection) |
| T-9 | Coverage + final report | Line coverage **Domain ≥ 90 %, Application ≥ 80 %** (plan 14 §4.4), with gaps closed. Then the final report |

**Test rules:**
- **When a test fails:** if the code breaks the rule in 03, fix the **code**. If the test misread 03, fix the test. Never weaken, skip or delete a test to go green. Every bug found goes into a **"Bugs found by tests"** table in `DECISIONS.md` (test, rule, fix commit).
- **`[Skip]` is allowed only** for an environment limit (for example, a Testcontainers-only test in the cloud). The reason must be written, and the test must run in CI.
- **Extra test packages allowed:** FsCheck (xUnit v3 integration), NSubstitute, Respawn, `Microsoft.Testing.Extensions.CodeCoverage`. If needed: PdfPig for PDF text and a QR decoder. FluentAssertions stays banned; use Shouldly.
- **Commits:** `T-nn — title`. `PROGRESS.md` gets a test-phase table and checklist.

**The run ends** when all of these hold:
- Part 1 checklist ticked;
- T-1…T-9 done;
- all tests green locally (and CI green if it runs);
- coverage thresholds met;
- **`store/sessions/Build-report.md`** pushed.

The report covers:
- sessions and commits;
- endpoint, job and effect coverage;
- the test counts per project, and coverage;
- the RUL coverage list;
- the bugs found by tests;
- every `DECISIONS.md` `REVIEW` item;
- known gaps;
- a **review guide** that points to the files to read line by line (settlement, pooling, allocation, auth, PII, money — plan 14 §4.6).

---

## 4. Message to paste into the cloud session

```text
Plan approved with the changes below. Then execute it and DO NOT STOP until everything is finished —
the build (all sessions + close-out) AND the test phase. I will review only at the end.

Changes to your plan:
1. Git: don't merge anything. Open ONE draft PR for the whole run (claude/funny-wright-yum3nm → main),
   title "Store backend — full build + tests", with a phase/session checklist in the description that you
   update after each session. Mark it ready only at the very end. Never force-push. Drop the
   squash-merge + merge-back step.
2. Settlement effects (ILineSettlementEffect and every *Effect) live in
   Application/Features/Settlement/Effects/ and work through Domain methods and repositories. Only the
   row-lock queries (FOR UPDATE, SKIP LOCKED, conditional UPDATE) stay in Infrastructure repositories.
3. Quartz jobs are thin shells that call one Application command each; no logic in the job class.
4. Keys: add typed options Store:Jwt:*, Store:Otp:PepperBase64, Store:Tokens:StatusKeyBase64,
   Store:Downloads:LinkKeyBase64 with ValidateOnStart. Dev-only values in appsettings.Development.json
   marked "DEV ONLY", placeholders in deploy/env/store.env.example, Production fails if missing. No real
   secrets anywhere.
5. S-02b: check schema parity + HasPendingModelChanges first; add the empty AlignDmsNamespaces migration
   only if tooling reports pending changes. Log which case applied.
6. Keep the Mapster Compile() startup check in Development, and add its test in the test phase.
7. /dev/* stays Development/Staging only; its Production-404 test comes in the test phase.
8. Subagents may build layers, but you review every subagent diff against Cloud-build-plan §3.2 before the
   gate; subagents never commit and never edit PROGRESS.md or DECISIONS.md.
9. After close-out, don't write a summary turn — go straight into the test phase.

New: TEST PHASE (after close-out, same run, no stopping). This replaces "no new tests" once the build is
done. Add it to Cloud-build-plan.md as §4b and to PROGRESS.md as a table + checklist:
T-1 test infrastructure (shared PG fixture with reset, StoreApiFactory + FakeClock, SMS/email sink
    readers, token helpers per role, data builders, SettleViaDevTrigger helper)
T-2 settlement S-16 (5x same confirmation → one change; parallel confirmations; amount mismatch →
    payment_review; confirmation after expiry; expiry releases stock + restores cart; duplicate-course race;
    zakat in; counters once; architecture test: only IOrderSettlement sets paid)
T-3 pooling S-23 (FsCheck invariants balance = Σ remaining and units × price = Σ converted; the
    100+150+120 on 350 example; parallel contributions)
T-4 allocation S-25 (RUL-47 a–e; all-or-nothing; parallel officers never share a unit; zakat mismatch;
    allocation.mobile_required; lapse re-queue + SMS)
T-5 every RUL test from plan 14's "Tests" lists, tagged [Rule], filling ImplementedRules.txt per §13.1
    until RuleCoverageTests passes (payment-service RULs excluded)
T-6 role × route policy matrix incl. segregation of duties
T-7 all 130 endpoints: success, 400, 401/403, ≥1 business code; Mapster Compile test; OpenAPI vs 11;
    log redaction for new PII; /dev/* 404 in Production
T-8 PDFs (text, Arabic shaping, totals), QR TLV decode, gapless numbering under 20 parallel settlements,
    CSV BOM, donor DTOs never carry beneficiary identity
T-9 coverage Domain ≥ 90 %, Application ≥ 80 %, then store/sessions/Build-report.md
Test rules: if a test fails because the code breaks 03, fix the CODE; if the test misread 03, fix the test.
Never weaken, skip or delete a test to go green; [Skip] only for environment limits with a written reason
(it must run in CI). Log every bug found in DECISIONS.md "Bugs found by tests" (test, rule, fix commit).
Allowed test packages: FsCheck (xUnit v3), NSubstitute, Respawn, Microsoft.Testing.Extensions.CodeCoverage,
and if needed PdfPig and a QR decoder. Shouldly only; FluentAssertions stays banned. Commits "T-nn — title".

The run ends only when: Part 1 checklist ticked, T-1…T-9 done, all tests green, coverage thresholds met, and
store/sessions/Build-report.md is pushed (sessions and commits, endpoint/job/effect coverage, test counts and
coverage, RUL list, bugs found by tests, every REVIEW item, known gaps, and a review guide pointing me to the
files to read line by line: settlement, pooling, allocation, auth, PII, money). Then mark the PR ready and
write one final summary.

Until then: never end your turn to report, ask or wait. No stop conditions — use the defaults and log them.
If a session or test step can't go green after 3 honest attempts, revert it, mark it blocked with the error,
continue with the next step whose dependencies are done, and retry blocked items before the report. After a
context compaction, re-read PROGRESS.md, DECISIONS.md, Cloud-build-plan §2 and §4b, and continue from the
"Next:" line. Start now.
```
