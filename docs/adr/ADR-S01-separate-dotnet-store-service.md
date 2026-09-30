# ADR-S01 — Build the store as a separate .NET service

- **Status:** Proposed
- **Date:** 2026-09-29
- **Deciders:** Lotfi (developer), association representative
- **Related:** BR-S01–S14, BR-S10, BR-S11, BR-S12, ADR-001, ADR-S02, ADR-S03, ADR-S08

## Context

ta3lom.sa is being rebuilt as an Angular 22 static site with a NestJS 12 API and MySQL 8 on
Hostinger shared hosting (ADR-001). That stack serves content. The association now wants a store
that sells courses and books, lets donors sponsor seats and copies for beneficiaries, and sells
donation products with goals, modelled on store.kafeef.sa.

The store is a different kind of system from the content site:

- It moves money and must produce finance-grade records: gapless invoice and receipt numbers,
  a zakat ledger, pooled contributions converted into units, refunds that reverse all effects
  (RUL-39, RUL-45, RUL-58, RUL-59).
- It needs continuous background work: webhook settlement, a reconciliation job every 5 minutes,
  order expiry every 5 minutes, an outbox dispatcher polling every second, PDF rendering, nightly
  integrity checks (database operations §5).
- It holds customer accounts and encrypted beneficiary personal data (PDPL).

Hostinger shared hosting has no shell for npm, runs one Node process per website with no
scheduler guarantees, and its build/deploy form cannot run migrations reliably (main
06-deployment-architecture, constraints 2 and 8; the migrations-on-boot plan did not work).
The developer's primary background is .NET backend development.

## Options considered

### Option A — Extend the NestJS API (`ta3lom-api`)
Add store modules to the existing API and tables to the existing MySQL database.

One backend, one database, one auth system, one language. But every constraint of shared hosting
applies to financial code: no reliable scheduler for the reconciliation, expiry and outbox work,
no shell to fix a failed migration, shared CPU/RAM with the content site, and a store incident can
take the content site down with it. MySQL lacks the partial unique indexes the model relies on
(ADR-S02). The content API would grow from a small CMS into the most complex part of the estate.

### Option B — SaaS store (Salla or Zid), as Kafeef does
Kafeef runs on Salla with its donation extension.

Fastest to launch and no hosting to run. But it covers causes with goals and nothing more: no seat
sponsorship, no pooling of partial contributions into units, no beneficiary register, no FIFO
allocation, no seat status for donors (BR-S03, BR-S08); course access and digital watermarking
would be add-ons at best. It is a separate branded storefront, not part of ta3lom.sa (BR-S12);
data and reports live in the platform; fees are monthly plus per-transaction; and the separation of
invoices and donation receipts (RUL-59) and the zakat ring-fence (RUL-20) cannot be guaranteed.

### Option C — Separate ASP.NET Core service with its own database  ← chosen
A new repository `ta3lom-store-api`: .NET 10 LTS, modular monolith, Minimal APIs, PostgreSQL 18,
on a Hostinger VPS, consumed by a store feature inside ta3lom-web.

## Decision

Build the store as a **separate ASP.NET Core (.NET 10 LTS) service** in the new repository
`repos/ta3lom-store-api`, with its own PostgreSQL database, deployed at `store-api.ta3lom.sa`.
The main site and its API are unchanged apart from the integration points in
[08 §7](../08-solution-architecture.md#7-integration-with-the-main-site). The two backends share
nothing but the browser and the domain.

## Consequences

**Positive**
- Financial logic runs on a platform with a scheduler, a shell, containers and proper migrations
- A store incident does not affect the content site, and vice versa
- The domain model (units, pool, allocation, ledgers) is built exactly as the business rules
  describe, not bent into a SaaS product model
- The developer works in the stack he knows best for transactional back-ends
- The association owns the data, the reports and the customer relationship

**Negative**
- **Two backends** in two languages (TypeScript and C#), two dependency ecosystems, two CI pipelines
- **Two databases** (MySQL and PostgreSQL) to back up, monitor and restore
- **Two auth systems**: main-site editors and store staff log in separately; a staff member who
  does both has two accounts
- **Two deployment models**: Hostinger push-to-build for the main site, GitHub Actions + SSH
  deploy for the store; a VPS to patch and monitor (ADR-S03)
- More build effort than a SaaS store — estimated in [12-implementation-plan](../12-implementation-plan.md)
- API types for the store are generated from OpenAPI into ta3lom-web, alongside the main API's types

**Neutral**
- The main-site FR-40 to FR-45 donation flow is proposed to be superseded by the store (revision R3, Q-09)
- The store could later host other paid services of the association without touching the main site

## Revisit when

- The main API must move off shared hosting anyway (then consolidating both on one VPS is cheap,
  though not merging the codebases)
- The maintainer after handover has no .NET skills and the association cannot contract them
- Salla or Zid ship native seat sponsorship with allocation tracking and a headless API that
  ta3lom-web could render
