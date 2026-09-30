# ADR-S05 — Take payments on a hosted page; the webhook is the only proof

- **Status:** Proposed
- **Date:** 2026-09-29
- **Deciders:** Lotfi (developer), association representative
- **Related:** Q-01, RUL-29, RUL-32–38, RUL-56–58, FR-S33–S39, FR-S64, main FR-42/43, ADR-S08

## Context

The store takes one payment per order for purchases and gifts together (BR-S07), with mada,
Apple Pay, Visa/Mastercard and STC Pay (RUL-36). The association is a non-profit with no card
security team; PCI DSS scope must stay minimal. The main site already set the rule that a return
URL is never proof of payment (main FR-43).

The payment provider and merchant agreement are not yet chosen (Q-01), including whether sales
and gifts go through one merchant account or two. Development cannot wait for that decision.
S2 adds recurring giving (BR-S14), which needs card tokens, and possibly BNPL for purchase-only
carts (FR-S39).

## Options considered

### Option A — Direct card API (card fields on our page, server-to-server charge)
Full control of the checkout look.

Card data passes through our page or servers: PCI SAQ D or A-EP, quarterly scans, a card security
programme the association cannot run. Rejected.

### Option B — Embedded gateway SDK (card fields rendered by gateway JavaScript in our page)
Smooth in-page experience; some gateways show Apple Pay and mada in the same widget.

Card data goes browser → gateway, but the fields live in our DOM, which places the merchant in
**SAQ A-EP**; it also requires third-party `script-src`, `frame-src` and `connect-src` entries in a
CSP that is otherwise hash-only (ta3lom-web CLAUDE.md, rule 15).

### Option C — Hosted payment page by redirect (or gateway-served iframe), webhook-authoritative  ← chosen
The API creates a payment for the order and returns the gateway's hosted-page URL; the browser
leaves ta3lom.sa, pays, and returns to `/store/orders/{ref}/thanks`, which polls status. The order
becomes `paid` only from a verified webhook or the reconciliation job.

## Decision

1. **Hosted payment page by full redirect** is the default; a gateway-served iframe is acceptable.
   A DOM-embedded SDK is not used in S1. PCI scope: **SAQ-A** (08 §6.4). RUL-32.
2. **The webhook is the truth** (RUL-33): an order becomes `paid` only when a signature-verified
   webhook confirms capture, or the reconciliation job reads a captured status from the gateway
   API (RUL-38). The webhook handler re-fetches the payment from the gateway before settling.
   Events are idempotent on `payment_event (provider, provider_event_id)` (RUL-34). A captured
   amount different from `orders.grand_total`, or a capture that arrives after the order was
   `expired` or `cancelled`, goes to `payment_review`; finance accepts it (→ `paid`, full
   settlement) or refunds it (RUL-29, RUL-35). Reconciliation runs every 5 minutes for payments
   `initiated` more than 5 minutes (RUL-38).
3. **Adapter**: the application depends on `IPaymentGateway` (create hosted payment, get status,
   refund, verify webhook, parse event). One implementation per provider in
   `Ta3lom.Store.Infrastructure/Payments/<Provider>`; a `FakePaymentGateway` for local and CI.
   `payment.provider` records which adapter took each payment.
4. **Provider: pending Q-01.** Recommendation: **Moyasar**, with HyperPay and Tap as alternatives.
   The comparison below is indicative; every row must be confirmed in writing with the provider
   during Phase 0.

| Criterion | Moyasar | HyperPay | Tap | PayTabs |
|---|---|---|---|---|
| mada | Yes | Yes (often a separate entity/channel id) | Yes | Yes |
| Apple Pay | Yes | Yes | Yes | Yes |
| STC Pay | Yes | Yes | Yes | Yes |
| Hosted page by redirect | Yes (payment page / invoice link); JS form also available | Primarily COPYandPAY widget (script in our page) — redirect option to confirm | Yes (hosted checkout / charge redirect) | Yes (hosted payment page) |
| Webhooks and verification | Yes; shared secret token in payload — mechanism to confirm | Yes; encrypted payload (AES-GCM) — to confirm | Yes; hash-string header — to confirm | Yes (IPN/callback with signature) — to confirm |
| Refunds API (full and partial) | Yes | Yes | Yes | Yes |
| Recurring / card tokens (S2) | Yes (tokenisation) | Yes (registration tokens) | Yes (saved cards) | Yes (tokenisation) |
| Sandbox self-service | Yes | On request | Yes | Yes |
| Non-profit onboarding and documents | To confirm | To confirm | To confirm | To confirm |
| Fees (mada / cards / STC Pay / setup) | To confirm | To confirm | To confirm | To confirm |
| Settlement period and payout | To confirm | To confirm | To confirm | To confirm |

Selection criteria, in order: non-profit onboarding accepted for both sales and donations;
redirect hosted page with all four methods; signed webhooks and a status API; refunds API;
fees; tokenisation for S2.

## Consequences

**Positive**
- Card data never touches ta3lom.sa or the store API; SAQ-A
- Closing the browser after paying still results in `paid` (webhook or reconciliation)
- A provider change, or a second merchant account for gifts (Q-01), is an adapter and
  configuration change, not a rewrite
- CSP keeps its hash-only `script-src`; no gateway script on our origin

**Negative**
- The customer leaves ta3lom.sa to pay; the gateway's page styling is limited
- The thanks page must handle "still pending" gracefully (polling, then a clear message)
- Reconciliation, expiry and payment-review flows are extra work that a simple return-URL design
  would skip (FR-S37, FR-S38)
- Each provider's webhook scheme differs; the adapter must be tested per provider

**Neutral**
- Apple Pay domain verification is handled on the gateway's domain for a redirect flow
- BNPL (FR-S39, S2) would be a second adapter, used only for purchase-only carts (RUL-36)

## Revisit when

- Q-01 is closed — update this record with the chosen provider and verified rows of the table
- The association wants in-page Apple Pay or card fields and accepts SAQ A-EP
- Recurring giving (BR-S14) is scheduled — confirm the tokenisation flow keeps SAQ-A
