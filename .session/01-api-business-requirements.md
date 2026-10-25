# 01: Store API business requirements (r5 donations MVP)

**Project:** `StoreApi` (`lotfi029/ta3lom_store_api`), .NET 10 · PostgreSQL 18
**Date:** 25 Oct 2026 · **Status:** approved by the owner (Lotfi). The client still has to sign off.
**Based on:**

- [scope-change-r5-donations-only.md](../scope-change-r5-donations-only.md)
- [donations-mvp-requirements.md](../requirements/donations-mvp-requirements.md) (reference site: dsenaiah.org.sa)

**Companion docs:**

- [02-api-erd.md](02-api-erd.md)
- [03-api-rebuild-plan.md](03-api-rebuild-plan.md)
- [04-api-session-prompt.md](04-api-session-prompt.md)

---

## 1. Purpose

The store API collects **donations** for the association. A donor can give to:

- a **specific project**, using one of its fixed-price options or any amount; or
- a **general category**, any amount.

There are no courses or books, and no sales.

## 2. Releases

| Session | Scope |
|---|---|
| **This session: "R5-Core"** | Clean the repo · data model · **auth** (donors + staff) · **catalogue** (categories, projects, options, media) · **donations** (cart, checkout creates a *pending* donation order, order status, donor history) · **admin** (catalogue, donors, orders, staff, settings, audit, dashboard counts) |
| **Next session: "R5-Payments"** | Payment gateway (hosted page), webhook, `payment` / `payment_event`, settlement (`paid`), **donation receipts** (DR/DRC PDF + message), totals per project and category, reconciliation, expiring unpaid orders, refunds, the finance reports and dashboard totals |

Anything that depends on money being captured belongs to **R5-Payments**.

## 3. Actors

| Actor | Can do |
|---|---|
| **Visitor** | Browse categories and projects, search, build a donation cart (guest cart) |
| **Donor** (signed in by mobile number + SMS code) | Check out, see their donation orders, manage their profile, export or delete their data |
| **Staff:** `admin` | Everything, including staff and settings |
| **Staff:** `store_manager` | Catalogue, media, donors (read and block), orders (read) |
| **Staff:** `finance` | Orders (read, cancel pending), donors (read), dashboard. Refunds come next session |
| **Staff:** `viewer` | Read-only admin |

## 4. Business requirements

| ID | Requirement | Priority | Release |
|---|---|---|---|
| **BR-A01** | **Categories.** Staff manage donation categories (bilingual name and description, image, order, active). An active category accepts **general gifts** unless staff turn that off | Must | Core |
| **BR-A02** | **Projects.** Staff manage projects in a category: bilingual title, summary and story (Markdown), cover image, SEO fields, badge, order. Status is draft → published → archived | Must | Core |
| **BR-A03** | **Donation options.** A project has 0…n fixed-price options (e.g. "كفالة يوم 50 ﷼"). The donor picks one and a quantity | Must | Core |
| **BR-A04** | **Open amount.** A project can accept any amount at or above a minimum (per project, otherwise the global setting) | Must | Core |
| **BR-A05** | **General category gift.** A donor gives any amount to a category (quick donate) | Must | Core |
| **BR-A06** | **Discovery.** Public listing with a category filter, Arabic-aware search, sorting, paging, a project page by slug, a slug list for prerendering, and redirects from old slugs | Must | Core |
| **BR-A07** | **Donation cart.** Server-side cart for guests and donors. It merges on sign-in. Lines are project+option×qty, project+amount, or category+amount | Must | Core |
| **BR-A08** | **Checkout.** A signed-in donor confirms their name and email, and the API creates an **idempotent** donation order in `pending_payment` with re-priced line snapshots. The payment redirect is added next session | Must | Core |
| **BR-A09** | **Donor sign-in.** Mobile + 6-digit SMS code, no passwords. PDPL consent is recorded on first sign-in | Must | Core |
| **BR-A10** | **Donor account.** Profile, my donation orders, export and delete (anonymise) | Must | Core |
| **BR-A11** | **Staff sign-in.** Email + password + mandatory TOTP. Forced password change after a one-time password, plus self-service change. Lockout | Must | Core |
| **BR-A12** | **Admin.** Catalogue, media library, donors (search, block), donation orders (search, detail, cancel pending), staff and roles, settings, message templates (SMS), audit log, dashboard (counts) | Must | Core |
| **BR-A13** | **Audit.** Every staff change and every security event (logins, lockouts, token reuse) is written to `audit_log` | Must | Core |
| BR-A14 | Payment (hosted page, webhook truth, idempotent events, retry, reconciliation) | Must | **Payments** |
| BR-A15 | Donation receipts (gapless DR/DRC, Arabic PDF, SMS + email) | Must | **Payments** |
| BR-A16 | Totals per project and category; finance reports (CSV) | Must | **Payments** |
| BR-A17 | Refunds (finance only) | Should | **Payments** |
| — | Zakat, dedication (إهداء) cards, recurring giving, waqf, goals and progress bars, project updates, comments | Won't (MVP) | Later |

