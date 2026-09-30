# 15 — Payment Service Hand-off

**Revision:** S1 draft · 30 September 2026
**Audience:** the next working session (payment service), which will run in a **cloud** session.
It may have no access to the local `D:\Freelance\ELAMS` folder, so this document is self-contained
and is also stored in the claude.ai project under `store/`.

**Context in one paragraph.** ta3lom.sa (E-Learning Association, Riyadh) is adding a store for
courses, books, seat sponsorships and donation products. The store API is a .NET 10 modular
monolith (`ta3lom-store-api`, PostgreSQL 18, EF Core 10, Minimal APIs) hosted on a Hostinger VPS.
The backend plan ([14](14-backend-implementation-plan.md)) builds everything **except** payment
processing. That is this service. The backend stops at an order in `pending_payment`, and resumes
when the payment service confirms payment through the contract below.

---

## 1. What the payment service owns

| Area | Detail | Rules / FR |
|---|---|---|
| Gateway | Port `IPaymentGateway` + adapter for the provider chosen in Q-01 (Moyasar recommended; HyperPay / Tap / PayTabs alternatives, ADR-S05). Hosted payment page only; card data never touches our servers | RUL-32, RUL-36, FR-S33 |
| Starting a payment | Implements `IPaymentInitiator` (§3), called by checkout after the order is committed | FR-S33 |
| Webhooks | `POST /v1/payments/webhooks/{provider}`: signature verification, `payment_event` inbox with a unique `(provider, provider_event_id)`, 401 on a bad signature | RUL-33, RUL-34, FR-S34 |
| Payment records | `payment` rows and their state machine (`initiated → authorized → captured / failed / voided / refunded / partially_refunded`) | RUL-37 |
| Confirming orders | Calls `IOrderSettlement.SettleAsync` **only** on a verified capture (webhook or reconciliation) | RUL-33 |
| Failed payments | Reports failure so the order becomes `payment_failed` (§2.3) | RUL-29, RUL-37 |
| Retry | `POST /v1/orders/{ref}/retry-payment` (#43) within the order's `payment_due_at` | RUL-37, FR-S36 |
| Reconciliation | Job every 5 min: payments `initiated` for more than 5 min, until `payment_due_at` + 24 h, → query the gateway → settle missed captures | RUL-38, FR-S37 |
| Payment review | Resolving orders parked in `payment_review` by settlement (amount mismatch, late capture): accept → settle; or refund. Endpoint #89 | RUL-35, FR-S38 |
| Refunds | Eligibility (#90, the RUL-57 matrix), execution (#91, `Idempotency-Key`, `staff:refund`), list (#92), reversal effects (RUL-58), credit note and cancellation receipt, `refunds` report | RUL-56–58, FR-S64 |
| BNPL | Tabby/Tamara for purchase-only carts (S2) | RUL-36, FR-S39 |

**Endpoints (numbers from 11-api-contract §12):** #43, #44, #89, #90, #91, #92.

---

## 2. Contract the backend provides — `IOrderSettlement`

Built in backend session S-16 (plan 14). Lives in `Ta3lom.Store.Application/Checkout/Settlement/`.

### 2.1 Types

```csharp
namespace Ta3lom.Store.Application.Checkout.Settlement;

/// Raised by the payment service after a VERIFIED capture (webhook or reconciliation). Never from a return URL.
public sealed record PaymentConfirmation(
    Guid     OrderId,
    string   ConfirmationId,      // unique per capture, e.g. "{provider}:{provider_event_id}" or "recon:{providerPaymentId}:captured"
    decimal  CapturedAmount,      // SAR, 2 dp — compared with orders.grand_total (RUL-35)
    string   Provider,            // "moyasar" | "hyperpay" | …
    string   ProviderPaymentId,
    string   Method,              // "mada" | "applepay" | "card" | "stcpay" | "bnpl"
    DateTimeOffset CapturedAt);

public enum SettlementOutcome { Paid, AlreadyPaid, PaymentReview, NotFound }

public interface IOrderSettlement
{
    /// Runs the settlement transaction (database/01 §4.4 without the inbox step). Idempotent:
    /// a second call for an already-paid order returns AlreadyPaid and changes nothing.
    Task<Result<SettlementOutcome>> SettleAsync(PaymentConfirmation confirmation, CancellationToken ct);
}
```

### 2.2 Guarantees

| Guarantee | How |
|---|---|
| Exactly once | `SELECT … FROM orders WHERE id = @id FOR UPDATE`; a status already in `paid / fulfilled / partially_refunded / refunded` → `AlreadyPaid`, no effects |
| Amount safety | `CapturedAmount ≠ grand_total` → `payment_review` + `FinanceAlert`, no effects (RUL-35) |
| Late capture | Order `expired` or `cancelled` → `payment_review` (RUL-35) |
| Atomic effects | Every paid-order effect (RUL-39) commits in the same transaction as the status change; notifications and PDFs go through the outbox after commit |
| Single path | An architecture test proves `IOrderSettlement` is the only code that can set `orders.status = 'paid'` |
| Retry-safe | Npgsql execution strategy replays the whole unit on a serialization failure or deadlock |

### 2.3 Other order hooks the payment service will need

These are **not yet built** by the backend plan. Add them in the payment session (small, in the Checkout module):

| Hook | Effect | Rule |
|---|---|---|
| `IOrderPayments.MarkPaymentFailedAsync(orderId, reason)` | `pending_payment → payment_failed` (retry still allowed until `payment_due_at`) | RUL-29, RUL-37 |
| `IOrderPayments.AcceptReviewAsync(orderId, confirmation)` | `payment_review → paid` and runs the same settlement effects | RUL-35 |
| `IOrderRefunds.ApplyAsync(RefundApplied)` | RUL-58 reversals in one transaction: goal counters; units `awaiting_allocation → cancelled`; the unconverted pool remainder; zakat `refund`; enrolment `revoked`; download grant revoked; shipping refunded when every print line is cancelled before shipment; order `partially_refunded / refunded` (also from `fulfilled`); credit note / cancellation receipt via the outbox | RUL-57, RUL-58 |

### 2.4 Test trigger (backend, Development/Staging only)

`POST /dev/orders/{ref}/confirm-payment` `{ "amount"?: "350.00", "confirmationId"?: "…" }` calls
`IOrderSettlement`. It lets the backend be tested without a gateway. The payment service must keep
it working and must never enable it in Production.

---

## 3. Contract the payment service implements — `IPaymentInitiator`

Called by `POST /v1/checkout` (#41) **after** the order transaction commits. The backend ships
`NullPaymentInitiator` (no redirect) until this exists.

```csharp
public sealed record OrderPaymentRequest(
    Guid OrderId, string Reference, decimal Amount, string Currency /* "SAR" */,
    string CustomerName, string CustomerMobileE164, string CustomerEmail, string Locale /* ar|en */,
    string ReturnUrl,            // https://ta3lom.sa/{locale}/store/orders/{ref}/thanks?t={statusToken}
    bool   HasGiftLines,         // BNPL must be refused when true (RUL-36)
    DateTimeOffset PaymentDueAt);

public sealed record PaymentStart(string? RedirectUrl, string? SdkPayload, DateTimeOffset ExpiresAt);

public interface IPaymentInitiator
{
    Task<Result<PaymentStart>> StartAsync(OrderPaymentRequest request, CancellationToken ct);
}
```

The checkout response (11 §6.2) carries `payment.redirectUrl` / `payment.sdkPayload` from `PaymentStart`.
The thanks page polls `GET /v1/orders/{ref}/status` (#42, backend). It **never** treats the return URL as proof of payment.

---

## 4. Data the payment service owns (tables already in the schema)

| Table | Use |
|---|---|
| `payment` | One row per attempt; `UNIQUE (provider, provider_payment_id)`; partial index `ix_payment_reconcile` for the reconciliation job |
| `payment_event` | Webhook inbox; `UNIQUE (provider, provider_event_id)`; the payload is scrubbed after 18 months (retention job) |
| `refund`, `refund_line` | Refunds by finance through the gateway |
| `invoice` (`kind = 'credit_note'`), `donation_receipt` (`kind = 'cancellation'`) | Documents for refunds; numbers from `document_counter` (`CN`, `DRC`) |
| `idempotency_key` | Already used by checkout; re-use it for refunds |

**Reads:** `orders` (status, `grand_total`, `payment_due_at`, contact), `order_line` (for refund eligibility).
**Writes to backend-owned tables** go only through the hooks in §2, never directly.

If Option A is chosen (§5), `payment_event` can be inserted in the **same transaction** as
settlement, exactly as database/01 §4.4 describes. With Option B, settlement idempotency relies on
the order lock and status check (§2.2).

---

## 5. Deployment decision to make first

| Option | How | Pros | Cons |
|---|---|---|---|
| **A — module inside `ta3lom-store-api`** (recommended by ADR-S08) | `Payments/` module in the same solution, in-process calls to `IOrderSettlement` | One deploy, one transaction for inbox + settlement, no service auth | Same release cycle as the store |
| **B — separate cloud service** | Its own deploy; calls the store through an internal endpoint `POST /internal/v1/orders/{id}/settle` (service-to-service auth: mTLS or signed client credentials) | Isolated PCI-adjacent surface, independent scaling | Two deploys, network failure modes, a second idempotency layer, extra hosting cost |

Record the choice as **ADR-S09**. If B, add the internal endpoint and its auth to 11 and to the backend plan.

---

## 6. Launch gate (payment service)

- [ ] Q-01 closed: provider contract, live keys, merchant account(s) (one or two, for sales vs gifts).
- [ ] The webhook URL is registered; a live low-value payment is settled through `IOrderSettlement`; replaying its webhook changes nothing.
- [ ] Reconciliation settles a payment whose webhook was blocked, within 10 minutes.
- [ ] Refund of that payment succeeds; the RUL-58 reversals leave the nightly integrity checks at 0 rows.
- [ ] Payment review: an amount-mismatch test order is resolved both ways on staging.
- [ ] BNPL is refused for carts with gifts (if BNPL is enabled).
- [ ] PCI scope confirmed as SAQ-A (hosted page only); no PAN or CVV in logs, DB or traces.
- [ ] `/dev/orders/{ref}/confirm-payment` is absent in Production.

---

## 7. Reading list for the payment session

| Need | Document (project `store/` or `Docs/store/`) |
|---|---|
| Rules | 03-business-rules.md: RUL-29, RUL-32 … RUL-38, RUL-56 … RUL-59 |
| Settlement transaction | database/01-database-design.md §4.4 (settlement), §4.7 (refund) |
| Gateway port and adapter sketch | 09-backend-dotnet.md §4.4, §5.3, §5.4 (refund policy), §5.12 |
| Endpoints | 11-api-contract.md §6.2–§6.4, §7, §9.9 (#43, #44, #89–#92) |
| Decision record | adr/ADR-S05-hosted-payments-webhook-truth.md |
| Flows | 07-process-flows.md (e) payment/settlement, (j) refund |
| Backend plan boundary | 14-backend-implementation-plan.md §0, S-15, S-16 |

## 8. Open questions for the payment session

1. **Q-01:** provider, fees and onboarding for a licensed non-profit. One merchant account, or separate ones for sales and donations?
2. **Deployment:** Option A or B (§5).
3. **Q-07:** final refund policy wording (RUL-57 is the proposal).
4. Whether donations need a different settlement descriptor or bank account from sales (finance).
