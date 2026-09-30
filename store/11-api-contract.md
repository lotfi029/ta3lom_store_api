# 11 — API Contract

**Revision:** S1 draft r1 · 29 September 2026 (reconciled with the canonical changes after round 1)

The REST contract of `ta3lom-store-api`, consumed by the store feature and the admin area of
`ta3lom-web`. The implementation is described in [09-backend-dotnet.md](09-backend-dotnet.md); the
rules behind every validation are in [03-business-rules.md](03-business-rules.md) (RUL-nn) and the
requirements in [04-functional-requirements.md](04-functional-requirements.md) (FR-S). Field names
map to the columns of [`001_store_schema.sql`](../../database/store/001_store_schema.sql) in
camelCase (`seat_price` → `seatPrice`).

The running OpenAPI document (`/openapi/v1.json`, Scalar UI at `/docs` on staging) is generated
from `Ta3lom.Store.Contracts` and is the machine-readable form of this file. When they disagree,
fix whichever is wrong in the same pull request (09 §10.3).

---

## Contents

1. [Conventions](#1-conventions)
2. [Catalog (public)](#2-catalog-public)
3. [Cart](#3-cart)
4. [Auth (customers)](#4-auth-customers)
5. [Me (customer account)](#5-me-customer-account)
6. [Checkout and orders](#6-checkout-and-orders)
7. [Payment webhooks](#7-payment-webhooks)
8. [Cards and downloads](#8-cards-and-downloads)
9. [Admin](#9-admin)
10. [Health](#10-health)
11. [TypeScript types](#11-typescript-types)
12. [Endpoint index](#12-endpoint-index)
13. [Open points](#13-open-points)

---

## 1. Conventions

### 1.1 Base URL and versioning

| | |
|---|---|
| Production | `https://store-api.ta3lom.sa/v1` |
| Staging | `https://store-api-staging.ta3lom.sa/v1` |
| Local | `https://localhost:7180/v1` |
| Health | `/health/live`, `/health/ready` (unversioned) |

The major version is in the path. Additive changes (new endpoints, new optional fields, new error
codes, new enum values documented as open) do not change the version. Clients must ignore unknown
fields. A breaking change ships as `/v2` alongside `/v1` for at least one ta3lom-web release.

### 1.2 Authentication

| Area | Paths | Credential |
|---|---|---|
| Public | `/v1/catalog/**`, `/v1/cards/{token}`, `/v1/downloads/{token}` | none |
| Order status | `/v1/orders/{ref}/status` | customer bearer **or** the per-order status token from `POST /v1/checkout`; never by reference alone |
| Guest cart | `/v1/cart/**` | `t3s_cart` cookie (optional; a customer token takes precedence) |
| Customer | `/v1/me/**`, `/v1/checkout`, `/v1/orders/{ref}/retry-payment` | `Authorization: Bearer <access token>`, audience `store-customer` |
| Customer session | `/v1/auth/**` | `t3s_rt` refresh cookie |
| Staff | `/v1/admin/**` | `Authorization: Bearer <access token>`, audience `store-staff`, role claims |
| Staff session | `/v1/admin/auth/**` | `t3s_srt` refresh cookie |
| Webhooks | `/v1/payments/webhooks/{provider}` | provider signature (§7) |

Tokens: customer access 15 min, staff access 10 min, both kept in memory by the client (Angular
signal), never in `localStorage`. On `401 auth.unauthenticated` the client calls the refresh
endpoint once (single-flight) and retries.

Cookies (all `HttpOnly; Secure; SameSite=Lax`, host-only on `store-api.ta3lom.sa`, which is the same
site as `ta3lom.sa`):

| Cookie | Path | Max-Age | Content |
|---|---|---|---|
| `t3s_rt` | `/v1/auth` | 60 days | customer refresh token (raw; only its SHA-256 is stored) |
| `t3s_srt` | `/v1/admin/auth` | 8 h | staff refresh token |
| `t3s_cart` | `/v1` | 30 days | Data Protection-sealed guest cart id (RUL-11, RUL-16) |

CORS: origins `https://ta3lom.sa`, `https://www.ta3lom.sa` (plus `http://localhost:4200` in
Development), `Access-Control-Allow-Credentials: true`, exposed headers `ETag`, `Retry-After`,
`Location`, `Idempotent-Replayed`. Browser calls to cookie routes use `withCredentials: true`. Unsafe requests to cookie
routes with any other `Origin` return `403 http.origin_forbidden`.

Staff roles (RUL-62, FR-S71) and the policies used in this document:

| Policy tag | Roles |
|---|---|
| `staff:read` | any staff role, including `viewer` |
| `staff:catalogue` | `admin`, `store_manager` |
| `staff:stock` | `admin`, `store_manager`, `fulfilment` |
| `staff:fulfilment` | `admin`, `fulfilment` |
| `staff:finance` | `admin`, `finance` |
| `staff:refund` | `finance` only (RUL-56) |
| `staff:beneficiaries` | `admin`, `allocation_officer` |
| `staff:allocate` | `allocation_officer` only (RUL-47) |
| `staff:reveal-pii` | `allocation_officer` only (FR-S52) |
| `staff:resend` | `admin`, `finance`, `store_manager` (proposed, 09 OP-13) |
| `staff:admin` | `admin` |

Segregation of duties (RUL-62): `staff:refund`, `staff:allocate` and `staff:reveal-pii` are **not**
satisfied by `admin`; the person must hold `finance` or `allocation_officer` explicitly (roles are an
array, so one account may hold several). A `blocked` customer cannot sign in or check out (RUL-61).

### 1.3 Format

- JSON, UTF-8, **camelCase**. Enum values are the DDL strings (`pending_payment`, `awaiting_allocation`).
- **Money is a string** with exactly two decimals: `"350.00"`, SAR implied (RUL-31). Clients never
  parse money into JavaScript numbers for arithmetic; the API returns every total the UI shows.
- Timestamps: ISO 8601 UTC, `"2026-09-29T10:15:00Z"`. Dates: `"2026-09-02"`. Campaign ends are
  stored in UTC from an Asia/Riyadh wall time (`2026-12-31 23:59:59+03` → `"2026-12-31T20:59:59Z"`).
- Bilingual content: both languages are returned as `…Ar` / `…En` (`titleEn` may be `null`); the client
  applies `pick()` (RUL-03, ADR-003). Error `title`/`detail` follow `Accept-Language: ar|en` (default `ar`).
- Mobiles: accepted as `05XXXXXXXX` (or `+9665XXXXXXXX`), returned masked for display
  (`"05•••••567"`) except in the owner's own profile.
- IDs: UUID v7 strings. Human references: order `T3-26-004187`, documents `INV-2026-000412`,
  `DR-2026-000187`, beneficiary `B-000123`.

### 1.4 Pagination and filtering

`?page=1&pageSize=20` (1-based, `pageSize` 1–100, default 20). List responses:

```json
{ "items": [ … ], "total": 57, "page": 1, "pageSize": 20 }
```

Sorting uses `?sort=` with documented values per endpoint; date ranges use `from`/`to`
(`YYYY-MM-DD`, Asia/Riyadh, inclusive).

### 1.5 Idempotency

`Idempotency-Key: <uuid>` is **required** on `POST /v1/checkout` and
`POST /v1/admin/orders/{ref}/refunds`, optional on `POST /v1/orders/{ref}/retry-payment`.

| Situation | Response |
|---|---|
| First request | row claimed in `idempotency_key` (`scope`, `key`, `request_hash`); processed; `status_code` + `response` stored; pruned after 24 h (`PruneIdempotencyKeys`) |
| Same key, same body | stored response replayed, header `Idempotent-Replayed: true` |
| Same key, different body | `422 idempotency.key_reused` |
| Same key while the first is still running | `409 idempotency.in_progress` |
| Header missing | `400 idempotency.key_required` |

`scope` = `checkout:{customerId}`, `retry:{customerId}` or `refund:{staffId}`; `request_hash` = SHA-256
of the canonical JSON body. A `5xx` result releases the key so the client can retry with it (09 §5.16).
Checkout is also naturally idempotent: a converted cart returns the order it produced. The refund key
is passed to the gateway as its idempotency key.

### 1.6 Errors — ProblemDetails (RFC 9457)

Every error is `application/problem+json` with a stable machine `code`:

```json
{
  "type": "https://store-api.ta3lom.sa/problems/cart.campaign_ended",
  "title": "انتهت الحملة",
  "status": 409,
  "detail": "لم يعد هذا المنتج يستقبل مساهمات جديدة.",
  "code": "cart.campaign_ended",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

Validation errors add `errors` (camelCase field → messages). Checkout line errors add `lines`.
The client switches on `code`, never on `title`.

| Code | HTTP | Meaning | Rule |
|---|---|---|---|
| `validation.failed` | 400 | shape validation; see `errors` | — |
| `http.not_found` | 404 | unknown route or resource | — |
| `http.rate_limited` | 429 | rate limit; `Retry-After` header | NFR |
| `http.origin_forbidden` | 403 | unsafe cookie request from a foreign origin | — |
| `auth.unauthenticated` | 401 | missing/expired access token | — |
| `auth.forbidden` | 403 | role does not allow the action | RUL-62, FR-S71 |
| `auth.account_blocked` | 403 | customer `status = 'blocked'`: sign-in and checkout refused | RUL-61, FR-S79 |
| `auth.refresh_invalid` / `auth.refresh_expired` | 401 | refresh cookie unknown / expired | RUL-60 |
| `auth.refresh_reused` | 401 | rotated token reused; family revoked | RUL-60 |
| `auth.refresh_race` | 409 | concurrent refresh within 20 s; retry once | — |
| `auth.invalid_credentials` | 401 | staff email or password wrong (uniform) | RUL-62 |
| `auth.account_locked` | 423 | staff locked; `lockedUntil` | RUL-62 (FR-53) |
| `auth.totp_required` / `auth.totp_invalid` / `auth.totp_not_enrolled` | 401 | staff second factor | RUL-62 |
| `otp.mobile_invalid` | 400 | not a Saudi mobile | RUL-60 |
| `otp.rate_limited` | 429 | 3 / mobile / 15 min or 10 / IP / hour; `retryAfterSeconds` | RUL-60 |
| `otp.invalid` | 400 | wrong code; `attemptsLeft` | RUL-60 |
| `otp.expired` | 400 | no open code or older than 5 min | RUL-60 |
| `otp.too_many_attempts` | 429 | 5 wrong attempts on this code | RUL-60 |
| `concurrency.conflict` | 409 | row changed (xmin); reload and retry | — |
| `idempotency.key_required` / `idempotency.key_reused` / `idempotency.in_progress` | 400 / 422 / 409 | §1.5 | — |
| `catalog.product_not_found` | 404 | slug unknown or not published | RUL-02 |
| `cart.product_unavailable` | 409 | product not published / format inactive | RUL-02, RUL-05 |
| `cart.mode_not_allowed` | 422 | kind/mode not allowed for this product | RUL-12 |
| `cart.quantity_limit` | 422 | quantity above the line maximum; `max` | RUL-12 |
| `cart.amount_below_minimum` | 422 | below `min_gift_amount` / cause `min_amount`; `min` | RUL-12 |
| `cart.campaign_ended` | 409 | offer or cause closed | RUL-09, RUL-10 |
| `cart.duplicate_course` | 409 | course already in the cart as `buy` | RUL-13 |
| `cart.course_owned` | 409 | customer already enrolled | RUL-13 |
| `cart.course_in_unpaid_order` | 409 | course is in one of the customer's `pending_payment`/`payment_failed` orders; `orderRef` | RUL-13 |
| `cart.zakat_not_allowed` | 422 | zakat on a non-eligible product | RUL-19 |
| `cart.dedication_not_allowed` | 422 | dedication on a `buy` line | RUL-21 |
| `cart.dedication_invalid` | 400 | name/mobile/template invalid | RUL-21 |
| `cart.dedication_free_text_disabled` | 422 | free text while the setting is off | RUL-21 |
| `cart.out_of_stock` | 409 | print quantity above available; `available` | RUL-30 |
| `cart.too_many_lines` | 422 | more than 50 lines | RUL-18 |
| `cart.total_exceeded` | 422 | above `cart_max_total`; `max` | RUL-18 |
| `cart.line_not_found` | 404 | line id not in this cart | — |
| `checkout.cart_empty` | 409 | nothing to pay | — |
| `checkout.lines_invalid` | 422 | one or more lines fail re-validation; `lines[]` | RUL-24 |
| `checkout.price_changed` | 409 | reprice differs; `quote` with new totals | RUL-15 |
| `checkout.out_of_stock` | 409 | reservation failed | RUL-30 |
| `checkout.address_required` | 422 | print line without address | RUL-26 |
| `checkout.contact_required` | 422 | name or email missing | RUL-25 |
| `checkout.terms_not_accepted` | 422 | terms checkbox false | FR-S30 |
| `checkout.payment_method_not_allowed` | 422 | method not offered (BNPL in S1) | RUL-36 |
| `checkout.gateway_unavailable` | 503 | gateway session could not be created; order kept `pending_payment` | RUL-37 |
| `order.not_found` | 404 | unknown reference or not the caller's | — |
| `order.retry_not_allowed` | 409 | status not `payment_failed`/`pending_payment` | RUL-37 |
| `order.payment_window_closed` | 410 | `payment_due_at` passed | RUL-29, RUL-37 |
| `order.cancel_not_allowed` | 409 | cancel only from `pending_payment`/`payment_failed` | RUL-29 |
| `order.not_in_review` | 409 | resolve only from `payment_review` | RUL-35 |
| `webhook.signature_invalid` | 401 | signature/secret check failed | RUL-33, FR-S34 |
| `webhook.provider_unknown` | 404 | unknown `{provider}` | — |
| `download.limit_reached` | 403 | `download_count = max_downloads` | RUL-41 |
| `download.revoked` | 410 | grant revoked (refund) | RUL-58 |
| `download.link_expired` | 410 | signed link older than 10 min or not this customer's | RUL-41 |
| `enrolment.not_active` | 409 | launch on a revoked/expired enrolment | RUL-42 |
| `document.not_found` / `document.not_ready` | 404 / 409 | PDF not issued / still rendering | RUL-59 |
| `card.not_found` | 404 | unknown dedication token | RUL-22 |
| `refund.window_passed` | 422 | outside the RUL-57 window | RUL-57 |
| `refund.progress_exceeded` | 422 | course progress ≥ 20 % | RUL-57 |
| `refund.no_enrolment` | 422 | course line without enrolment | RUL-57 |
| `refund.already_downloaded` | 422 | digital book downloaded | RUL-57 |
| `refund.in_transit` | 422 | print line shipped but not yet delivered (refundable before shipment or after delivery) | RUL-57 |
| `refund.units_allocated` | 422 | no units still awaiting allocation | RUL-57 |
| `refund.pool_converted` | 422 | contribution fully converted into units | RUL-57 |
| `refund.already_refunded` | 422 | nothing left to refund on the line | RUL-58 |
| `refund.amount_exceeds` | 422 | amount above the eligible maximum; `max` | RUL-57 |
| `refund.lines_not_refundable` | 422 | at least one line fails; `lines[]` with codes above | RUL-57 |
| `refund.gateway_failed` | 502 | gateway refused; refund row `failed` | RUL-56 |
| `allocation.no_units` | 409 | no awaiting units (or all locked by another officer) | RUL-48 |
| `allocation.rejected` | 422 | one or more beneficiaries fail; `errors[]` with the codes below | RUL-47 |
| `allocation.not_approved` / `allocation.group_mismatch` / `allocation.already_enrolled` / `allocation.zakat_mismatch` / `allocation.no_matching_unit` | — | per-beneficiary reasons inside `allocation.rejected` | RUL-47 a–d |
| `allocation.mobile_required` | 422 | course seat for a beneficiary with no mobile (own or guardian's); returned inside `allocation.rejected` | RUL-47(e) |
| `allocation.conflict` | 409 | concurrent allocation hit `ux_enrolment_live`; retry | RUL-47c |
| `unit.invalid_transition` | 409 | e.g. complete a unit that is not `allocated` | RUL-50 |
| `beneficiary.not_found` | 404 | — | — |
| `beneficiary.mobile_exists` | 409 | `ux_beneficiary_mobile` | RUL-51 |
| `beneficiary.import_invalid` | 422 | CSV rows invalid; `rows[]` | FR-S52 |
| `beneficiary.no_live_seat` | 409 | invite resend without an allocated course seat | RUL-52 |
| `cause_update.already_notified` | 409 | delete after donors were notified | RUL-54 |
| `product.slug_taken` / `category.slug_taken` | 409 | slug unique | — |
| `product.type_locked` | 409 | type change after the first order | RUL-01 |
| `product.publish_incomplete` | 422 | missing Arabic fields, detail row, cover or price; `missing[]` | RUL-02, RUL-03 |
| `offer.active_exists` | 409 | `ux_offer_one_active` | RUL-06 |
| `offer.dates_invalid` | 400 | `endsAt ≤ startsAt` | RUL-06 |
| `cause.general_fund_exists` | 409 | `ux_one_general_fund` | RUL-46 |
| `media.alt_required` | 422 | image without `altAr` | RUL-03 |
| `media.type_not_allowed` / `media.too_large` | 415 / 413 | upload checks | — |
| `zakat.balance_insufficient` | 422 | voucher amount above the product's zakat balance; `balance` | RUL-20 |
| `goal.adjustment_invalid` | 422 | correction with `amount = 0` and `units = 0`, or it would make a counter negative | RUL-46, FR-S58 |
| `stock.below_reserved` | 422 | adjustment would make `stock_on_hand < stock_reserved` | RUL-30 |
| `shipment.invalid_transition` | 409 | e.g. deliver before ship | RUL-40 |
| `setting.unknown_key` / `setting.invalid_value` | 404 / 422 | settings editor | FR-S76 |
| `template.render_failed` | 422 | Scriban syntax error; `line`, `column` | FR-S77 |
| `staff.email_taken` | 409 | `staff_user.email` unique | — |
| `http.error` | 4xx | any other HTTP-level error without a specific code (e.g. 405 method not allowed, 415); default of 09 §5.14 | — |
| `server.error` | 500 | unexpected; no internals | — |

### 1.7 Caching

| Endpoints | Headers |
|---|---|
| Catalogue content (`/catalog/home`, `/products`, `/products/{slug}`, `/categories`, `/config`, `/updates`) | `Cache-Control: public, max-age=60, stale-while-revalidate=300` + weak `ETag` (hash of the newest `updated_at` / `setting.updated_at` involved); `If-None-Match` → `304` |
| Live figures (`/catalog/live`, `/products/{slug}/feed`) | `Cache-Control: public, max-age=15` + `ETag` (hash of the `goal_progress.updated_at` / stock values) |
| Cards (`/cards/{token}`) | `Cache-Control: public, max-age=300`, `X-Robots-Tag: noindex` |
| Everything with a cookie or bearer token, admin, checkout, status | `Cache-Control: no-store` |

Prerendered pages embed catalogue content at build time and call `/catalog/live` after hydration for
price, stock and goal figures (FR-S07).

### 1.8 Rate limits

Limits per 09 §5.15. A `429` carries `Retry-After` (seconds) and, for OTP, `retryAfterSeconds` in
the body.

---

## 2. Catalog (public)

All catalog endpoints are anonymous, cacheable (§1.7) and return only `published` products
(RUL-02). Rate policy `public`.

### 2.1 `GET /v1/catalog/home`

Store home data (FR-S01): featured products, donation opportunities, courses, sponsorable courses,
books, impact figures and the general-fund band, in the prototype's order.

```json
{
  "featured":    [ { "slug": "water-for-schools", "…": "ProductCard" }, { "slug": "digital-teacher", "…": "ProductCard" } ],
  "causes":      [ { "slug": "water-for-schools" }, { "slug": "tablet-for-student" }, { "slug": "learning-data" }, { "slug": "school-bag" } ],
  "courses":     [ { "slug": "digital-teacher" }, { "slug": "smart-study" }, { "slug": "python-kids" }, { "slug": "early-childhood" }, { "slug": "qudrat-prep" }, { "slug": "family-remote" } ],
  "sponsorable": [ { "slug": "digital-teacher" }, { "slug": "smart-study" }, { "slug": "python-kids" } ],
  "books":       [ { "slug": "science-stars-book" }, { "slug": "formative-guide" }, { "slug": "letters-tales" }, { "slug": "study-planner" } ],
  "impact": [
    { "value": 1840, "labelAr": "مقعداً مكفولاً", "labelEn": "seats sponsored" },
    { "value": 6210, "labelAr": "نسخة وُزّعت", "labelEn": "books delivered" },
    { "value": 3902, "labelAr": "متبرعاً وكافلاً", "labelEn": "donors & sponsors" },
    { "value": 97,   "labelAr": "٪ من التبرعات تصل مباشرة للبرامج", "labelEn": "% of gifts reach programs directly" }
  ],
  "generalFund": { "slug": "education-fund", "raised": "143900.00", "target": "500000.00", "percent": 28, "donorCount": 1120 }
}
```

(Arrays abbreviated; each element is a `ProductCard`, §2.3.) Impact figures: the source of the
prototype's static numbers is not defined (OP-2). Serves FR-S01, RUL-08.

### 2.2 `GET /v1/catalog/categories`

```json
[
  { "slug": "tech",   "nameAr": "التقنية في التعليم", "nameEn": "EdTech",          "sort": 0 },
  { "slug": "teach",  "nameAr": "مهارات المعلم",      "nameEn": "Teacher skills",  "sort": 0 },
  { "slug": "study",  "nameAr": "مهارات الدراسة",     "nameEn": "Study skills",    "sort": 0 },
  { "slug": "kids",   "nameAr": "الطفولة المبكرة",    "nameEn": "Early childhood", "sort": 0 },
  { "slug": "family", "nameAr": "الأسرة",             "nameEn": "Family",          "sort": 0 }
]
```

Only `is_active` categories. Serves FR-S02.

### 2.3 `GET /v1/catalog/products`

Query: `type` (`course|book|cause`), `category` (slug), `sponsorable` (`true` → products with an
open offer; `/store/sponsor`), `q` (search, FR-S03), `sort` (`featured` = `product.sort`, `newest` =
`published_at desc`, `price_asc`, `price_desc`), `fields` (`slug` → slim rows for prerender), `page`,
`pageSize`.

Search: the server normalises `q` with the database function `ar_normalize()` (the same function that
builds `product.search_vector` and `ix_product_trgm`), so ة/ه, ى/ي and أ/إ/آ/ا variants and
diacritics match (FR-S03). Clients send `q` as typed.

`fields=slug` (used by the main site's existing `scripts/routes.mjs` → `routes.txt`, change r1-16):

```json
{ "items": [ { "type": "course", "slug": "digital-teacher", "updatedAt": "2026-09-20T08:00:00Z" },
             { "type": "cause",  "slug": "education-fund",  "updatedAt": "2026-09-18T12:30:00Z" } ],
  "total": 15, "page": 1, "pageSize": 100 }
```

If the store API is unreachable at build time, the ta3lom-web build fails; a stale or empty product
list is never shipped.

Response `PagedResponse<ProductCard>`:

```json
{
  "items": [
    {
      "id": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e01",
      "type": "course",
      "slug": "digital-teacher",
      "titleAr": "المعلّم الرقمي: أدوات التعليم الإلكتروني",
      "titleEn": "The Digital Teacher: e-learning tools",
      "summaryAr": "دورة عملية تمكّن المعلم من تصميم درس تفاعلي كامل باستخدام أدوات مجانية، مع تقييم تكويني ومتابعة للطلاب.",
      "summaryEn": "A hands-on course that takes a teacher from zero to a complete interactive lesson using free tools.",
      "badge": "best",
      "category": { "slug": "teach", "nameAr": "مهارات المعلم", "nameEn": "Teacher skills" },
      "cover": { "url": "https://store-api.ta3lom.sa/files/public/covers/digital-teacher.webp",
                 "altAr": "معلمة تشرح درساً تفاعلياً على شاشة", "altEn": "A teacher presenting an interactive lesson",
                 "width": 1200, "height": 800 },
      "price": "350.00",
      "compareAtPrice": "450.00",
      "sponsorship": {
        "offerId": "01999c1e-6b10-7a22-9d3e-2b3c4d5e6f01",
        "seatPrice": "350.00", "unitLabelAr": "مقعد", "unitLabelEn": "seat",
        "targetUnits": 300, "fundedUnits": 184, "percent": 61, "donorCount": 96,
        "endsAt": "2026-12-31T20:59:59Z", "isOpen": true
      },
      "goal": null
    },
    {
      "id": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e0b",
      "type": "cause",
      "slug": "water-for-schools",
      "titleAr": "سقيا الطلاب — برادات مياه للمدارس",
      "titleEn": "Water for pupils — school water coolers",
      "summaryAr": "برّاد مياه مُفلتر لمدرسة في منطقة نائية يخدم قرابة 300 طالب لسنوات.",
      "summaryEn": "A filtered water cooler for a remote school, serving around 300 pupils for years.",
      "badge": "urgent",
      "category": null,
      "cover": { "url": "https://store-api.ta3lom.sa/files/public/covers/water-for-schools.webp",
                 "altAr": "طلاب يشربون من برّاد مياه في ساحة مدرسة", "altEn": "Pupils drinking from a water cooler in a schoolyard",
                 "width": 1200, "height": 800 },
      "price": null,
      "compareAtPrice": null,
      "sponsorship": null,
      "goal": {
        "unitAmount": "1500.00", "unitLabelAr": "برّاد", "unitLabelEn": "cooler",
        "target": "90000.00", "raised": "61250.00", "percent": 68, "donorCount": 318,
        "endsAt": "2026-12-31T20:59:59Z", "isOpen": true, "zakatEligible": false, "isGeneralFund": false
      }
    }
  ],
  "total": 15, "page": 1, "pageSize": 20
}
```

For books, `price` is the lowest active format price and `formats` is added
(`[{ "kind": "print", "price": "65.00" }, { "kind": "digital", "price": "35.00" }]`).
`percent` = `floor(raised × 100 ÷ target)`, may exceed 100; the client caps the bar (RUL-08).
Figures include `seed_*` (`v_goal`). Errors: `validation.failed` (unknown `type`/`sort`).
Serves FR-S02, FR-S03, RUL-04, RUL-08.

### 2.4 `GET /v1/catalog/products/{slug}`

Full product page (FR-S04/S05/S06). Common fields as `ProductCard` plus type-specific blocks.

Course (`digital-teacher`):

```json
{
  "id": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e01",
  "type": "course",
  "slug": "digital-teacher",
  "titleAr": "المعلّم الرقمي: أدوات التعليم الإلكتروني",
  "titleEn": "The Digital Teacher: e-learning tools",
  "summaryAr": "دورة عملية تمكّن المعلم من تصميم درس تفاعلي كامل…",
  "summaryEn": "A hands-on course that takes a teacher from zero…",
  "bodyAr": null, "bodyEn": null,
  "badge": "best",
  "category": { "slug": "teach", "nameAr": "مهارات المعلم", "nameEn": "Teacher skills" },
  "cover": { "url": "https://store-api.ta3lom.sa/files/public/covers/digital-teacher.webp", "altAr": "…", "altEn": "…", "width": 1200, "height": 800 },
  "seo": { "titleAr": null, "titleEn": null, "descAr": null, "descEn": null },
  "course": {
    "price": "350.00", "compareAtPrice": "450.00",
    "lessons": 24, "durationMinutes": 1080,
    "levelAr": "مبتدئ – متوسط", "levelEn": "Beginner – intermediate",
    "instructorAr": "أ. ريم السبيعي", "instructorEn": "Reem Al-Subaie",
    "accessDays": null, "learnerCount": 1240,
    "modules": [
      { "sort": 1, "titleAr": "أساسيات الفصل الافتراضي", "titleEn": "Virtual classroom basics", "durationSeconds": 2520 },
      { "sort": 2, "titleAr": "تصميم درس تفاعلي في 10 دقائق", "titleEn": "An interactive lesson in 10 minutes", "durationSeconds": 3480 },
      { "sort": 3, "titleAr": "التقييم التكويني الرقمي", "titleEn": "Digital formative assessment", "durationSeconds": 4200 },
      { "sort": 4, "titleAr": "إدارة الصف عن بُعد", "titleEn": "Remote classroom management", "durationSeconds": 2940 },
      { "sort": 5, "titleAr": "مشروع التخرج وشهادة الإتمام", "titleEn": "Capstone and certificate", "durationSeconds": 2100 }
    ]
  },
  "sponsorship": {
    "offerId": "01999c1e-6b10-7a22-9d3e-2b3c4d5e6f01",
    "seatPrice": "350.00", "unitLabelAr": "مقعد", "unitLabelEn": "seat",
    "targetUnits": 300, "fundedUnits": 184, "percent": 61, "donorCount": 96,
    "groupLabelAr": "معلمات المدارس الأهلية الناشئة في القرى والهجر",
    "groupLabelEn": "Teachers at small rural private schools",
    "allowPartial": true, "zakatEligible": false,
    "startsAt": "2026-09-01T00:00:00Z", "endsAt": "2026-12-31T20:59:59Z", "isOpen": true
  },
  "related": [ { "slug": "early-childhood", "type": "course", "titleAr": "أساسيات التعليم في الطفولة المبكرة", "titleEn": "Early-childhood teaching essentials", "price": "280.00" } ]
}
```

Book (`science-stars-book`) replaces `course` with:

```json
"book": {
  "authorAr": "فريق جمعية التعلم الإلكتروني", "authorEn": "E-Learning Association team",
  "pages": 132, "isbn": null, "publishedYear": 2026,
  "formats": [
    { "id": "01999c1e-7c20-7b31-8e4f-3c4d5e6f7a01", "kind": "print",   "sku": "BK-SCIENCE-STAR-P", "price": "65.00", "compareAtPrice": "80.00", "available": 500, "maxQuantity": 20 },
    { "id": "01999c1e-7c20-7b31-8e4f-3c4d5e6f7a02", "kind": "digital", "sku": "BK-SCIENCE-STAR-D", "price": "35.00", "compareAtPrice": null, "available": null, "maxQuantity": 1 }
  ]
}
```

Cause (`tablet-for-student`) replaces it with:

```json
"cause": {
  "unitAmount": "950.00", "unitLabelAr": "جهاز", "unitLabelEn": "tablet",
  "target": "190000.00", "raised": "72400.00", "percent": 38, "donorCount": 402,
  "allowCustomAmount": true, "minAmount": "10.00", "zakatEligible": true,
  "beneficiariesAr": "200 طالب", "beneficiariesEn": "200 students",
  "closeOnTarget": false, "isGeneralFund": false,
  "startsAt": "2026-09-01T00:00:00Z", "endsAt": "2027-02-28T20:59:59Z", "daysLeft": 152, "isOpen": true
}
```

`available` = `stock_on_hand − stock_reserved` (print only). `daysLeft` is computed in Asia/Riyadh.
Errors: `catalog.product_not_found`. Serves FR-S04–S06, FR-S09 (SEO fields), RUL-04–RUL-10.

### 2.5 `GET /v1/catalog/live?slugs=a,b,c`

Live figures for prerendered pages and listings (FR-S07). Up to 50 slugs.

```json
{
  "items": [
    { "slug": "digital-teacher", "price": "350.00", "compareAtPrice": "450.00",
      "sponsorship": { "fundedUnits": 184, "targetUnits": 300, "percent": 61, "donorCount": 96, "isOpen": true } },
    { "slug": "science-stars-book",
      "formats": [ { "kind": "print", "price": "65.00", "available": 500 }, { "kind": "digital", "price": "35.00", "available": null } ],
      "sponsorship": { "fundedUnits": 612, "targetUnits": 1000, "percent": 61, "donorCount": 188, "isOpen": true } },
    { "slug": "learning-data",
      "goal": { "raised": "51800.00", "target": "60000.00", "percent": 86, "donorCount": 733, "daysLeft": 47, "isOpen": true } }
  ],
  "asOf": "2026-09-29T10:15:00Z"
}
```

Unknown or unpublished slugs are omitted. Serves FR-S07, RUL-08.

### 2.6 `GET /v1/catalog/products/{slug}/updates`

Public delivery updates of a cause, newest first (`is_public = true`). Paged.

```json
{
  "items": [
    { "id": "01999d02-1a2b-7c3d-8e4f-5a6b7c8d9e01",
      "titleAr": "تم تركيب البرّاد في مدرسة الهجرة الابتدائية — القصيم",
      "titleEn": "Cooler installed at Al-Hijra Primary — Qassim",
      "bodyAr": null, "bodyEn": null,
      "image": { "url": "https://store-api.ta3lom.sa/files/public/updates/hijra-cooler.webp", "altAr": "برّاد مياه مركّب في ممر المدرسة", "altEn": "Installed water cooler in a school corridor" },
      "unitsDelivered": 1, "occurredOn": "2026-09-02" }
  ],
  "total": 1, "page": 1, "pageSize": 20
}
```

Serves FR-S06, FR-S59, RUL-54.

### 2.7 `GET /v1/catalog/products/{slug}/feed`

Latest gifts (FR-S06), last 10 paid gift lines for the product.

```json
[
  { "displayAr": "فاعل خير", "displayEn": "Anonymous", "amount": "500.00", "units": null, "at": "2026-09-29T09:58:00Z" },
  { "displayAr": "عن والدتي — رحمها الله", "displayEn": "For my late mother", "amount": "1500.00", "units": 1, "at": "2026-09-29T09:10:00Z" },
  { "displayAr": "نورة ع.", "displayEn": "Noura A.", "amount": "100.00", "units": null, "at": "2026-09-28T17:40:00Z" }
]
```

For sponsorship products `amount` is omitted and `units` given (the prototype shows seats, not
money). Orders with `is_anonymous` show "فاعل خير" (RUL-23). The name format for other donors is
09 OP-18. Serves FR-S06, RUL-23.

### 2.8 `GET /v1/catalog/slugs` — withdrawn (r1)

Replaced by `GET /v1/catalog/products?fields=slug` (§2.3), read by the main site's existing
`scripts/routes.mjs`; there is no second prerender mechanism. Index number 8 is kept and not reused.

### 2.9 `GET /v1/catalog/config`

Public settings the buy box and checkout need.

```json
{
  "vatEnabled": false, "vatRate": "15.00",
  "shippingFlatFee": "25.00", "minGiftAmount": "10.00", "cartMaxTotal": "100000.00",
  "cartMaxLines": 50, "printMaxQuantity": 20, "giftMaxQuantity": 999,
  "paymentMethods": [ "mada", "applepay", "card", "stcpay" ],
  "dedicationFreeTextEnabled": false,
  "dedicationTemplates": [
    { "key": "hasanat",  "ar": "اللهم اجعله في ميزان حسناتك", "en": "May it weigh in your good deeds" },
    { "key": "departed", "ar": "صدقة جارية عن روح الفقيد",   "en": "A lasting charity for the departed" },
    { "key": "success",  "ar": "هدية بمناسبة نجاحك",          "en": "A gift for your success" },
    { "key": "eid",      "ar": "عيدكم مبارك",                 "en": "Eid Mubarak" }
  ],
  "sponsorPresets": { "course": [1, 5, 10, 25], "book": [10, 25, 50, 100] },
  "causeUnitPresets": [1, 2, 5, 10],
  "amountPresets": [ "50.00", "100.00", "250.00", "500.00" ]
}
```

Presets come from FR-S12/FR-S13 (constants in S1). Serves FR-S12–S14, RUL-12, RUL-21, RUL-26, RUL-27.

---

## 3. Cart

Server-side cart for guests (cookie `t3s_cart`) and customers (bearer token) (RUL-11, FR-S17). The
customer cart wins when both are present; the guest cart is merged at sign-in (§4.2). Rate policy
`cart`. All responses `Cache-Control: no-store`. A presented customer token must be valid (`401` otherwise, so the client
refreshes instead of silently falling back to a guest cart).

### 3.1 Cart DTO

Returned by every cart endpoint except the summary. Example: one course bought, one print book ×2,
five sponsored seats, one dedicated cooler (settings as seeded: VAT disabled).

```json
{
  "id": "01999e10-2b3c-7d4e-8f5a-6b7c8d9e0f01",
  "isGuest": true,
  "purchases": [
    { "id": "01999e10-3c4d-7e5f-9a6b-7c8d9e0f1a01", "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e01",
      "slug": "digital-teacher", "type": "course", "kind": "buy", "mode": "units",
      "titleAr": "المعلّم الرقمي: أدوات التعليم الإلكتروني", "titleEn": "The Digital Teacher: e-learning tools",
      "bookFormat": null, "quantity": 1, "maxQuantity": 1, "unitPrice": "350.00", "amount": "350.00",
      "intent": null, "dedication": null, "issues": [] },
    { "id": "01999e10-3c4d-7e5f-9a6b-7c8d9e0f1a02", "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e07",
      "slug": "science-stars-book", "type": "book", "kind": "buy", "mode": "units",
      "titleAr": "نجوم العلوم — كتاب التجارب المنزلية", "titleEn": "Science Stars — home experiments book",
      "bookFormat": { "id": "01999c1e-7c20-7b31-8e4f-3c4d5e6f7a01", "kind": "print" },
      "quantity": 2, "maxQuantity": 20, "unitPrice": "65.00", "amount": "130.00",
      "intent": null, "dedication": null, "issues": [] }
  ],
  "gifts": [
    { "id": "01999e10-3c4d-7e5f-9a6b-7c8d9e0f1a03", "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e02",
      "slug": "smart-study", "type": "course", "kind": "sponsor", "mode": "units",
      "titleAr": "مهارات المذاكرة الذكية", "titleEn": "Smart study skills",
      "bookFormat": null, "quantity": 5, "maxQuantity": 999, "unitPrice": "120.00", "amount": "600.00",
      "unitLabelAr": "مقعد", "unitLabelEn": "seat",
      "intent": "sadaqa", "dedication": null, "issues": [] },
    { "id": "01999e10-3c4d-7e5f-9a6b-7c8d9e0f1a04", "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e0b",
      "slug": "water-for-schools", "type": "cause", "kind": "donate", "mode": "units",
      "titleAr": "سقيا الطلاب — برادات مياه للمدارس", "titleEn": "Water for pupils — school water coolers",
      "bookFormat": null, "quantity": 1, "maxQuantity": 999, "unitPrice": "1500.00", "amount": "1500.00",
      "unitLabelAr": "برّاد", "unitLabelEn": "cooler",
      "intent": "sadaqa",
      "dedication": { "toName": "أم خالد", "toMobile": "05•••••543", "fromName": "نورة",
                      "templateKey": "hasanat", "text": null },
      "issues": [] }
  ],
  "summary": {
    "purchasesTotal": "480.00", "vatIncluded": "0.00", "shippingTotal": "25.00",
    "giftsTotal": "2100.00", "grandTotal": "2605.00",
    "lineCount": 4, "requiresShipping": true, "hasGifts": true
  },
  "notices": [],
  "expiresAt": "2026-10-29T10:15:00Z"
}
```

- `unitPrice` is indicative (RUL-15); the checkout quote is authoritative.
- `issues` lists codes that would fail checkout now (e.g. `cart.campaign_ended` after the end date,
  RUL-09) so the cart page can flag the line before checkout.
- `shippingTotal` = `shipping_flat_fee` when a print line exists, else `"0.00"` (RUL-26).
- `vatIncluded` is informational (RUL-27); `"0.00"` while `vat_enabled` is false.

### 3.2 `GET /v1/cart`

Current cart, or an empty cart DTO (`id: null`) without creating a row. Auth: cookie or customer.
Serves FR-S17, FR-S18.

### 3.3 `GET /v1/cart/summary`

Header badge on every ta3lom.sa page (FR-S19, database/01 §8). Cheap; no lines.

```json
{ "lineCount": 4, "grandTotal": "2605.00" }
```

### 3.4 `POST /v1/cart/lines`

Adds a line; creates the cart and sets `t3s_cart` on first use (guest). Returns `201` with the cart.

Request `AddCartLineRequest`:

| Field | Type | Notes |
|---|---|---|
| `productId` | uuid | required |
| `bookFormatId` | uuid? | required for `buy` on a book (RUL-05) |
| `kind` | `buy` \| `sponsor` \| `donate` | RUL-12 |
| `mode` | `units` \| `amount` | RUL-12 |
| `quantity` | int 1–999 | `units` mode; `1` for `amount` |
| `amount` | money string? | `amount` mode only |
| `intent` | `sadaqa` \| `zakat`? | gifts only; default `sadaqa` (RUL-19) |
| `dedication` | object? | gifts only (RUL-21): `toName` 2–60, `toMobile` `05XXXXXXXX`, `fromName` optional, `templateKey`, `text` ≤ 140 (only if enabled) |

Sponsor by amount (FR-S12 acceptance: 100 SAR on a 350 SAR seat):

```json
{ "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e01", "kind": "sponsor", "mode": "amount",
  "quantity": 1, "amount": "100.00", "intent": "sadaqa" }
```

Resulting line: `"mode": "amount", "amount": "100.00", "seatEquivalent": "0.28"` (display only,
`floor(amount ÷ seat_price, 2)`; 100 ÷ 350 = 0.2857 → 0.28).

Dedicated donation:

```json
{ "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e0b", "kind": "donate", "mode": "units", "quantity": 1,
  "intent": "sadaqa",
  "dedication": { "toName": "أم خالد", "toMobile": "0559876543", "fromName": "نورة", "templateKey": "hasanat", "text": null } }
```

Errors: `validation.failed`, `cart.product_unavailable`, `cart.mode_not_allowed`,
`cart.quantity_limit`, `cart.amount_below_minimum`, `cart.campaign_ended`, `cart.duplicate_course`,
`cart.course_owned`, `cart.course_in_unpaid_order`, `cart.zakat_not_allowed`, `cart.dedication_not_allowed`,
`cart.dedication_invalid`, `cart.dedication_free_text_disabled`, `cart.out_of_stock`,
`cart.too_many_lines`, `cart.total_exceeded`. Serves FR-S10–S16, FR-S20; RUL-12, RUL-13 (owned, in cart, or in an unpaid order), RUL-14
(merge returns the existing line with the new quantity), RUL-18, RUL-19, RUL-21.

### 3.5 `PATCH /v1/cart/lines/{lineId}`

```json
{ "quantity": 3 }
```

`units` lines only. Returns the cart. Errors: `cart.line_not_found`, `cart.mode_not_allowed`
(amount line), `cart.quantity_limit`, `cart.out_of_stock`, `cart.total_exceeded`. Serves FR-S18, RUL-12, RUL-18.

### 3.6 `DELETE /v1/cart/lines/{lineId}`

Returns the cart (`200`). Errors: `cart.line_not_found`. Serves FR-S18.

### 3.7 `DELETE /v1/cart`

Empties the cart; `204`. Serves FR-S18.

---

## 4. Auth (customers)

Mobile + SMS OTP (ADR-S04, RUL-60, FR-S21). Rate policies `otp-request`, `otp-verify`, `auth-refresh`.

### 4.1 `POST /v1/auth/otp/request`

```json
{ "mobile": "0551234567", "locale": "ar" }
```

`202`:

```json
{ "expiresInSeconds": 300 }
```

The response is the same whether or not the mobile has an account. Errors: `otp.mobile_invalid`,
`otp.rate_limited` (`retryAfterSeconds`). Serves FR-S21, RUL-60.

### 4.2 `POST /v1/auth/otp/verify`

```json
{ "mobile": "0551234567", "code": "482913" }
```

`200` + `Set-Cookie: t3s_rt=…; Path=/v1/auth; HttpOnly; Secure; SameSite=Lax; Max-Age=5184000`
(and `t3s_cart` deleted after merge):

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsImtpZCI6ImMxIiwidHlwIjoiSldUIn0.eyJzdWIiOiIwMTk5OWYwMC0…",
  "expiresIn": 900,
  "customer": {
    "id": "01999f00-1a2b-7c3d-8e4f-5a6b7c8d9e01",
    "mobile": "+966551234567", "fullName": "نورة العتيبي", "email": "noura@example.com",
    "locale": "ar", "anonymousDefault": false, "isNew": false, "profileComplete": true
  },
  "cartNotices": [
    { "code": "cart.merge_dropped_owned_course", "slug": "smart-study" },
    { "code": "cart.merge_combined", "slug": "science-stars-book" }
  ]
}
```

`cartNotices` (RUL-17): `cart.merge_dropped_owned_course` (a `buy` course the customer owns),
`cart.merge_combined` (identical line combined, capped at the line maximum), and — when the customer cart would
break RUL-18 — `cart.too_many_lines` / `cart.total_exceeded` for the guest line left out. Without a customer cart the
guest cart simply becomes theirs.

First login creates the `customer` (RUL-61). A beneficiary's `customer` row already exists from
allocation (RUL-47, RUL-52), so their first login simply signs them in. A `blocked` customer is
refused (RUL-61). Errors: `otp.invalid` (`attemptsLeft`), `otp.expired`, `otp.too_many_attempts`,
`auth.account_blocked`. Serves FR-S21, FR-S54; RUL-17, RUL-52, RUL-60.

### 4.3 `POST /v1/auth/refresh`

Cookie `t3s_rt` only; no body. `200` `{ "accessToken": "…", "expiresIn": 900 }` + rotated cookie.
Errors: `auth.refresh_invalid`, `auth.refresh_expired`, `auth.refresh_reused` (family revoked, cookie
cleared), `auth.refresh_race`. Serves RUL-60.

### 4.4 `POST /v1/auth/logout`

Revokes the current refresh token and clears the cookie. `204`. Serves FR-S28.

### 4.5 `POST /v1/auth/logout-all`

Bearer required. Revokes every refresh-token family of the customer ("sign out everywhere"). `204`.
Serves FR-S28.

---

## 5. Me (customer account)

Bearer (`customer`), `Cache-Control: no-store`, rate policy `me`. A customer only ever sees their
own rows; another customer's id returns `404`.

### 5.1 `GET /v1/me` · `PATCH /v1/me`

```json
{
  "id": "01999f00-1a2b-7c3d-8e4f-5a6b7c8d9e01",
  "mobile": "+966551234567",
  "fullName": "نورة العتيبي",
  "email": "noura@example.com",
  "locale": "ar",
  "anonymousDefault": false,
  "marketingOptIn": false,
  "pdplConsentAt": "2026-09-12T18:04:00Z",
  "createdAt": "2026-09-12T18:03:41Z"
}
```

`PATCH` accepts `fullName` (2–100), `email`, `locale` (`ar|en`), `anonymousDefault`,
`marketingOptIn`, `pdplConsent` (`true` sets `pdpl_consent_at`). The mobile cannot be changed in S1
(it is the login, RUL-61). Errors: `validation.failed`. Serves FR-S22, RUL-61.

### 5.2 Addresses

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/v1/me/addresses` | — | `Address[]` |
| POST | `/v1/me/addresses` | `AddressInput` | `201 Address` |
| PUT | `/v1/me/addresses/{id}` | `AddressInput` | `Address` |
| DELETE | `/v1/me/addresses/{id}` | — | `204` |
| POST | `/v1/me/addresses/{id}/default` | — | `Address` (others lose `isDefault`, `ux_address_default`) |

```json
{
  "id": "01999f00-2b3c-7d4e-8f5a-6b7c8d9e0f02",
  "recipientName": "نورة العتيبي",
  "recipientMobile": "0551234567",
  "city": "بريدة",
  "district": "الريان",
  "shortAddress": "QBRA2929",
  "street": "طريق الملك عبدالعزيز",
  "buildingNo": "8123",
  "postalCode": "52361",
  "isDefault": true
}
```

Validation mirrors the DDL: `shortAddress` `^[A-Z]{4}[0-9]{4}$`, `postalCode` five digits, Saudi
mobile. KSA only (RUL-26). Errors: `validation.failed`, `http.not_found`. Serves FR-S22, RUL-26, RUL-61.

### 5.3 `GET /v1/me/enrolments`

```json
[
  { "id": "0199a012-4d5e-7f6a-8b7c-9d0e1f2a3b01",
    "product": { "slug": "digital-teacher", "titleAr": "المعلّم الرقمي: أدوات التعليم الإلكتروني", "titleEn": "The Digital Teacher: e-learning tools",
                 "coverUrl": "https://store-api.ta3lom.sa/files/public/covers/digital-teacher.webp" },
    "source": "purchase", "status": "active", "progressPct": 35,
    "grantedAt": "2026-09-14T11:02:00Z", "expiresAt": null, "startedAt": "2026-09-14T19:30:00Z",
    "completedAt": null, "certificateNo": null, "canLaunch": true }
]
```

`status` `pending_sync` shows "access being prepared" (RUL-42). Serves FR-S23, RUL-42.

### 5.4 `POST /v1/me/enrolments/{id}/launch`

Returns a short-lived start/resume link from `ILmsGateway` (Q-05).

```json
{ "url": "https://ta3lom.sa/learn/digital-teacher?t=…", "expiresAt": "2026-09-29T10:20:00Z" }
```

Errors: `enrolment.not_active`. Serves FR-S23.

### 5.5 `GET /v1/me/enrolments/{id}/certificate`

`302` to the certificate for a `completed` enrolment. Where certificates are produced (LMS or store)
is open (OP-3). Errors: `http.not_found`, `enrolment.not_active`. Serves FR-S23.

### 5.6 `GET /v1/me/books`

```json
{
  "downloads": [
    { "grantId": "0199a013-5e6f-7a8b-9c0d-1e2f3a4b5c01", "slug": "formative-guide",
      "titleAr": "دليل المعلم للتقويم التكويني", "titleEn": "The teacher's guide to formative assessment",
      "orderRef": "T3-26-004187", "downloadCount": 1, "maxDownloads": 5, "revoked": false }
  ],
  "shipments": [
    { "orderRef": "T3-26-004187", "status": "shipped", "carrier": "SMSA", "trackingNo": "SA290017734",
      "shippedAt": "2026-09-16T08:10:00Z", "deliveredAt": null,
      "lines": [ { "titleAr": "نجوم العلوم — كتاب التجارب المنزلية", "titleEn": "Science Stars — home experiments book", "quantity": 2 } ] }
  ]
}
```

Serves FR-S24, RUL-40, RUL-41.

### 5.7 `POST /v1/me/downloads/{grantId}/link`

Issues a signed link valid for 10 minutes, bound to this customer (RUL-41). Rate policy `download`.

```json
{ "url": "https://store-api.ta3lom.sa/v1/downloads/CfDJ8Kx…", "expiresAt": "2026-09-29T10:25:00Z", "remaining": 4 }
```

Errors: `download.limit_reached`, `download.revoked`. Serves FR-S24, FR-S41, RUL-41.

### 5.8 `GET /v1/me/giving`

Totals and gift lines (FR-S25). Paged over lines, newest first.

```json
{
  "totals": { "given": "2200.00", "zakat": "0.00", "unitsSponsored": 5, "pooledContributions": 1, "dedications": 1 },
  "items": [
    { "orderLineId": "0199a014-6f7a-7b8c-9d0e-1f2a3b4c5d01", "orderRef": "T3-26-004187", "paidAt": "2026-09-14T11:02:00Z",
      "kind": "sponsor", "mode": "units", "slug": "smart-study",
      "titleAr": "مهارات المذاكرة الذكية", "titleEn": "Smart study skills",
      "quantity": 5, "amount": "600.00", "intent": "sadaqa",
      "units": { "awaitingAllocation": 3, "allocated": 2, "completed": 0 },
      "groupLabelAr": "طلاب الأسر المستفيدة من الضمان الاجتماعي", "groupLabelEn": "Students from social-security families",
      "dedication": null },
    { "orderLineId": "0199a014-6f7a-7b8c-9d0e-1f2a3b4c5d02", "orderRef": "T3-26-003951", "paidAt": "2026-09-03T16:40:00Z",
      "kind": "sponsor", "mode": "amount", "slug": "digital-teacher",
      "titleAr": "المعلّم الرقمي: أدوات التعليم الإلكتروني", "titleEn": "The Digital Teacher: e-learning tools",
      "quantity": 1, "amount": "100.00", "intent": "sadaqa",
      "pooled": { "converted": "100.00", "remaining": "0.00", "seatPrice": "350.00", "shareOfSeat": "0.29", "sweptToFundAt": null },
      "dedication": null },
    { "orderLineId": "0199a014-6f7a-7b8c-9d0e-1f2a3b4c5d03", "orderRef": "T3-26-004187", "paidAt": "2026-09-14T11:02:00Z",
      "kind": "donate", "mode": "units", "slug": "water-for-schools",
      "titleAr": "سقيا الطلاب — برادات مياه للمدارس", "titleEn": "Water for pupils — school water coolers",
      "quantity": 1, "amount": "1500.00", "intent": "sadaqa",
      "latestUpdate": { "titleAr": "تم تركيب البرّاد في مدرسة الهجرة الابتدائية — القصيم", "occurredOn": "2026-09-02" },
      "dedication": { "toName": "أم خالد", "cardUrl": "https://ta3lom.sa/ar/store/card/q3Vx9LmP2aT7cN4bY8kR0w", "smsStatus": "sent" } }
  ],
  "total": 3, "page": 1, "pageSize": 20
}
```

Never includes beneficiary identities (RUL-50). Serves FR-S25, BR-S08, RUL-45, RUL-46, RUL-50, RUL-54.

### 5.9 `GET /v1/me/giving/lines/{orderLineId}`

Detail for one gift line: the seat map for units, the pooled share, or the cause timeline.

```json
{
  "orderLineId": "0199a014-6f7a-7b8c-9d0e-1f2a3b4c5d01",
  "kind": "sponsor",
  "units": [
    { "ref": "U-1", "status": "allocated", "region": "القصيم", "groupLabelAr": "طلاب الأسر المستفيدة من الضمان الاجتماعي",
      "fundedAt": "2026-09-14T11:02:00Z", "allocatedAt": "2026-09-20T09:00:00Z", "completedAt": null,
      "timeline": [ { "status": "awaiting_allocation", "at": "2026-09-14T11:02:00Z" }, { "status": "allocated", "at": "2026-09-20T09:00:00Z" } ] },
    { "ref": "U-2", "status": "awaiting_allocation", "region": null, "fundedAt": "2026-09-14T11:02:00Z",
      "timeline": [ { "status": "awaiting_allocation", "at": "2026-09-14T11:02:00Z" } ] }
  ],
  "updates": []
}
```

`ref` is a per-line ordinal, not the unit id. `lapsed` units are shown as "being re-allocated"
(RUL-50). For `donate` lines, `updates` lists the cause's updates (public and donor-only). Serves
FR-S25, RUL-50, RUL-54.

### 5.10 Orders and documents

| Method | Path | Response |
|---|---|---|
| GET | `/v1/me/orders` | `PagedResponse<OrderSummary>` |
| GET | `/v1/me/orders/{ref}` | `OrderDetail` |
| GET | `/v1/me/documents/{number}` | `application/pdf` (invoice, credit note, receipt, cancellation) |

```json
{
  "reference": "T3-26-004187",
  "status": "paid",
  "placedAt": "2026-09-14T10:58:12Z",
  "paidAt": "2026-09-14T11:02:00Z",
  "purchasesTotal": "480.00", "shippingTotal": "25.00", "giftsTotal": "2100.00",
  "vatTotal": "0.00", "grandTotal": "2605.00", "refundedTotal": "0.00",
  "isAnonymous": false,
  "lines": [
    { "lineNo": 1, "kind": "buy", "mode": "units", "titleAr": "المعلّم الرقمي: أدوات التعليم الإلكتروني", "titleEn": "The Digital Teacher: e-learning tools",
      "quantity": 1, "unitPrice": "350.00", "amount": "350.00", "vatAmount": "0.00", "intent": null, "fulfilmentStatus": "enrolled", "refundedAmount": "0.00" },
    { "lineNo": 2, "kind": "buy", "mode": "units", "titleAr": "نجوم العلوم — كتاب التجارب المنزلية", "titleEn": "Science Stars — home experiments book",
      "format": "print", "quantity": 2, "unitPrice": "65.00", "amount": "130.00", "vatAmount": "0.00", "intent": null, "fulfilmentStatus": "shipped", "refundedAmount": "0.00" },
    { "lineNo": 3, "kind": "sponsor", "mode": "units", "titleAr": "مهارات المذاكرة الذكية", "titleEn": "Smart study skills",
      "quantity": 5, "unitPrice": "120.00", "amount": "600.00", "vatAmount": "0.00", "intent": "sadaqa", "fulfilmentStatus": "funded", "refundedAmount": "0.00" },
    { "lineNo": 4, "kind": "donate", "mode": "units", "titleAr": "سقيا الطلاب — برادات مياه للمدارس", "titleEn": "Water for pupils — school water coolers",
      "quantity": 1, "unitPrice": "1500.00", "amount": "1500.00", "vatAmount": "0.00", "intent": "sadaqa", "fulfilmentStatus": "received", "refundedAmount": "0.00",
      "dedication": { "toName": "أم خالد", "smsStatus": "sent" } }
  ],
  "payments": [ { "method": "mada", "status": "captured", "amount": "2605.00", "createdAt": "2026-09-14T10:58:15Z" } ],
  "documents": [
    { "kind": "invoice", "number": "INV-2026-000412", "issuedAt": "2026-09-14T11:02:00Z", "ready": true },
    { "kind": "receipt", "number": "DR-2026-000187", "issuedAt": "2026-09-14T11:02:00Z", "ready": true }
  ],
  "shipment": { "status": "shipped", "carrier": "SMSA", "trackingNo": "SA290017734" }
}
```

`OrderSummary` = `reference`, `placedAt`, `status`, `grandTotal`, `lineCount`, `hasInvoice`,
`hasReceipt`. Documents: `document.not_found`, `document.not_ready`. Serves FR-S26, RUL-29, RUL-59.

### 5.11 `GET /v1/me/export`

`application/json` attachment (`Content-Disposition: attachment; filename="ta3lom-store-export.json"`)
with profile, addresses, orders and lines, giving, enrolments, download grants and dedications sent.
Beneficiary data is never included. Serves FR-S27, RUL-61 (database/03 §7).

### 5.12 `DELETE /v1/me`

```json
{ "confirm": true }
```

`204`; anonymises the account as in database/03 §7 (name/email/mobile replaced, addresses deleted,
tokens revoked, `status = 'anonymised'`), clears the refresh cookie. Financial records are kept
(RUL-61). Serves FR-S27. Whether deletion needs a fresh OTP is OP-4.

---

## 6. Checkout and orders

### 6.1 `POST /v1/checkout/quote`

Re-validates and reprices the current cart without writing (RUL-15, RUL-24, FR-S31). Auth: cart
cookie or customer. Body optional: `{ "shippingAddressId": "…" }`.

```json
{
  "lines": [
    { "lineId": "01999e10-3c4d-7e5f-9a6b-7c8d9e0f1a01", "status": "ok", "unitPrice": "350.00", "amount": "350.00", "vatAmount": "0.00" },
    { "lineId": "01999e10-3c4d-7e5f-9a6b-7c8d9e0f1a02", "status": "changed", "previousUnitPrice": "65.00", "unitPrice": "60.00", "amount": "120.00", "vatAmount": "0.00" },
    { "lineId": "01999e10-3c4d-7e5f-9a6b-7c8d9e0f1a03", "status": "ok", "unitPrice": "120.00", "amount": "600.00", "vatAmount": "0.00" },
    { "lineId": "01999e10-3c4d-7e5f-9a6b-7c8d9e0f1a04", "status": "ok", "unitPrice": "1500.00", "amount": "1500.00", "vatAmount": "0.00" }
  ],
  "purchasesTotal": "470.00", "shippingTotal": "25.00", "giftsTotal": "2100.00",
  "vatTotal": "0.00", "grandTotal": "2595.00",
  "priceChanged": true, "requiresShipping": true, "hasGifts": true, "valid": true
}
```

A failing line has `"status": "invalid", "code": "cart.campaign_ended"` and `valid` is false.
Serves FR-S31, RUL-15, RUL-24, RUL-26, RUL-27, RUL-31.

### 6.2 `POST /v1/checkout`

Creates the order and the first payment session (database/01 §4.3). Auth: customer (OTP-verified,
RUL-25). Headers: `Idempotency-Key` (required). Rate policy `checkout`.

```json
{
  "contact": { "fullName": "نورة العتيبي", "email": "noura@example.com" },
  "isAnonymous": false,
  "shippingAddressId": "01999f00-2b3c-7d4e-8f5a-6b7c8d9e0f02",
  "shippingAddress": null,
  "saveAddress": false,
  "paymentMethod": "mada",
  "acceptTerms": true,
  "expectedGrandTotal": "2605.00",
  "locale": "ar"
}
```

`shippingAddress` (inline `AddressInput`) may replace `shippingAddressId`. `isAnonymous` is
accepted only when the cart has gifts (RUL-23). `acceptTerms` must be `true`; the server stores
`orders.terms_accepted_at` (NOT NULL) from it as E-Commerce Law evidence (FR-S30). If the repriced total differs from
`expectedGrandTotal`, nothing is created and `409 checkout.price_changed` returns the new quote
(§6.1 shape in `quote`).

`201` + `Location: /v1/orders/T3-26-004187/status`:

```json
{
  "orderId": "0199a010-1a2b-7c3d-8e4f-5a6b7c8d9e01",
  "reference": "T3-26-004187",
  "status": "pending_payment",
  "grandTotal": "2605.00",
  "paymentDueAt": "2026-09-14T11:58:12Z",
  "statusToken": "st_7Hq2LmX9vB4kPz1R",
  "payment": {
    "paymentId": "0199a010-2b3c-7d4e-8f5a-6b7c8d9e0f01",
    "provider": "moyasar",
    "method": "mada",
    "redirectUrl": "https://checkout.moyasar.com/invoices/5c6e1f9a-…",
    "expiresAt": "2026-09-14T11:58:12Z"
  }
}
```

The client redirects to `redirectUrl`; the gateway returns the browser to
`/{locale}/store/orders/T3-26-004187/thanks?t=st_7Hq2LmX9vB4kPz1R`. That return is never proof of
payment (RUL-33). If the gateway session fails after the order is created, the response is
`503 checkout.gateway_unavailable` with `reference` and `statusToken` in the body; the client offers
retry (§6.4).

Errors: `auth.unauthenticated`, `auth.account_blocked`, `idempotency.*`, `validation.failed`, `checkout.cart_empty`,
`cart.course_in_unpaid_order` (inside `lines[]`, RUL-13),
`checkout.lines_invalid` (`lines[]`), `checkout.price_changed`, `checkout.out_of_stock`,
`checkout.address_required`, `checkout.contact_required`, `checkout.terms_not_accepted`,
`checkout.payment_method_not_allowed`, `checkout.gateway_unavailable`. Serves FR-S30–S33;
RUL-15, RUL-23–RUL-28, RUL-30–RUL-32, RUL-36.

### 6.3 `GET /v1/orders/{ref}/status`

Thank-you page polling (FR-S35). Auth: customer bearer **or** `?t={statusToken}` (HMAC of the order
id, returned by `POST /v1/checkout`); never anonymous by reference alone (r1-15). The thanks page uses
the bearer, because the customer signed in at checkout; the token covers a lost in-memory session.
Poll every 2 s for up to 60 s, then every 10 s.

```json
{
  "reference": "T3-26-004187",
  "status": "paid",
  "grandTotal": "2605.00",
  "paidAt": "2026-09-14T11:02:00Z",
  "paymentDueAt": "2026-09-14T11:58:12Z",
  "payment": { "status": "captured", "method": "mada", "failureMessage": null },
  "canRetry": false,
  "summary": {
    "purchases": [ { "titleAr": "المعلّم الرقمي: أدوات التعليم الإلكتروني", "titleEn": "The Digital Teacher: e-learning tools", "next": "open_course" },
                   { "titleAr": "نجوم العلوم — كتاب التجارب المنزلية", "titleEn": "Science Stars — home experiments book", "next": "track_shipment" } ],
    "gifts": [ { "titleAr": "مهارات المذاكرة الذكية", "titleEn": "Smart study skills", "units": 5, "next": "view_giving" },
               { "titleAr": "سقيا الطلاب — برادات مياه للمدارس", "titleEn": "Water for pupils — school water coolers", "units": 1, "next": "view_giving" } ],
    "dedications": [ { "toName": "أم خالد", "cardUrl": "https://ta3lom.sa/ar/store/card/q3Vx9LmP2aT7cN4bY8kR0w" } ],
    "documents": [ { "kind": "invoice", "number": "INV-2026-000412", "ready": true },
                   { "kind": "receipt", "number": "DR-2026-000187", "ready": false } ]
  }
}
```

With the status token only, `summary` omits `dedications` and document numbers. `status` values
per RUL-29; `payment_failed` + `canRetry: true` shows the retry button. When the order `expired` or
failed for good, its lines are back in the active cart (`"cartRestored": true`; products already in
the cart are skipped, RUL-29). Errors: `order.not_found`.
Serves FR-S35, RUL-29, RUL-33, RUL-37.

### 6.4 `POST /v1/orders/{ref}/retry-payment`

Auth: customer. Optional `Idempotency-Key`. Creates a new `payment` row and gateway session on the
same order (RUL-37).

```json
{ "paymentMethod": "applepay" }
```

`201` with the `payment` object of §6.2. Errors: `order.not_found`, `order.retry_not_allowed`,
`order.payment_window_closed`, `checkout.payment_method_not_allowed`,
`checkout.gateway_unavailable`. Serves FR-S36, RUL-37.

---

## 7. Payment webhooks

### 7.1 `POST /v1/payments/webhooks/{provider}`

`{provider}` = `moyasar` (Q-01; others when adapters exist). Anonymous; authenticity by provider
signature or secret token (09 §5.12). Excluded from the public OpenAPI document. Rate policy
`webhook`.

Request: the provider's raw JSON (stored verbatim in `payment_event.payload`). Moyasar-style example
(fields to be confirmed at Q-01):

```json
{
  "id": "evt_9f1c2b7e-4d3a-4c1e-9b2f-5a6d7e8f9a01",
  "type": "payment_paid",
  "created_at": "2026-09-14T08:01:58.000Z",
  "secret_token": "********",
  "live": true,
  "data": {
    "id": "pay_7b2e4f1a-…", "status": "paid", "amount": 260500, "currency": "SAR",
    "source": { "type": "creditcard", "company": "mada" },
    "metadata": { "payment_id": "0199a010-2b3c-7d4e-8f5a-6b7c8d9e0f01", "order_ref": "T3-26-004187" }
  }
}
```

| Result | Response |
|---|---|
| Verified, processed (paid, failed, review, ignored) | `200 { "received": true }` |
| Verified duplicate (same `provider_event_id`) | `200 { "received": true, "duplicate": true }` (RUL-34) |
| Signature invalid | `401 webhook.signature_invalid`; stored with `signature_valid = false` |
| Unknown provider | `404 webhook.provider_unknown` |
| Transient failure (DB, gateway re-fetch) | `500`; the gateway retries |

The status and amount applied are those re-fetched from the gateway API, not those in the body. A
capture on an `expired` or `cancelled` order, or for a different amount, puts the order in
`payment_review` (RUL-35). Reconciliation (every 5 min, payments `initiated` > 5 min, RUL-38) applies
the same handler when a webhook is lost.
Serves FR-S34, FR-S38; RUL-33, RUL-34, RUL-35, RUL-39.

---

## 8. Cards and downloads

### 8.1 `GET /v1/cards/{token}`

Public dedication card data for `/store/card/{token}` (FR-S60, RUL-22). Anonymous, `noindex`.

```json
{
  "toName": "أم خالد",
  "messageAr": "اللهم اجعله في ميزان حسناتك",
  "messageEn": "May it weigh in your good deeds",
  "fromName": "نورة",
  "product": { "slug": "water-for-schools", "titleAr": "سقيا الطلاب — برادات مياه للمدارس", "titleEn": "Water for pupils — school water coolers",
               "coverUrl": "https://store-api.ta3lom.sa/files/public/covers/water-for-schools.webp" },
  "locale": "ar",
  "issuedAt": "2026-09-14T11:02:00Z"
}
```

Never includes the amount (RUL-22). If the order is anonymous and no sender name was typed,
`fromName` is `"فاعل خير"` (RUL-23). Available only once the order is paid; a refunded line returns
`404`. Errors: `card.not_found`. Serves FR-S60, RUL-22, RUL-23.

### 8.2 `GET /v1/downloads/{token}`

The signed link from §5.7. Streams the watermarked PDF (`application/pdf`,
`Content-Disposition: attachment; filename="formative-guide.pdf"`), increments `download_count`,
writes `download_log`. Errors: `download.link_expired`, `download.limit_reached`, `download.revoked`.
Serves FR-S41, RUL-41.

---

## 9. Admin

Base `/v1/admin`. Bearer with audience `store-staff` except §9.1 login steps. Every write is audited
in `audit_log` (`actor` = staff email, BR-S11) by the save interceptor. `Cache-Control: no-store`.
Rate policy `admin` (`staff-login` for §9.1). Tables list each endpoint; JSON examples follow for
the non-trivial DTOs. `Page<X>` = `PagedResponse<X>` (§1.4).

### 9.1 Staff authentication (RUL-62, FR-S70)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| POST | `/admin/auth/login` | anonymous | `{ email, password }` | `{ mfaToken, totpEnrolled }` | `auth.invalid_credentials`, `auth.account_locked` |
| POST | `/admin/auth/totp/enrol` | `mfaToken` (not enrolled) | — | `{ otpauthUri, secretBase32 }` | `auth.totp_required` |
| POST | `/admin/auth/totp/verify` | `mfaToken` | `{ code }` | `{ accessToken, expiresIn, staff }` + `t3s_srt` cookie | `auth.totp_invalid`, `auth.account_locked` |
| POST | `/admin/auth/refresh` | `t3s_srt` cookie | — | `{ accessToken, expiresIn }` | `auth.refresh_*` |
| POST | `/admin/auth/logout` | cookie | — | `204` | — |
| GET | `/admin/me` | `staff:read` | — | `StaffMe` | — |

`mfaToken` is a 5-minute JWT (audience `store-staff-mfa`) sent as the bearer on the TOTP steps.
Five failures (password or TOTP) in 15 minutes lock the account for one hour (FR-53 rule).

```json
{ "accessToken": "eyJ…", "expiresIn": 600,
  "staff": { "id": "0199a100-1a2b-7c3d-8e4f-5a6b7c8d9e01", "email": "finance@ta3lom.sa", "name": "سارة القحطاني",
             "roles": [ "finance" ], "lastLoginAt": "2026-09-28T07:12:00Z" } }
```

### 9.2 Dashboard (FR-S74)

| Method | Path | Policy | Response |
|---|---|---|---|
| GET | `/admin/dashboard` | `staff:read` | `Dashboard` |

```json
{
  "sales":  { "today": "1830.00", "last7Days": "14215.00", "last30Days": "61940.00" },
  "gifts":  { "today": "4600.00", "last7Days": "38250.00", "last30Days": "152300.00" },
  "ordersAwaitingPayment": 6, "ordersInReview": 1,
  "topProducts": [ { "slug": "water-for-schools", "amount": "21000.00" }, { "slug": "digital-teacher", "amount": "9800.00" } ],
  "campaigns": [ { "slug": "learning-data", "percent": 86, "endsAt": "2026-11-15T20:59:59Z" } ],
  "allocation": { "awaiting": 212, "overdue30": 14, "oldestAwaiting": "2026-08-20T12:00:00Z" },
  "alerts": [ { "code": "allocation.overdue", "count": 14 }, { "code": "outbox.dead", "count": 0 }, { "code": "integrity.failed", "count": 0 } ]
}
```

"Sales" = `buy` lines + shipping; "gifts" = `sponsor` + `donate` lines, paid orders, net of refunds.

### 9.3 Products, formats and causes (FR-S72, FR-S58)

| Method | Path | Policy | Request | Response | Errors | Rules |
|---|---|---|---|---|---|---|
| GET | `/admin/products` | `staff:read` | `?type&status&category&q&page` | `Page<AdminProductRow>` | — | — |
| POST | `/admin/products` | `staff:catalogue` | `ProductInput` | `201 AdminProduct` (`draft`) | `product.slug_taken`, `validation.failed` | RUL-01, RUL-03 |
| GET | `/admin/products/{id}` | `staff:read` | — | `AdminProduct` (all blocks, any status) | `http.not_found` | — |
| PUT | `/admin/products/{id}` | `staff:catalogue` | `ProductInput` | `AdminProduct` | `product.type_locked`, `product.slug_taken`, `concurrency.conflict` | RUL-01 |
| PUT | `/admin/products/{id}/course` | `staff:catalogue` | `CourseDetailInput` | `AdminProduct` | `validation.failed` | RUL-04, RUL-42 |
| PUT | `/admin/products/{id}/modules` | `staff:catalogue` | `CourseModuleInput[]` (replaces) | `AdminProduct` | — | FR-S04 |
| PUT | `/admin/products/{id}/book` | `staff:catalogue` | `BookDetailInput` | `AdminProduct` | — | RUL-05 |
| POST | `/admin/products/{id}/formats` | `staff:catalogue` | `BookFormatInput` | `201 BookFormat` | `validation.failed` (one per kind) | RUL-05 |
| PUT | `/admin/formats/{formatId}` | `staff:catalogue` | `BookFormatInput` (no stock fields) | `BookFormat` | `concurrency.conflict` | RUL-05 |
| PUT | `/admin/products/{id}/cause` | `staff:catalogue` | `CauseDetailInput` | `AdminProduct` | `cause.general_fund_exists` | RUL-07, RUL-10 |
| POST | `/admin/products/{id}/publish` | `staff:catalogue` | — | `AdminProduct` (`published`) | `product.publish_incomplete` (`missing[]`) | RUL-02, RUL-03 |
| POST | `/admin/products/{id}/archive` | `staff:catalogue` | — | `AdminProduct` (`archived`) | — | RUL-02 |
| GET | `/admin/products/{id}/preview` | `staff:read` | — | public `ProductPage` DTO (§2.4) regardless of status | — | FR-S72 |
| PUT | `/admin/products/{id}/goal-seed` | `staff:catalogue` | `{ seedRaised, seedUnits, seedDonors, reason }` | `GoalFigures` | `validation.failed` | database/03 §3 |
| GET | `/admin/products/{id}/goal-adjustments` | `staff:finance` | `?page` | `Page<GoalAdjustment>` (sweeps and corrections) | — | RUL-46 |
| POST | `/admin/products/{id}/goal-adjustments` | `staff:finance` | `{ amount, units, note }` | `201 GoalAdjustment` (`kind: "correction"`) + new `GoalFigures` | `goal.adjustment_invalid` | FR-S58, RUL-46 |

`ProductInput`:

```json
{
  "type": "cause", "slug": "school-bag", "categoryId": null, "coverAssetId": "0199a200-3c4d-7e5f-9a6b-7c8d9e0f1a01",
  "titleAr": "الحقيبة المدرسية المتكاملة", "titleEn": "The complete school bag",
  "summaryAr": "حقيبة بكامل أدواتها وقرطاسيتها مع قصة من إصدارات الجمعية.", "summaryEn": "A fully stocked school bag with stationery and one of our story books.",
  "bodyAr": "تُوزّع الحقائب قبل بداية كل فصل دراسي عبر المدارس الشريكة.", "bodyEn": "Distributed before each term through partner schools.",
  "badge": null, "sort": 13,
  "seoTitleAr": null, "seoTitleEn": null, "seoDescAr": null, "seoDescEn": null
}
```

`CauseDetailInput` (dates in Asia/Riyadh wall time; the API converts, RUL-09):

```json
{
  "unitAmount": "180.00", "unitLabelAr": "حقيبة", "unitLabelEn": "bag",
  "targetAmount": "54000.00", "allowCustomAmount": true, "minAmount": "10.00",
  "zakatEligible": true, "beneficiariesAr": "300 طالب", "beneficiariesEn": "300 students",
  "closeOnTarget": false, "isGeneralFund": false,
  "startsAtLocal": "2026-09-01T00:00", "endsAtLocal": "2027-01-15T23:59"
}
```

`GoalAdjustment` (a pool sweep pair shares `pairId`; `kind = 'correction'` is the only kind staff can create):

```json
[ { "id": 311, "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e05", "kind": "pool_sweep_out", "amount": "-50.00", "units": 0,
    "sponsorshipOfferId": "01999c1e-6b10-7a22-9d3e-2b3c4d5e6f05", "pairId": "0199b001-2c3d-7e4f-8a5b-6c7d8e9f0a01",
    "note": "RUL-46 sweep at campaign end", "at": "2026-10-31T21:05:00Z", "actor": "system" },
  { "id": 318, "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e05", "kind": "correction", "amount": "120.00", "units": 0,
    "sponsorshipOfferId": null, "pairId": null, "note": "Bank transfer gift recorded offline, voucher 2026/114",
    "at": "2026-11-02T08:10:00Z", "actor": "finance@ta3lom.sa" } ]
```

`CourseDetailInput`: `price`, `compareAtPrice` (> price or null, RUL-04), `lessons`,
`durationMinutes`, `levelAr/En`, `instructorAr/En`, `accessDays` (null = lifetime), `lmsCourseRef`,
`learnerCount`. `BookFormatInput`: `kind`, `sku`, `price`, `compareAtPrice`, `isActive`,
`weightGrams` and `lowStockThreshold` (print; `null` = setting `low_stock_default_threshold`), `fileAssetId` (digital, private `book_file`). `AdminProductRow`: `id`, `type`,
`slug`, `titleAr`, `status`, `price` or goal percent, `updatedAt`, `hasActiveOffer`.

### 9.4 Sponsorship offers (FR-S50, RUL-06)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/products/{id}/offers` | `staff:read` | — | `Offer[]` (history, newest first) | — |
| POST | `/admin/products/{id}/offers` | `staff:catalogue` | `OfferInput` | `201 Offer` (+ `sponsorship_pool` row) | `offer.active_exists`, `offer.dates_invalid` |
| PUT | `/admin/offers/{offerId}` | `staff:catalogue` | `OfferInput` (seat price change applies to new lines only) | `Offer` | `offer.dates_invalid`, `concurrency.conflict` |
| POST | `/admin/offers/{offerId}/close` | `staff:catalogue` | `{ sweepPool: true }` | `Offer` (`isActive: false`) + sweep result | — |

```json
{
  "id": "01999c1e-6b10-7a22-9d3e-2b3c4d5e6f02",
  "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e02",
  "seatPrice": "120.00", "unitLabelAr": "مقعد", "unitLabelEn": "seat", "targetUnits": 500,
  "beneficiaryGroup": "social_security_students",
  "groupLabelAr": "طلاب الأسر المستفيدة من الضمان الاجتماعي", "groupLabelEn": "Students from social-security families",
  "allowPartial": true, "zakatEligible": false, "closeOnTarget": false,
  "startsAt": "2026-09-01T00:00:00Z", "endsAt": "2026-11-30T20:59:59Z", "isActive": true,
  "pool": { "balance": "80.00" }, "fundedUnits": 342, "awaitingAllocation": 57
}
```

`zakatEligible` on offers is S2 (RUL-19); the API rejects `true` in S1 with `validation.failed`.
Closing with `sweepPool` applies RUL-46 immediately (otherwise `CloseCampaigns` does it at 00:05): the
remainder moves to the general fund as a `goal_adjustment` pair (`pool_sweep_out` / `pool_sweep_in`) and
the response carries `{ "swept": "50.00", "pairId": "…" }`. Zakat is never swept (none in S1 pools).

### 9.5 Categories

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/categories` | `staff:read` | — | `Category[]` incl. inactive | — |
| POST | `/admin/categories` | `staff:catalogue` | `{ slug, nameAr, nameEn, sort, isActive }` | `201 Category` | `validation.failed` (slug `^[a-z0-9-]{2,60}$`), `category.slug_taken` |
| PUT | `/admin/categories/{id}` | `staff:catalogue` | same | `Category` | — |

Categories are deactivated, not deleted. Serves FR-S72.

### 9.6 Media (FR-S72, RUL-03)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| POST | `/admin/media` | `staff:catalogue` | `multipart/form-data`: `file`, `kind` (`image|pdf|book_file`), `altAr`, `altEn` | `201 MediaAsset` | `media.alt_required`, `media.type_not_allowed`, `media.too_large` |
| GET | `/admin/media` | `staff:read` | `?kind&page` | `Page<MediaAsset>` | — |
| PATCH | `/admin/media/{id}` | `staff:catalogue` | `{ altAr, altEn }` | `MediaAsset` | `media.alt_required` |

```json
{ "id": "0199a200-3c4d-7e5f-9a6b-7c8d9e0f1a01", "kind": "image", "isPublic": true,
  "url": "https://store-api.ta3lom.sa/files/public/covers/school-bag.webp",
  "mime": "image/webp", "sizeBytes": 148233, "width": 1200, "height": 800,
  "altAr": "حقيبة مدرسية مع أدوات وقرطاسية", "altEn": "A school bag with stationery", "createdAt": "2026-09-10T09:00:00Z" }
```

Images are re-encoded to WebP (max 2400 px); `book_file` is stored private (`ck_media_private_book`)
and has no `url`. Limits: images 10 MB, PDFs 200 MB (OP-6).

### 9.7 Stock (FR-S43, RUL-30)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/stock` | `staff:read` | `?lowOnly=true` | `StockRow[]` | — |
| GET | `/admin/stock/{formatId}/movements` | `staff:read` | `?page` | `Page<StockMovement>` | — |
| POST | `/admin/stock/{formatId}/movements` | `staff:stock` | `{ delta, reason: "restock"|"adjustment"|"return", note }` | `201 StockRow` | `stock.below_reserved`, `validation.failed` (`delta ≠ 0`) |

```json
{ "formatId": "01999c1e-7c20-7b31-8e4f-3c4d5e6f7a01", "sku": "BK-SCIENCE-STAR-P", "titleAr": "نجوم العلوم — كتاب التجارب المنزلية",
  "onHand": 486, "reserved": 6, "available": 480, "lowStockThreshold": null, "effectiveThreshold": 20, "isLow": false }
```

`sale` and `sponsor_distribution` movements are written by the system only. The threshold is
`book_format.low_stock_threshold` (set through `PUT /admin/formats/{formatId}`), falling back to the
setting `low_stock_default_threshold` (20); `LowStockAlert` emails staff daily at 08:00 (FR-S43).

### 9.8 Cause delivery updates (FR-S59, RUL-54)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/products/{id}/updates` | `staff:read` | `?page` | `Page<CauseUpdate>` | — |
| POST | `/admin/products/{id}/updates` | `staff:catalogue` | `CauseUpdateInput` | `201 CauseUpdate` | `validation.failed` |
| PUT | `/admin/cause-updates/{updateId}` | `staff:catalogue` | `CauseUpdateInput` | `CauseUpdate` | — |
| DELETE | `/admin/cause-updates/{updateId}` | `staff:catalogue` | — | `204` (only if not yet notified) | `cause_update.already_notified` |

```json
{ "titleAr": "تم تركيب البرّاد في مدرسة الهجرة الابتدائية — القصيم", "titleEn": "Cooler installed at Al-Hijra Primary — Qassim",
  "bodyAr": null, "bodyEn": null, "assetId": "0199a200-3c4d-7e5f-9a6b-7c8d9e0f1a07",
  "unitsDelivered": 1, "occurredOn": "2026-09-02", "isPublic": true, "notifyDonors": true }
```

`notifyDonors: true` queues `CauseUpdatePublished` once; `notified_at` is then set and the flag is
read-only.

### 9.9 Orders (FR-S73, FR-S38)

| Method | Path | Policy | Request | Response | Errors | Rules |
|---|---|---|---|---|---|---|
| GET | `/admin/orders` | `staff:read` | `?q` (reference or mobile) `&status&from&to&kind&page` | `Page<AdminOrderRow>` | — | — |
| GET | `/admin/orders/{ref}` | `staff:read` | — | `AdminOrderDetail` | `order.not_found` | — |
| POST | `/admin/orders/{ref}/cancel` | `staff:finance` | `{ reason }` | `AdminOrderDetail` (`cancelled`, stock released) | `order.cancel_not_allowed` | RUL-29, RUL-30 |
| POST | `/admin/orders/{ref}/resend-documents` | `staff:resend` | `{ channels: ["sms","email"] }` | `202` | `document.not_ready` | RUL-63 |
| POST | `/admin/orders/{ref}/payment-review/resolve` | `staff:finance`; `decision: "refund"` also needs `staff:refund` (RUL-56, RUL-62) | `{ decision: "accept"|"refund", note }` | `AdminOrderDetail` | `order.not_in_review` | RUL-35 |

`AdminOrderDetail` = customer `OrderDetail` (§5.10) plus:

```json
{
  "customer": { "id": "01999f00-1a2b-7c3d-8e4f-5a6b7c8d9e01", "mobile": "+966551234567", "fullName": "نورة العتيبي" },
  "contact": { "name": "نورة العتيبي", "mobile": "+966551234567", "email": "noura@example.com" },
  "shippingAddress": { "recipientName": "نورة العتيبي", "city": "بريدة", "shortAddress": "QBRA2929", "postalCode": "52361" },
  "payments": [
    { "id": "0199a010-2b3c-7d4e-8f5a-6b7c8d9e0f01", "provider": "moyasar", "providerPaymentId": "pay_7b2e4f1a-…",
      "method": "mada", "amount": "2605.00", "status": "captured", "failureCode": null, "createdAt": "2026-09-14T10:58:15Z" }
  ],
  "events": [
    { "providerEventId": "evt_9f1c2b7e-…", "eventType": "payment_paid", "signatureValid": true,
      "receivedAt": "2026-09-14T11:01:59Z", "processedAt": "2026-09-14T11:02:00Z", "error": null }
  ],
  "notifications": [
    { "channel": "sms", "templateKey": "order_paid", "recipient": "05•••••567", "status": "sent", "sentAt": "2026-09-14T11:02:03Z" },
    { "channel": "sms", "templateKey": "dedication_card", "recipient": "05•••••543", "status": "sent", "sentAt": "2026-09-14T11:02:04Z" }
  ],
  "refunds": [],
  "sourceCartId": "01999e10-2b3c-7d4e-8f5a-6b7c8d9e0f01"
}
```

Review covers an amount mismatch and a capture on an `expired`/`cancelled` order (RUL-35). `accept`
marks the order `paid` and runs the full settlement (RUL-39); `refund` refunds the full capture.

### 9.10 Refunds (FR-S64, RUL-56 … RUL-58)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/orders/{ref}/refund-eligibility` | `staff:finance` | — | `RefundEligibility[]` per line | `order.not_found` |
| POST | `/admin/orders/{ref}/refunds` | `staff:refund` + `Idempotency-Key` | `RefundRequest` | `201 Refund` (`requested` → `succeeded`) | `refund.*`, `idempotency.*` |
| GET | `/admin/refunds` | `staff:finance` | `?status&from&to&page` | `Page<Refund>` | — |

Eligibility (the screen shows why a line is not refundable, FR-S64):

```json
[
  { "orderLineId": "0199a011-1a2b-7c3d-8e4f-5a6b7c8d9e01", "lineNo": 1, "titleAr": "المعلّم الرقمي: أدوات التعليم الإلكتروني",
    "refundable": false, "reasonCode": "refund.progress_exceeded", "maxAmount": "0.00", "maxUnits": null },
  { "orderLineId": "0199a011-1a2b-7c3d-8e4f-5a6b7c8d9e03", "lineNo": 3, "titleAr": "مهارات المذاكرة الذكية",
    "refundable": true, "reasonCode": null, "maxAmount": "360.00", "maxUnits": 3, "financeDiscretion": true },
  { "orderLineId": "0199a011-1a2b-7c3d-8e4f-5a6b7c8d9e04", "lineNo": 4, "titleAr": "سقيا الطلاب — برادات مياه للمدارس",
    "refundable": false, "reasonCode": "refund.window_passed", "maxAmount": "0.00", "maxUnits": null }
]
```

Request:

```json
{ "reason": "Donor error: sponsored 5 seats instead of 2",
  "lines": [ { "orderLineId": "0199a011-1a2b-7c3d-8e4f-5a6b7c8d9e03", "units": 3, "amount": "360.00" } ] }
```

Response:

```json
{ "id": "0199a300-5e6f-7a8b-9c0d-1e2f3a4b5c01", "orderRef": "T3-26-004187", "amount": "360.00",
  "status": "succeeded", "providerRefundId": "ref_1c9e…", "requestedBy": "finance@ta3lom.sa",
  "createdAt": "2026-09-15T08:30:00Z", "completedAt": "2026-09-15T08:30:02Z",
  "documents": [ { "kind": "cancellation", "number": "DRC-2026-000004" } ],
  "orderStatus": "partially_refunded" }
```

Print lines (RUL-57): before shipment the refund is full, and includes the shipping fee when every print
line of the order is cancelled; a shipped, undelivered line returns `refund.in_transit`; after delivery,
7 days, unused, return required, shipping not refunded unless faulty. A duplicate-course line left
`pending` by settlement (RUL-13 race) always shows as refundable.

If the gateway answers asynchronously, `status` is `requested` and effects apply on its refund
webhook. Errors: `refund.lines_not_refundable` (`lines[]`), `refund.amount_exceeds`,
`refund.gateway_failed`. Serves FR-S64; RUL-56, RUL-57, RUL-58.

### 9.11 Dedication resend and download reset

| Method | Path | Policy | Request | Response | Errors | Rules |
|---|---|---|---|---|---|---|
| POST | `/admin/dedications/{id}/resend` | `staff:resend` | `{ toMobile? }` (correction allowed) | `202` | `http.not_found` | FR-S60, RUL-22 |
| POST | `/admin/download-grants/{id}/reset` | `staff:resend` | `{ reason }` | `DownloadGrant` (`downloadCount: 0`) | `download.revoked` | RUL-41 |

### 9.12 Shipments (FR-S42, RUL-40)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/shipments` | `staff:read` | `?status=preparing&page` | `Page<Shipment>` | — |
| GET | `/admin/shipments/{id}/packing-slip` | `staff:fulfilment` | — | `application/pdf` | — |
| POST | `/admin/shipments/{id}/ship` | `staff:fulfilment` | `{ carrier, trackingNo }` | `Shipment` (`shipped`; SMS) | `shipment.invalid_transition` |
| POST | `/admin/shipments/{id}/deliver` | `staff:fulfilment` | `{ deliveredOn? }` | `Shipment` (`delivered`) | `shipment.invalid_transition` |
| POST | `/admin/shipments/{id}/return` | `staff:fulfilment` | `{ note, restock: true }` | `Shipment` (`returned`; stock movement `return` if restocked) | `shipment.invalid_transition` |

```json
{ "id": "0199a400-6f7a-7b8c-9d0e-1f2a3b4c5d01", "orderRef": "T3-26-004187", "status": "preparing",
  "address": { "recipientName": "نورة العتيبي", "recipientMobile": "+966551234567", "city": "بريدة", "district": "الريان",
               "shortAddress": "QBRA2929", "street": "طريق الملك عبدالعزيز", "buildingNo": "8123", "postalCode": "52361" },
  "lines": [ { "sku": "BK-SCIENCE-STAR-P", "titleAr": "نجوم العلوم — كتاب التجارب المنزلية", "quantity": 2 } ],
  "carrier": null, "trackingNo": null, "placedAt": "2026-09-14T10:58:12Z", "shippedAt": null, "deliveredAt": null }
```

Delivering updates print lines to `delivered` and may complete the order (RUL-40).

### 9.13 Beneficiaries (FR-S52, RUL-51, PDPL)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/beneficiaries` | `staff:beneficiaries` | `?group&status&zakat&region&kind&q` (code) `&page` | `Page<BeneficiaryRow>` (masked) | — |
| POST | `/admin/beneficiaries` | `staff:beneficiaries` | `BeneficiaryInput` | `201 BeneficiaryRow` (`pending`) | `beneficiary.mobile_exists` |
| GET | `/admin/beneficiaries/{id}` | `staff:beneficiaries` | — | `BeneficiaryDetail` (masked, units history) | `beneficiary.not_found` |
| PUT | `/admin/beneficiaries/{id}` | `staff:beneficiaries` | `BeneficiaryInput` | `BeneficiaryRow` | `beneficiary.mobile_exists` |
| POST | `/admin/beneficiaries/{id}/eligibility` | `staff:beneficiaries` | `{ status: "approved"|"rejected"|"inactive", zakatEligible, notes }` | `BeneficiaryRow` (`verified_by/at`) | `validation.failed` |
| POST | `/admin/beneficiaries/{id}/reveal` | `staff:reveal-pii` | `{ reason }` | `BeneficiaryPii` | — |
| POST | `/admin/beneficiaries/{id}/invite` | `staff:beneficiaries` | — | `202` (SMS invite resent, RUL-52) | `beneficiary.no_live_seat` |
| POST | `/admin/beneficiaries/import` | `staff:beneficiaries` | `multipart`: `file` (CSV, UTF-8), `dryRun` (default `true`) | `ImportReport` | `beneficiary.import_invalid` |
| GET | `/admin/beneficiaries/import/template` | `staff:beneficiaries` | — | `text/csv` header row | — |

Row (list, masked):

```json
{ "id": "0199a500-7a8b-7c9d-8e0f-1a2b3c4d5e01", "code": "B-000123", "kind": "teacher",
  "beneficiaryGroup": "rural_private_school_teachers", "region": "القصيم",
  "organisationName": "مدرسة الهجرة الأهلية", "nameMasked": "ه••• ا•••", "mobileMasked": "05•••••218",
  "eligibilityStatus": "approved", "zakatEligible": false, "linkedCustomer": true,
  "verifiedBy": "programs@ta3lom.sa", "verifiedAt": "2026-09-05T10:00:00Z" }
```

`BeneficiaryInput`: `kind`, `beneficiaryGroup`, `region`, `fullName`, `mobile?`, `guardianName?`,
`organisationName?`, `eligibilityNotes?`. For a minor, `mobile` is the guardian's (RUL-47e). The code
`B-000123` comes from `beneficiary_code_seq`. `linkedCustomer` becomes true at the first course-seat
allocation.

Reveal (audited as `beneficiary.reveal` with the reason; FR-S52):

```json
{ "code": "B-000123", "fullName": "هيا العنزي", "mobile": "+966551998218", "guardianName": null, "revealedAt": "2026-09-29T10:15:00Z" }
```

CSV columns: `kind,beneficiary_group,region,full_name,mobile,guardian_name,organisation_name,eligibility_notes`.
Import report:

```json
{ "dryRun": true, "rows": 120, "valid": 117, "created": 0,
  "errors": [ { "row": 14, "code": "beneficiary.mobile_exists", "field": "mobile" },
              { "row": 51, "code": "validation.failed", "field": "kind", "message": "must be student|teacher|parent|school|library" } ] }
```

Imported rows are `pending`; approval is a separate audited step (RUL-51).

### 9.14 Allocation (FR-S53, FR-S55, RUL-47 … RUL-50)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/allocation/backlog` | `staff:read` | — | `BacklogRow[]` (`v_allocation_backlog`) | — |
| GET | `/admin/allocation/products/{productId}/units` | `staff:beneficiaries` | `?status=awaiting_allocation&page` (FIFO order) | `Page<UnitRow>` | — |
| GET | `/admin/allocation/products/{productId}/candidates` | `staff:beneficiaries` | `?region&zakatOnly&page` | `Page<BeneficiaryRow>` matching RUL-47 a–c | — |
| POST | `/admin/allocation/products/{productId}/allocate` | `staff:allocate` | `{ beneficiaryIds[], note }` | `AllocationResult` | `allocation.no_units`, `allocation.rejected` (incl. `allocation.mobile_required`), `allocation.conflict` |
| GET | `/admin/allocation/batches/{batchId}` | `staff:beneficiaries` | — | `AllocationBatch` with units | `http.not_found` |
| POST | `/admin/units/{unitId}/complete` | `staff:allocate` | `{ deliveredOn, proofAssetId? }` (book copies) | `UnitRow` (`completed`) | `unit.invalid_transition` |

Backlog:

```json
[
  { "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e02", "slug": "smart-study", "titleAr": "مهارات المذاكرة الذكية",
    "awaiting": 57, "overdue30": 9, "allocated": 240, "completed": 45, "oldestAwaiting": "2026-08-20T12:00:00Z",
    "groupLabelAr": "طلاب الأسر المستفيدة من الضمان الاجتماعي" }
]
```

Allocate request / result:

```json
{ "beneficiaryIds": [ "0199a500-7a8b-7c9d-8e0f-1a2b3c4d5e01", "0199a500-7a8b-7c9d-8e0f-1a2b3c4d5e02" ],
  "note": "دفعة سبتمبر — القصيم" }
```

```json
{ "batchId": "0199a600-8b9c-7d0e-8f1a-2b3c4d5e6f01", "allocated": 2, "unitsShort": false,
  "units": [ { "unitId": "0199a0f0-1111-7222-8333-444455556601", "beneficiaryCode": "B-000123", "fundedAt": "2026-08-20T12:00:00Z", "hasZakat": false },
             { "unitId": "0199a0f0-1111-7222-8333-444455556602", "beneficiaryCode": "B-000124", "fundedAt": "2026-08-21T09:30:00Z", "hasZakat": false } ] }
```

Rejected request (nothing allocated):

```json
{ "type": "https://store-api.ta3lom.sa/problems/allocation.rejected", "title": "تعذّر التخصيص", "status": 422,
  "code": "allocation.rejected",
  "errors": [ { "beneficiaryCode": "B-000130", "code": "allocation.group_mismatch" },
              { "beneficiaryCode": "B-000131", "code": "allocation.already_enrolled" } ] }
```

The oldest units are taken with `FOR UPDATE SKIP LOCKED` (RUL-48); `unitsShort: true` means fewer
units were free than beneficiaries sent, and the extra beneficiaries were not allocated. For each
course seat the API finds or creates the `customer` row for the beneficiary's mobile, creates the
enrolment (`source = 'sponsored'`), sets `beneficiary.customer_id` and queues the SMS invite (RUL-47,
RUL-52). A unit that later lapses (`LapseUnits`) gets its enrolment `revoked` and the beneficiary a
short SMS; they can be allocated again (RUL-50).

### 9.15 Zakat ledger (FR-S61, RUL-20)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/zakat` | `staff:finance` | `?productId&from&to` | `{ balances: ZakatBalance[], entries: Page<ZakatEntry> }` | — |
| POST | `/admin/zakat/disbursements` | `staff:finance` | `ZakatVoucherInput` | `201 ZakatEntry` (`direction: "out"`, `reference` = voucher no.) | `zakat.balance_insufficient`, `validation.failed` |

```json
{ "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e0c", "slug": "tablet-for-student",
  "received": "18400.00", "disbursed": "9500.00", "refunded": "0.00", "balance": "8900.00" }
```

Disbursement voucher for cause zakat (RUL-20); sponsorship zakat goes out automatically at allocation:

```json
{ "productId": "01999c1e-5a3b-7f10-8c2d-1a2b3c4d5e0c", "amount": "4750.00", "voucherNo": "ZV-2026-0007",
  "disbursedOn": "2026-09-25", "purpose": "5 tablets delivered to zakat-eligible students, Qassim" }
```

The row is written with `reference = voucherNo`; the balance can never go below zero (integrity check 7).

### 9.16 Reports (FR-S75)

| Method | Path | Policy | Request | Response |
|---|---|---|---|---|
| GET | `/admin/reports/{name}` | `staff:finance` | `?from&to&productId&format=json|csv` | JSON rows or `text/csv` (UTF-8 BOM) |

| `{name}` | Source | Columns |
|---|---|---|
| `sales` | `v_sales_daily` (`kind = 'buy'`) | day, product_type, orders, gross, vat, refunded |
| `vat` | `order_line` + `orders.shipping_total` | day, net, vat, gross (purchases + shipping) |
| `gifts` | `order_line` (`sponsor`, `donate`) | product, kind, intent, lines, amount, refunded |
| `zakat` | `zakat_ledger`, `v_zakat_balance` | product, received, disbursed, refunded, balance |
| `allocation` | `v_allocation_backlog`, `sponsored_unit` | product, awaiting, overdue_30, allocated, completed, oldest_awaiting |
| `refunds` | `refund`, `refund_line` | date, order, lines, amount, status, requested_by |
| `shipments` | `shipment` | order, status, carrier, tracking, shipped_at, delivered_at |

```json
{ "name": "sales", "from": "2026-09-01", "to": "2026-09-29",
  "rows": [ { "day": "2026-09-14", "productType": "course", "orders": 7, "gross": "2380.00", "vat": "0.00", "refunded": "0.00" } ] }
```

### 9.17 Settings (FR-S76, RUL §M)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/settings` | `staff:read` | — | `Setting[]` | — |
| PUT | `/admin/settings/{key}` | `staff:admin` | `{ value }` (JSON value, typed per key) | `Setting` | `setting.unknown_key`, `setting.invalid_value` |

```json
[
  { "key": "vat_enabled", "value": false, "updatedAt": "2026-09-01T00:00:00Z", "updatedBy": null },
  { "key": "vat_rate", "value": 15, "updatedAt": "2026-09-01T00:00:00Z", "updatedBy": null },
  { "key": "shipping_flat_fee", "value": "25.00", "updatedAt": "2026-09-01T00:00:00Z", "updatedBy": null },
  { "key": "dedication_templates", "value": [ { "key": "hasanat", "ar": "اللهم اجعله في ميزان حسناتك", "en": "May it weigh in your good deeds" } ],
    "updatedAt": "2026-09-01T00:00:00Z", "updatedBy": null }
]
```

Keys: those of RUL §M plus `seller_vat_no` and `dedication_templates`. Money settings are strings;
counts and percentages are integers.

### 9.18 Message templates (FR-S77, RUL-63)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/templates` | `staff:read` | — | `Template[]` (`key`, `channel`, `locale`, `subject`, `body`, `updatedAt`, `variables[]`) | — |
| PUT | `/admin/templates/{key}/{channel}/{locale}` | `staff:admin` | `{ subject?, body }` | `Template` | `template.render_failed` |
| POST | `/admin/templates/{key}/{channel}/{locale}/test` | `staff:admin` | `{ recipient, sampleData? }` | `202 { rendered }` | `template.render_failed` |

```json
{ "key": "order_paid", "channel": "sms", "locale": "ar", "subject": null,
  "body": "تم استلام طلبك {{ reference }} بمبلغ {{ total }} ر.س. الإيصال: {{ link }}",
  "variables": [ "reference", "total", "link" ], "updatedAt": "2026-09-01T00:00:00Z" }
```

### 9.19 Staff users (RUL-62, FR-S71)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/staff` | `staff:admin` | — | `StaffUser[]` | — |
| POST | `/admin/staff` | `staff:admin` | `{ email, name, roles[] }` | `201 { staff, oneTimePassword }` | `staff.email_taken`, `validation.failed` |
| PUT | `/admin/staff/{id}` | `staff:admin` | `{ name, roles[], isActive }` | `StaffUser` | — |
| POST | `/admin/staff/{id}/reset-password` | `staff:admin` | — | `{ oneTimePassword }` | — |
| POST | `/admin/staff/{id}/reset-totp` | `staff:admin` | — | `204` (re-enrol at next login) | — |
| POST | `/admin/staff/{id}/unlock` | `staff:admin` | — | `204` | — |

```json
{ "id": "0199a100-1a2b-7c3d-8e4f-5a6b7c8d9e02", "email": "programs@ta3lom.sa", "name": "هند الشمري",
  "roles": [ "allocation_officer" ], "isActive": true, "totpEnrolled": true, "lockedUntil": null,
  "lastLoginAt": "2026-09-28T08:40:00Z", "createdAt": "2026-09-02T09:00:00Z" }
```

Roles are validated against the six values of `staff_user.roles`. Security events (create, role
change, reset) are audited.

### 9.20 Customers (FR-S79)

| Method | Path | Policy | Request | Response | Errors |
|---|---|---|---|---|---|
| GET | `/admin/customers` | `staff:read` | `?mobile` (required, exact) or `?email` | `CustomerRow[]` | — |
| GET | `/admin/customers/{id}` | `staff:read` | — | `Customer360` (profile, orders, giving totals, enrolments) | `http.not_found` |
| POST | `/admin/customers/{id}/block` | `staff:finance` | `{ reason }` | `CustomerRow` (`blocked`; tokens revoked) | — |
| POST | `/admin/customers/{id}/unblock` | `staff:finance` | — | `CustomerRow` (`active`) | — |

Lookup is by exact mobile or email only (no browsing of the customer base).

### 9.21 Audit log (FR-S78)

| Method | Path | Policy | Request | Response |
|---|---|---|---|---|
| GET | `/admin/audit` | `staff:admin` | `?actor&entity&entityId&action&from&to&page` | `Page<AuditEntry>` |

```json
{ "id": 48213, "at": "2026-09-20T09:00:04Z", "actor": "programs@ta3lom.sa", "action": "unit.allocate",
  "entity": "sponsored_unit", "entityId": "0199a0f0-1111-7222-8333-444455556601",
  "diff": { "status": [ "awaiting_allocation", "allocated" ], "beneficiary_id": [ null, "0199a500-…" ] } }
```

---

## 10. Health

| Method | Path | Auth | Response |
|---|---|---|---|
| GET | `/health/live` | none | `200 { "status": "Healthy" }` |
| GET | `/health/ready` | none | `200` Healthy/Degraded or `503` Unhealthy |

```json
{ "status": "Degraded", "totalDurationMs": 18,
  "checks": [ { "name": "postgres", "status": "Healthy", "durationMs": 4 },
              { "name": "outbox", "status": "Degraded", "durationMs": 3, "data": { "pending": 612, "oldestSeconds": 1480 } },
              { "name": "data-protection", "status": "Healthy", "durationMs": 1 },
              { "name": "files", "status": "Healthy", "durationMs": 2 },
              { "name": "scheduler", "status": "Healthy", "durationMs": 0 } ] }
```

Serves FR-S83 (09 §8.3).

### 10.1 Development-only routes (`/dev/*`)

Mapped **only** when `ASPNETCORE_ENVIRONMENT` is `Development` or `Staging` (plan 14 §4.3); they return 404 in
Production and are excluded from the OpenAPI document. They are test tooling, not part of the contract:

| Method | Path | Purpose | Session |
|---|---|---|---|
| GET | `/dev/sms?count=50` | Latest messages of the SMS sink (`[{ sentAt, to, body }]`), newest first | S-04 |
| GET | `/dev/mail` | Latest `.eml` files of the email sink | S-17 |
| POST | `/dev/orders/{ref}/confirm-payment` | Simulated payment confirmation through `IOrderSettlement` (`{ amount?, confirmationId? }`) | S-16 |

---

## 11. TypeScript types

`ta3lom-web` never hand-writes API types (ta3lom-web CLAUDE.md). For the store, the pipeline mirrors
the main site's `npm run types:pull`:

1. `ta3lom-store-api` builds `openapi/v1.json` from `Ta3lom.Store.Contracts`
   (`Microsoft.AspNetCore.OpenApi`; document transformers publish money as `string`, enums as string
   unions, `code` values as an enum on `ProblemDetails`). It is committed and attached to each release.
2. `ta3lom-web` runs `npm run types:pull:store`, which fetches the release asset (or the sibling
   repo's `openapi/v1.json`) and runs
   `openapi-typescript openapi/store-v1.json -o src/app/core/store/store-api.types.ts`.
3. Store code imports only from `core/store/store-api.types.ts`:

```ts
import type { components, paths } from '@core/store/store-api.types';
export type ProductCard = components['schemas']['ProductCard'];
export type CartDto = components['schemas']['CartDto'];
export type StoreErrorCode = components['schemas']['StoreErrorCode'];          // 'cart.campaign_ended' | …
type AddLineBody = paths['/v1/cart/lines']['post']['requestBody']['content']['application/json'];
```

Money stays a `string` in TypeScript; formatting uses `Intl.NumberFormat` on the string value only
for display. CI in `ta3lom-web` fails if the pulled file differs from the committed one without a
matching change.

---

## 12. Endpoint index

| # | Method | Path | Auth | Serves |
|---|---|---|---|---|
| 1 | GET | `/v1/catalog/home` | public | FR-S01 |
| 2 | GET | `/v1/catalog/categories` | public | FR-S02 |
| 3 | GET | `/v1/catalog/products` | public | FR-S02, FR-S03 |
| 4 | GET | `/v1/catalog/products/{slug}` | public | FR-S04–S06, FR-S09 |
| 5 | GET | `/v1/catalog/live` | public | FR-S07 |
| 6 | GET | `/v1/catalog/products/{slug}/updates` | public | FR-S06, RUL-54 |
| 7 | GET | `/v1/catalog/products/{slug}/feed` | public | FR-S06, RUL-23 |
| 8 | — | ~~`/v1/catalog/slugs`~~ withdrawn (r1); use #3 with `fields=slug` | — | FR-S09 |
| 9 | GET | `/v1/catalog/config` | public | FR-S12–S14 |
| 10 | GET | `/v1/cart` | cart cookie / customer | FR-S17, FR-S18 |
| 11 | GET | `/v1/cart/summary` | cart cookie / customer | FR-S19 |
| 12 | POST | `/v1/cart/lines` | cart cookie / customer | FR-S10–S16, RUL-12–14/18 |
| 13 | PATCH | `/v1/cart/lines/{lineId}` | cart cookie / customer | FR-S18, RUL-12 |
| 14 | DELETE | `/v1/cart/lines/{lineId}` | cart cookie / customer | FR-S18 |
| 15 | DELETE | `/v1/cart` | cart cookie / customer | FR-S18 |
| 16 | POST | `/v1/auth/otp/request` | public | FR-S21, RUL-60 |
| 17 | POST | `/v1/auth/otp/verify` | public | FR-S21, RUL-17/52/60 |
| 18 | POST | `/v1/auth/refresh` | `t3s_rt` | RUL-60 |
| 19 | POST | `/v1/auth/logout` | `t3s_rt` | FR-S28 |
| 20 | POST | `/v1/auth/logout-all` | customer | FR-S28 |
| 21 | GET | `/v1/me` | customer | FR-S22 |
| 22 | PATCH | `/v1/me` | customer | FR-S22, RUL-61 |
| 23 | GET | `/v1/me/addresses` | customer | FR-S22 |
| 24 | POST | `/v1/me/addresses` | customer | FR-S22 |
| 25 | PUT | `/v1/me/addresses/{id}` | customer | FR-S22 |
| 26 | DELETE | `/v1/me/addresses/{id}` | customer | FR-S22 |
| 27 | POST | `/v1/me/addresses/{id}/default` | customer | FR-S22 |
| 28 | GET | `/v1/me/enrolments` | customer | FR-S23 |
| 29 | POST | `/v1/me/enrolments/{id}/launch` | customer | FR-S23, RUL-42 |
| 30 | GET | `/v1/me/enrolments/{id}/certificate` | customer | FR-S23 |
| 31 | GET | `/v1/me/books` | customer | FR-S24 |
| 32 | POST | `/v1/me/downloads/{grantId}/link` | customer | FR-S24, RUL-41 |
| 33 | GET | `/v1/me/giving` | customer | FR-S25 |
| 34 | GET | `/v1/me/giving/lines/{orderLineId}` | customer | FR-S25, RUL-50 |
| 35 | GET | `/v1/me/orders` | customer | FR-S26 |
| 36 | GET | `/v1/me/orders/{ref}` | customer | FR-S26 |
| 37 | GET | `/v1/me/documents/{number}` | customer | FR-S26, RUL-59 |
| 38 | GET | `/v1/me/export` | customer | FR-S27 |
| 39 | DELETE | `/v1/me` | customer | FR-S27, RUL-61 |
| 40 | POST | `/v1/checkout/quote` | cart cookie / customer | FR-S31, RUL-15/24 |
| 41 | POST | `/v1/checkout` | customer + Idempotency-Key | FR-S30–S33 |
| 42 | GET | `/v1/orders/{ref}/status` | customer or status token | FR-S35 |
| 43 | POST | `/v1/orders/{ref}/retry-payment` | customer | FR-S36, RUL-37 |
| 44 | POST | `/v1/payments/webhooks/{provider}` | provider signature | FR-S34, RUL-33/34/35 |
| 45 | GET | `/v1/cards/{token}` | public | FR-S60, RUL-22 |
| 46 | GET | `/v1/downloads/{token}` | signed link | FR-S41, RUL-41 |
| 47 | POST | `/v1/admin/auth/login` | anonymous | FR-S70 |
| 48 | POST | `/v1/admin/auth/totp/enrol` | mfa token | FR-S70 |
| 49 | POST | `/v1/admin/auth/totp/verify` | mfa token | FR-S70 |
| 50 | POST | `/v1/admin/auth/refresh` | `t3s_srt` | FR-S70 |
| 51 | POST | `/v1/admin/auth/logout` | `t3s_srt` | FR-S70 |
| 52 | GET | `/v1/admin/me` | staff:read | FR-S71 |
| 53 | GET | `/v1/admin/dashboard` | staff:read | FR-S74 |
| 54 | GET | `/v1/admin/products` | staff:read | FR-S72 |
| 55 | POST | `/v1/admin/products` | staff:catalogue | FR-S72 |
| 56 | GET | `/v1/admin/products/{id}` | staff:read | FR-S72 |
| 57 | PUT | `/v1/admin/products/{id}` | staff:catalogue | FR-S72, RUL-01 |
| 58 | PUT | `/v1/admin/products/{id}/course` | staff:catalogue | FR-S72, RUL-04 |
| 59 | PUT | `/v1/admin/products/{id}/modules` | staff:catalogue | FR-S72 |
| 60 | PUT | `/v1/admin/products/{id}/book` | staff:catalogue | FR-S72 |
| 61 | POST | `/v1/admin/products/{id}/formats` | staff:catalogue | FR-S72, RUL-05 |
| 62 | PUT | `/v1/admin/formats/{formatId}` | staff:catalogue | FR-S72, RUL-05 |
| 63 | PUT | `/v1/admin/products/{id}/cause` | staff:catalogue | FR-S58, RUL-07 |
| 64 | POST | `/v1/admin/products/{id}/publish` | staff:catalogue | FR-S72, RUL-02/03 |
| 65 | POST | `/v1/admin/products/{id}/archive` | staff:catalogue | FR-S72, RUL-02 |
| 66 | GET | `/v1/admin/products/{id}/preview` | staff:read | FR-S72 |
| 67 | PUT | `/v1/admin/products/{id}/goal-seed` | staff:catalogue | FR-S58 |
| 68 | GET | `/v1/admin/products/{id}/offers` | staff:read | FR-S50 |
| 69 | POST | `/v1/admin/products/{id}/offers` | staff:catalogue | FR-S50, RUL-06 |
| 70 | PUT | `/v1/admin/offers/{offerId}` | staff:catalogue | FR-S50 |
| 71 | POST | `/v1/admin/offers/{offerId}/close` | staff:catalogue | FR-S50, FR-S56, RUL-46 |
| 72 | GET | `/v1/admin/categories` | staff:read | FR-S72 |
| 73 | POST | `/v1/admin/categories` | staff:catalogue | FR-S72 |
| 74 | PUT | `/v1/admin/categories/{id}` | staff:catalogue | FR-S72 |
| 75 | POST | `/v1/admin/media` | staff:catalogue | FR-S72, RUL-03 |
| 76 | GET | `/v1/admin/media` | staff:read | FR-S72 |
| 77 | PATCH | `/v1/admin/media/{id}` | staff:catalogue | FR-S72 |
| 78 | GET | `/v1/admin/stock` | staff:read | FR-S43 |
| 79 | GET | `/v1/admin/stock/{formatId}/movements` | staff:read | FR-S43 |
| 80 | POST | `/v1/admin/stock/{formatId}/movements` | staff:stock | FR-S43, RUL-30 |
| 81 | GET | `/v1/admin/products/{id}/updates` | staff:read | FR-S59 |
| 82 | POST | `/v1/admin/products/{id}/updates` | staff:catalogue | FR-S59, RUL-54 |
| 83 | PUT | `/v1/admin/cause-updates/{updateId}` | staff:catalogue | FR-S59 |
| 84 | DELETE | `/v1/admin/cause-updates/{updateId}` | staff:catalogue | FR-S59 |
| 85 | GET | `/v1/admin/orders` | staff:read | FR-S73 |
| 86 | GET | `/v1/admin/orders/{ref}` | staff:read | FR-S73 |
| 87 | POST | `/v1/admin/orders/{ref}/cancel` | staff:finance | FR-S73, RUL-29 |
| 88 | POST | `/v1/admin/orders/{ref}/resend-documents` | staff:resend | FR-S73 |
| 89 | POST | `/v1/admin/orders/{ref}/payment-review/resolve` | staff:finance (+ staff:refund for `refund`) | FR-S38, RUL-35 |
| 90 | GET | `/v1/admin/orders/{ref}/refund-eligibility` | staff:finance | FR-S64, RUL-57 |
| 91 | POST | `/v1/admin/orders/{ref}/refunds` | staff:refund + Idempotency-Key | FR-S64, RUL-56–58 |
| 92 | GET | `/v1/admin/refunds` | staff:finance | FR-S64, FR-S75 |
| 93 | POST | `/v1/admin/dedications/{id}/resend` | staff:resend | FR-S60 |
| 94 | POST | `/v1/admin/download-grants/{id}/reset` | staff:resend | RUL-41 |
| 95 | GET | `/v1/admin/shipments` | staff:read | FR-S42 |
| 96 | GET | `/v1/admin/shipments/{id}/packing-slip` | staff:fulfilment | FR-S42 |
| 97 | POST | `/v1/admin/shipments/{id}/ship` | staff:fulfilment | FR-S42, RUL-40 |
| 98 | POST | `/v1/admin/shipments/{id}/deliver` | staff:fulfilment | FR-S42, RUL-40 |
| 99 | POST | `/v1/admin/shipments/{id}/return` | staff:fulfilment | FR-S42, RUL-40 |
| 100 | GET | `/v1/admin/beneficiaries` | staff:beneficiaries | FR-S52 |
| 101 | POST | `/v1/admin/beneficiaries` | staff:beneficiaries | FR-S52, RUL-51 |
| 102 | GET | `/v1/admin/beneficiaries/{id}` | staff:beneficiaries | FR-S52 |
| 103 | PUT | `/v1/admin/beneficiaries/{id}` | staff:beneficiaries | FR-S52 |
| 104 | POST | `/v1/admin/beneficiaries/{id}/eligibility` | staff:beneficiaries | FR-S52, RUL-51 |
| 105 | POST | `/v1/admin/beneficiaries/{id}/reveal` | staff:reveal-pii | FR-S52 |
| 106 | POST | `/v1/admin/beneficiaries/{id}/invite` | staff:beneficiaries | FR-S54, RUL-52 |
| 107 | POST | `/v1/admin/beneficiaries/import` | staff:beneficiaries | FR-S52 |
| 108 | GET | `/v1/admin/beneficiaries/import/template` | staff:beneficiaries | FR-S52 |
| 109 | GET | `/v1/admin/allocation/backlog` | staff:read | FR-S53, FR-S75 |
| 110 | GET | `/v1/admin/allocation/products/{productId}/units` | staff:beneficiaries | FR-S53, RUL-48 |
| 111 | GET | `/v1/admin/allocation/products/{productId}/candidates` | staff:beneficiaries | FR-S53, RUL-47 |
| 112 | POST | `/v1/admin/allocation/products/{productId}/allocate` | staff:allocate | FR-S53, RUL-47/48 |
| 113 | GET | `/v1/admin/allocation/batches/{batchId}` | staff:beneficiaries | FR-S53 |
| 114 | POST | `/v1/admin/units/{unitId}/complete` | staff:allocate | FR-S55, RUL-50 |
| 115 | GET | `/v1/admin/zakat` | staff:finance | FR-S61, RUL-20 |
| 116 | POST | `/v1/admin/zakat/disbursements` | staff:finance | FR-S61, RUL-20 |
| 117 | GET | `/v1/admin/reports/{name}` | staff:finance | FR-S75 |
| 118 | GET | `/v1/admin/settings` | staff:read | FR-S76 |
| 119 | PUT | `/v1/admin/settings/{key}` | staff:admin | FR-S76 |
| 120 | GET | `/v1/admin/templates` | staff:read | FR-S77 |
| 121 | PUT | `/v1/admin/templates/{key}/{channel}/{locale}` | staff:admin | FR-S77 |
| 122 | POST | `/v1/admin/templates/{key}/{channel}/{locale}/test` | staff:admin | FR-S77 |
| 123 | GET | `/v1/admin/staff` | staff:admin | FR-S71 |
| 124 | POST | `/v1/admin/staff` | staff:admin | FR-S71, RUL-62 |
| 125 | PUT | `/v1/admin/staff/{id}` | staff:admin | FR-S71 |
| 126 | POST | `/v1/admin/staff/{id}/reset-password` | staff:admin | FR-S70 |
| 127 | POST | `/v1/admin/staff/{id}/reset-totp` | staff:admin | FR-S70 |
| 128 | POST | `/v1/admin/staff/{id}/unlock` | staff:admin | FR-S70 |
| 129 | GET | `/v1/admin/customers` | staff:read | FR-S79 |
| 130 | GET | `/v1/admin/customers/{id}` | staff:read | FR-S79 |
| 131 | POST | `/v1/admin/customers/{id}/block` | staff:finance | FR-S79 |
| 132 | POST | `/v1/admin/customers/{id}/unblock` | staff:finance | FR-S79 |
| 133 | GET | `/v1/admin/audit` | staff:admin | FR-S78 |
| 134 | GET | `/health/live` | public | FR-S83 |
| 135 | GET | `/health/ready` | public | FR-S83 |
| 136 | GET | `/v1/admin/products/{id}/goal-adjustments` | staff:finance | RUL-46, FR-S58 |
| 137 | POST | `/v1/admin/products/{id}/goal-adjustments` | staff:finance | RUL-46, FR-S58 |

**Total: 136 active endpoints** (numbers 1–137; #8 withdrawn in r1) — 45 public/customer (catalog 8,
cart 6, auth 5, me 19, checkout and orders 4, webhook 1, cards and downloads 2), 89 admin, 2 health.
The zakat disbursement voucher is #116 (reshaped in r1, not a new endpoint).

---

## 13. Open points

| # | Point | Proposed handling |
|---|---|---|
| OP-1 | Idempotency-key storage | **Resolved — schema** (`idempotency_key`, `PruneIdempotencyKeys`; §1.5, 09 §5.16) |
| OP-2 | Source of the home "impact" figures (prototype constants) | Computed from `goal_progress` + `sponsored_unit` where possible; the rest as settings |
| OP-3 | Where course certificates are produced (`enrolment.certificate_no`) | Depends on Q-05 (LMS); the endpoint redirects either way |
| OP-4 | Account deletion (FR-S27) confirmation strength | Require an OTP verified in the last 10 minutes |
| OP-5 | Order status by reference | **Resolved — canonical decision r1-15** (bearer or per-order status token, §6.3) |
| OP-6 | Upload size limits | Images 10 MB, PDFs 200 MB (Caddy `request_body` and Kestrel limits) |
| OP-7 | Low-stock threshold | **Resolved — schema** (`book_format.low_stock_threshold`, setting `low_stock_default_threshold`, §9.7) |
| OP-8 | Accepting a `payment_review` | **Resolved — RUL-35** (accept → `paid`, full settlement; or refund) |
| OP-9 | Role mapping | Partly **resolved — RUL-62** (segregation of duties; admin manages staff, settings, catalogue). Resend and customer-block mapping still proposed (09 OP-13) |
| OP-10 | Gift-feed display name format (09 OP-18) | First name + initial; dedications as "عن …" |
| OP-11 | Webhook payload fields for the chosen provider (09 OP-21) | Fill §7.1 when Q-01 closes |