## 5. Business rules

| ID | Rule |
|---|---|
| **D-01** | A gift line has **exactly one target**: a project or a category. A project line is either an *option × quantity* or an *open amount*. A category line is always an *amount* |
| **D-02** | Money is in SAR, `numeric(12,2)`, sent over the wire as strings (`"50.00"`). `amount = unit_amount × quantity` (quantity is 1 for amount lines) |
| **D-03** | An open or category amount must be ≥ the minimum (`project.min_amount` → setting `min_gift_amount`, default 10) and ≤ 100,000. Quantity is 1…999 |
| **D-04** | Cart limits: ≤ 50 lines and ≤ 100,000 ﷼. Identical option lines merge (their quantities add up); amount lines never merge |
| **D-05** | Only **published** projects in an **active** category, and **active** categories with general gifts enabled, accept new lines. The checkout re-validates and re-prices every line from the database; the cart price is only indicative |
| **D-06** | A project can be published only with: Arabic title, summary and story; a cover image (an image asset); a category; and at least one active option **or** an open amount allowed |
| **D-07** | Allowed status changes: draft → published, published → archived, archived → published. Archived projects drop out of listings, but their old orders keep their snapshots |
| **D-08** | A slug matches `^[a-z0-9-]{2,80}$` and is unique. Changing the slug of a published project keeps the old slug for redirects |
| **D-09** | An order reference looks like `T3-YY-NNNNNN` (from a sequence). Checkout is idempotent per donor and `Idempotency-Key`. An order starts in `pending_payment`, and staff can cancel it while it's pending |
| **D-10** | Donor OTP: 6 digits, valid 5 min, 5 attempts. At most 3 per mobile per 15 min and 10 per IP per hour (counted under a lock). Errors never reveal whether an account exists |
| **D-11** | Sessions: access tokens last 15 min (donor) and 10 min (staff). Refresh tokens rotate, have an **absolute** family lifetime (60 days donor, 8 h staff), and reuse revokes the whole family |
| **D-12** | Staff: password ≥ 12 characters, not on the common-password list, PBKDF2-SHA512 at 600k iterations. 5 failures within 15 min lock the account for 1 h. A TOTP step can't be reused. The last active admin can't be removed |
| **D-13** | A blocked or anonymised donor can't sign in, refresh or check out |
| **D-14** | PDPL: `pdpl_consent_at` is set on first sign-in. Deletion anonymises the donor but keeps their orders. OTPs and idempotency keys are deleted after 24 h; guest carts after 30 days idle |

## 6. Non-functional requirements (MVP)

| Area | Requirement |
|---|---|
| Security | CORS / Origin allow-list: `https://store.ta3lom.sa`, `https://store-staging.ta3lom.sa` (+ localhost in Development). `/dev/*` exists in **Development only**. Non-Development environments refuse missing or DEV keys. Fallback policy is **authenticated**: public routes opt out explicitly. Rate limits on auth, cart, checkout and admin |
| Errors | RFC 7807 problem details with stable codes. Postgres 23505 → 409, 23503 → 422, concurrency → 409 `concurrency.conflict` |
| Concurrency | `xmin` concurrency tokens on `project`, `category`, `customer`, `staff_user`, `orders`. Updates send `version` |
| Performance | Public catalogue cached (ETag = body hash, output cache by known query keys only, evicted on catalogue change). ≤ 5 DB round trips per project page |
| Media | Images: jpeg/png/webp ≤ 10 MB and ≤ 40 MP, re-encoded to WebP ≤ 2400 px. SVG rejected. Content sniffed by magic bytes. Keys generated by the server |
| i18n | Arabic required, English optional with Arabic fallback. Errors localised (ar/en `.resx`) |
| Quality | Build passes with `-warnaserror`, `dotnet format`, architecture tests. Coverage: Domain ≥ 90 %, Application ≥ 80 %. Integration tests on `postgres:18` (Testcontainers) |
| Repo | **Code only**: no docs, specs, spikes, OpenAPI snapshot, deploy or tools folders in the repo (owner decision, 25 Oct). OpenAPI is served at runtime in Development |

## 7. Open questions (don't block R5-Core)

- **Q-01:** which payment provider. Blocks R5-Payments.
- **Q-02:** whether donations are outside VAT.
- **Q-10:** what content goes live at launch.
- **Q-11:** what the receipt must contain.
- **Q-14:** how a category gift is spent.
- **Q-15:** the minimum gift.

The defaults for this session are: minimum 10 ﷼, and category gifts are reported per category.
