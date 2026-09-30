# Build progress
Next: S-02b — DMS alignment (step 1: SharedKernel)

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
| S-02b DMS alignment | todo | | — | — | | |
| S-03a-lite CI | todo | | — | — | | |
| S-04 customer OTP | todo | | 16–20 | PruneOtp | | |
| S-05 staff identity | todo | | 47–52, 123–128 | — | | |
| S-08 public catalogue | todo | | 1–7, 9 | — | | |
| S-09 admin catalogue | todo | | 54–77, 118, 119 | — | | |
| S-13 cart | todo | | 10–15 | ExpireGuestCarts | | |
| S-15 checkout | todo | | 40–42 | — | | |
| S-16 settlement core | todo | | 87 + dev trigger | ExpireUnpaidOrders; gift/donate effects | | integrity check |
| P0-3 Arabic PDF spike | todo | | — | — | | |
| S-17 outbox, documents | todo | | 88 | OutboxDispatcher; OrderPaid, RenderDocuments, FinanceAlert, DedicationCard (stub) | | |
| S-19 enrolment + LMS | todo | | 29 | CourseEnrolmentEffect; LmsEnrol | | |
| P0-4 watermark spike | todo | | — | — | | |
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
| M2 Identity | tools/smoke/M2.http | |
| M3 Catalogue | tools/smoke/M3.http | |
| M4 First settled order | tools/smoke/M4.http | |
| M5 Fulfilment | tools/smoke/M5.http | |
| M6 Giving | tools/smoke/M6.http | |
| M7 Operations-complete | tools/smoke/M7.http | |

## Part 1 checklist
- [ ] 130 endpoints of plan 14 §13.2 mapped and answering
- [ ] 13 jobs of §13.3 registered (12 scheduled + OutboxDispatcher; all except ReconcilePayments)
- [ ] Every settlement effect of §13.4 registered (5 line effects + gift/donate counters)
- [ ] 17 outbox message types of 09 §4.9 handled (all except RefundProcessed)
- [ ] Integrity checks 0 rows after the full smoke scenario
- [ ] Spikes P0-3 and P0-4 written up in store/spikes/
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
