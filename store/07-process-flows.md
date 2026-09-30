# 07 — Store Process Flows

**Revision:** S1 draft r1 · 29 September 2026

Sequence, state and flow diagrams for the store's main processes. The diagrams show how the business
rules in [03](03-business-rules.md) play out across the browser (`ta3lom-web`), the store API
(`Ta3lom.Store.Api`), PostgreSQL (schema `store`), the payment gateway (`IPaymentGateway`, provider
pending Q-01), and the background workers (`OutboxDispatcher`, Quartz jobs).

**Conventions**

- State names are the exact values allowed by the CHECK constraints in `001_store_schema.sql`.
- Table and column names are the DDL's.
- "tx" means one database transaction. Every business event commits with its `outbox_message` rows in
  the same transaction (database design §1.6).
- Messages to customers and staff are sent after commit, by the `OutboxDispatcher` (RUL-63). An SMS
  or email failure never rolls back a payment. **The one exception is the OTP SMS**, which is sent
  directly within the request so that the plain code is never stored (RUL-63, flow l).
- Endpoint paths follow [11 — API contract](11-api-contract.md).
- Revision r1 applies the canonical changes in `_changes-r1.md`. Open points they settle are marked
  "Resolved — RUL-nn" at the end of this document.
- Outbox message types are written in PascalCase (`OrderPaid`, `DedicationCard`, …), as in the
  `outbox_message.type` comment. Template keys are the seeded `message_template.key` values (`otp`,
  `order_paid`, `dedication_card`, `beneficiary_invite`, `shipment_shipped`, `refund_done`,
  `cause_update`).

