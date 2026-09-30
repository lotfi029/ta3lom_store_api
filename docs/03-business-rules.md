# 03 — Store Business Rules

**Revision:** S1 draft · 29 September 2026

This is the **single source of truth** for how the store behaves. Every functional requirement,
API validation, database constraint and test case traces back to a rule here (`RUL-nn`). When the
code and this document disagree, this document wins — or is changed first.

Terminology:

| Term | Meaning |
|---|---|
| **Purchase** | Buying a course or book *for yourself*. Line kind `buy`. |
| **Sponsorship** | Paying for seats (course) or copies (book) that the association gives to beneficiaries. Line kind `sponsor`. |
| **Donation** | Giving to a donation product (cause). Line kind `donate`. |
| **Gift** | A sponsorship or a donation (anything not a purchase). |
| **Unit** | One seat, one copy, or one cause unit (a cooler, a tablet…). |
| **Pool** | Money from partial sponsorship contributions waiting to become whole units. |
| **Allocation** | Assigning a funded sponsored unit to a named beneficiary. |
| **Dedication (إهداء)** | Giving on behalf of someone who receives a card. Not a transfer of goods. |

---

## A. Catalogue

**RUL-01 — Product types.** Every product is exactly one of `course`, `book`, `cause`. The type is immutable after the first order.

**RUL-02 — Publication.** Only `published` products appear in the catalogue or accept cart lines. `draft` products are admin-only. `archived` products stay visible in customers' history but cannot be bought.

**RUL-03 — Bilingual content.** Arabic fields are mandatory. English fields are optional and fall back to Arabic at display time (same `pick()` rule as the main site, ADR-003).

**RUL-04 — Course price.** A course has one price. An optional `compare_at_price` is shown struck through only if it is higher than the price.

**RUL-05 — Book formats.** A book has one or more formats: `print` and/or `digital`. Each format has its own price. Only `print` has stock and weight.

**RUL-06 — Sponsorable products.** A course or book *may* have one active **sponsorship offer** with:
seat price (may differ from the retail price — usually lower, as it covers cost only), unit label
(seat / copy), target units, beneficiary group description, start and end dates, and whether partial
contributions are allowed.

**RUL-07 — Cause products.** A cause has: an optional unit amount (e.g. 1,500 SAR per cooler) and
unit label, a target amount, an end date, whether custom amounts are allowed, a minimum amount,
and a zakat-eligible flag. A cause with no unit amount accepts amounts only (the general fund).

**RUL-08 — Goal display.** Progress = raised ÷ target, rounded down to a whole percent and capped at 100 on the bar. The raw number may exceed 100%. The display shows raised amount (or funded units), target, donor count, and either the end date or "goal reached".

**RUL-09 — Campaign end.** After `ends_at` (Asia/Riyadh), a cause or sponsorship offer stops accepting new cart lines and shows "campaign ended". Lines already in a cart are rejected at checkout (RUL-24). An admin may extend the date.

**RUL-10 — Goal reached.** Reaching the target does **not** close a campaign unless the admin set `close_on_target`. By default, giving stays open until the end date (Kafeef behaviour).

---

## B. Cart

**RUL-11 — One cart per visitor.** A guest gets a server-side cart identified by an opaque, random cart id in an HttpOnly cookie. On sign-in, the guest cart merges into the customer's cart (RUL-17).

**RUL-12 — Line kinds and modes.**

| Kind | Allowed on | Mode `units` | Mode `amount` |
|---|---|---|---|
| `buy` | course, book format | ✅ (course qty = 1; print qty 1–20; digital qty = 1) | ❌ |
| `sponsor` | course/book with an open offer | ✅ qty 1–999 × seat price | ✅ if the offer allows partial; min 10 SAR |
| `donate` | cause | ✅ if the cause has a unit amount; qty 1–999 | ✅ if custom amounts are allowed; ≥ cause minimum (default 10 SAR) |

**RUL-13 — One enrolment per course.** A customer cannot buy a course they already own, that is already in their cart as `buy`, or that is in one of their unpaid (`pending_payment` / `payment_failed`) orders. Sponsoring the same course is always allowed. If a duplicate is paid anyway (a race), settlement creates no second enrolment, marks the line for refund and alerts finance.

