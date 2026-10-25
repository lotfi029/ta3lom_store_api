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
| L-5 | done | Fix L-5 | `MeTests.After_deletion_the_same_mobile_signs_up_as_a_new_donor_and_admin_search_finds_only_the_new_one` (F4), `MigrationTests.The_anonymised_mobile_migration_clears_placeholders_and_keeps_blocked_mobiles`, `MigrationTests.All_migrations_apply_on_an_empty_database_and_leave_no_pending_model_changes` (F4) |
| L-6 | done | Fix L-6, L-9 | `StaffAdminTests.An_admin_creates_lists_updates_and_resets_staff` (audit `staff.password_reset` / `staff.totp_reset`) |
| L-9 | done | Fix L-6, L-9 | `KeyRingGuardTests.An_empty_key_ring_path_stops_the_host`, `…An_unwritable_key_ring_path_stops_the_host`, `StaffAuthTests.A_TOTP_secret_that_cannot_be_decrypted_answers_503_alerts_staff_and_keeps_the_enrolment` |
| L-10 | done | Fix L-10 | `OutboxDispatcherTests.A_message_whose_last_claim_never_finished_is_dead_lettered_once_with_one_alert`, `…A_message_at_the_cap_is_never_claimed_again_while_a_lease_runs`, `…Every_claim_counts_an_attempt` |
| L-11 | done | Fix L-11, L-12 | `PublicCatalogTests.Search_filters_and_pages_in_one_query_with_a_correct_total` (250 projects, 2 categories, page 2/4, admin list too) |
| L-12 | done | Fix L-11, L-12 | `PublicCatalogTests.An_old_slug_does_not_redirect_into_an_inactive_category` |
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

- L-5 / P-4: migration `20261025153628_AnonymisedMobileNull`: drop `ux_customer_mobile` → `mobile DROP NOT NULL` →
  `UPDATE … SET mobile = NULL WHERE status = 'anonymised'` → filtered unique `ux_customer_mobile (mobile) WHERE mobile IS NOT
  NULL` → CHECK `ck_customer_mobile_present` (`status = 'anonymised' OR mobile IS NOT NULL`; blocked donors keep their
  mobile). The name `ck_customer_mobile` is already the E.164 format check, hence `_present`. Down restores placeholder
  mobiles for NULL rows and leaves the extensions alone. `Customer.Mobile` and the mobile in `MeResponse`,
  `AdminCustomerRow/Detail` (and the export) are nullable; `ReleaseMobile` and the placeholder mobile are gone.
- L-6: reset password / reset TOTP save and revoke sessions in one `ExecuteInTransactionAsync`; audited
  `staff.password_reset`, `staff.totp_reset` (the save interceptor's row audit stays as well).
- L-9: outside Development, `Store:DataProtection:KeysPath` must be set and writable (probe file) or the host stops.
  P-1: a TOTP secret that cannot be decrypted answers 503 `auth.totp_unavailable` (new code, ar/en), queues
  `StaffAlert("totp_key_lost", staffId)`, counts no failure and leaves the enrolment untouched; recovery = admin reset-totp.
  `ITotpService.VerifyStep` returns `TotpCheck(Step, KeyUnavailable)`.
- L-10 / P-3: `attempts` was already incremented in the claim UPDATE (kept); the claim now also requires
  `attempts < MaxAttempts`, and each dispatcher pass first dead-letters rows at the cap whose lease ran out (once each,
  one `outbox_dead_letter` staff alert per row, none for a staff alert itself).
- L-11: `ICatalogSearch.SearchPageAsync(CatalogSearchRequest)` applies status, category (slug or id), active-category and
  paging in the matching SQL and returns `(Ids, Total)`; no LIMIT before the filters. Public and admin listings use it
  (one extra count query only when the page is past the end).
- **REVIEW** L-11 (owner note): the listing without `q` orders by `sort, created_at, id` (was `sort, published_at desc`);
  search results order by rank, similarity, then `sort, created_at, id`.
- L-12: an old slug redirects only to a published project whose category is active; otherwise 404.
- L-3: RequestOtp takes the advisory lock on `hashtextextended('otp-ip:' || ipHash)` before the mobile lock (fixed order).
- L-4: `ICartRepository.GetOrCreateForCustomerForUpdateAsync`: `INSERT … ON CONFLICT (customer_id) WHERE customer_id IS
  NOT NULL DO NOTHING` (the predicate of `ux_cart_customer`), then lock + load; used by cart writes and the sign-in merge.

## Migrations

- `20261025153628_AnonymisedMobileNull` (L-5)
