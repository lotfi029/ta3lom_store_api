# Plan 14 coverage audit — does the cloud run cover everything?

**Date:** 30 September 2026
**Compared:** [plan 14](../14-backend-implementation-plan.md) (every section) against the cloud session's run plan, together with [Run-plan-review.md](Run-plan-review.md)
**Result:** the run covers all 26 sessions, the 130 endpoints, the 13 jobs and every settlement effect. **14 gaps** were found:

- items plan 14 names that the run plan skips or leaves implicit;
- 1 missing package decision;
- no mechanism that proves every Build, Tests and Done-when line was done.

§4 gives an addendum to paste after the Run-plan-review message.

Items that need a VPS stay **deferred by your decision** (no hosting until the app is finished). They are listed in §3 so nothing is forgotten.

---

## 1. Section-by-section check

| Plan 14 section | In the run? | Note |
|---|---|---|
| §0 Scope (no payments, dev trigger, hand-off 15) | ✅ | `NullPaymentInitiator`, `/dev/orders/{ref}/confirm-payment`, `IOrderSettlement` |
| §1 Endpoints 130 / 136 | ✅ | Exclusions #43, #44, #89–#92 correct |
| §1 Tables 49 / views 4, migrations own the schema | ✅ | Schema parity in every gate |
| §1 RULs enforced **and tested** (except payment RULs) | ✅ | Built in the sessions, tested in T-2…T-5 (review) |
| §1 Jobs: 12 scheduled + dispatcher | ✅ | Names match 09 §6 |
| §1 **Outbox message types (09 §4.9)** | ⚠️ **Gap G1** | The run plan names only 5 handlers. 17 types are in scope (18 minus `RefundProcessed`), see §2 |
| §1 Documents: invoice, donation receipt, **packing slip** | ✅ | Packing slip in S-21 |
| §1 Adapters: SMS, SMTP, LMS, file storage, each with a fake | ⚠️ **G2, G3** | No email sink without Docker/Mailpit; real SMS provider class not mentioned; `IEInvoicingGateway` missing |
| §1 Environments: local compose, CI, staging, production | ⏸ / ✅ | CI ✅; compose, staging and production deferred (hosting) |
| §2 Milestones M1–M7 demos | ⚠️ **G4** | Replaced by smoke checks, but the milestone scenarios aren't run at phase ends |
| §3 Order and critical path | ✅ | Same order; dependency guard for blocked sessions |
| §4.1 One branch per session, PR per session | ✅ adapted | One branch and one PR; commits per session (your decision) |
| §4.3 Fakes table, `/dev/*` dev-only, architecture test | ✅ / ⚠️ G2, G3 | Test in T-7 |
| §4.4 Gates: build, format, tests, integration, architecture, RUL coverage, OpenAPI diff, vulnerable packages, **coverage %**, Trivy | ✅ / ⏸ | Coverage in T-9; Trivy deferred (no image) |
| §4.5 Definition of done | ⚠️ **G5** | No per-session proof that every Build/Tests/Done-when line was done; "11 fixed in the same PR" not explicit; the PR must list RUL/FR/endpoints |
| §4.6 Human review focus | ✅ | Review guide in `Build-report.md` |
| §5 Spikes P0-3 Arabic PDF, P0-4 watermark | ❌ **G6** | Missing. Both run fine in the cloud |
| §5 P0-2 VPS | ⏸ | Hosting |
| §6 S-01, S-02 leftovers | ⚠️ **G7** | S-02 "idempotent migration script runs twice with no error" and "unknown route → ProblemDetails" aren't re-checked |
| §6 S-03 Dockerfile, compose, deploy | ⏸ | Hosting (S-03a-lite = CI only) |
| §6–§11 Session Build lists | ⚠️ **G8** | Many details are only implicit in "read the section" (§2.2 below) |
| §6–§11 Session Tests lists | ✅ | T-2…T-8 |
| §6–§11 Session "Done when" | ⚠️ **G5** | Staging parts become local smoke; the rest must be checked |
| §12 Hardening, UAT, cutover | ⚠️ **G9** / ⏸ | The code-side parts can run now: STRIDE code walk, `/docs` and `/dev` off in Production, local load smoke. ZAP, restore drill, UAT and cutover are deferred |
| §13.1 RUL map | ✅ | T-5 fills `ImplementedRules.txt` |
| §13.2 Endpoint map | ✅ | Coverage audit in close-out |
| §13.3 Jobs map | ✅ | — |
| §13.4 Effects map + `settlement.effect_missing` guard removed after S-23 | ✅ | — |
| §14 Risks | ⚠️ **G10** | B-03 needs the P0-3 spike (G6); B-06 schema parity ✅; B-02/B-07 tests ✅; B-11: 15 updated if the code differs, not stated |
| §15 Launch checklist | ⚠️ **G11** / ⏸ | Code-side items can be verified now (§3); the rest is deferred |
| — Packages | ⚠️ **G12** | Image EXIF stripping (S-09) needs an image library; none approved. **Arabic font file** for QuestPDF (P0-3) needed in the repo |
| — Seeded settings | ⚠️ **G13** | 09 §4.10 needs `seller_vat_no`, `vat_rate`, `dedication_templates` and the RUL §M keys in `StoreSettings`; not stated |
| — Migrations style | ⚠️ **G14** | "Expand → migrate → contract" and backward-compatible migrations (§15 rollback) not stated |

