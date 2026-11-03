# PROGRESS — ta3lom store API

## Final session (plan 01: F2 … F4, branch `final/api-payments-hosting`)

| Phase | Commit | Tests | Status |
|---|---|---|---|
| Baseline (`main` e5435c1) | — | build, format, 590 tests on postgres:18 (Testcontainers; `STORE_TEST_DB` unset) | green |
| F2a — schema and domain | F2a — schema and domain | 624 (domain 137) | done |
| F2b — gateway port and adapters | F2b — gateway port and adapters | 663 (+ 39 payments, 1 sandbox skipped) | done |
| F2c — checkout, webhook, settlement | — | — | pending |
| F2d — jobs and status | — | — | pending |
| F3a — receipts | — | — | pending |
| F3b — refunds and review | — | — | pending |
| F3c — finance views and export | — | — | pending |
| F4 — hosting | — | — | pending |

### Resume notes
- Docker Hub answers 429 in the cloud session: `docker pull mirror.gcr.io/library/postgres:18 && docker tag … postgres:18`.
- Tests: `env -u STORE_TEST_DB -u STORE_TEST_DB_CONNECTION dotnet test --solution Ta3lom.Store.slnx` (the session sets an
  external-DB variable; the gate is Testcontainers postgres:18).