**RUL-14 — Merging lines.** Adding an identical line (same product, format, kind, mode `units`, intent, no dedication) increases the quantity instead of adding a row. `amount` lines and dedicated lines never merge.

**RUL-15 — Prices in the cart are indicative.** The cart stores the unit price seen by the visitor for display. **Checkout always reprices from the catalogue.** If a price changed, the customer is shown the new total before paying.

**RUL-16 — Cart expiry.** Guest carts expire 30 days after the last change. Customer carts do not expire.

**RUL-17 — Merge on sign-in.** Guest lines move into the customer cart. Duplicates follow RUL-14. A `buy` course line the customer already owns is dropped with a notice.

**RUL-18 — Cart limit.** At most 50 lines and 100,000 SAR per cart. Larger gifts are handled by the association offline (contact prompt).

---

## C. Gift options

**RUL-19 — Intent.** Every `donate` line has an intent: `sadaqa` (default) or `zakat`. `zakat` is allowed only if the cause is `zakat_eligible`. `sponsor` lines are `sadaqa` unless the offer is marked zakat-eligible (S2). `buy` lines have no intent.

**RUL-20 — Zakat ring-fence.** Zakat money is tracked per product in the zakat ledger. It is allocated only to beneficiaries with `zakat_eligible = true`. It is never used for administration or overheads, and is reported separately.
Outflows are recorded two ways. For sponsorships, the ledger records the zakat share of each unit at allocation. For causes, finance records a **disbursement voucher** (`zakat_ledger` direction `out`, `reference` = voucher number, with the amount, date and purpose) when zakat-funded units are delivered. The balance can never go below zero (integrity check 7).

**RUL-21 — Dedication.** Any gift line may carry one dedication: recipient name (required, 2–60 chars), recipient Saudi mobile (required, `05XXXXXXXX`, stored as E.164 `+9665XXXXXXXX`), sender display name (optional), and a message chosen from approved templates, or free text up to 140 chars if the admin enables it. Purchases cannot be dedicated.

**RUL-22 — Dedication delivery.** The card SMS is sent **only after payment is confirmed**, at most once per line, with a link to a public card page (`/store/card/{token}`). The card shows the recipient name, message, sender name and product title. It never shows the amount.

**RUL-23 — Anonymous giving.** A donor may choose "فاعل خير" at checkout. Their name is then hidden in public feeds and on dedication cards (unless a sender name is typed), but still appears on their own receipt.

---

## D. Checkout and orders

**RUL-24 — Checkout validation.** At checkout the server re-validates every line: the product is published, the campaign is open, the offer is open, stock is available, limits are respected (RUL-12, RUL-18), and there is no duplicate course ownership. Failing lines are returned with a reason. Nothing is charged until all lines pass.

**RUL-25 — Identity.** Checkout requires an OTP-verified mobile (RUL-60). Name and email are required. Email receives receipts.

**RUL-26 — Shipping.** A shipping address is required only if the cart has a `print` line. Shipping is a flat fee per order (setting, default 25 SAR), within KSA only, VAT-inclusive. Gifts and digital goods never add shipping.

**RUL-27 — VAT.** If `vat_enabled` is set: purchase prices are VAT-inclusive at the configured rate (default 15%). Per line, `vat = round(amount × rate ÷ (100 + rate), 2)` and `net = amount − vat`. Gifts carry VAT 0 and are outside scope (pending Q-02). If `vat_enabled` is false, VAT is 0 everywhere and invoices are not tax invoices.

**RUL-28 — Order reference.** `T3-YY-NNNNNN`, where YY is the two-digit year and NNNNNN is a gap-tolerant database sequence. The reference is unique and shown to the customer.

**RUL-29 — Order states.**

```
pending_payment ──► paid ──► fulfilled
      │               │  └──► partially_refunded ──► refunded
      │               └──────────────────────────► refunded
      ├──► payment_failed  (customer may retry → new payment on the same order)
      └──► expired         (no success within 60 minutes)
cancelled       (staff, only from pending_payment or payment_failed)
payment_review  (captured amount ≠ grand_total, or capture arrives on an expired/cancelled order)
                ──► paid | refunded   (finance decides)
fulfilled ──► partially_refunded ──► refunded
fulfilled ──► refunded                            (refunds after fulfilment, RUL-57)
```

