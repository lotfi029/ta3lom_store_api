# ADR-S03 — Host the store on a Hostinger VPS with Docker Compose

- **Status:** Proposed
- **Date:** 2026-09-29
- **Deciders:** Lotfi (developer), association representative
- **Related:** Q-08, ADR-001, ADR-S01, ADR-S02, NFR (availability), 08-solution-architecture §4–5

## Context

The store API is ASP.NET Core .NET 10 with PostgreSQL 18 (ADR-S01, ADR-S02). Hostinger shared
hosting, where the main site lives, cannot run .NET or PostgreSQL. The association already has a
Hostinger account and billing relationship and prefers low, predictable monthly cost. There is one
developer, who must be able to deploy, roll back and restore without a platform team.

The store needs: long-running background work (jobs, outbox), a writable persistent file volume
(book files, PDFs), a staging environment with a sandbox gateway, TLS on `store-api.ta3lom.sa`,
and off-server backups.

Data residency is open (Q-08): Hostinger VPS locations do not include Saudi Arabia.

## Options considered

### Option A — Hostinger VPS (KVM 2) with Docker Compose  ← chosen
Ubuntu 24.04, Docker Compose services `caddy`, `store-api`, `postgres:18`, `backup`; staging and
production as separate compose projects on the same machine; images from GHCR.

Cheapest option with full control; same vendor as the main site. The developer owns patching,
firewall, backups and monitoring. Single machine: a host failure takes the store down until
restored (RTO ≤ 4 h).

### Option B — Azure App Service + Azure Database for PostgreSQL (Flexible Server)
Managed runtime, managed database with point-in-time restore, deployment slots for zero-downtime
swaps.

Less operations work and better availability. Several times the monthly cost of a VPS once the
database, storage and a staging slot are counted; a second cloud vendor and billing relationship
for the association; background jobs still need an always-on plan. KSA region availability for
the needed services must be checked at decision time.

### Option C — Windows VPS with IIS (or a Windows shared .NET host)
The traditional .NET hosting model.

Familiar to .NET developers, but Windows licences raise the cost, PostgreSQL on Windows is a
second-class setup, shared .NET hosts do not allow long-running background services or
PostgreSQL, and container-based CI/CD (build once, run the same image in staging and production)
is harder.

## Decision

Run the store on a **Hostinger VPS KVM 2** (2 vCPU, 8 GB RAM, 100 GB NVMe), Ubuntu 24.04, with
**Docker Compose**: `caddy` (TLS, reverse proxy), `store-api` (chiselled ASP.NET image, blue/green),
`postgres:18`, `backup` sidecar. GitHub Actions builds the image, pushes it to GHCR and deploys
over SSH. Staging (`store-api-staging.ta3lom.sa`) and production share the host in separate
compose projects. Details in [08 §4–5](../08-solution-architecture.md#4-deployment-view).

## Consequences

**Positive**
- Low fixed cost; the association stays with one hosting vendor
- The same container image runs in CI, staging and production
- Full control over PostgreSQL configuration, backups and the files volume
- Moving to another host (including a KSA provider) is a compose file, a data restore and a DNS change

**Negative**
- The developer is the operator: OS patching, firewall, SSH hardening, fail2ban, certificate
  renewal (automated by Caddy), backup verification
- Single host, no automatic failover; RPO ≤ 15 min, RTO ≤ 4 h (database operations §4)
- Staging shares CPU and RAM with production; heavy staging tests can affect production
- Data is hosted outside KSA unless Q-08 changes the location

**Neutral**
- The main site stays on shared hosting; nothing there changes
- Plan upgrade to KVM 4 is available without code changes

## Revisit when

- **Q-08 is answered "the data must stay in KSA"** — move to a KSA-located VPS or cloud region
  (same images and compose file); the backup bucket moves too
- Sustained CPU above 70% or RAM above 85% during normal (non-campaign) weeks
- The association requires an availability target above what one host can give (e.g. 99.9% with
  failover), or a second developer/ops person joins who prefers a managed platform
