# Store Database Operations

Migrations, environments, backups, retention, integrity checks and PDPL handling for the store's
PostgreSQL database.

---

## 1. Environments

| Env | Where | Database | Data |
|---|---|---|---|
| Local | Developer machine, `docker compose up db` | `ta3lom_store_dev` | Seed + fake orders from `tools/fake-data` |
| CI | GitHub Actions service container `postgres:18` | ephemeral | Migrations + seed, then integration tests (Testcontainers) |
| Staging | VPS, separate compose project `store-staging` | `ta3lom_store_staging` | Seed; gateway in **sandbox** mode |
| Production | VPS, compose project `store-prod` | `ta3lom_store` | Real |

Staging and production share the VPS but use separate containers, volumes, DB roles and secrets.

---

## 2. Migrations (EF Core)

- Migrations live in `repos/ta3lom-store-api/src/Ta3lom.Store.Infrastructure/Persistence/Migrations`.
- One migration per pull request that changes the model. Name it by intent (`AddCauseUpdateNotify`), not `Update1`.
- CHECK constraints, partial indexes, views and grants are added with `migrationBuilder.Sql(...)` or `HasCheckConstraint`/`HasFilter` in the same migration.
- **Deployment never runs `Database.Migrate()` at app start-up.** CI produces an idempotent script:
  ```bash
  dotnet ef migrations script --idempotent -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure -o artifacts/migrate.sql
  # or: tools/scripts/migrate-script.sh
  ```
  The deploy job runs it with the `store_owner` role **before** switching containers. It stops on the first error.
- Destructive changes (dropping or renaming a column) use expand → migrate data → contract across two releases.
- After each migration, update [`001_store_schema.sql`](../../db/reference/001_store_schema.sql) by hand (it keeps its comments) until `SchemaParityTests` passes, then regenerate the [data dictionary](02-data-dictionary.md) with `tools/scripts/regenerate-data-dictionary.sh`. `tools/scripts/regenerate-schema-sql.sh` writes a pg_dump of the migrated schema to `db/reference/generated/` for DBA review. `Model_has_no_changes_missing_a_migration` fails when the model changed without `dotnet ef migrations add`.
- Grants (§7 of database/01) are in the initial migration and skip roles that do not exist yet; operations create `store_owner`, `store_app` and `store_report` (S-03).

## 3. Seeding

| Data | Mechanism |
|---|---|
| Settings, message templates, document counters, dedication templates | `SeedRunner` in the background after start-up (`Store:Seed:RunOnStartup`), `INSERT … ON CONFLICT DO NOTHING`: inserts only missing rows, never overwrites staff edits |
| Categories and the prototype catalogue | [`002_store_seed.sql`](../../db/reference/002_store_seed.sql) for staging; production catalogue entered by staff. Its settings/template/counter inserts are `ON CONFLICT DO NOTHING`, so it runs before or after `SeedRunner` |
| First admin | CLI: `dotnet Ta3lom.Store.Api.dll create-admin --email … ` prints a one-time password; TOTP enrolment forced at first login |
| Opening goal figures | `goal_progress.seed_*` by admin screen (offline giving before launch), audited |

A new year's `document_counter` rows are created lazily inside the numbering transaction.

---

## 4. Backups and restore

| What | How | Frequency | Retention |
|---|---|---|---|
| Logical dump | `pg_dump -Fc` from a sidecar container, encrypted with `age`, uploaded off-server (S3-compatible bucket; location per Q-08) | Every 6 h | 14 days hourly-level, 12 monthly |
| WAL archiving / PITR | `pgBackRest` to the same bucket | Continuous | 7 days |
| Files volume (book PDFs, receipts, images) | `restic` to the bucket | Daily | 30 days |
| Data Protection key ring | Copied with the files backup; **without it, beneficiary PII cannot be decrypted** | Daily | Same as data |

**Restore drill:** monthly on staging. Restore the latest dump, run the integrity checks (§6),
record the time taken. Target RPO ≤ 15 min (PITR), RTO ≤ 4 h.

---

## 5. Scheduled jobs that touch the database (Quartz.NET)

| Job | Schedule | Effect | Rule |
|---|---|---|---|
| `ExpireUnpaidOrders` | every 5 min | `pending_payment`/`payment_failed` past `payment_due_at` → `expired`, release reserved stock | RUL-29/30 |
| `ReconcilePayments` | every 5 min | Query gateway for `initiated` > 5 min (until `payment_due_at` + 24 h); apply captures via the settlement transaction | RUL-38 |
| `OutboxDispatcher` | continuous (1 s poll, `FOR UPDATE SKIP LOCKED`) | Send notifications, render PDFs, LMS sync, with exponential backoff up to 24 h | RUL-63 |
| `CloseCampaigns` | 00:05 Riyadh | Offers/causes past `ends_at`: stop sales; sweep pool remainder to the general fund (`goal_adjustment` pair) | RUL-09/46 |
| `AllocationAlerts` | 07:00 Riyadh | Units awaiting > 30 / 90 days → staff alert / escalation | RUL-49 |
| `LapseUnits` | 02:00 Riyadh | Allocated units with an enrolment not started in 60 days → `lapsed` → back to awaiting | RUL-50 |
| `ExpireGuestCarts` | 03:00 | Delete guest carts past `expires_at` | RUL-16 |
| `LmsProgressSync` | hourly | Pull progress and completion from the LMS adapter → `enrolment.progress_pct`, `completed_at`; complete sponsored units | RUL-42/50 |
| `SponsorDigest` | 1st of month, 09:00 Riyadh | Email each sponsor a summary of their units' status changes | RUL-63 |
| `LowStockAlert` | 08:00 Riyadh | Print formats with available ≤ threshold → staff alert | FR-S43 |
| `PruneIdempotencyKeys` | hourly | Delete `idempotency_key` older than 24 h | API §1 |
| `PruneOtp` | hourly | Delete `otp_challenge` older than 24 h | PDPL |
| `IntegrityChecks` | 04:00 | Checks in §6; failures go to the staff alert channel | — |
| `Retention` | weekly | §7 | PDPL |

