# Review 08 — progress

Baseline main d7f8d4d (tree = 3239d10, PR #12 head; CI green, local gates green). F0: no change.
Owner decisions: A-3 → 423 auth.account_locked (no verify, no count); A-2 → never resend + `unknown` + StaffAlert;
A-5 → order.idempotency_key + stale claim (2 min) replays the order; A-7 → IPv6 /64, IPv4-mapped → IPv4.

| Id | Status | Commit | Test |
|---|---|---|---|
| A-1 | done | 4db5f6e | OutboxTimeoutTests (handler timeout → retried, batch continues; SMS timeout → failed send) |
| A-2 | done | 913cb0f | NotificationOnceTests (unknown outcome → once + alert; refused → retried); migration 20261025174608_NotificationSendingStatus |
| A-3 | done | e65ac1e | StaffAuthTests.A_locked_account_cannot_change_its_password_even_with_the_right_one (423, hash unchanged, not counted, audited reason locked) |
| L-5 | done | e65ac1e | StaffAuthTests.Enrolling_an_authenticator_is_audited (staff.totp_enrolled) |
| A-4 | done | f802287 | Domain ProjectTests.An_option_claimed_by_id_is_not_reused_by_label_for_another_input, …Option_ids_in_one_replacement_are_unique; AdminCatalogTests duplicate ids → 400 |
| A-5 | done | (next) | CheckoutTests.A_claim_left_in_progress_after_the_order_committed_replays_that_order, …A_claim_left_in_progress_before_any_order_lets_the_retry_check_out; migration 20261025180041_OrderIdempotencyKey |
| L-12 | done | (next) | CheckoutTests.The_stored_replay_… (replay Location = original) |
