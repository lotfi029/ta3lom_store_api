# Review 08 — progress

Baseline main d7f8d4d (tree = 3239d10, PR #12 head; CI green, local gates green). F0: no change.
Owner decisions: A-3 → 423 auth.account_locked (no verify, no count); A-2 → never resend + `unknown` + StaffAlert;
A-5 → order.idempotency_key + stale claim (2 min) replays the order; A-7 → IPv6 /64, IPv4-mapped → IPv4.

| Id | Status | Commit | Test |
|---|---|---|---|
| A-1 | done | 4db5f6e | OutboxTimeoutTests (handler timeout → retried, batch continues; SMS timeout → failed send) |
| A-2 | done | 913cb0f | NotificationOnceTests (unknown outcome → once + alert; refused → retried); migration 20261025174608_NotificationSendingStatus |
| A-3 | done | (next) | StaffAuthTests.A_locked_account_cannot_change_its_password_even_with_the_right_one (423, hash unchanged, not counted, audited reason locked) |
| L-5 | done | (next) | StaffAuthTests.Enrolling_an_authenticator_is_audited (staff.totp_enrolled) |