---

## 6. Nightly integrity checks

Each query must return **zero rows**.

```sql
SET search_path = store;

-- 1. Order totals equal the sum of lines (+ shipping)
SELECT o.reference FROM orders o JOIN order_line l ON l.order_id = o.id
GROUP BY o.id HAVING o.purchases_total + o.gifts_total <> sum(l.amount);

-- 2. Paid orders have exactly one captured payment for the full amount
SELECT o.reference FROM orders o
WHERE o.status IN ('paid','fulfilled') AND
      (SELECT coalesce(sum(amount),0) FROM payment p WHERE p.order_id = o.id AND p.status IN ('captured','partially_refunded','refunded')) <> o.grand_total;

-- 3. Stock counters match the ledger
SELECT f.sku FROM book_format f
WHERE f.kind = 'print' AND f.stock_on_hand <> (SELECT coalesce(sum(delta),0) FROM stock_movement m WHERE m.book_format_id = f.id);

-- 4. Pool balance equals unconverted contributions
SELECT sp.sponsorship_offer_id FROM sponsorship_pool sp
WHERE sp.balance <> (SELECT coalesce(sum(remaining),0) FROM pool_contribution c
                     WHERE c.sponsorship_offer_id = sp.sponsorship_offer_id AND c.swept_to_fund_at IS NULL);

-- 5. Full-unit lines created exactly quantity units
SELECT l.id FROM order_line l JOIN orders o ON o.id = l.order_id
WHERE l.kind = 'sponsor' AND l.mode = 'units' AND o.paid_at IS NOT NULL
  AND l.quantity <> (SELECT count(*) FROM sponsored_unit u WHERE u.order_line_id = l.id);

-- 6. Goal counters match paid gift lines + adjustments (excluding seed)
SELECT g.product_id FROM goal_progress g
WHERE g.raised_amount <> (SELECT coalesce(sum(l.amount - l.refunded_amount),0) FROM order_line l JOIN orders o ON o.id = l.order_id
                          WHERE l.product_id = g.product_id AND l.kind IN ('sponsor','donate') AND o.paid_at IS NOT NULL)
                       + (SELECT coalesce(sum(a.amount),0) FROM goal_adjustment a WHERE a.product_id = g.product_id);

-- 7. Zakat never disbursed beyond receipts
SELECT product_id FROM v_zakat_balance WHERE balance < 0;

-- 8. Every paid order with gift lines has a receipt; with purchase lines an invoice
SELECT o.reference FROM orders o WHERE o.paid_at IS NOT NULL
  AND o.gifts_total > 0 AND NOT EXISTS (SELECT 1 FROM donation_receipt r WHERE r.order_id = o.id AND r.kind = 'receipt');
SELECT o.reference FROM orders o WHERE o.paid_at IS NOT NULL
  AND o.purchases_total > 0 AND NOT EXISTS (SELECT 1 FROM invoice i WHERE i.order_id = o.id AND i.kind = 'invoice');

-- 9. Document numbers are gapless
SELECT series, yr FROM (
  SELECT 'DR' AS series, extract(year FROM issued_at)::int AS yr, count(*) AS n, max(split_part(number,'-',3)::int) AS mx
  FROM donation_receipt WHERE kind = 'receipt' GROUP BY 2) t WHERE n <> mx;   -- repeat for INV, CN, DRC
```

---

## 7. Retention and PDPL

| Data | Retention | Action |
|---|---|---|
| Orders, lines, payments, invoices, receipts, zakat ledger | 10 years (financial records) | Kept; customer identity anonymised on account deletion |
| `payment_event.payload` | 18 months | Payload replaced by `{}` after 18 months; row kept |
| `otp_challenge` | 24 h | Deleted |
| Guest carts | 30 days idle | Deleted |
| `notification` | 12 months | Deleted |
| `outbox_message` (processed) | 30 days; `SponsorDigest` rows 13 months (the last-digest record, S-45) | Deleted |
| `download_log`, IP hashes | 12 months | Deleted |
| `audit_log` | 5 years | Kept |
| Beneficiary PII | While active + 2 years | `full_name_enc`/`mobile_enc` nulled; code and aggregate history kept |

**Customer data request (PDPL rights):**
- *Export:* `GET /v1/me/export` → JSON of profile, addresses, orders, giving, enrolments.
- *Delete:* `DELETE /v1/me` → `customer.status = 'anonymised'`, name/email/mobile replaced
  (`mobile = '+9665' || random 8 digits`, marked), addresses deleted, refresh tokens revoked.
  Orders keep `contact_*` replaced with `"محذوف / deleted"`. Enrolments are revoked unless the course was sponsored for them.

---

## 8. Monitoring

- `pg_stat_statements` enabled; the slowest 10 queries are reviewed weekly during the first month.
- Alerts: connections > 80% of `max_connections`, replication/backup failures, disk > 80%, any integrity-check row, and an outbox backlog over 500 or older than 30 min.
- Connection pooling: Npgsql's built-in pool, `Maximum Pool Size=40`. No PgBouncer at this scale.

## 9. Sizing (VPS KVM 2: 2 vCPU / 8 GB)

`shared_buffers = 2GB`, `effective_cache_size = 5GB`, `work_mem = 16MB`, `maintenance_work_mem = 256MB`,
`max_connections = 100`, `wal_compression = on`, `random_page_cost = 1.1` (NVMe).
