# 04 — Store Functional Requirements

**Revision:** S1 draft · 29 September 2026

IDs `FR-S01` … `FR-S99` are reserved for the store. They do not collide with the main site's `FR-01` … `FR-65`.
Each requirement names its business requirement (`BR-S`) and business rules (`RUL`). The rules
carry the detail; requirements say *what the system does* and *how it is accepted*.

Priority: **M** Must · **S** Should · **C** Could. Release: **S1** first release · **S2** next.

---

## A. Catalogue and discovery

| ID | Requirement | Pri | Rel | Traces |
|---|---|---|---|---|
| FR-S01 | **Store home** at `/{ar,en}/store`: hero, featured accordion, donation opportunities (4), courses (6), how sponsorship works, sponsorable courses (3), books, impact numbers, general-fund band — as in the prototype | M | S1 | BR-S12 |
| FR-S02 | **Catalogue** at `/store/courses`, `/store/books`, `/store/sponsor`, `/store/give` with type tabs, category chips from the API, and sorting (featured, newest, price) | M | S1 | BR-S01–S04 |
| FR-S03 | **Search** across titles and summaries in both languages; tolerant of Arabic variants (ة/ه, ى/ي, أ/إ/ا) | S | S1 | — |
| FR-S04 | **Course page**: title, badges, category, summary, lesson/hour/learner/level figures, body, curriculum, instructor, related products, buy box | M | S1 | BR-S01, RUL-04 |
| FR-S05 | **Book page**: cover, author, pages, formats with prices, description, buy box | M | S1 | BR-S02, RUL-05 |
| FR-S06 | **Cause page**: goal bar, unit value, beneficiaries, donors, days left, "where your gift goes", delivery updates, latest gifts feed (anonymised per RUL-23), buy box | M | S1 | BR-S04, BR-S08, RUL-23, RUL-55 |
| FR-S07 | **Live figures on prerendered pages**: price, stock, goal and donor count are fetched at runtime so that a static page is never stale | M | S1 | ADR-002, RUL-08 |
| FR-S08 | **Share** a product: copy link, WhatsApp, X, Telegram, email; the URL carries the locale | S | S1 | — |
| FR-S09 | **SEO** for every catalogue route: title, description, canonical, OG image = cover, JSON-LD (`Course`, `Book`, `Product`/`DonateAction`) | M | S1 | main NFR |

**Acceptance (FR-S07):** change a price in the admin → the prerendered page shows the new price on the next load without a rebuild. After a rebuild, the prerendered HTML also shows it.

---

## B. Buy box and cart

| ID | Requirement | Pri | Rel | Traces |
|---|---|---|---|---|
| FR-S10 | **Course buy box**: tabs "Enrol myself" / "Sponsor seats" (the second only if an offer is open); enrol shows the price and what is included | M | S1 | RUL-12/13 |
| FR-S11 | **Book buy box**: tabs "Buy a copy" / "Sponsor copies"; format selector; quantity stepper for print only | M | S1 | RUL-05/12 |
| FR-S12 | **Sponsor mode**: beneficiary group statement, goal bar, "by number" (presets 1/5/10/25 for courses, 10/25/50/100 for books, plus a stepper) or "give an amount" (presets + input) with a live "equals X seats" calculation | M | S1 | RUL-06/12/45 |
| FR-S13 | **Cause buy box**: by units (presets 1/2/5/10 + stepper) or by amount (50/100/250/500 + input); amount-only for the general fund; sadaqa/zakat selector on eligible causes with a zakat notice | M | S1 | BR-S06, RUL-07/19, RUL-53 |
| FR-S14 | **Dedication form** on every gift buy box: toggle, recipient name, recipient mobile, sender name, message template (free text if enabled); validated before adding to the cart | M | S1 | BR-S05, RUL-21 |
| FR-S15 | **Quick give** on donation cards: amount input + add, without opening the product | M | S1 | Kafeef |
| FR-S16 | **Add to cart** and **Buy now / Give now** (add, then go to checkout) | M | S1 | — |
| FR-S17 | **Server-side cart** for guests (cookie) and customers; merge on sign-in | M | S1 | RUL-11/16/17 |
| FR-S18 | **Cart page**: two groups, "Your purchases" and "Your giving"; each line shows title, description, dedication, amount, qty stepper (units lines, print), remove; summary with purchases (VAT incl.), shipping, gifts, total | M | S1 | BR-S07, RUL-14/26/31 |
| FR-S19 | **Header cart badge** on every ta3lom.sa page (store and main site) | M | S1 | BR-S12 |
| FR-S20 | **Limits**: line and cart limits enforced with clear messages | M | S1 | RUL-12/18 |

**Acceptance (FR-S12):** with a 350 SAR seat, entering 100 SAR shows "equals 0.29 seats" and adds a line of 100 SAR, mode `amount`. Entering 5 seats adds 1,750 SAR, mode `units`, qty 5.

---

## C. Identity and account

