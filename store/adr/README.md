# Store Architecture Decision Records

**Revision:** S1 draft · 29 September 2026

Store decisions use the prefix `ADR-S` and the main-site template
(`Docs/architecture/adr/ADR-000-template.md`). Main-site ADRs (`ADR-001` …) keep their meaning and
are cited where the store depends on them.

| ID | Decision | Status | Date |
|---|---|---|---|
| [ADR-S01](ADR-S01-separate-dotnet-store-service.md) | Build the store as a separate .NET service (not NestJS, not Salla/Zid) | Proposed | 2026-09-29 |
| [ADR-S02](ADR-S02-postgresql.md) | Use PostgreSQL 18 for the store database | Proposed | 2026-09-29 |
| [ADR-S03](ADR-S03-hostinger-vps-docker.md) | Host the store on a Hostinger VPS with Docker Compose | Proposed | 2026-09-29 |
| [ADR-S04](ADR-S04-otp-customer-identity.md) | Customers by mobile OTP; staff separate with password + TOTP | Proposed | 2026-09-29 |
| [ADR-S05](ADR-S05-hosted-payments-webhook-truth.md) | Hosted payment page; webhook is the only proof; provider behind an adapter | Proposed | 2026-09-29 |
| [ADR-S06](ADR-S06-sponsorship-units-and-pooling.md) | Sponsorship as a unit ledger with a FIFO pool and allocation register | Proposed | 2026-09-29 |
| [ADR-S07](ADR-S07-store-inside-ta3lom-web.md) | Store UI as a feature inside ta3lom-web, hybrid prerender/client | Proposed | 2026-09-29 |
| [ADR-S08](ADR-S08-modular-monolith-and-outbox.md) | Modular monolith with a PostgreSQL transactional outbox | Proposed | 2026-09-29 |

## Pending inputs

| ADR | Waiting on |
|---|---|
| ADR-S03 | Q-08 data residency |
| ADR-S05 | Q-01 provider and merchant agreement |

Status moves to **Accepted** at client sign-off of the S1 documentation set. A decision that
changes later gets a new ADR that supersedes the old one; the old file stays.

## Main-site ADRs referenced

| ADR | Used for |
|---|---|
| ADR-001 | Main stack stays as is (Angular, NestJS, MySQL on Hostinger shared) |
| ADR-002 | Prerendering over SSR — catalogue pages prerendered, live figures at runtime |
| ADR-003 | Bilingual columns and `pick()` fallback |
| ADR-006 | Token pattern reused for store customers and staff |
