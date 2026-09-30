# ADR-S02 — Use PostgreSQL 18 for the store database

- **Status:** Proposed
- **Date:** 2026-09-29
- **Deciders:** Lotfi (developer), association representative
- **Related:** BR-S11, RUL-13, RUL-31, RUL-34, RUL-45, RUL-47, RUL-51, ADR-001, ADR-S01, ADR-S03

## Context

The store is a separate .NET service on a VPS (ADR-S01, ADR-S03), so it is not limited to the
MySQL that Hostinger shared hosting offers. The data model is designed to let the database
enforce the rules it can (database design §1):

- Partial unique indexes: one live enrolment per course (`ux_enrolment_live`, RUL-13), one active
  offer per product (`ux_offer_one_active`), one general fund, one default address, one active
  customer cart
- CHECK constraints on every state value and on money arithmetic; `numeric(12,2)` money (RUL-31)
- `FOR UPDATE SKIP LOCKED` for FIFO allocation and the outbox (RUL-48)
- Idempotency via unique `(provider, provider_event_id)` with `ON CONFLICT DO NOTHING` (RUL-34), the
  `idempotency_key` store for `Idempotency-Key` requests, and `ux_notification_once` so an outbox
  retry never sends the same message twice
- Case-insensitive emails (`citext`), staff roles as `text[]`, `jsonb` snapshots
- Arabic-tolerant search (`tsvector` + `pg_trgm` over the SQL function `ar_normalize(text)`, FR-S03)
- Time-ordered UUID keys (`uuidv7()`)

The DDL (`database/store/001_store_schema.sql`, 49 tables, 4 views) was executed and verified on
PostgreSQL. Year-one volume is small (< 50k orders).

## Options considered

### Option A — Reuse the main site's MySQL 8 on shared hosting
The store API on the VPS connects to the MySQL database on shared hosting over Remote MySQL.

No second engine. But it couples the two systems at the data layer, which ADR-S01 exists to
avoid; every query crosses data centres over the public internet with Remote MySQL's IP
allow-list; the shared plan's resource limits apply to financial transactions; and backups stay
on the shared-hosting export path. MySQL 8 has CHECK constraints and `SKIP LOCKED` but no partial
(filtered) unique indexes, no `citext`, no arrays and no transactional DDL, so several rules would
move from the database into code.

### Option B — SQL Server (Express or Standard) in a Linux container
The natural pairing for .NET, with filtered indexes and mature EF Core support.

Express is free but capped at 10 GB per database and limited memory and CPU; Standard carries a
licence cost the association would pay yearly. The container needs ~2 GB RAM minimum on an 8 GB
VPS already shared with staging. There is no `citext` equivalent without collation work, and
Arabic-tolerant search needs Full-Text Search, which is heavier to run in a container.

### Option C — PostgreSQL 18 in a container on the VPS  ← chosen
Free, first-class EF Core provider (Npgsql), every feature listed in the context, modest memory.

## Decision

Use **PostgreSQL 18** in the `postgres:18` container on the store VPS, schema `store`, accessed
through **EF Core 10 + Npgsql** with `EFCore.NamingConventions` (snake_case). Roles `store_owner`
(migrations), `store_app` (DML), `store_report` (read-only views).

## Consequences

**Positive**
- Business invariants are enforced by the database as the last line of defence
- `uuidv7()` built in; `Guid.CreateVersion7()` in .NET yields the same ordering
- No licence cost; small footprint (`shared_buffers = 2GB` on the 8 GB VPS)
- Transactional DDL makes a failed migration roll back cleanly

**Negative**
- The estate runs **two database engines**; the developer operates both
- Backups, WAL archiving, upgrades and restore drills are self-managed on the VPS (database
  operations §4), not provided by a host
- Staff reporting tools that expect MySQL need a separate connection (`store_report`)

**Neutral**
- PostgreSQL 16/17 would need a `uuidv7()` shim; production pins 18
- No PgBouncer at this scale; Npgsql pooling is enough

## Revisit when

- Q-08 requires KSA hosting and a managed PostgreSQL service in KSA becomes the simpler option
  (the schema moves unchanged)
- `audit_log` or `outbox_message` approach 5M rows despite retention (partitioning)
- The association standardises on a different engine for all its systems
