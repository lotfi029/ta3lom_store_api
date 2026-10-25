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
| A-5 | done | b656db9 | CheckoutTests.A_claim_left_in_progress_after_the_order_committed_replays_that_order, …A_claim_left_in_progress_before_any_order_lets_the_retry_check_out; migration 20261025180041_OrderIdempotencyKey |
| L-12 | done | b656db9 | CheckoutTests.The_stored_replay_… (replay Location = original) |
| A-6 | done | 718c291 | RefreshTokenRaceTests.Revoking_all_tokens_during_a_rotation_also_revokes_the_successor |
| A-7 | done | 851614f | CustomerAuthTests.IPv6_addresses_of_one_64_prefix_share_the_per_IP_limit, ClientNetworkTests |
| L-10 | done | 851614f | OtpVolumeTests.Parallel_requests_crossing_half_the_daily_cap_alert_staff_once (was 10 alerts) |
| A-8 | done | cf4956a | StartupGuardTests: Production without Sms AppSid/SenderId, sink SMS, without SMTP password, sink e-mail → host stops |
| L-1 | done | cf4956a | StartupGuardTests: DEV ONLY key + "\n", another DEV ONLY key → host stops |
| L-2 | done | 08243d7 | LogRedactionTests (X-Forwarded-For / X-Real-IP masked) |
| L-3 | done | 08243d7 | OutboxHealthTests.A_dead_lettered_or_overdue_message_makes_the_outbox_degraded |
| L-4 | done | 08243d7 | JobRunTests (one scheduled fire runs once across instances; failed Result logged, no throw); migration 20261025182258_JobRunClaims |
| L-6 | done | 61848fd | MediaUploadTests (alt text of 301 characters → 400) |
| L-7 | done | 61848fd | CatalogConsistencyTests.Parallel_setting_updates_cannot_together_break_the_money_rules (5 rounds, one 422 each) |
| L-8 | done | (next) | CartTests.A_guest_cart_holds_… (2nd write re-issues t3s_cart, Max-Age 2592000) |
| L-9 | done | (next) | CartTests.A_cart_write_racing_the_account_deletion_leaves_no_cart_behind |
| L-11 | done | (next) | Domain CartTests.Adding_to_an_option_line_checks_the_total_with_the_line_at_its_new_price, …Merging_into_an_option_line_checks_the_total_at_the_donor_line_price |