When an order expires or its payment fails for good, its lines are restored to the customer's active cart unless the same product is already there, so the customer does not rebuild the cart.

`paid` means **captured and confirmed by the gateway**. `fulfilled` means every line has reached its terminal fulfilment state (RUL-40).

**RUL-30 — Stock reservation.** Print stock is reserved when the order is created (`pending_payment`) and released on `expired`, `payment_failed` after 60 minutes, or `cancelled`. Stock is committed on `paid`.

**RUL-31 — Totals.** `grand_total = purchases_total + shipping_total + gifts_total`. All money is `numeric(12,2)` in SAR and is never stored or computed as floating point.

---

## E. Payments

**RUL-32 — Hosted payment only.** Card entry happens on the gateway's hosted page or SDK. Our servers never receive a PAN or CVV.

**RUL-33 — The webhook is the truth.** An order becomes `paid` **only** when a gateway webhook with a verified signature confirms capture, or when the reconciliation job reads a captured status from the gateway API. The browser return URL is never proof of payment (same rule as FR-43).

**RUL-34 — Idempotency.** Every gateway event is stored with its provider event id under a unique constraint. A repeated event is acknowledged and ignored.

**RUL-35 — Amount check.** A captured amount different from the order's `grand_total`, or a capture on an order that is already `expired` or `cancelled`, puts the order in `payment_review`. Nothing is fulfilled until finance resolves it: accept it (→ `paid`, full settlement runs) or refund it.

**RUL-36 — Payment methods.** mada, Apple Pay, Visa/Mastercard and STC Pay. Buy-now-pay-later (Tabby/Tamara) is allowed **only if the cart has no gift lines** (S2).

**RUL-37 — Retry.** A customer may retry a failed payment on the same order within 60 minutes. Each attempt is a new `payment` row.

**RUL-38 — Reconciliation.** Every 5 minutes, a job queries the gateway for payments `initiated` more than 5 minutes ago (up to the order's `payment_due_at` + 24 h) and applies any missed capture. A paid order therefore reaches `paid` within about 10 minutes even if every webhook is lost.

---

## F. Fulfilment

**RUL-39 — What happens on `paid`.** All of the following happen in one database transaction:

| Line | Effect |
|---|---|
| `buy` course | Create an `enrolment` (source `purchase`) and send the access notice |
| `buy` book digital | Create a `download_grant` (5 downloads, no expiry) |
| `buy` book print | Commit stock; create or extend the order's `shipment` (status `preparing`) |
| `sponsor` mode `units` | Create N `sponsored_unit` rows (status `awaiting_allocation`) |
| `sponsor` mode `amount` | Add a `pool_contribution` and run pooling (RUL-45) |
| `donate` | Add to the cause's raised amount; record the zakat ledger if intent is `zakat` |
| any gift | Update goal progress and donor count; queue the dedication card if dedicated |
| order | Queue the invoice and/or donation receipt, receipt email and confirmation SMS |

**RUL-40 — Line fulfilment states.** `buy` course: `enrolled`. Digital: `available`. Print: `preparing → shipped → delivered` (or `returned`). Sponsor: `funded` (fulfilment of the *order* ends here; unit allocation is tracked separately). Donate: `received`. The order is `fulfilled` when every line is terminal. For print lines, terminal means `delivered`.

**RUL-41 — Digital downloads.** A download link is signed, per user, and valid for 10 minutes. Each download is watermarked with the buyer's name, masked mobile and order reference. A limit of 5 downloads per grant; staff can reset it.

**RUL-42 — Course access.** Access is lifetime unless the course defines `access_days`. Access is granted through the LMS adapter (Q-05). If the LMS call fails, the enrolment is created as `pending_sync` and retried. The customer is never charged twice.

---

## G. Sponsorship and allocation

**RUL-43 — Funded units.** One `units`-mode sponsorship line of quantity N creates exactly N sponsored units, each linked to that order line.

**RUL-44 — Offer cap.** Sponsorship may exceed the target (it stays open until the end date, RUL-10). Units beyond the target are allocated as more beneficiaries are approved.

**RUL-45 — Pooling.** Partial contributions go into the product's pool in arrival order. When `pool_balance ≥ seat_price`, the system creates one sponsored unit (source `pooled`) and records which contributions funded it and how much each gave (`unit_funding`). This repeats while the balance allows. The remainder stays in the pool.

**RUL-46 — Pool at campaign end.** If an offer ends with a remainder smaller than one seat, the remainder moves to the association's general education fund (the cause with `is_general_fund`). The contributors are told in their account history. This is stated at the point of giving.
The move is recorded as a `goal_adjustment` pair: `pool_sweep_out` on the sponsorship product and `pool_sweep_in` on the general fund. Goal counters and integrity check 6 include adjustments, and the contributions keep `swept_to_fund_at`. Zakat money is never swept to the general fund. S1 offers are not zakat-eligible (RUL-19), so a pool never holds zakat in S1. From S2, a zakat remainder moves to a zakat-eligible cause chosen by finance.

**RUL-47 — Allocation.** Only staff with the `allocation_officer` role allocate. A unit is allocated to one beneficiary who:
(a) is `approved`; (b) matches the offer's beneficiary group; (c) does not already hold an active enrolment in the same course (course seats); (d) is `zakat_eligible` if any zakat money funded the unit; (e) for course seats, has a mobile number (their own, or a guardian's for a minor).
An allocation batch is **all-or-nothing**: if any selected beneficiary fails a check, nothing is allocated and the officer sees every failure at once.
Allocation creates an enrolment (course) or a distribution record (book copy) for the beneficiary. For a course seat, the system finds or creates the `customer` row for the beneficiary's mobile, so the enrolment has an owner before the first login (RUL-52).

