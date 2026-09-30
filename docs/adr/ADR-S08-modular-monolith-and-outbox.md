# ADR-S08 — Structure the store API as a modular monolith with a database outbox

- **Status:** Proposed
- **Date:** 2026-09-29
- **Deciders:** Lotfi (developer), association representative
- **Related:** RUL-39, RUL-63, FR-S34, FR-S80, FR-S83, database design §1.6 and §4.4, ADR-S01, ADR-S03

## Context

The store has clear business areas: Catalog, Cart, Checkout/Ordering, Payments, Fulfilment,
Giving (sponsorship, pool, beneficiaries, allocation, causes, zakat), Identity, Documents,
Notifications, Admin/Reporting, Settings. Some events cross many of them in one step: payment
settlement touches orders, stock, enrolments, units, pool, goal counters, zakat ledger, document
numbering and notifications, and **must commit atomically** (RUL-39). Side effects outside the
database (SMS, email, PDF rendering, LMS calls, rebuild dispatch) must happen reliably after the
commit, and a failed SMS must never un-pay an order (RUL-63).

One developer builds and runs it on one VPS with 8 GB RAM (ADR-S03).

## Options considered

### Option A — Microservices (one service per area)
Independent deployment and scaling per area.

Settlement would become a distributed transaction or a saga across services; each service needs
its own database, deployment and monitoring. Far beyond what one developer and one VPS can
operate, for a year-one volume under 50k orders.

### Option B — Monolith + message broker (RabbitMQ or similar)
Publish domain events to a broker after commit; consumers handle side effects.

Decouples side effects, but "commit then publish" can lose events if the process dies between the
two (the dual-write problem) unless an outbox is added anyway. Adds a broker container to run,
secure, back up and monitor.

### Option C — Modular monolith + transactional outbox in PostgreSQL  ← chosen
One deployable (`Ta3lom.Store.Api`) with module folders in each layer; side effects written as
rows in `outbox_message` in the same transaction as the business change; a hosted
`OutboxDispatcher` sends them.

## Decision

- **One process, modules by folder**: `Ta3lom.Store.Api` (host, endpoint groups per module, auth,
  OpenAPI), `Ta3lom.Store.Application` (handlers, validators, ports), `Ta3lom.Store.Domain`,
  `Ta3lom.Store.Infrastructure`, `Ta3lom.Store.Contracts`. Minimal APIs grouped per module.
- **Module boundaries enforced by tests** (`Ta3lom.Store.ArchitectureTests`, NetArchTest): Domain
  depends on nothing; Application does not reference Infrastructure; a module uses another module
  only through its application-layer interfaces.
- **Plain handler classes** (`ICommandHandler<TCommand,TResult>`, `IQueryHandler<…>`) registered
  by Scrutor scanning; **no MediatR** (commercial licence since 2025). Result pattern in the
  application layer; ProblemDetails at the edge.
- **Transactional outbox**: handlers add `outbox_message` rows (`type`, `payload`) inside the
  business transaction. `OutboxDispatcher` (hosted service) polls every second with
  `FOR UPDATE SKIP LOCKED`, dispatches by `type` to handlers (SMS, email, PDF render, LMS sync,
  rebuild dispatch), and retries with exponential backoff up to 24 h (`attempts`,
  `next_attempt_at`, `last_error`). Each outbox handler is idempotent; every sent message is
  recorded in `notification`, and `ux_notification_once (outbox_message_id, channel, recipient)`
  makes a duplicate send impossible. The **OTP SMS is the one exception**: it is sent directly in
  the request so the plain code is never stored (RUL-63).
- **No broker.** Jobs run in-process with Quartz.NET (`ReconcilePayments` every 5 min,
  `ExpireUnpaidOrders`, `CloseCampaigns`, `LmsProgressSync` hourly, `SponsorDigest` monthly,
  `LowStockAlert` daily, `PruneIdempotencyKeys` hourly and the rest in database operations §5).

## Consequences

**Positive**
- Settlement, allocation, refund and pooling are single ACID transactions (database design §4)
- No lost side effects: if it committed, its outbox row exists
- One image to build, deploy, roll back and monitor
- Modules can be extracted later if ever needed, because boundaries are already enforced
- Outbox backlog and age are simple health signals (`/health/ready`, FR-S83)

**Negative**
- Delivery is at-least-once; every outbox handler must be idempotent (e.g. a dedication SMS
  checks `dedication.sms_status` before sending, RUL-22)
- Polling adds a small constant DB load; latency of side effects is ~1 s, not immediate
- Everything scales together; a CPU-heavy PDF batch shares the process with checkout (mitigated by
  limiting concurrent renders)
- Architecture tests must be kept up to date as modules grow

**Neutral**
- `outbox_message` rows are pruned after 30 days (database operations §7)
- Adding a second API instance later works with the outbox as is; Quartz then needs its
  PostgreSQL job store

## Revisit when

- A second team or a second deployable (e.g. a separate LMS integration service) needs events
  from the store — then add a broker fed **from** the outbox
- Outbox throughput exceeds a few hundred messages per second, or its lag regularly exceeds
  one minute
- One module's load profile (e.g. PDF rendering) needs independent scaling