| # | Flow | Rules |
|---|---|---|
| a | [Purchase a course, end to end](#a-purchase-a-course-end-to-end) | RUL-13, 15, 24, 25, 33, 39, 42 |
| b | [Sponsor seats with a dedication](#b-sponsor-seats-with-a-dedication) | RUL-06, 21, 22, 23, 43 |
| c | [Partial share pooling](#c-partial-share-pooling) | RUL-45 |
| d | [Quick give](#d-quick-give-donation) | RUL-07, 12, 19, FR-S15 |
| e | [Checkout, hosted payment, webhook settlement, outbox](#e-checkout--hosted-payment--webhook-settlement--outbox) | RUL-24, 28–39, 61 |
| f | [Order state machine](#f-order-state-machine) | RUL-29, 35, 57 |
| g | [Sponsored unit lifecycle](#g-sponsored-unit-lifecycle) | RUL-50, 57 |
| h | [Allocation by the allocation officer](#h-allocation-by-the-allocation-officer) | RUL-47, 48, 49, 52, 62 |
| i | [Beneficiary invitation and first login](#i-beneficiary-invitation-and-first-login) | RUL-52, 60 |
| j | [Refund decision](#j-refund-decision) | RUL-13, 56, 57, 58, 62 |
| k | [Campaign end and pool sweep](#k-campaign-end-and-pool-sweep) | RUL-09, 20, 46 |
| l | [OTP sign-in with rate limits](#l-otp-sign-in-with-rate-limits) | RUL-17, 60, 61, 63 |
| m | [Digital download](#m-digital-download-signed-url--watermark) | RUL-41 |
| n | [Print shipment lifecycle](#n-print-shipment-lifecycle) | RUL-26, 30, 40 |

---

## a. Purchase a course, end to end

A visitor enrols in a course for themselves. The page is prerendered. The price is fetched live. The
order becomes `paid` only through the gateway webhook (or reconciliation), and never through the
browser's return.

```mermaid
sequenceDiagram
    autonumber
    actor V as Visitor
    participant W as ta3lom-web
    participant A as Store API
    participant D as PostgreSQL
    participant G as Gateway
    participant O as OutboxDispatcher
    participant L as ILmsGateway

    V->>W: Open /ar/store/p/{slug} (prerendered HTML)
    W->>A: GET /v1/catalog/live?slugs={slug}
    A-->>W: items[0]: price, compareAtPrice, sponsorship (11 §2.5)
    V->>W: Enrol myself, Add to cart
    W->>A: POST /v1/cart/lines kind=buy mode=units qty=1
    A->>D: check published (RUL-02), not owned, not in cart as buy (RUL-13)
    A->>D: INSERT cart_line (unit_price indicative, RUL-15)
    A-->>W: cart, badge count 1
    V->>W: Checkout
    W->>A: OTP sign-in (flow l)
    A-->>W: access token, t3s_rt cookie, guest cart merged (RUL-17)
    W->>A: POST /v1/checkout + Idempotency-Key, acceptTerms true
    A->>D: re-validate and reprice every line (RUL-24, RUL-15), course not in an unpaid order (RUL-13)
    A->>D: tx INSERT orders (pending_payment), order_line, payment (initiated)
    A->>G: create payment (grand_total, return URL with status token)
    A-->>W: 201 reference T3-26-00xxxx, statusToken, redirectUrl
    W->>G: browser redirect to hosted page
    V->>G: pay (mada / Apple Pay / card / STC Pay)
    G-->>A: webhook captured (signed)
    A->>D: settlement tx (flow e)
    Note over A,D: orders.status = paid, payment.status = captured<br/>INSERT enrolment (source purchase)<br/>INSERT invoice INV-2026-nnnnnn<br/>outbox OrderPaid, RenderDocuments, LmsEnrol
    G-->>V: redirect to /ar/store/orders/{ref}/thanks?t={statusToken}
    W->>A: GET /v1/orders/{ref}/status with bearer (after silent refresh), else ?t= (polling)
    A-->>W: paid
    O->>L: Enrol(customer, lms_course_ref)
    alt LMS accepts
        O->>D: enrolment.lms_enrolment_ref set, order_line.fulfilment_status = enrolled
    else LMS fails
        O->>D: enrolment.status = pending_sync, order_line.fulfilment_status = pending_sync, retry with backoff (RUL-42)
    end
    O-->>V: SMS + email order_paid with receipt link (RUL-63)
    Note over D: when every line is terminal, orders.status = fulfilled (RUL-40)
```

**Notes**

- **Duplicate course (RUL-13).** Checkout refuses a course that is already owned, already in the cart
  as `buy`, or already in one of the customer's `pending_payment`/`payment_failed` orders
  (`cart.course_in_unpaid_order`). If a duplicate is paid anyway (a race), settlement creates no second
  enrolment (`ux_enrolment_live`), marks the line for refund and sends `FinanceAlert` (flow j).
- The customer is never charged twice for an LMS failure. The enrolment exists from the settlement
  transaction, and only the LMS sync is retried (RUL-42).
- The default `InternalAccessLmsGateway` (until Q-05) succeeds without a remote call. Progress and
  completion come back through the hourly `LmsProgressSync` job.
- The order moves to `fulfilled` once the course line is `enrolled`. A course purchase can still be
  refunded afterwards within the RUL-57 window (flow j).

---

## b. Sponsor seats with a dedication

A donor sponsors 5 seats (course offer with `seat_price` 350.00) on behalf of their father. The father
gets a card by SMS after payment. The seats are later allocated by the association (flows g, h).

```mermaid
sequenceDiagram
    autonumber
    actor S as Sponsor
    participant W as ta3lom-web
    participant A as Store API
    participant D as PostgreSQL
    participant O as OutboxDispatcher
    participant M as ISmsSender
    actor R as Recipient

    S->>W: Product page, Sponsor seats tab, preset 5
    W-->>S: 5 x 350 SAR = 1,750 SAR (FR-S12)
    S->>W: Dedication: name, 05XXXXXXXX, sender name, template
    W->>W: validate RUL-21 (name 2-60, Saudi mobile, template or free text up to 140)
    W->>A: POST /v1/cart/lines kind=sponsor mode=units qty=5 dedication
    A->>D: offer open, is_active, before ends_at (RUL-06, RUL-09)
    A->>D: INSERT cart_line with dedication jsonb (never merged, RUL-14)
    S->>W: Checkout, anonymous checkbox shown because the cart has gifts (RUL-23)
    W->>A: POST /v1/checkout
    A->>D: tx orders, order_line (sponsorship_offer_id, intent sadaqa), dedication (card_token, sms_status pending)
    Note over S,A: hosted payment and webhook (flow e)
    A->>D: settlement tx
    Note over D: INSERT 5 sponsored_unit (source full, status awaiting_allocation)<br/>+ 5 sponsored_unit_event (to_status awaiting_allocation)<br/>order_line.fulfilment_status = funded<br/>goal_progress funded_units +5, raised_amount +1750, donor_count +1<br/>INSERT donation_receipt DR-2026-nnnnnn<br/>outbox DedicationCard, OrderPaid
    O->>D: load dedication, template dedication_card (order locale)
    O->>M: SMS to recipient with /store/card/{card_token}
    alt sent
        O->>D: dedication.sms_status = sent, sent_at, notification (sent)
    else provider error
        O->>D: notification (failed), outbox retry with backoff, sms_status stays pending
    end
    R->>W: open /ar/store/card/{token}
    W->>A: GET /v1/cards/{token}
    A-->>W: to_name, message, from name, product title (never the amount, RUL-22)
    S->>W: My giving: seat map 5 awaiting allocation
```

**Notes**

- The card SMS is sent **at most once per line**, and only after payment (RUL-22). Staff can resend it
  (FR-S60). A resend is an explicit staff action, logged in `audit_log`.
- **Sender shown on the card.** It is the typed `from_name`. If none was typed and the order is
  anonymous, it is "فاعل خير" (RUL-23). If none was typed and the order is not anonymous, the rules do
  not say (open point 1).
- A dedication does **not** give the recipient the seats. The units stay in the allocation pool
  (BR-S05).

---

## c. Partial share pooling

Partial sponsorship contributions (`sponsor`, mode `amount`) go into the offer's pool in arrival order.
Each time the pool holds a full seat price, one unit is created and its funders are recorded (RUL-45).
This runs inside the settlement transaction of the order that brought the money.

```mermaid
flowchart TD
    A["Settlement: sponsor line, mode amount"] --> B["INSERT pool_contribution<br/>amount = line amount, remaining = amount<br/>is_zakat = (intent = zakat)"]
    B --> C["SELECT sponsorship_pool FOR UPDATE<br/>balance = balance + amount"]
    C --> D{"balance >= seat_price?"}
    D -- no --> Z["UPDATE sponsorship_pool.balance<br/>remainder waits (RUL-45)"]
    D -- yes --> E["INSERT sponsored_unit<br/>source pooled, status awaiting_allocation,<br/>unit_price = seat_price"]
    E --> F["need = seat_price"]
    F --> G["next pool_contribution with remaining > 0<br/>ORDER BY created_at (FIFO)"]
    G --> H["take = min(remaining, need)<br/>INSERT unit_funding (unit, contribution, take)<br/>remaining -= take, need -= take"]
    H --> I{"contribution is_zakat?"}
    I -- yes --> J["sponsored_unit.has_zakat = true (RUL-47d)"]
    I -- no --> K{"need = 0?"}
    J --> K
    K -- no --> G
    K -- yes --> L["balance -= seat_price<br/>goal_progress.funded_units += 1<br/>INSERT sponsored_unit_event"]
    L --> D
```

**Numeric example.** Offer with `seat_price` = 350.00. Three donors contribute 100, 150 and 120, in that
order, each in their own paid order.

| Step | Event | Pool balance | Units created | `pool_contribution.remaining` (A / B / C) |
|---|---|---|---|---|
| 1 | A pays 100.00 | 100.00 | — | 100 / — / — |
| 2 | B pays 150.00 | 250.00 | — | 100 / 150 / — |
| 3 | C pays 120.00 | 370.00, which is ≥ 350 | **U1** (source `pooled`) | A → 0, B → 0, C → 20 |
| | after U1 | 20.00 | | 0 / 0 / 20 |

`unit_funding` rows for U1: (U1, A, 100.00), (U1, B, 150.00), (U1, C, 100.00). The total is 350.00.

- **Zakat branch.** The `is_zakat` branch exists in the schema, but it is inactive in S1, because
  offers are not zakat-eligible until S2 (RUL-20).
- **Invariant.** `sponsorship_pool.balance` (20.00) = Σ `remaining` of unswept contributions
  (20.00). This is nightly integrity check 4.
- **What donors see (FR-S25).** A: "0.29 of a seat, converted into a seat". B: "0.43, converted". C:
  "0.34: 100 SAR converted, 20 SAR waiting in the pool".
- **If the offer ends now,** C's 20.00 is swept to the general fund (flow k).
- **Goal counters.** `goal_progress.raised_amount` rises with every paid contribution (+100, +150, +120).
  `funded_units` rises only when U1 is created.

---

## d. Quick give (donation)

A donor gives an open amount from a donation card without opening the product page (FR-S15, the Kafeef
pattern).

```mermaid
sequenceDiagram
    autonumber
    actor V as Visitor
    participant W as ta3lom-web
    participant A as Store API
    participant D as PostgreSQL

    V->>W: Donation card, type 100, Add
    W->>W: client pre-check: amount >= cause min_amount (default 10, RUL-12)
    alt below minimum
        W-->>V: inline error "Enter at least 10 SAR" (role=alert)
    else valid
        W->>W: optimistic: badge +1, toast "Added to your giving" (live region)
        W->>A: POST /v1/cart/lines kind=donate mode=amount amount=100.00 intent=sadaqa
        A->>D: product published and type cause (RUL-01/02)
        A->>D: campaign open: now < cause_detail.ends_at or ends_at null (RUL-09)
        A->>D: allow_custom_amount, amount >= min_amount, cart limits (RUL-18)
        alt accepted
            A->>D: INSERT cart_line (quantity 1, ck_cart_amount_qty)
            A-->>W: 201 cart
            W->>W: replace local cart with the server cart
        else rejected (campaign_closed, limit, below minimum)
            A-->>W: 422 ProblemDetails code
            W->>W: roll back badge, error toast with the reason
        end
    end
```

**Notes**

- Quick give always creates `intent = sadaqa`. To give zakat, the donor opens the product page, where
  the sadaqa/zakat selector is shown for zakat-eligible causes (RUL-19).
- The quick-give field is shown only when the cause allows custom amounts. A cause with
  `allow_custom_amount = false` shows a "Give" link to its product page instead.
- The line is paid through the normal checkout (flow e). On settlement:
  - `goal_progress.raised_amount += 100` and `donor_count += 1`;
  - `order_line.fulfilment_status = received` (RUL-40);
  - the gift appears in the public feed, anonymised if the order is anonymous (RUL-23).

---

## e. Checkout → hosted payment → webhook settlement → outbox

This is the most important flow. It has three parts: e.1 order creation and hand-off, e.2 the
webhook and settlement, and e.3 failure, retry, expiry and reconciliation. The outbox dispatch is e.4.

### e.1 Order creation and hand-off

```mermaid
sequenceDiagram
    autonumber
    participant W as ta3lom-web
    participant A as Store API
    participant D as PostgreSQL
    participant G as Gateway

    W->>A: POST /v1/checkout (Idempotency-Key, contact, address if print, isAnonymous, paymentMethod, acceptTerms true, expectedGrandTotal)
    A->>D: idempotency_key (scope checkout:customer, key): same body returns the stored response, a different body is refused
    A->>A: customer not blocked (RUL-61), acceptTerms true (FR-S30)
    A->>D: load active cart and lines
    A->>D: re-validate every line (RUL-24): published, campaign/offer open, stock, limits, course not owned or in an unpaid order (RUL-13)
    alt any line fails
        A-->>W: 422 checkout.lines_invalid with per-line reasons (nothing created)
    else repriced total differs from expectedGrandTotal
        A-->>W: 409 checkout.price_changed with a new quote (RUL-15)
    else all lines pass
        A->>D: BEGIN
        A->>D: SELECT book_format FOR UPDATE (print lines)
        A->>D: UPDATE book_format stock_reserved += q WHERE available >= q (RUL-30)
        A->>D: INSERT orders reference T3-YY-NNNNNN (RUL-28), status pending_payment, payment_due_at = now + 60 min, terms_accepted_at = now
        A->>D: INSERT order_line snapshots (title, unit_price, vat per RUL-27), dedication rows
        A->>D: INSERT payment (status initiated), UPDATE cart status converted
        A->>D: COMMIT
        A->>G: create payment (amount = grand_total, currency SAR, return URL with ?t=statusToken, metadata order id)
        alt gateway session created
            G-->>A: provider_payment_id, hosted URL
            A->>D: UPDATE payment.provider_payment_id, store idempotency_key response
            A-->>W: 201 reference, statusToken, payment.redirectUrl (or SDK config, Q-01)
        else gateway unavailable
            A-->>W: 503 checkout.gateway_unavailable with reference and statusToken
        end
    end
```

- **Gateway call outside the transaction.** It is not wrapped in the database transaction. If it fails,
  the order stays `pending_payment` with an `initiated` payment and no `provider_payment_id`. The API
  returns `503 checkout.gateway_unavailable`, the client can retry through
  `POST /v1/orders/{ref}/retry-payment`, and the expiry job releases stock at `payment_due_at`.
- **Status token.** `statusToken` is an HMAC of the order id. `GET /v1/orders/{ref}/status` accepts the
  customer's bearer **or** `?t={statusToken}`, and never the reference alone.
- **Totals.** `grand_total = purchases_total + shipping_total + gifts_total`, enforced by
  `ck_order_total` (RUL-31).

### e.2 Webhook and settlement

```mermaid
sequenceDiagram
    autonumber
    participant G as Gateway
    participant A as Store API webhook endpoint
    participant D as PostgreSQL

    G->>A: POST /v1/payments/webhooks/{provider} (event)
    A->>A: verify signature
    alt signature invalid
        A->>D: INSERT payment_event signature_valid = false (not processed)
        A-->>G: 401, logged (FR-S34)
    else signature valid
        A->>D: BEGIN
        A->>D: INSERT payment_event ON CONFLICT (provider, provider_event_id) DO NOTHING
        alt duplicate event (RUL-34)
            A->>D: COMMIT
            A-->>G: 200, nothing changed
        else new event
            A->>D: SELECT orders FOR UPDATE
            alt event = failed
                A->>D: payment.status = failed, failure_code, failure_message
                A->>D: orders.status = payment_failed (if pending_payment)
            else event = captured and order already paid
                A->>D: mark event processed only
            else captured on an expired or cancelled order (RUL-35)
                A->>D: payment.status = captured, orders.status = payment_review
                A->>D: outbox FinanceAlert
            else captured amount differs from grand_total (RUL-35)
                A->>D: payment.status = captured, orders.status = payment_review
                A->>D: outbox FinanceAlert
            else captured amount = grand_total
                A->>D: payment.status = captured, orders.status = paid, paid_at = now
                A->>D: per line effects (RUL-39, table below)
                A->>D: document_counter FOR UPDATE, INSERT invoice (INV) if purchase lines, donation_receipt (DR) if gift lines (RUL-59)
                A->>D: outbox OrderPaid, RenderDocuments, DedicationCard per dedicated line, LmsEnrol per course
            end
            A->>D: payment_event.processed_at = now
            A->>D: COMMIT (retried on serialization failure)
            A-->>G: 200
        end
    end
```

| Line | Effect in the settlement transaction (RUL-39) | `order_line.fulfilment_status` |
|---|---|---|
| `buy` course | INSERT `enrolment` (source `purchase`), guarded by `ux_enrolment_live`. If one already exists (RUL-13 race): no second enrolment, line marked for refund, `FinanceAlert` | `enrolled` (or `pending_sync` after an LMS failure) |
| `buy` digital | INSERT `download_grant` (`max_downloads` = `download_limit`) | `available` |
| `buy` print | `stock_on_hand -= q`, `stock_reserved -= q`, `stock_movement` (reason `sale`), UPSERT `shipment` (`preparing`) | `preparing` |
| `sponsor` units | N × `sponsored_unit` (source `full`, `awaiting_allocation`) + events | `funded` |
| `sponsor` amount | `pool_contribution` + pooling (flow c) | `funded` |
| `donate` | nothing per unit (RUL-53) | `received` |
| any gift | `goal_progress` (raised, funded units, donor count), `zakat_ledger` (`in`) for zakat intent | — |

### e.3 Failure, retry, expiry and reconciliation

```mermaid
flowchart TD
    P["orders.status = pending_payment<br/>payment.status = initiated"] --> Q{"what happens next?"}
    Q -->|webhook captured| S["settlement (e.2)"]
    Q -->|webhook failed| F["payment failed<br/>orders payment_failed"]
    Q -->|no webhook for 5 min| R["ReconcilePayments (every 5 min, RUL-38)<br/>payments initiated more than 5 min ago,<br/>up to payment_due_at + 24 h"]
    R -->|gateway says captured| S
    R -->|gateway says failed| F
    R -->|still pending| Q
    F --> T{"customer retries<br/>before payment_due_at? (RUL-37)"}
    T -->|yes| N["POST /v1/orders/{ref}/retry-payment<br/>new payment row (initiated), same order"]
    N --> Q
    T -->|no| X["ExpireUnpaidOrders (every 5 min)<br/>pending_payment or payment_failed past payment_due_at<br/>orders expired, stock_reserved released (RUL-30)"]
    Q -->|payment_due_at passed, nothing captured| X
    X --> C["restore the order's lines to the customer's active cart,<br/>skipping products already there (RUL-29)"]
    C --> L{"capture arrives later<br/>(webhook or reconciliation)?"}
    L -->|yes| V["orders payment_review (RUL-35)<br/>finance accepts: paid, full settlement<br/>or refunds"]
    L -->|no| E["final: expired"]
```

- **Proof of payment.** The browser's return URL plays no part in this flow (RUL-33). The thank-you
  page polls `GET /v1/orders/{ref}/status` with the bearer or the status token (see 10-frontend §9).
- **Browser closed after paying.** The order still becomes `paid` within about 10 minutes, through
  reconciliation, even if every webhook is lost (RUL-38).
- **Cart restore.** When the order expires, its lines are restored to the customer's active cart, and
  a product already in the cart is not duplicated (RUL-29). The status response then carries
  `cartRestored: true`, and the web client reloads its cart (10-frontend §6.1). An order that finance
  later accepts from `payment_review` is settled normally. What happens to its restored lines is
  open point 12.
- **Staff cancellation.** Staff (`staff:finance`) can cancel from `pending_payment` or
  `payment_failed` (RUL-29). This releases reserved stock in the same transaction. A capture that
  arrives after cancellation also goes to `payment_review` (RUL-35).

### e.4 Outbox dispatch

```mermaid
flowchart LR
    A["OutboxDispatcher loop (1 s)"] --> B["SELECT outbox_message<br/>WHERE processed_at IS NULL AND next_attempt_at ≤ now<br/>FOR UPDATE SKIP LOCKED LIMIT n"]
    B --> C["handler by type:<br/>OrderPaid, DedicationCard, RenderDocuments,<br/>LmsEnrol, BeneficiaryInvite, ShipmentShipped, RefundProcessed…"]
    C --> D{"success?"}
    D -->|yes| E["processed_at = now<br/>notification status sent, provider_msg_id"]
    D -- no --> F["attempts += 1, last_error<br/>next_attempt_at = exponential backoff, max 24 h<br/>notification status failed"]
    F --> G{"backoff exhausted?"}
    G -- no --> A
    G -- yes --> H["staff alert, message stays unprocessed<br/>(outbox backlog alert over 500 or older than 30 min)"]
    E --> A
```

- **Recording.** Every SMS and email is recorded in `notification`, linked to the order through
  `related_order_id` (FR-S80).
- **At-least-once delivery.** Handlers must be idempotent.
  - `notification.outbox_message_id` with the unique index `ux_notification_once (outbox_message_id,
    channel, recipient)` stops a retried message from sending the same SMS or email twice.
  - `DedicationCard` also checks `dedication.sms_status`. `RenderDocuments` checks for an existing
    `pdf_asset_id`.
- **Not in the outbox.** The OTP SMS is sent directly (RUL-63, flow l).

---


## f. Order state machine

These are the exact `orders.status` values from the DDL CHECK constraint, with the transitions that
RUL-29 and RUL-35 allow.

```mermaid
stateDiagram-v2
    [*] --> pending_payment : checkout creates order (RUL-28)
    pending_payment --> paid : captured = grand_total (webhook or reconciliation)
    pending_payment --> payment_failed : gateway failure
    pending_payment --> payment_review : captured differs from grand_total (RUL-35)
    pending_payment --> expired : no capture within 60 min
    pending_payment --> cancelled : staff
    payment_failed --> paid : retry captured (RUL-37)
    payment_failed --> payment_review : retry captured, amount differs
    payment_failed --> expired : timeout, lines restored to cart
    payment_failed --> cancelled : staff
    expired --> payment_review : late capture (RUL-35)
    cancelled --> payment_review : late capture (RUL-35)
    payment_review --> paid : finance accepts, full settlement
    payment_review --> refunded : finance refunds
    paid --> fulfilled : every line terminal (RUL-40)
    paid --> partially_refunded : refund of part (RUL-58)
    paid --> refunded : refund of all
    fulfilled --> partially_refunded : refund after fulfilment (RUL-57)
    fulfilled --> refunded : refund after fulfilment (RUL-57)
    partially_refunded --> refunded : remaining amount refunded
    expired --> [*]
    cancelled --> [*]
    refunded --> [*]
```

| State | Set by | Side effects |
|---|---|---|
| `pending_payment` | `POST /v1/checkout` | Stock is reserved. `payment_due_at` = `placed_at` + `order_payment_timeout_min`. `terms_accepted_at` is set |
| `paid` | Settlement tx, or finance accepting a `payment_review` | RUL-39 effects, documents, outbox |
| `payment_failed` | Webhook or reconciliation | Stock stays reserved until `payment_due_at` |
| `payment_review` | Settlement tx, when the amount does not match or the capture lands on an `expired`/`cancelled` order | Nothing is fulfilled. `FinanceAlert` is sent. Finance resolves it with `POST /v1/admin/orders/{ref}/payment-review/resolve` |
| `expired` | `ExpireUnpaidOrders` | Stock is released. The lines go back to the active cart, skipping products already there (RUL-29) |
| `cancelled` | Staff (`cancelled_at`) | Stock is released. The change is audited |
| `fulfilled` | Fulfilment events (enrolment, delivery, …) | `fulfilled_at` |
| `partially_refunded` / `refunded` | Refund success (flow j), also from `fulfilled` | `refunded_total`, RUL-58 reversals |

---

## g. Sponsored unit lifecycle

These are the exact `sponsored_unit.status` values. Every transition writes a `sponsored_unit_event`
row (`from_status`, `to_status`, `at`, `actor`). Sponsors see the status, group and region only. They
never see who the beneficiary is (RUL-50, PDPL).

```mermaid
stateDiagram-v2
    [*] --> awaiting_allocation : settlement, source full (RUL-43) or pooling, source pooled (RUL-45)
    awaiting_allocation --> allocated : allocation officer, batch (RUL-47, flow h)
    awaiting_allocation --> cancelled : refund within 48 h for donor error (RUL-57)
    allocated --> completed : course completed (LmsProgressSync) or copy delivered with proof (FR-S55)
    allocated --> lapsed : beneficiary did not start within lapse_days (60), enrolment revoked, SMS
    lapsed --> awaiting_allocation : LapseUnits job returns the unit to the FIFO queue
    completed --> [*]
    cancelled --> [*]
```

| Transition | Trigger | Data |
|---|---|---|
| → `awaiting_allocation` | Settlement | `funded_at` (drives FIFO, RUL-48), `has_zakat` (always false in S1, because offers are not zakat-eligible until S2) |
| → `allocated` | `POST /v1/admin/allocation/products/{productId}/allocate` | `beneficiary_id`, `allocation_batch_id`, `allocated_at`, `allocated_by`. Creates an `enrolment` (source `sponsored`, owned by the beneficiary's `customer`, flow h) or a `distribution` |
| → `completed` | `LmsProgressSync` (hourly) sees the course completed, or a staff member marks a book copy delivered with `distribution.proof_asset_id` (`POST /v1/admin/units/{unitId}/complete`) | `completed_at` |
| → `lapsed` | `LapseUnits` job, 02:00 Riyadh: allocated units whose enrolment has no `started_at` after `lapse_days` | `lapsed_at`. The beneficiary's `enrolment.status` becomes `revoked`, and a short SMS is sent (RUL-50) |
| `lapsed` → `awaiting_allocation` | Same job run | `beneficiary_id` is cleared. The same person may be allocated again later (RUL-50). Whether the unit keeps its original `funded_at` is open point 4 |
| → `cancelled` | Refund success (flow j) | `refund_line.units` |

Alerts (RUL-49):

- `AllocationAlerts`, 07:00 Riyadh, raises a staff alert for units in `awaiting_allocation` for more
  than 30 days (`allocation_alert_days`), and escalates at 90 days (`allocation_escalate_days`).
- Sponsors see status changes in their account straight away. The email summary is monthly, from the
  `SponsorDigest` job (RUL-63).

---

## h. Allocation by the allocation officer

Only staff who explicitly hold `allocation_officer` can allocate (`staff:allocate`). The `admin` role
does not grant it (RUL-62, segregation of duties).

```mermaid
sequenceDiagram
    autonumber
    actor O as Allocation officer
    participant W as Admin UI
    participant A as Store API
    participant D as PostgreSQL
    participant Q as OutboxDispatcher

    O->>W: /admin/store/allocation, choose a product with an open or closed offer
    W->>A: GET /v1/admin/allocation/backlog
    A->>D: v_allocation_backlog (awaiting, overdue_30, oldest_awaiting)
    W->>A: GET /v1/admin/allocation/products/{productId}/candidates
    A->>D: beneficiaries approved, group = offer group, no live enrolment, mobile present for course seats (masked PII)
    A-->>W: candidates
    O->>W: select N beneficiaries, Allocate
    W->>A: POST /v1/admin/allocation/products/{productId}/allocate {beneficiaryIds}
    A->>A: policy staff:allocate (allocation_officer only, RUL-47, RUL-62)
    A->>D: BEGIN
    A->>D: SELECT N sponsored_unit awaiting_allocation ORDER BY funded_at FOR UPDATE SKIP LOCKED (RUL-48)
    A->>A: check RUL-47 a to e for each pair
    alt any pair fails (not approved, wrong group, already enrolled, zakat unit to non-eligible, no mobile)
        A->>D: ROLLBACK
        A-->>W: 422 with the reason per beneficiary
    else all pairs valid
        A->>D: INSERT allocation_batch
        A->>D: UPDATE sponsored_unit status allocated, beneficiary_id, allocated_at, allocated_by
        A->>D: INSERT sponsored_unit_event
        A->>D: course seat: find or INSERT customer for the beneficiary mobile (own or guardian), SET beneficiary.customer_id
        A->>D: course seat: INSERT enrolment (source sponsored, customer_id = that customer)
        A->>D: book copy: INSERT distribution
        A->>D: has_zakat: INSERT zakat_ledger (direction out, zakat share of the unit, reference unit id)
        A->>D: INSERT audit_log unit.allocate
        A->>D: outbox BeneficiaryInvite (course seats), SponsorUnitAllocated
        A->>D: COMMIT
        A-->>W: 201 batch summary
    end
    Q-->>O: (async) beneficiary SMS invitation (flow i), sponsor account status updated
```

**Notes**

- **Concurrency.** `FOR UPDATE SKIP LOCKED` lets two officers work on the same product without
  taking the same units.
- **PII access.** Revealing a beneficiary's decrypted name or mobile is a separate audited action
  (`POST /v1/admin/beneficiaries/{id}/reveal`, `staff:reveal-pii`, which is `allocation_officer` only).
  Allocation itself works on codes (`B-000123`), group and region.
- **Customer row at allocation (RUL-47, RUL-52).** The customer is found or created at this step, so
  the enrolment has an owner before the beneficiary ever signs in. That customer row carries only the
  mobile until the beneficiary completes their profile.
- **Zakat pairing (RUL-47d).** It only matters from S2. In S1 no sponsored unit carries zakat, because
  offers are not zakat-eligible and pools hold no zakat (RUL-20, RUL-46). The batch is all-or-nothing
  as drawn. Partial success is open point 5.

---

## i. Beneficiary invitation and first login

When a course seat is allocated, the beneficiary's `customer` row and enrolment already exist
(flow h). The invitation SMS points them to the store. Their first OTP login simply signs them in,
and the sponsored course is already under "My courses" (RUL-52, FR-S54).

```mermaid
sequenceDiagram
    autonumber
    participant Q as OutboxDispatcher
    participant D as PostgreSQL
    participant M as ISmsSender
    actor B as Beneficiary
    participant W as ta3lom-web
    participant A as Store API

    Note over Q,D: allocation committed (flow h): customer found or created, beneficiary.customer_id set, enrolment (source sponsored) created, outbox BeneficiaryInvite
    Q->>D: load beneficiary, decrypt mobile_enc (Data Protection)
    Q->>M: SMS beneficiary_invite with https://ta3lom.sa/ar/store/login?invite=1
    Q->>D: notification (sent), deduplicated by ux_notification_once
    B->>W: open the link, enter mobile 05XXXXXXXX
    W->>A: POST /v1/auth/otp/request {mobile, locale}
    Note over A,D: same OTP flow as every customer (flow l)
    B->>W: enter code
    W->>A: POST /v1/auth/otp/verify {mobile, code}
    A->>D: verify code, customer already exists, not blocked (RUL-61)
    A->>D: INSERT refresh_token, last_login_at
    A-->>W: access token, t3s_rt cookie
    W->>A: GET /v1/me/enrolments
    A-->>W: course (source sponsored), Start
```

**Notes**

- **Mobile.** Every course seat has a mobile, either the beneficiary's own or a guardian's for a minor
  (RUL-47e). A seat cannot be allocated without one. Book copies go to organisations and need no
  invitation.
- **Resending.** Staff can resend the invitation with `POST /v1/admin/beneficiaries/{id}/invite`.
- **Donor privacy.** A beneficiary who is also a donor keeps one account. Donors never see beneficiary
  identities (RUL-50).

---

## j. Refund decision

Only staff who explicitly hold `finance` can refund (`staff:refund`), and only through the gateway's
refund API (RUL-56). The `admin` role does not grant it (RUL-62). The system checks each line against
RUL-57 and shows why a line is not refundable (`GET /v1/admin/orders/{ref}/refund-eligibility`,
FR-S64). Database effects are applied only after the gateway confirms. Refunds are allowed from
`paid`, `fulfilled` and `partially_refunded` (RUL-29).

```mermaid
flowchart TD
    S["Finance opens order, selects lines, units or amount"] --> R{"staff holds role finance?<br/>(admin alone is not enough)"}
    R -->|no| X["403"]
    R -->|yes| K{"line kind"}
    K -->|buy course| C1{"within refund_window_days (7) of paid_at<br/>AND enrolment.progress_pct under 20,<br/>OR line marked for refund as a RUL-13 duplicate?"}
    K -->|buy digital| C2{"download_grant.download_count = 0?"}
    K -->|buy print| C3{"shipment status"}
    C3 -->|preparing, not shipped| C3a["full refund of the print lines;<br/>shipping refunded too if every print line is cancelled"]
    C3 -->|delivered| C3b{"within 7 days of delivery, unused,<br/>return shipment received?"}
    K -->|sponsor units| C4{"units still awaiting_allocation<br/>AND within 48 h AND donor error?"}
    K -->|sponsor amount| C5{"within 48 h?<br/>refundable = pool_contribution.remaining"}
    K -->|donate| C6{"within 48 h AND donor error?"}
    C1 -->|no| N["not refundable, reason shown"]
    C2 -->|no| N
    C3b -->|no| N
    C4 -->|no| N
    C5 -->|no or remaining = 0| N
    C6 -->|no| N
    C1 -->|yes| OK["line refundable"]
    C2 -->|yes| OK
    C3a --> OK
    C3b -->|"yes (shipping only if faulty)"| OK
    C4 -->|"yes (finance discretion)"| OK
    C5 -->|yes| OK
    C6 -->|"yes (finance discretion)"| OK
    OK --> P["POST /v1/admin/orders/{ref}/refunds + Idempotency-Key<br/>INSERT refund (requested), refund_line"]
    P --> G["IPaymentGateway.Refund(captured payment, amount)"]
    G --> Q{"gateway result"}
    Q -->|failed| F["refund.status = failed<br/>no business effect, finance alerted"]
    Q -->|succeeded, response or webhook| T["one tx, RUL-58 effects"]
    T --> T1["refund succeeded, completed_at<br/>order_line.refunded_amount, fulfilment_status refunded when full<br/>orders.refunded_total, status partially_refunded or refunded<br/>payment.status partially_refunded or refunded"]
    T1 --> T2["reversals: goal_progress down<br/>sponsored_unit cancelled (awaiting only)<br/>pool_contribution.remaining and sponsorship_pool.balance down<br/>zakat_ledger direction refund<br/>enrolment revoked, download_grant.revoked_at<br/>print: shipment cancelled lines or return, stock_movement reason return"]
    T2 --> T3["documents: credit note CN (invoice kind credit_note) for purchases<br/>donation_receipt kind cancellation (DRC) for gifts<br/>outbox RefundProcessed (refund_done SMS + email), audit_log"]
```

**Notes**

- **Windows and thresholds.** These are settings: `refund_window_days`, `refund_progress_max_pct` and
  `gift_refund_window_hours` (RUL §M). The course window is counted from purchase (`orders.paid_at`).
- **Pool contributions.** Only the unconverted part can be refunded. In the flow c example, C can get
  back at most 20.00 within 48 hours. A and B get nothing back, because their money is already in unit
  U1.
- **Double submission.** The Idempotency-Key (stored in `idempotency_key`, scope `refund:{staffId}`)
  and the `refund` row prevent a second gateway refund.
- **Refunding a `payment_review` order.** This uses the same gateway refund, and the order moves
  `payment_review → refunded`.

---

## k. Campaign end and pool sweep

A campaign stops accepting new lines at `ends_at`, Asia/Riyadh time (RUL-09). This is checked on every
add and at checkout. The nightly job handles the pool remainder (RUL-46). The sweep is recorded as a
**`goal_adjustment` pair**, so that both products' goal counters still match integrity check 6.

```mermaid
flowchart TD
    J["CloseCampaigns job, 00:05 Asia/Riyadh"] --> A["offers and causes with ends_at before now,<br/>not yet processed"]
    A --> T{"type"}
    T -->|cause| C["nothing to sweep; product shows campaign ended (RUL-09)<br/>money already counts toward the cause (RUL-55)"]
    T -->|sponsorship offer| L["SELECT sponsorship_pool FOR UPDATE"]
    L --> B{"balance > 0?"}
    B -->|no| Z["done"]
    B -->|"yes (always under one seat_price)"| S["for each pool_contribution with remaining > 0<br/>and swept_to_fund_at IS NULL:<br/>swept_to_fund_at = now"]
    S --> P["pair_id = new uuid, total = sum of remaining"]
    P --> O["INSERT goal_adjustment kind pool_sweep_out<br/>product = offer product, sponsorship_offer_id,<br/>amount = -total, pair_id, actor system"]
    O --> I["INSERT goal_adjustment kind pool_sweep_in<br/>product = general fund (cause_detail.is_general_fund),<br/>amount = +total, pair_id, note offer id"]
    I --> G["goal_progress.raised_amount: offer product -total,<br/>general fund +total"]
    G --> E["sponsorship_pool.balance = 0<br/>outbox PoolSwept per contributor (RUL-46)<br/>audit_log offer.pool_sweep"]
```

In the flow c example, C's remaining 20.00 is swept. The job writes `pool_sweep_out` (−20.00) on the
course and `pool_sweep_in` (+20.00) on the education fund, linked by one `pair_id`. C's "My giving" line
then reads: "20 SAR of your share moved to the Education support fund when the campaign ended."

- **Invariants.**
  - `sponsorship_pool.balance` = Σ `remaining` of unswept contributions (check 4).
  - `goal_progress.raised_amount` = paid gift lines − refunds + Σ `goal_adjustment.amount` (check 6).
    This holds for both products.
  - A pair always sums to 0.
- **Zakat.** Zakat is never swept to the general fund. In S1 pools hold no zakat, because offers are
  not zakat-eligible until S2 (RUL-20, RUL-46).
- **Early close.** An admin can extend `ends_at` before 00:05 to prevent the sweep (RUL-09).
  `POST /v1/admin/offers/{offerId}/close` closes an offer early and runs the same sweep. With
  `close_on_target`, sales stop when the target is reached (RUL-10). The sweep itself runs only when
  the offer is closed.
- **Lines already in carts.** They are rejected at checkout with `line.campaign_closed` (RUL-24).
- **Manual corrections.** Finance can post `correction` adjustments with
  `POST /v1/admin/products/{id}/goal-adjustments`. These are audited.

---

## l. OTP sign-in with rate limits

Customers sign in with a mobile number and a 6-digit SMS code (RUL-60). The response to a code request
is the same whether or not an account exists. The OTP SMS is the one message that bypasses the outbox.
It is sent directly within the request, so the plain code is never stored anywhere (RUL-63).

```mermaid
sequenceDiagram
    autonumber
    actor C as Customer
    participant W as ta3lom-web
    participant A as Store API
    participant D as PostgreSQL
    participant M as ISmsSender

    C->>W: mobile 05XXXXXXXX
    W->>A: POST /v1/auth/otp/request {mobile, locale}
    A->>D: count otp_challenge for this mobile in the last 15 min
    alt 3 or more
        A-->>W: 429 otp.rate_limited, retryAfterSeconds
    else under the limit
        A->>D: count otp_challenge for this ip_hash in the last hour
        alt 10 or more
            A-->>W: 429 otp.rate_limited, retryAfterSeconds
        else allowed
            A->>D: INSERT otp_challenge (purpose login, code_hash = HMAC(code, pepper), expires_at = now + 5 min, ip_hash)
            A->>M: send SMS template otp directly (not through the outbox)
            A-->>W: 202 expiresInSeconds 300
        end
    end
    C->>W: 6-digit code
    W->>A: POST /v1/auth/otp/verify {mobile, code}
    A->>D: latest open otp_challenge for the mobile, FOR UPDATE
    alt expired or consumed
        A-->>W: 400 otp.expired
    else attempts already 5
        A-->>W: 400 otp.too_many_attempts (request a new code)
    else code wrong (constant-time compare)
        A->>D: attempts += 1
        A-->>W: 400 otp.invalid, attemptsLeft
    else code right
        A->>D: BEGIN
        A->>D: consumed_at = now
        A->>D: find or INSERT customer (a beneficiary's row already exists, RUL-52)
        alt customer.status = blocked (RUL-61)
            A->>D: ROLLBACK
            A-->>W: 403 auth.account_blocked
        else active
            A->>D: last_login_at, merge guest cart from t3s_cart into the customer cart (RUL-17)
            A->>D: INSERT refresh_token (subject_type customer, new family_id, token_hash)
            A->>D: COMMIT
            A-->>W: access token (15 min, store-customer), cartNotices, Set-Cookie t3s_rt (HttpOnly, Secure, SameSite=Lax, Path=/v1/auth, 60 days), t3s_cart cleared
        end
    end
```

**Merge on sign-in (RUL-17).**

- Guest lines move into the customer's active cart, and the guest `cart` becomes `merged`.
- Identical undedicated `units` lines merge (RUL-14).
- A `buy` course line for a course the customer already owns is dropped and reported as a
  `cartNotices` entry.

**Later sessions.** `POST /v1/auth/refresh` rotates the refresh token (`replaced_by`). Presenting a
revoked token again revokes the whole family (`auth.refresh_reused`). Staff use the same pattern
under `/v1/admin/auth/*`, with the cookie `t3s_srt` (`Path=/v1/admin/auth`, 8 h) and a mandatory TOTP
step.

---

## m. Digital download (signed URL + watermark)

A buyer downloads their digital book from "My books" (RUL-41, FR-S41).

```mermaid
sequenceDiagram
    autonumber
    actor C as Buyer
    participant W as ta3lom-web
    participant A as Store API
    participant D as PostgreSQL
    participant F as IFileStorage (private volume)
    participant P as PdfSharp watermark

    C->>W: My books, Download
    W->>A: POST /v1/me/downloads/{grantId}/link (Bearer)
    A->>D: download_grant belongs to customer, revoked_at is null, download_count under max_downloads
    alt not allowed
        A-->>W: 409 download.limit_reached, download.revoked or 404
    else allowed
        A-->>W: url /v1/downloads/{token}, expiresAt = now + 10 min, remaining
        W->>A: browser navigates to /v1/downloads/{token} (no Bearer needed)
        A->>A: verify token signature, expiry and customer binding
        alt invalid or expired
            A-->>C: 410 download.link_expired, request a new link
        else valid
            A->>D: BEGIN, UPDATE download_grant download_count += 1 WHERE download_count under max_downloads
            A->>D: INSERT download_log (ip_hash), COMMIT
            A->>F: read book_file (media_asset.storage_key, is_public = false)
            A->>P: stamp every page: buyer full_name, masked mobile, order reference
            A-->>C: application/pdf, Content-Disposition attachment, Cache-Control no-store
        end
    end
```

- **Staff reset.** Staff can reset the counter with `POST /v1/admin/download-grants/{id}/reset`
  (FR-S41). The reset is audited.
- **Refunds.** A refunded grant (`revoked_at`) cannot issue new links. A digital book is refundable
  only while `download_count = 0` (RUL-57).
- **Private files.** Book files are never served from the public volume (`ck_media_private_book`).

---

## n. Print shipment lifecycle

These are the exact `shipment.status` values. The print `order_line.fulfilment_status` follows the
shipment. The order is `fulfilled` only once every print line is `delivered` (RUL-40).

```mermaid
stateDiagram-v2
    [*] --> preparing : settlement, UPSERT shipment with the address snapshot (RUL-39)
    preparing --> shipped : fulfilment staff enter carrier and tracking_no
    shipped --> delivered : carrier confirms, staff mark delivered
    shipped --> returned : undeliverable or refused
    delivered --> returned : return within 7 days of delivery (RUL-57)
    delivered --> [*]
    returned --> [*]
```

```mermaid
sequenceDiagram
    autonumber
    participant D as PostgreSQL
    actor F as Fulfilment staff
    participant A as Store API
    participant Q as OutboxDispatcher
    actor C as Customer

    Note over D: settlement: stock committed (stock_movement sale), shipment preparing, print lines preparing
    F->>A: GET /v1/admin/shipments?status=preparing
    F->>A: GET /v1/admin/shipments/{id}/packing-slip
    F->>A: POST /v1/admin/shipments/{id}/ship {carrier, trackingNo}
    A->>D: shipment shipped, shipped_at, updated_by, print lines shipped, audit_log
    A->>D: outbox ShipmentShipped
    Q->>C: SMS shipment_shipped (reference, tracking)
    F->>A: POST /v1/admin/shipments/{id}/deliver
    A->>D: delivered_at, print lines delivered
    A->>D: if every line is terminal, orders.status fulfilled, fulfilled_at
    Q->>C: SMS delivered (RUL-63)
    opt return
        F->>A: POST /v1/admin/shipments/{id}/return
        A->>D: print lines returned, stock_movement reason return (if resellable)
        Note over A,D: money back only through the refund flow (j)
    end
```

- **One shipment per order.** `shipment.order_id` is unique, so all print lines of an order travel
  together.
- **Refund before shipping.** A refund while the shipment is still `preparing` is a full refund. The
  shipping fee is refunded too when every print line is cancelled (RUL-57). The reserved or committed
  stock is returned with a `stock_movement`.
- **Shipping fee.** It is a flat fee per order (RUL-26). After delivery it is refunded only if the
  goods were faulty (RUL-57).
- **Low stock.** `LowStockAlert` (daily, 08:00) warns staff when a print format's available stock
  falls to or below `book_format.low_stock_threshold`, or to the default setting when that is null.
- **Carrier and handling.** These depend on Q-06.

---

## Open points

1. **Dedication sender fallback.** Still open. When no sender name is typed and the order is not
   anonymous, the card could show the order's contact name or the prototype's "محبّ". RUL-22/23 do
   not say which.
2. **Capture after expiry.** Resolved — RUL-35, RUL-29. The order goes to `payment_review`, and
   finance accepts or refunds it.
3. **Refunds from `fulfilled`.** Resolved — RUL-29. `fulfilled → partially_refunded → refunded` is
   now allowed.
4. **Lapse effects.**
   - Enrolment: Resolved — RUL-50. The enrolment is revoked, an SMS is sent, and the person may be
     allocated again.
   - Still open: whether a re-queued unit keeps its original `funded_at` for FIFO (proposal: yes).
5. **Allocation pairing and batch outcome.**
   - Zakat pairing: Resolved for S1 — RUL-20/46. No sponsored unit carries zakat in S1. The pairing
     rule is needed in S2.
   - Still open: whether a batch with some invalid beneficiaries fails as a whole (as drawn) or
     partially succeeds.
6. **Enrolment before the beneficiary has an account.** Resolved — RUL-47, RUL-52. The customer is
   found or created at allocation, and the first login simply signs in.
7. **Pool sweep accounting and zakat.** Resolved — RUL-46, RUL-20. The sweep is recorded as a
   `goal_adjustment` pair, integrity check 6 includes adjustments, and there is no zakat in pools in
   S1.
8. **Missing templates and mobiles.**
   - Mobiles: Resolved — RUL-47e. A course seat requires a mobile (own or guardian's). Book copies need
     no invitation.
   - Still open: RUL-63 lists a "delivered" SMS, but only `shipment_shipped` is seeded, and the lapse
     SMS (RUL-50) has no seeded template either.
9. **Download counting.** Still open. The flow counts a download each time the signed link streams the
   file, so two streams from one 10-minute link count twice. Confirm, or count only when the link is
   issued.
10. **Order status during a retry.** Still open. RUL-29 does not say whether an order in
    `payment_failed` returns to `pending_payment` while a retry payment is `initiated`. The diagram
    keeps `payment_failed` until a capture or expiry.
11. **Enrolment status with an external LMS.** Still open (depends on Q-05). The enrolment could be
    created `active` in the settlement transaction and set to `pending_sync` only if the LMS call
    fails (as drawn), or it could stay `pending_sync` until the LMS confirms.
12. **Restored lines when `payment_review` is later accepted.** New. The expired order's lines are
    already back in the cart when finance accepts the late capture and the order becomes `paid`. The
    customer could then pay twice for the same items (for a course, RUL-13 blocks this at checkout).
    Proposal: removing the restored lines at acceptance, or at least warning the customer, has to be
    decided.