**RUL-48 — FIFO allocation.** The oldest `awaiting_allocation` units are allocated first, so that no donor waits longer than necessary.

**RUL-49 — Allocation SLA.** A unit awaiting allocation for 30 days raises an admin alert. At 90 days it is escalated to the manager.

**RUL-50 — Unit lifecycle.**

```
awaiting_allocation ──► allocated ──► completed      (course finished / copy delivered)
                            └──────► lapsed ──► awaiting_allocation   (beneficiary never started within 60 days; seat re-allocated)
```

When a unit lapses, the beneficiary's enrolment becomes `revoked` and they are sent a short SMS. The same person may be allocated again later. A lapsed unit keeps its original `funded_at`, so it goes back to the front of the FIFO queue (RUL-48).

Every transition is time-stamped and visible to the sponsor as a status only. **Donors never see beneficiary identities** (PDPL). They see group, region and status.

**RUL-51 — Beneficiary register.** A beneficiary has a code (`B-000123`), a kind (student / teacher / parent / school / library), region, group, eligibility status (`pending / approved / rejected / inactive`), zakat eligibility, and encrypted contact details. Creating or approving a beneficiary is audited.

**RUL-52 — Beneficiary access.** When a course seat is allocated, the beneficiary receives an SMS invitation. Their `customer` row already exists (RUL-47), so the first OTP login simply signs them in. The enrolment appears under "My courses", and `beneficiary.customer_id` is set at allocation.

---

## H. Donation products

**RUL-53 — Cause units.** `units` mode records quantity × unit amount. Units are a display and reporting convenience. The money is pooled at the cause level, and delivery is reported through updates (RUL-54), not per-unit allocation.

**RUL-54 — Delivery updates.** Staff post updates on a cause: text, optional photo, units delivered and date. Public updates appear on the product page. All donors to that cause see them in "My giving" and get an SMS or email for updates flagged `notify`.

**RUL-55 — Over-target money.** Money received after the target is reached is used for the same purpose (more units) until the end date. The product page states this.

---

## I. Refunds

**RUL-56 — Who refunds.** Only staff with the `finance` role, through the gateway's refund API. Refunds are never processed by hand outside the system.

**RUL-57 — Refund eligibility.**

