# ADR-S07 — Build the store UI as a feature inside ta3lom-web

- **Status:** Proposed
- **Date:** 2026-09-29
- **Deciders:** Lotfi (developer), association representative
- **Related:** BR-S12, FR-S01–S09, FR-S19, FR-S82, main ADR-002, main ADR-003, main ADR-006, ADR-S04

## Context

The store must look and behave as part of ta3lom.sa, Arabic first, following the approved
prototype (BR-S12). ta3lom-web is an Angular 22 app, zoneless and standalone, prerendered to
static files on Hostinger (main ADR-002), with two locale builds (`/ar`, `/en`), strict budgets,
a hash-only CSP and an existing client-rendered admin area. The header must show a cart badge on
every page of the site (FR-S19). Catalogue pages need crawlable HTML and link previews (FR-S09);
cart, checkout and account pages are per-user.

## Options considered

### Option A — Separate Angular app at `store.ta3lom.sa`
Its own repository, build and deploy.

Independent release cycle and no effect on the main site's build. But the header, footer, tokens,
i18n, SEO service, image component and accessibility primitives would be duplicated or extracted
into a shared library that one developer must version; the cart badge on the main site would be
a cross-app integration; a separate subdomain splits SEO and makes the store feel like a
different site; two prerender pipelines to run.

### Option B — Feature inside ta3lom-web with hybrid rendering  ← chosen
Store routes under `/{ar,en}/store/**` and `/admin/store/**` in the same app, lazy-loaded,
using the same layout, tokens and conventions.

One design system, one header with the cart badge, one set of non-negotiables. The main site's
build now depends on the store API for prerendering, and the store's releases ride with the main
site's.

### Option C — Fully client-rendered store routes inside ta3lom-web
Simpler: no prerendering for the store at all.

Product and cause pages would have no crawlable content or link previews (WhatsApp sharing matters
for donations, FR-S08). Rejected for catalogue pages; used for per-user pages.

## Decision

Build the store as **`src/app/features/store/**`** in ta3lom-web, with store-specific UI in
`features/store/ui/**` and `core/store/` for the `STORE_API_URL` token, customer auth (token in a
signal), the cart store and interceptors. Admin screens live under `/admin/store/**` in the
existing admin area and authenticate against the store API as staff (ADR-S04).

Rendering:

| Routes | Mode |
|---|---|
| `/store`, `/store/courses`, `/store/books`, `/store/sponsor`, `/store/give`, `/store/p/:slug` | `RenderMode.Prerender`; live figures (price, stock, goal, donors) fetched at runtime (FR-S07) |
| `/store/cart`, `/store/checkout`, `/store/orders/:ref/thanks`, `/store/account/**`, `/store/card/:token`, `/store/login`, `/admin/store/**` | `RenderMode.Client` |

All ta3lom-web CLAUDE.md non-negotiables apply (logical CSS, RTL first, `pick()`, `SeoService`,
one `h1`, required alt, 44 px targets, reduced motion, token in a signal, budgets, no
`bypassSecurityTrust*`, `tokens.css`), component prefix `ta-`, e.g. `<ta-money>`).

## Consequences

**Positive**
- The store is visibly part of ta3lom.sa; one header, one footer, one theme, both locales
- Every existing guardrail (lint rules, budgets, axe, CSP check) covers the store automatically
- Cart badge and nav integration are ordinary components
- Product pages are crawlable and shareable with correct previews

**Negative**
- The existing `scripts/routes.mjs` → `routes.txt` mechanism is extended to fetch store slugs
  (`GET /v1/catalog/products?fields=slug`); no second mechanism. If the store API is unreachable
  at build time the build **fails**, so a stale or empty product list is never shipped silently —
  which also means a store outage blocks a main-site content release until it recovers
- Store releases are ta3lom-web releases; a store UI fix redeploys the whole static site
- The CSP must allow `store-api.ta3lom.sa` in `connect-src` and `img-src`
- The app carries two API clients and two auth contexts; interceptors must never mix tokens
- Lazy chunks must stay within the route budgets; checkout and admin are the largest

**Neutral**
- Client-rendered store routes need `.htaccess` rewrites to the locale's CSR entry, like `/admin`
- Generated store API types sit next to the main API types in `core/`

## Revisit when

- The store gets its own team or release cadence that conflicts with the main site's
- The ta3lom-web build time or bundle budgets are exceeded because of the store
- The association wants a separately branded store domain
