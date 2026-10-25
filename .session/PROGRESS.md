# Review 08 — progress

Baseline main d7f8d4d (tree = 3239d10, PR #12 head; CI green, local gates green). F0: no change.
Owner decisions: A-3 → 423 auth.account_locked (no verify, no count); A-2 → never resend + `unknown` + StaffAlert;
A-5 → order.idempotency_key + stale claim (2 min) replays the order; A-7 → IPv6 /64, IPv4-mapped → IPv4.

| Id | Status | Commit | Test |
|---|---|---|---|
| A-1 | done | 4db5f6e | OutboxTimeoutTests (handler timeout → retried, batch continues; SMS timeout → failed send) |
| A-2 | done | (next) | NotificationOnceTests (unknown outcome → once + alert; refused → retried); migration 20261025174608_NotificationSendingStatus |
