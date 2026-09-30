# ta3lom Store — Documentation (S1)

Business, database and implementation documentation for the ta3lom store: courses, books, seat
sponsorship and donation products. The store is built in **.NET** (API) and **Angular** (UI inside
`ta3lom-web`).

**Revision:** S1 draft · 29 September 2026 · **Status:** for client review and sign-off
**Visual reference:** [`design/ta3lom-store-prototype.html`](../../design/ta3lom-store-prototype.html) (approved)
**Prototype notes:** [store-prototype.md](store-prototype.md)

---

## Decisions already made

| Topic | Decision | Record |
|---|---|---|
| Backend | ASP.NET Core **.NET 10 (LTS)**, modular monolith, Minimal APIs | [ADR-S01](adr/ADR-S01-separate-dotnet-store-service.md), [ADR-S08](adr/ADR-S08-modular-monolith-and-outbox.md) |
| Database | **PostgreSQL 18**, EF Core 10 + Npgsql, schema `store` | [ADR-S02](adr/ADR-S02-postgresql.md) |
| Hosting | **Hostinger VPS** (Ubuntu 24.04, Docker Compose, Caddy TLS) at `store-api.ta3lom.sa`. Main site unchanged on shared hosting | [ADR-S03](adr/ADR-S03-hostinger-vps-docker.md) |
| Customer identity | Mobile + SMS OTP, JWT access token in memory, rotating refresh token in an HttpOnly cookie. Staff: email + password + TOTP | [ADR-S04](adr/ADR-S04-otp-customer-identity.md) |
| Payments | Hosted payment page, webhook is the only proof, gateway behind an adapter (provider pending Q-01) | [ADR-S05](adr/ADR-S05-hosted-payments-webhook-truth.md) |
| Sponsorship | Unit ledger + FIFO pool + allocation register | [ADR-S06](adr/ADR-S06-sponsorship-units-and-pooling.md) |
| Frontend | Feature `store` inside **ta3lom-web** (Angular 22). Catalogue prerendered, cart/checkout/account client-rendered | [ADR-S07](adr/ADR-S07-store-inside-ta3lom-web.md) |
| New repository | `repos/ta3lom-store-api` | [09-backend-dotnet.md](09-backend-dotnet.md) |

---

## Reading order

### Business

| # | Document | What it answers |
|---|---|---|
| 01 | [Business requirements](01-business-requirements.md) | Why, objectives, BR-S01…S14, scope, assumptions, open questions |
| 02 | [Stakeholders & personas](02-stakeholders-and-personas.md) | Who uses it and what they need |
| 03 | [**Business rules**](03-business-rules.md) | **The source of truth**: RUL-01…RUL-63 and settings |
| 04 | [Functional requirements](04-functional-requirements.md) | FR-S01…FR-S83 with acceptance criteria |
| 05 | [User stories](05-user-stories.md) | US-S stories with Gherkin acceptance |
| 06 | [Non-functional requirements](06-non-functional-requirements.md) | Performance, security, PDPL, accessibility, availability |
| 07 | [Process flows](07-process-flows.md) | Purchase, sponsorship, pooling, allocation, payment, refund, state machines |

### Database

| Document | |
|---|---|
| [database/01-database-design.md](database/01-database-design.md) | Principles, ERDs, transactions (settlement, pooling, allocation, refund), indexes, EF Core mapping |
| [database/02-data-dictionary.md](database/02-data-dictionary.md) | Every table and column (generated from the DDL) |
| [database/03-database-operations.md](database/03-database-operations.md) | Migrations, backups, jobs, integrity checks, retention/PDPL |
| [`database/store/`](../../database/store/) | `001_store_schema.sql`, `002_store_seed.sql`, `003_integrity_checks.sql`, `tests/smoke_scenario.sql` — executed and verified on PostgreSQL |

### Implementation

| # | Document | |
|---|---|---|
| 08 | [Solution architecture](08-solution-architecture.md) | Context, containers, integration with ta3lom.sa, deployment, security |
| 09 | [Backend — .NET](09-backend-dotnet.md) | Solution layout, modules, patterns, libraries, jobs, testing |
| 10 | [Frontend — Angular](10-frontend-angular.md) | Routes, rendering modes, state, components, admin, i18n |
| 11 | [API contract](11-api-contract.md) | Every endpoint, DTOs, errors, auth |
| 12 | [Implementation plan](12-implementation-plan.md) | Phases, sessions, gates, estimates, risks |
| 13 | [Traceability matrix](13-traceability-matrix.md) | BR → RUL → FR → US → tables → endpoints → screens |
| 14 | [**Backend implementation plan (.NET)**](14-backend-implementation-plan.md) | Session-by-session build of `ta3lom-store-api` **without payment processing** (r2): milestones, dependencies, build lists, tests, gates, Claude Code prompts |
| 15 | [Payment service hand-off](15-payment-service-handoff.md) | Scope and contracts (`IOrderSettlement`, `IPaymentInitiator`) for the separate payment-service session |
| 16 | [Cloud session preparation](16-cloud-session-preparation.md) | What to prepare before the payment service is built in a cloud Claude session: network allowlist, repo, secrets, decisions, kick-off prompt |
| 17 | [Claude Code prompts](17-claude-code-prompts.md) | Kick-off, per-session and review prompts for building the backend with Clean Architecture on .NET 11 |
| — | [adr/](adr/) | ADR-S01 … ADR-S08 |

---

## ID conventions

| Prefix | Meaning |
|---|---|
| `BR-S` | Store business requirement |
| `RUL-` | Business rule (03) |
| `FR-S` | Store functional requirement |
| `NFR-S` | Store non-functional requirement |
| `US-S` | Store user story |
| `ADR-S` | Store architecture decision |
| `Q-` | Open question (01 §7) |

Main-site IDs (`BR-`, `FR-`, `ADR-`) keep their meaning. Store documents cite them where they
depend on them (e.g. ADR-002 prerendering, ADR-006 tokens).

---

## Before build starts (Phase 0 gate)

1. Close Q-01 (payment provider + sandbox keys), Q-02 (VAT), Q-05 (LMS) and Q-08 (data residency) — see [01 §7](01-business-requirements.md#7-open-questions-must-close-before-phase-3)
2. Provision the VPS and the DNS record `store-api.ta3lom.sa`
3. Register the SMS sender ID (can take 1–3 weeks — start first)
4. Create `repos/ta3lom-store-api` under the same GitHub owner as the other repos
5. Client sign-off on 01, 03 and 04
