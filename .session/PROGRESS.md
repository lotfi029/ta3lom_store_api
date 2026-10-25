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

## Migrations

(none yet)