| ID | Requirement | Pri | Rel | Traces |
|---|---|---|---|---|
| FR-S21 | **OTP sign-in**: mobile → 6-digit SMS code → session. Rate limits and lockouts per RUL-60. Works from the header or inline at checkout | M | S1 | BR-S09, RUL-60 |
| FR-S22 | **Profile**: name, email, locale, default anonymity, marketing consent; saved addresses (add, edit, default, delete) | M | S1 | RUL-61 |
| FR-S23 | **My courses**: enrolments with progress, start/resume link (LMS adapter), certificate when completed | M | S1 | RUL-42 |
| FR-S24 | **My books**: digital download button with remaining downloads; print shipments with status and tracking | M | S1 | RUL-41 |
| FR-S25 | **My giving**: totals (given, units sponsored, dedications), every gift line with its status; for units a seat map (awaiting / allocated / completed) with group and region; for causes the delivery-update timeline; for pooled shares "pooled — X of 1 seat" | M | S1 | BR-S08, RUL-50 |
| FR-S26 | **Orders & receipts**: list with reference, date, total, status; download invoice / donation receipt PDFs | M | S1 | RUL-59 |
| FR-S27 | **Data rights**: export my data (JSON), delete my account (anonymise) with confirmation | M | S1 | RUL-61, PDPL |
| FR-S28 | **Sign out** everywhere (revoke refresh-token family) | S | S1 | — |

---

## D. Checkout and payment

| ID | Requirement | Pri | Rel | Traces |
|---|---|---|---|---|
| FR-S30 | **Checkout page**: 3-step indicator; details (name, OTP-verified mobile, email); anonymity option if gifts exist; shipping address only if print; payment method; terms checkbox (acceptance time stored on the order); pay button showing the total | M | S1 | BR-S07, RUL-25/26, RUL-23 |
| FR-S31 | **Server validation & repricing** before any charge; return per-line errors and price changes | M | S1 | RUL-15/24, RUL-27 |
| FR-S32 | **Order creation** with reference, snapshots, stock reservation, `pending_payment` | M | S1 | RUL-28/30 |
| FR-S33 | **Hosted payment** via the gateway adapter (mada, Apple Pay, cards, STC Pay); redirect or SDK sheet | M | S1 | RUL-32/36 |
| FR-S34 | **Webhook settlement**: signature-verified, idempotent, runs the settlement transaction | M | S1 | RUL-33/34/39 |
| FR-S35 | **Thank-you page** `/store/orders/{ref}/thanks`: polls status; shows receipt summary, dedication card previews, next actions; handles failure (retry) and pending states | M | S1 | RUL-33/37 |
| FR-S36 | **Payment retry** for a failed payment on the same order within the timeout | M | S1 | RUL-37 |
| FR-S37 | **Reconciliation job** for missed webhooks; **expiry job** for unpaid orders | M | S1 | RUL-29/38 |
| FR-S38 | **Payment review** state when the captured amount ≠ total; finance screen to resolve | M | S1 | RUL-35 |
| FR-S39 | **BNPL** (Tabby/Tamara) for purchase-only carts | C | S2 | RUL-36 |

**Acceptance (FR-S34):** a replayed valid webhook returns 200 and changes nothing. An invalid signature returns 401 and is logged. Closing the browser after paying still results in `paid` within about 10 minutes (reconciliation, RUL-38).

---

## E. Fulfilment

| ID | Requirement | Pri | Rel | Traces |
|---|---|---|---|---|
| FR-S40 | **Course enrolment** on payment through an `ILmsGateway` adapter; `pending_sync` with retry on failure | M | S1 | RUL-42, Q-05 |
| FR-S41 | **Digital download**: signed 10-minute URL, per-buyer PDF watermark, download limit | M | S1 | RUL-41 |
| FR-S42 | **Shipments**: admin queue of orders to ship, packing slip PDF, carrier + tracking entry, shipped / delivered / returned; SMS to the customer | M | S1 | RUL-40, Q-06 |
| FR-S43 | **Stock management**: stock on hand, reserved, available; restock and adjustment with reason; low-stock alert when available ≤ `book_format.low_stock_threshold` (default setting 20) | M | S1 | RUL-30 |

---

## F. Giving operations

