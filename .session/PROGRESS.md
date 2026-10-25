# R5-Core fixes — review 07: progress

Baseline: `main` 0de2bbb. Branch: session branch `claude/youthful-feynman-yr7h4h` restarted from `origin/main`
(its earlier PR #11 is merged) — **REVIEW** (the session is bound to this branch name; `fix/r5-core-review` not used).

## Items

| Id | Status | Commit | Regression test |
|---|---|---|---|
| F0 | done (no change) | — | CI on main green: run 118 (0de2bbb), run 117 (7c2e070) |
| C-1 | done | Fix C-1 | `SiteRebuildTests.A_newer_change_arriving_while_an_older_one_is_handled_still_rebuilds_once`, `…An_older_change_in_retry_backoff_is_absorbed_by_a_newer_due_one`, `…A_burst_of_five_changes_is_one_rebuild` |

## Decisions

- Owner review of the fix plan applied: P-1 … P-7 (each recorded at its item).
- C-1: absorb only older, unclaimed, pending rows; the "newer exists" check counts only pending, non-dead-lettered rows.
  The dispatcher now handles a claimed batch oldest first in PostgreSQL's `(occurred_at, uuid)` order (RETURNING has no
  order), so a burst claimed in one batch is still one rebuild.

## Migrations

(none yet)