| Line | Refundable when |
|---|---|
| Course | Within 7 days of purchase **and** progress < 20% → full refund; access revoked |
| Digital book | Only if never downloaded |
| Print book | Before shipment: full refund, including the shipping fee if every print line is cancelled. After delivery: within 7 days, unused and in original condition (return shipment required); the shipping fee is not refunded unless the item is faulty |
| Sponsorship (units) | Only units still `awaiting_allocation`, and only within 48 hours, for a donor error; at finance discretion |
| Pool contribution | Only within 48 hours and only the part not yet converted into a unit |
| Donation | Only within 48 hours, for a donor error; at finance discretion |

**RUL-58 — Refund effects.** Refunding a gift reverses goal progress, pool balance and the zakat ledger. A credit note is issued for a refunded purchase. A cancelled donation receipt is issued for a refunded gift. Orders become `partially_refunded` or `refunded`.

---

## J. Documents

**RUL-59 — Two documents per order, never one.**

| Document | When | Covers | Numbering |
|---|---|---|---|
| Sales invoice (simplified tax invoice if VAT-registered) | Order paid and has purchase lines | `buy` lines + shipping | `INV-YYYY-NNNNNN` |
| Donation receipt (إيصال تبرع) | Order paid and has gift lines | `sponsor` + `donate` lines, zakat shown separately | `DR-YYYY-NNNNNN` |

Numbers are gapless per series per year and are issued in the payment transaction. Documents are bilingual PDFs, stored, immutable, and re-downloadable from the account.

---

## K. Identity and accounts

**RUL-60 — OTP login.** Mobile in Saudi format. A 6-digit code, valid for 5 minutes, with 5 wrong attempts per code. Limits: at most 3 codes per mobile per 15 minutes and 10 per IP per hour. The code is stored hashed. On success: a short-lived access token plus a rotating refresh token in an HttpOnly cookie (as ADR-006, for customers).

**RUL-61 — Account data.** Name, mobile (unique login), email, preferred locale, saved addresses, default anonymity preference. A `blocked` customer cannot sign in or check out, but keeps read access to documents through links already emailed to them. A customer can export or delete their data (PDPL). Deleting an account anonymises it: financial records are kept with the name replaced, as the law requires records to be retained.

**RUL-62 — Staff accounts.** These are separate from customers: email + password + TOTP 2FA (mandatory). Roles: `admin`, `store_manager`, `finance`, `allocation_officer`, `fulfilment`, `viewer`. A user can hold several roles. The lockout rule matches FR-53.
**Segregation of duties:** `admin` manages staff, settings and the catalogue, but does **not** implicitly refund, allocate or reveal beneficiary PII. Those actions need the `finance` or `allocation_officer` role explicitly, even for an admin.

---

## L. Notifications

**RUL-63 — Notifications sent.**

| Event | Customer | Other |
|---|---|---|
| OTP requested | SMS code | — |
| Order paid | SMS + email with receipt link | — |
| Course enrolled | Included in order email | — |
| Shipment shipped / delivered | SMS | — |
| Dedication | — | SMS card to recipient |
| Unit allocated / completed | Email monthly digest (job `SponsorDigest`, 1st of month) + account status | Beneficiary: SMS invitation |
| Cause update (notify) | SMS or email | — |
| Refund processed | SMS + email | — |
| Unit awaiting > 30 days | — | Staff alert |

Notifications are written to an outbox inside the business transaction and sent by a background worker. A failure never rolls back a payment. **The one exception is the OTP SMS.** It is sent directly within the request, so the plain code is never stored anywhere, not even in the outbox.

---

## M. Numbers the admin can change (settings)

| Key | Default |
|---|---|
| `vat_enabled` / `vat_rate` | false / 15 |
| `shipping_flat_fee` | 25.00 |
| `min_gift_amount` | 10.00 |
| `cart_max_total` | 100000.00 |
| `order_payment_timeout_min` | 60 |
| `download_limit` | 5 |
| `allocation_alert_days` / `allocation_escalate_days` | 30 / 90 |
| `lapse_days` | 60 |
| `refund_window_days` (course) / `refund_progress_max_pct` | 7 / 20 |
| `low_stock_default_threshold` | 20 (per-format override on `book_format.low_stock_threshold`) |
| `gift_refund_window_hours` | 48 |
| `dedication_free_text_enabled` | false |
