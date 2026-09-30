# ADR-S04 — Identify customers by mobile OTP; separate staff identity with TOTP

- **Status:** Proposed
- **Date:** 2026-09-29
- **Deciders:** Lotfi (developer), association representative
- **Related:** BR-S09, RUL-25, RUL-52, RUL-60–62, FR-S21, FR-S28, FR-S54, FR-S70–S71, main ADR-006, main FR-53

## Context

The main site has no visitor accounts; only editors log in (main ADR-006). The store needs
customer identity to hold purchased courses and books, giving history and receipts, and it
needs a verified mobile for SMS: order confirmations, dedication cards, shipment notices
(RUL-63). Beneficiaries receive an SMS invitation and must land in an account that shows their
sponsored course (RUL-52). Friction must be the lowest possible (BR-S09): many buyers and donors
are on mobile, one-off, and Arabic-speaking.

Staff operate money, refunds and beneficiary personal data. Their compromise is the highest-impact
threat (08 §6.2).

## Options considered

### Option A — Mobile number + SMS one-time code  ← chosen
Enter `05XXXXXXXX`, receive a 6-digit code, done. No password to forget.

Lowest friction, and verifies the mobile the store needs anyway. Costs an SMS per login; depends on
a registered sender ID; exposed to SIM-swap and SMS pumping fraud, which must be rate-limited.

### Option B — Email + password
Conventional, no per-login cost.

Adds registration, password reset, breached-password checks and a mail round-trip; the store would
still need to verify the mobile separately for SMS. Highest friction for one-off donors.

### Option C — Nafath (national SSO) or social login (Google, Apple)
Nafath gives a strong, verified national identity; social login is one tap for some users.

Nafath onboarding is heavy for a small non-profit and asks for more identity than a book purchase
needs; social logins do not yield a verified Saudi mobile and add third-party scripts and CSP
entries. Either could be added later as an extra way to sign in.

## Decision

**Customers** sign in with **mobile + SMS OTP** per RUL-60: 6-digit code, 5-minute validity,
5 attempts per code, 3 codes per mobile per 15 minutes, 10 per IP per hour, code stored as
HMAC-SHA256 with a server pepper (`otp_challenge.code_hash`). Only `+9665` numbers are accepted.

Tokens follow the **main-site ADR-006 pattern**, adapted for the store:

| | Customer | Staff |
|---|---|---|
| Audience | `store-customer` | `store-staff` |
| Access token | JWT, 15 min, in an Angular signal (never `localStorage`) | JWT, 10 min, in a signal |
| Refresh token | Rotating, 60 days, `refresh_token` row (SHA-256 hash, `family_id`), reuse revokes the family | Rotating, 8 h |
| Cookie | `t3s_rt`, HttpOnly, Secure, SameSite=Lax, `Path=/v1/auth`, host-only on `store-api.ta3lom.sa` | Same attributes under a separate name and path (`t3s_srt`, `Path=/v1/admin/auth`, as fixed in 11-api-contract) so a staff member who is also a customer in the same browser keeps two independent sessions |
| `refresh_token.subject_type` | `customer` | `staff` |

**Staff** are separate (`staff_user`): email + password (ASP.NET Core `PasswordHasher` v3) +
**mandatory TOTP** (Otp.NET, secret encrypted with Data Protection in `totp_secret_enc`), lockout
as main FR-53, roles per RUL-62 with **segregation of duties**: `admin` does not implicitly refund,
allocate or reveal beneficiary PII; those need `finance` / `allocation_officer` explicitly (roles
is an array, so one person may hold several, and granting them is audited). Staff auth endpoints
are `/v1/admin/auth/*`. The first admin is created by CLI and forced to enrol TOTP.

A course seat needs a beneficiary mobile (own or guardian's). **At allocation** the system finds or
creates the `customer` row for that mobile, creates the enrolment, sets `beneficiary.customer_id`
and sends the invitation SMS (RUL-47e, RUL-52). The invitation carries no credential; the first OTP
login simply signs in. A `blocked` customer cannot sign in or check out (RUL-61).

The OTP SMS is the one message sent directly rather than through the outbox, so the plain code is
never stored anywhere (RUL-63).

## Consequences

**Positive**
- One step to sign in; the checkout mobile is verified by construction (RUL-25)
- No password storage or reset flow for the public
- Same token hygiene as the main site; reviewers already know the pattern
- Staff access is protected by a second factor from day one

**Negative**
- SMS cost per login and dependency on the SMS provider and sender ID approval (started in Phase 0)
- SIM-swap takeover of a customer account is possible; impact is limited (no stored cards,
  refunds only by staff to the original payment method)
- SMS pumping risk requires per-mobile, per-IP and global daily caps and an alert
- Staff who also edit main-site content keep two separate logins

**Neutral**
- Customers without a Saudi mobile cannot sign in; in S1 the store is KSA-only (RUL-26)
- `t3s_rt` is host-only and never collides with the main site's refresh cookie

## Revisit when

- The association wants donors outside KSA (international numbers, or email sign-in)
- SMS spend on OTP exceeds a budget line the association sets
- Recurring giving (BR-S14, S2) needs stronger customer authentication for mandates — consider
  Nafath or passkeys then