---

## 2. Gap details

### 2.1 Outbox message types (G1) — each needs a handler, owned by one session

| Type | Session | Type | Session |
|---|---|---|---|
| `OrderPaid` | S-17 | `SponsorUnitAllocated` | S-25 |
| `RenderDocuments` | S-17 | `AllocationAlert`, `AllocationEscalation` | S-25 |
| `FinanceAlert` | S-16 raises, S-17 handles | `BeneficiaryInvite` | S-25 |
| `DedicationCard` | S-17 stub, S-26 complete | `UnitLapsed`, `LmsRevoke` | S-25 |
| `LmsEnrol` | S-19 | `SponsorUnitCompleted` | S-40 |
| `ShipmentShipped`, `ShipmentDelivered` | S-21 | `CauseUpdatePublished` | S-26 |
| `LowStockAlert` | S-41 | `PoolSwept` | S-42 |

`RefundProcessed` belongs to the payment service. OTP is a direct send (RUL-63 exception).

### 2.2 Build details only implicit in the run plan (G8) — must be ticked

| Session | Items |
|---|---|
| S-04 | Saudi mobile `05XXXXXXXX` → E.164 validator; **Origin check** on cookie endpoints; blocked customer can't verify (RUL-61); OTP never logged |
| S-05 | Staff CRUD, reset password, reset TOTP, unlock; lockout per main FR-53; `ICurrentActor` feeding `audit_log.actor`; placeholder allocate route for the policy matrix |
| S-08 | `GetGiftFeed` anonymised (RUL-23); `GetCatalogConfig` (presets, min gift, dedication templates, VAT flag); output caching keyed by locale-free URL; Arabic search with tashkeel |
| S-09 | Type locked after first order (RUL-01); preview; goal seed audited; curriculum replace; digital format needs private `book_file`; general-fund uniqueness; **EXIF stripped** on images; typed settings validation per key |
| S-13 | Header-badge summary endpoint (count + total, no auth); `MergeInto` (RUL-17); owned course dropped on merge with a notice |
| S-15 | `acceptTerms` → `terms_accepted_at`; shipping address only with print lines (RUL-26); shipping fee and totals (RUL-31); `payment_due_at`; cart → `converted`; order status never by reference alone |
| S-16 | Staff `CancelOrder` (from `pending_payment`/`payment_failed`); `ExpireOrder` restores lines to the active cart; retry on serialization failure; `FinanceAlert`; update doc 15 if the code contract differs |
| S-17 | Template locale fallback to ar; poison message → staff alert; PDFs stored as private `media_asset`; TLV QR only when `vat_enabled`; receipt shows the real donor name even when anonymous; licence number; SeedRunner adds the missing template keys |
| S-19 | Short-lived launch URL; `LmsEnrol` retries through the dispatcher |
| S-20 | Watermark: buyer name + masked mobile + order reference on every page; cached per grant |
| S-21 | Carrier + tracking, deliver, **return**; restock/adjust with a reason; admin order detail with payments, events, documents, notifications and shipment |
| S-24 | Invite (#106), CSV template (#108), import atomic per file |
| S-25 | Candidates exclude people with a live enrolment; **`distribution` row for books**; manual complete with proof (#114) |
| S-26 | Resend with an optional mobile correction (#93) |
| S-31 | Saudi National Address validation; certificate (#30); export JSON of all personal data; delete = anonymise per database/03 §7, revoke tokens, keep financial rows |
| S-33 | The 5 reports: `sales-vat`, `gifts-by-product`, `zakat`, `allocation`, `shipments` |
| S-35 | Retention per database/03 §7 (payload scrub > 18 months, notification/outbox/download_log deletes, beneficiary PII nulling); all 9 integrity queries; every RUL-63 event verified end to end |
| S-45 | Email with an SMS fallback when there's no email; skip sponsors with no change; record the last digest time |

### 2.3 Other gaps

| # | Fix |
|---|---|
| G2 | `SinkEmailSender` for Development: writes `.eml` files under `artifacts/mail/` plus a `GET /dev/mail` list (no Docker, so no Mailpit). `SmtpEmailSender` is selected by config |
| G3 | `IEInvoicingGateway` + `NoopEInvoicingGateway` (S-17). The real SMS adapter class (Unifonic or Taqnyat) goes behind config with empty keys (S-04) |
| G4 | At the end of each phase, run the **milestone scenario** as a smoke script `tools/smoke/M2.http`…`M7.http` (plan 14 §2 "Demo" column), with results in `PROGRESS.md` |
| G5 | **Per-session checklist:** before coding, copy every Build bullet, Tests line and Done-when line of the session's plan 14 section into `PROGRESS.md` as check boxes. A session is `done` only when every Build and Done-when box is ticked (staging items via local smoke). Tests boxes are ticked in the test phase. Any deviation from 11 is fixed in 11 in the same commit. The PR checklist lists RUL / FR / endpoint numbers per session |
| G6 | **Spikes:** P0-3 before S-17 (QuestPDF Arabic invoice + receipt with IBM Plex Sans Arabic, RTL/LTR numbers, table, QR, licence note). P0-4 before S-20 (PdfSharp stamp on a generated 300-page PDF: timing and size, go/no-go). Code in `spikes/` (not in the solution build), notes in `store/spikes/P0-3-arabic-pdf.md` and `P0-4-watermark.md` |
| G7 | STEP 0 also runs `migrate-script.sh` and applies the script **twice** to the dev DB (no error), and checks the `create-admin` stub compiles |
| G9 | **Close-out H-1 (code-side hardening):** STRIDE walk (08 §6.2) against the code with findings fixed or logged; `dotnet list package --vulnerable`; Production config has `/docs` (Scalar) and `/dev/*` off; security headers; local load smoke (k6 if installable, else a short `bombardier`/`hey` or scripted run) for catalogue p95 and 50 checkouts; tune indexes from `pg_stat_statements` if something is slow |
| G10 | If the implemented settlement contract differs from 15 §2–§3, update `store/15-payment-service-handoff.md` in the same commit |
| G11 | Close-out verifies the **code-side launch items**: RUL list complete, policy matrix covers every admin route, `/dev` 404 and `/docs` off in Production, `create-admin` works, settings keys seeded (`vat_enabled`, `vat_rate`, `seller_vat_no`, shipping fee, windows), migrations backward-compatible |
| G12 | Pre-approve **SkiaSharp** (MIT, + `SkiaSharp.NativeAssets.Linux.NoDependencies`) for re-encoding images to strip EXIF. **Don't** use ImageSharp (its licence has revenue terms). Add **IBM Plex Sans Arabic** (OFL) font files under `src/Ta3lom.Store.Infrastructure/Resources/Fonts/` with the licence text; if the font can't be downloaded, use Noto Naskh Arabic (OFL) and log it |
| G13 | `StoreSettings` typed record covers every RUL §M key plus `seller_vat_no` and `dedication_templates`, with a 60-second cache and invalidation on update (09 §4.10) |
| G14 | Every new migration is additive (expand). No drop or rename in the same migration as the code that stops using it (contract later). Logged in `DECISIONS.md` when relevant |

---

## 3. Deferred by your decision (hosting phase, listed in the final report)

| Item | Plan 14 |
|---|---|
| VPS, Docker, Caddy TLS, Tailscale | §5 P0-2 |
| Dockerfile (chiseled-extra, non-root), `compose.dev.yml`, `compose.yml`, Caddyfile, `deploy.yml`, `rollback.sh`, GHCR, Trivy | S-03 |
| Staging verification of every "Done when" | §4.5 |
| ZAP baseline, restore drill, UAT, cutover | §12 |
| Launch checklist items that need servers: backups, monitoring, SMS sender ID, SPF/DKIM/DMARC, TOTP enrolment of staff, opening goal seeds | §15 |
| Payment service | §0, doc 15 |

---

## 4. Addendum to paste (after the Run-plan-review §4 message, or right away if that is already running)

```text
Addendum — make the run cover ALL of plan 14. Add these to Cloud-build-plan.md (§4c "Plan 14 coverage")
and to PROGRESS.md, then continue without stopping.

1. Per-session checklist: before coding each S-nn, copy every Build bullet, Tests line and Done-when line
   of its plan 14 section into PROGRESS.md as check boxes. A session is done only when every Build and
   Done-when box is ticked (staging items verified by local smoke). Tests boxes are ticked in the test
   phase. Any deviation from 11 is fixed in 11 in the same commit; the PR checklist lists RUL/FR/endpoint
   numbers per session. Also include the items in store/sessions/Plan-14-coverage-audit.md §2.2 if the
   plan text is less explicit.
2. Outbox: all 17 message types of 09 §4.9 (not RefundProcessed) get a handler, in the session shown in
   the audit §2.1: OrderPaid, RenderDocuments, FinanceAlert, DedicationCard (S-17 stub → S-26), LmsEnrol,
   ShipmentShipped/Delivered, LowStockAlert, SponsorUnitAllocated, AllocationAlert/Escalation,
   BeneficiaryInvite, UnitLapsed, LmsRevoke, SponsorUnitCompleted, CauseUpdatePublished, PoolSwept.
3. Adapters: SinkEmailSender (.eml files in artifacts/mail + GET /dev/mail) for Development, SmtpEmailSender
   by config; IEInvoicingGateway + NoopEInvoicingGateway (S-17); a real SMS provider class behind config
   with empty keys (S-04).
4. Spikes: P0-3 before S-17 (QuestPDF Arabic invoice + receipt, IBM Plex Sans Arabic, RTL/LTR numbers,
   table, QR, licence note) and P0-4 before S-20 (PdfSharp watermark on a 300-page PDF: timing, size,
   go/no-go). Code in spikes/ outside the solution, notes in store/spikes/.
5. Packages also approved: SkiaSharp + SkiaSharp.NativeAssets.Linux.NoDependencies (EXIF strip by
   re-encode; not ImageSharp). Fonts: IBM Plex Sans Arabic (OFL) in Infrastructure/Resources/Fonts with
   its licence; fallback Noto Naskh Arabic, logged.
6. STEP 0 extra: apply the idempotent migration script twice to the dev DB (no error) and confirm the
   create-admin stub builds.
7. Milestones: at each phase end run the plan 14 §2 demo as tools/smoke/M2.http … M7.http and record the
   result in PROGRESS.md.
8. Settings: StoreSettings covers every RUL §M key plus seller_vat_no and dedication_templates, 60 s cache,
   invalidated on update (09 §4.10).
9. Migrations are additive (expand → migrate → contract); if the settlement contract differs from
   15 §2–§3, update store/15-payment-service-handoff.md in the same commit.
10. Close-out adds H-1 code-side hardening before the test phase: STRIDE walk (08 §6.2) with fixes or
    logged findings, vulnerable-package scan, Production config with /docs and /dev off, security headers,
    local load smoke (k6 if installable, else a scripted run) for catalogue p95 and 50 concurrent checkouts,
    index tuning from pg_stat_statements. Also verify the code-side items of plan 14 §15.
11. Build-report.md gets a "Plan 14 coverage" section: every plan 14 section → done / tested / deferred
    (hosting items listed in audit §3), with commit references.
Keep all run rules: never end your turn until the whole run (build + tests + report) is finished.
```

---

## 5. Put this file in the repo first (the cloud session reads only the repo)

Push the file to the run branch, where the session is working:

```powershell
cd D:\Freelance\ELAMS\repos
if (Test-Path push-tmp) { Remove-Item -Recurse -Force push-tmp }
git clone --branch claude/funny-wright-yum3nm https://github.com/lotfi029/ta3lom_store_api.git push-tmp
cd push-tmp
Copy-Item "D:\Freelance\ELAMS\Docs\store\sessions\Plan-14-coverage-audit.md","D:\Freelance\ELAMS\Docs\store\sessions\Run-plan-review.md" store\sessions\
git add store/sessions/Plan-14-coverage-audit.md store/sessions/Run-plan-review.md
git status --short          # exactly 2 lines starting with "A"
git commit -m "Add plan 14 coverage audit and run plan review"
git push origin claude/funny-wright-yum3nm
cd ..; Remove-Item -Recurse -Force push-tmp
```

If the branch doesn't exist on GitHub yet (the session hasn't pushed), use `--branch main` and `git push origin main` instead. Then tell the session to run `git pull` before it reads the file. The addendum already says to read it.