| ID | Requirement | Pri | Rel | Traces |
|---|---|---|---|---|
| FR-S50 | **Sponsorship offers** admin: create/edit/close an offer per course or book with seat price, target, group, dates, partial allowed | M | S1 | RUL-06, RUL-09, RUL-10 |
| FR-S51 | **Unit creation & pooling** on settlement | M | S1 | RUL-43/44/45 |
| FR-S52 | **Beneficiary register**: create, import (CSV), verify/approve/reject, zakat eligibility, group, region; PII encrypted, masked in lists, revealed only to `allocation_officer` with audit | M | S1 | RUL-51, PDPL |
| FR-S53 | **Allocation console**: per offer, backlog (awaiting / overdue) FIFO; choose N approved matching beneficiaries → allocate in a batch; the system validates RUL-47 | M | S1 | RUL-47/48 |
| FR-S54 | **Beneficiary invitation**: SMS invite; first OTP login links the customer; enrolment visible | M | S1 | RUL-52 |
| FR-S55 | **Unit lifecycle**: completion sync from the LMS; manual complete for books (distribution proof); lapse & re-allocate job | M | S1 | RUL-50 |
| FR-S56 | **Pool sweep** at campaign end to the general fund, with a customer-visible note | M | S1 | RUL-46 |
| FR-S57 | **Allocation alerts** at 30/90 days | S | S1 | RUL-49 |
| FR-S58 | **Causes** admin: unit, target, dates, zakat flag, general-fund flag, seed figures | M | S1 | RUL-07, RUL-09, RUL-10 |
| FR-S59 | **Delivery updates**: post text/photo/units delivered; public or private; notify donors | M | S1 | RUL-54 |
| FR-S60 | **Dedication cards**: SMS after payment; public card page `/store/card/{token}` (bilingual, noindex); resend by staff | M | S1 | BR-S05, RUL-22 |
| FR-S61 | **Zakat ledger**: inflows on payment; outflows on allocation (sponsorships) or a finance disbursement voucher (causes); balance per product, never negative | M | S1 | BR-S06, RUL-20 |

---

## G. Documents, refunds, finance

| ID | Requirement | Pri | Rel | Traces |
|---|---|---|---|---|
| FR-S62 | **Sales invoice** PDF (simplified tax invoice with QR when VAT-registered) for purchase lines | M | S1 | BR-S07, BR-S13, RUL-59, RUL-27 |
| FR-S63 | **Donation receipt** PDF for gift lines, zakat shown separately, association licence number | M | S1 | BR-S07, RUL-59 |
| FR-S64 | **Refunds**: finance selects lines/units/amount; the system checks RUL-57 and shows why a line is not refundable; calls the gateway; applies RUL-58 effects; issues credit note / cancellation receipt | M | S1 | RUL-56–58 |
| FR-S65 | **ZATCA Phase 2** reporting/clearance through an `IEInvoicingGateway` adapter | S | S2 | BR-S13, Q-02 |

---

## H. Administration and reporting

| ID | Requirement | Pri | Rel | Traces |
|---|---|---|---|---|
| FR-S70 | **Staff sign-in** with email + password + TOTP; lockout; roles | M | S1 | RUL-62 |
| FR-S71 | **Role-based access** (segregation of duties per RUL-62: admin manages staff, settings and the catalogue but needs `finance`/`allocation_officer` explicitly to refund, allocate or reveal PII): admin, store_manager (catalogue, campaigns, stock, updates), finance (orders, refunds, reports, zakat), allocation_officer (beneficiaries, allocation), fulfilment (shipments, stock), viewer (read-only) | M | S1 | RUL-62 |
| FR-S72 | **Product management**: CRUD for courses, books (formats, files), causes; categories; media with mandatory alt; draft/publish/archive; preview | M | S1 | BR-S10, RUL-01, RUL-02, RUL-03 |
| FR-S73 | **Order management**: search by reference/mobile/status/date; order detail with lines, payments, events, documents, notifications; resend receipt; cancel pending | M | S1 | BR-S10 |
| FR-S74 | **Dashboard**: today/7/30-day sales and gifts, top products, campaign progress, allocation backlog, alerts | S | S1 | BR-S11 |
| FR-S75 | **Reports** (CSV + screen): sales & VAT by day/product; gifts by product; zakat ledger; allocation backlog & completion; refunds; shipments | M | S1 | BR-S11 |
| FR-S76 | **Settings** editor for the keys in RUL §M | M | S1 | — |
| FR-S77 | **Message templates** editor (SMS/email, ar/en) with a test send | S | S1 | RUL-63 |
| FR-S78 | **Audit log** viewer: filter by actor, entity, date | M | S1 | BR-S11 |
| FR-S79 | **Customer lookup** (by mobile): orders, giving, enrolments; block/unblock | S | S1 | — |

---

## I. Notifications and integration

| ID | Requirement | Pri | Rel | Traces |
|---|---|---|---|---|
| FR-S80 | **Transactional outbox** and dispatcher with retries; every message recorded in `notification` | M | S1 | RUL-63 |
| FR-S81 | **SMS adapter** (`ISmsSender`) for a Saudi provider with a registered sender ID; **email adapter** (`IEmailSender`, SMTP) | M | S1 | Assumption 2 |
| FR-S82 | **Main-site integration**: "Store" item in the ta3lom.sa nav, cart badge, `/donate` → general fund (Q-09), store links in the footer | M | S1 | BR-S12 |
| FR-S83 | **Health endpoints** `/health/live`, `/health/ready` (DB, outbox lag) | M | S1 | main FR-60 |

---

## Won't have in S1

Coupons, gift cards, buying a course for a named person, recurring donations, waqf, multi-currency,
international shipping, product reviews, wish lists, and a customer-facing LMS player.
