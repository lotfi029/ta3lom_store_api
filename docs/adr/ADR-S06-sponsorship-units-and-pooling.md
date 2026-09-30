# ADR-S06 — Model sponsorship as a unit ledger with a FIFO pool

- **Status:** Proposed
- **Date:** 2026-09-29
- **Deciders:** Lotfi (developer), association representative
- **Related:** BR-S03, BR-S06, BR-S08, RUL-20, RUL-43–50, RUL-57–58, FR-S25, FR-S51–S57, FR-S61

## Context

Donors sponsor seats in courses and copies of books for beneficiaries the association selects
(BR-S03). They may pay for N whole seats, or give any amount that is pooled with others until a
seat is funded. The association wants donors to see what happened to their seat: awaiting
allocation, allocated, completed (BR-S08, OBJ-4). Donors must never see who the beneficiary is
(PDPL, RUL-50). Zakat money may only fund zakat-eligible beneficiaries (RUL-20). Refunds must
reverse exactly what a gift produced (RUL-58). Finance wants every riyal traceable from payment to
allocation (BR-S11).

## Options considered

### Option A — Amount-only counters
Each offer keeps `raised_amount` and `funded_units = floor(raised / seat_price)`; allocation
decrements a counter.

Simplest. But a donor's gift cannot be linked to any seat, so "your seat is allocated" cannot be
shown; zakat cannot be traced to the beneficiary it funded; refunds cannot tell whether the money
already became an allocated seat; the 30/90-day allocation SLA (RUL-49) has nothing to measure.

### Option B — Donor chooses a named beneficiary
Each sponsorship is attached to a specific person the donor picks from a list.

Strong emotional pull, but it exposes beneficiary identities to donors (PDPL), contradicts BR-S03
("the donor never picks a person"), and leaves unpopular profiles unfunded. Rejected by the
business requirements.

### Option C — Unit ledger + FIFO pool + allocation register  ← chosen
Every funded seat or copy is a `sponsored_unit` row. Whole-unit lines create N units at
settlement (RUL-43). Partial amounts become `pool_contribution` rows; when the offer's
`sponsorship_pool.balance` reaches the seat price, a unit with `source = 'pooled'` is created and
`unit_funding` records which contributions paid for it and how much (RUL-45), oldest first. Staff
allocate the oldest awaiting units first (RUL-48) to approved beneficiaries in batches
(`allocation_batch`); every transition is a `sponsored_unit_event`.

## Decision

Adopt **Option C**, as implemented in the schema:

- `sponsored_unit` with status `awaiting_allocation → allocated → completed`, `lapsed` back to
  awaiting (RUL-50), `cancelled` on refund; `has_zakat` set if any zakat money funded it
- `sponsorship_pool` (counter) + `pool_contribution.remaining` (ledger), invariant
  `balance = Σ remaining` checked nightly
- `unit_funding` linking pooled units to their contributions
- `beneficiary` with encrypted PII; allocation validates RUL-47 (approved, group match, no live
  enrolment, zakat eligibility)
- `zakat_ledger` in on settlement, out on allocation for sponsorship zakat; cause zakat goes out by
  a finance **disbursement voucher** (`direction = 'out'`, `reference` = voucher number); the balance
  is never negative (RUL-20). Offers are not zakat-eligible in S1, so pools hold no zakat
- Pool remainder at campaign end swept to the general fund and recorded as a **`goal_adjustment`**
  pair (`pool_sweep_out` on the offer, `pool_sweep_in` on the fund, same `pair_id`);
  `goal_progress.raised_amount` includes adjustments (RUL-46). Zakat is never swept
- A beneficiary course seat requires a mobile (own or guardian's); the customer is found or created
  at allocation (RUL-47e, RUL-52). On lapse the enrolment is revoked and the beneficiary may be
  allocated again later (RUL-50)
- Donors see group, region and status only

Cause products (RUL-53) are **not** per-unit: their units are a display convenience and delivery is
reported by `cause_update`.

## Consequences

**Positive**
- A donor sees each seat they funded, or "pooled — X of 1 seat", and its status (FR-S25)
- Zakat is traceable from inflow to the beneficiary it funded
- Refunds reverse precisely: only units still `awaiting_allocation`, only the unconverted part of
  a contribution (RUL-57)
- The allocation backlog and SLA alerts are simple queries (`v_allocation_backlog`, `ix_unit_fifo`)
- Two officers allocating at once never collide (`FOR UPDATE SKIP LOCKED`)

**Negative**
- More tables and a more complex settlement transaction (database design §4.4–4.5)
- Counters and ledgers must be reconciled nightly (integrity check 6 includes `goal_adjustment`);
  a mismatch is an alert, not a silent drift
- Staff must keep the beneficiary register current, or units wait and alerts fire
- A seat funded by five donors reports the same status to all five, which some may find less
  personal

**Neutral**
- Units beyond the target are allowed and allocated as beneficiaries are approved (RUL-44)
- Book copies use `distribution` records instead of enrolments

## Revisit when

- Sponsorship becomes recurring (BR-S14): a monthly mandate funding one seat per month needs a
  mandate entity above the unit
- The association decides to let donors choose a beneficiary group or region (not a person) —
  that is an offer-level attribute, compatible with this model
- Volume exceeds ~100k units a year and nightly reconciliation becomes slow
