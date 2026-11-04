# PROGRESS — ta3lom store API

## Final session (plan 01: F2 … F4, branch `final/api-payments-hosting`)

| Phase | Commit | Tests | Status |
|---|---|---|---|
| Baseline (`main` e5435c1) | — | build, format, 590 tests on postgres:18 (Testcontainers; `STORE_TEST_DB` unset) | green |
| F2a — schema and domain | F2a — schema and domain | 624 (domain 137) | done |
| F2b — gateway port and adapters | F2b — gateway port and adapters | 663 (+ 39 payments, 1 sandbox skipped) | done |
| F2c — checkout, webhook, settlement | F2c — checkout, webhook, settlement | 677 (+ 14 settlement) | done |
| F2d — jobs and status | F2d — jobs and status | full suite (+ 9: jobs, concurrency, expiry, late capture, status, dev routes) | done |
| F3a — receipts | F3a — receipts | 706 (+ 15 PDF/words, 5 receipt flows) | done |
| F3b — refunds and review | F3b — refunds and review | + 7 refund/review, policy matrix rows | done |
| F3c — finance views and export | F3c — finance views and export | + 11 (detail, resend, dashboard, CSV) | done |
| F4 — hosting | F4 — hosting | + CORS, forwarded-headers binding; image self-check; compose config ×2; restore test on a real dump | done |

### Resume notes
- Docker Hub answers 429 in the cloud session: `docker pull mirror.gcr.io/library/postgres:18 && docker tag … postgres:18`.
- Tests: `env -u STORE_TEST_DB -u STORE_TEST_DB_CONNECTION dotnet test --solution Ta3lom.Store.slnx` (the session sets an
  external-DB variable; the gate is Testcontainers postgres:18).
