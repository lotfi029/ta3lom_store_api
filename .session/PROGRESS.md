# R5-Core fixes — review 07: progress

Baseline: `main` 0de2bbb. Branch: session branch `claude/youthful-feynman-yr7h4h` restarted from `origin/main`
(its earlier PR #11 is merged) — **REVIEW** (the session is bound to this branch name; `fix/r5-core-review` not used).

## Items

| Id | Status | Commit | Regression test |
|---|---|---|---|
| F0 | done (no change) | — | CI on main green: run 118 (0de2bbb), run 117 (7c2e070) |
| A-1 | done | Fix A-1, A-2, L-7 | `StaffAuthTests.Parallel_wrong_passwords_are_all_counted_and_lock_the_account`, `…Parallel_wrong_TOTP_codes_are_all_counted_and_lock_the_account`, Domain `Failures_while_locked_count_without_extending_and_the_window_restarts_after_the_lock` |
| A-2 | done | Fix A-1, A-2, L-7 | `StaffAuthTests.Five_wrong_passwords_lock_and_a_locked_account_does_not_reveal_whether_the_password_is_right`, `…After_the_lock_runs_out_the_right_password_gets_an_MFA_token` |
| L-7 | done | Fix A-1, A-2, L-7 | `StaffAuthTests.Changing_the_password_issues_a_clean_session_and_revokes_the_old_family` (wrong current password counted + audited) |
| A-3 | done | Fix A-3 | `CartTests.Arabic_digits_are_accepted_and_malformed_amounts_are_400_never_500`, `AdminCatalogTests.Admin_money_fields_accept_Arabic_digits_and_refuse_malformed_ones`, `Application.Tests MoneyInputTests` |
| A-4 | done | Fix A-4 | `SearchPathGuardTests.A_search_path_without_public_stops_the_host`, `StaffAuthTests.The_email_is_matched_case_insensitively_with_the_store_public_search_path` |
| A-5 | done | Fix A-5 | `ImageOrientationTests.The_orientation_is_applied_before_the_image_is_stored` (origins 1, 3, 6, 8) |
| L-1 | done | Fix L-1, L-2 | `CheckoutTests.A_refused_checkout_releases_its_key_so_the_same_key_works_after_fixing_the_cart`, `…The_stored_replay_holds_no_status_token_and_the_replay_still_returns_a_working_one` |
| L-2 | done | Fix L-1, L-2 | `IdempotencyStoreTests.Claims_racing_releases_never_throw`, `CheckoutTests.Parallel_requests_with_one_key_never_fail_with_500` |
| L-3 | done | Fix L-3, L-4 | `CustomerAuthTests.Parallel_requests_for_different_mobiles_from_one_IP_never_exceed_ten_per_hour` |
| L-4 | done | Fix L-3, L-4 | `CartTests.Parallel_first_writes_of_a_donor_end_up_in_one_cart`, `CartTests.Parallel_adds_of_the_same_option_are_one_line_with_the_quantities_summed` (F4) |
| C-1 | done | Fix C-1 | `SiteRebuildTests.A_newer_change_arriving_while_an_older_one_is_handled_still_rebuilds_once`, `…An_older_change_in_retry_backoff_is_absorbed_by_a_newer_due_one`, `…A_burst_of_five_changes_is_one_rebuild` |

## Decisions

- Owner review of the fix plan applied: P-1 … P-7 (each recorded at its item).
- C-1: absorb only older, unclaimed, pending rows; the "newer exists" check counts only pending, non-dead-lettered rows.
  The dispatcher now handles a claimed batch oldest first in PostgreSQL's `(occurred_at, uuid)` order (RETURNING has no
  order), so a burst claimed in one batch is still one rebuild.

- A-1: the failure rule stays in `StaffUser.RecordFailure` (now returns `FailureOutcome`); callers lock the row
  (`IStaffUserRepository.LockForUpdateAsync`: `FOR UPDATE` + reload) inside `ExecuteInTransactionAsync(commitOnFailure)`.
  The password hash is verified outside the transaction. TOTP verify runs entirely on the locked row (failures and the
  time-step replay guard serialise).
- **REVIEW** A-1: a failure while locked is counted but does not extend the lock; the count is kept at lock time (no
  reset), so after 6 parallel wrong passwords `failed_count = 6`. P-5: the first failure after `locked_until` restarts
  the window; a successful sign-in resets both fields.
- **REVIEW** A-2: login never answers 423 now; `auth.account_locked` (423) stays for the TOTP enrol/verify steps, whose
  caller already proved the password with the MFA token. During a lock every login attempt is audited
  `staff.login_failed {reason: "locked"}`; only wrong passwords are counted.
- L-7: a wrong current password in `POST /v1/admin/auth/password` is a counted failure, audited `staff.password_change_failed`.

- A-3: one helper `Application/Abstractions/Wire/MoneyInput` (moved from AdminCatalog) used by cart add/update, project
  min amount, option amounts and money settings; settings are stored canonical (`"10.00"`). P-6: `,` and U+066C are
  rejected, never stripped. Quantities are JSON integers (never parsed from strings), so no change there.
- **REVIEW** A-3: money settings now also accept `"10"` / `"10.5"` (stored as `"10.00"` / `"10.50"`); before they had to
  have exactly two decimals.

- A-4: README uses `Search Path=store,public`; `DatabaseSearchPathValidator` refuses a search path without `public`
  outside Development (warning in Development); no search path at all is accepted. The test databases already use
  `store,public`.

- A-5: the processor draws the decoded bitmap upright for its `EncodedOrigin` (all 8 cases, width/height swapped for the
  90° ones) before resizing; the WebP output carries no EXIF. The test builds the JPEG itself and splices an APP1
  Orientation tag (no binary fixture).

- L-1 / P-2: only 2xx responses are stored; every other outcome (business 4xx and 409, 5xx) releases the key.
  `idempotency.in_progress` comes from the claim state only.
- **REVIEW** L-1: the stored checkout replay has `statusToken: ""`; on replay a per-endpoint shaper re-derives it from the
  reference (`IOrderStatusTokens`, deterministic HMAC). So the replay still returns a working token and no token is kept in
  `idempotency_key`.
- L-2: the claim reads with `SingleOrDefaultAsync` and retries the insert once when the conflicting row disappeared
  meanwhile; still nothing → 409 in progress, never 500.

- L-3: RequestOtp takes the advisory lock on `hashtextextended('otp-ip:' || ipHash)` before the mobile lock (fixed order).
- L-4: `ICartRepository.GetOrCreateForCustomerForUpdateAsync`: `INSERT … ON CONFLICT (customer_id) WHERE customer_id IS
  NOT NULL DO NOTHING` (the predicate of `ux_cart_customer`), then lock + load; used by cart writes and the sign-in merge.

## Migrations

(none yet)
