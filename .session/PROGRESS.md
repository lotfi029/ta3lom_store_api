# Review 08 — progress

Baseline main d7f8d4d (tree = 3239d10, PR #12 head; CI green, local gates green). F0: no change.
Owner decisions: A-3 → 423 auth.account_locked (no verify, no count); A-2 → never resend + `unknown` + StaffAlert;
A-5 → order.idempotency_key + stale claim (2 min) replays the order; A-7 → IPv6 /64, IPv4-mapped → IPv4.

| Id | Status | Commit | Test |
|---|---|---|---|
