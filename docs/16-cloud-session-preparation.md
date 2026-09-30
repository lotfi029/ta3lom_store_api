# 16 — Preparing the Cloud Session (Payment Service)

**Revision:** 30 September 2026
**For:** the next working session, which builds the payment service ([15](15-payment-service-handoff.md)) in a **cloud** Claude session.

A cloud session runs in an Anthropic-hosted Linux container. It has no view of your computer
(`D:\Freelance\ELAMS`) unless the session is linked to it, and its internet access is limited to an
allowlist. I tested the current cloud environment on **30 Sep 2026**, and several things the payment
work needs are **blocked today**. Prepare the items below before the session starts.

---

## 1. What the cloud environment can and cannot do (tested)

| Need | Result today | Impact |
|---|---|---|
| GitHub (`github.com`) | ✅ reachable | Clone, push, open PRs |
| npm, PyPI | ✅ reachable | Tooling only |
| .NET SDK download (`dot.net`, `builds.dotnet.microsoft.com`) | ❌ blocked (proxy 403) | **Cannot install .NET 10 → cannot build or test** |
| NuGet (`api.nuget.org`, `www.nuget.org`) | ❌ blocked | **Cannot restore packages** |
| Microsoft apt repo (`packages.microsoft.com`) | ❌ blocked | No apt install of .NET either |
| Docker daemon / Docker Hub | ❌ no daemon, registry blocked | **Testcontainers integration tests cannot run** |
| PostgreSQL | ⚠️ PostgreSQL 16 client/server present; 18 not installable | OK for tests with the `uuidv7()` shim |
| Payment provider APIs (Moyasar, HyperPay, Tap) | ❌ blocked | No live sandbox calls from the session |
| Incoming webhooks | ❌ a cloud container has no public URL | The provider cannot call the session |

---

## 2. Preparation checklist

### A. Open the network (decides whether the session can compile at all)

An organisation owner opens **Admin settings → Capabilities → network access** in Claude, and adds
these domains to the allowlist (or allows all domains):

| Purpose | Domains |
|---|---|
| .NET SDK install | `dot.net`, `builds.dotnet.microsoft.com`, `ci.dot.net`, `dotnetcli.azureedge.net` |
| NuGet restore | `api.nuget.org`, `www.nuget.org`, `globalcdn.nuget.org` |
| PostgreSQL 18 (optional) | `apt.postgresql.org` |
| Payment sandbox (after Q-01) | the provider's API host, e.g. `api.moyasar.com` (Moyasar) — confirm the exact host with the provider |

The domain list is my best current knowledge. The first command of the session (§4) verifies each
one, and any still blocked will show as a 403.

**If the network cannot be opened:** the session can still write code, but builds and tests run in
**GitHub Actions**. The session pushes a branch, CI (`ci.yml` from plan 14 S-03) builds and tests, and
the session reads the results. This is slower but workable. It requires §2-B.

### B. Put the code and docs where the cloud can reach them

| Item | Status | Action |
|---|---|---|
| Project docs (claude.ai project `store/`) | README, 03, 14, 15, 16, store-prototype + the reading list of 15 §7 | Done |
| GitHub repo `ta3lom-store-api` | Not created yet | Create it under the **same GitHub owner** as the other repos (Hostinger rule, main README). Push `Docs/store/` and `database/store/` into it (e.g. `docs/` and `db/reference/`) so the session has everything in one clone |
| Repo access for the session | — | Private repo: connect GitHub to Claude (or give the session a fine-grained token scoped to that one repo, set in the environment, **never pasted into chat**) |
| Backend code the payment service plugs into | Not written yet (plan 14) | See §2-E |

### C. Close the decisions first (the payment session cannot make them)

| # | Decision | Owner |
|---|---|---|
| Q-01 | Payment provider, and one merchant account or two (sales vs donations) | Association finance |
| ADR-S09 | Module inside `ta3lom-store-api` (Option A, recommended) or separate cloud service (Option B), 15 §5 | Lotfi |
| Q-07 | Refund policy wording (RUL-57) | Association legal |
| — | Sandbox account opened with the chosen provider | Lotfi |

### D. Secrets — how the session gets them safely

- Sandbox API keys and webhook secrets are **not pasted into the chat**.
- Store them as **GitHub Actions secrets** (for CI) and in the staging `.env` on the VPS.
- The cloud session works with the `FakePaymentGateway` and **captured webhook fixtures**. It does not need real keys to write and test the adapter.
- To capture fixtures: create 3–4 sandbox payments (success, failure, refund) from the provider dashboard, or from the staging VPS. Save the webhook JSON bodies and signature headers into `tests/…/Fixtures/webhooks/` in the repo.
- Live keys are added only on the VPS at cutover, never in the repo or a session.

### E. Order of work — the payment service needs something to plug into

The payment service calls `IOrderSettlement` and implements `IPaymentInitiator`. Those come from the
backend plan (S-01, S-02, S-15, S-16). Pick one:

| Option | When | What the cloud session does first |
|---|---|---|
| **1 — backend first (recommended)** | Payment session starts after S-16 is merged | Clone, build, implement the payment module against the real contracts |
| 2 — contracts first | Payment session starts now | Create the S-01 skeleton plus **only** the contract types from 15 §2–§3 (`PaymentConfirmation`, `SettlementOutcome`, `IOrderSettlement`, `IPaymentInitiator`, `OrderPaymentRequest`, `PaymentStart`) with a fake `IOrderSettlement`, then build the payment module. S-16 later replaces the fake |

### F. Testing without Docker

Testcontainers cannot run in the cloud container. Either:
- add an environment switch in the integration-test fixture, `STORE_TEST_DB=external`, that uses a PostgreSQL started inside the container (16 with the `uuidv7()` shim, or 18 if `apt.postgresql.org` is allowed); **or**
- run integration tests only in GitHub Actions (which has Docker) and unit tests in the session.

### G. Webhooks in development

A cloud container cannot receive webhooks. Develop against the **fixtures** (replay tests). End-to-end
webhook tests happen on **staging** (`store-api-staging.ta3lom.sa`) after deploy, with the provider's
sandbox webhook pointed there.

---

## 3. Session kick-off prompt

Paste this as the first message of the cloud session:

```text
We are building the ta3lom store PAYMENT SERVICE. Read the claude.ai project docs in this order:
store/15-payment-service-handoff.md (scope and contracts), store/16-cloud-session-preparation.md,
store/03-business-rules.md (RUL-29, 32–38, 56–59), store/14-backend-implementation-plan.md §0, S-15, S-16,
then the reading list in 15 §7.
Decisions already made: provider = <PROVIDER>, deployment = <Option A | B>, merchant accounts = <1 | 2>.
Repository: <github URL>, branch from main as feat/payments.
First run the environment check in 16 §4 and tell me what is blocked before writing code.
Work test-first against FakePaymentGateway and the webhook fixtures in tests/…/Fixtures/webhooks/.
Never ask me to paste API keys into the chat.
```

---

## 4. Environment check (first command of the session)

```bash
for u in https://github.com https://builds.dotnet.microsoft.com https://api.nuget.org/v3/index.json \
         https://www.nuget.org https://apt.postgresql.org https://api.moyasar.com; do
  printf '%-45s ' "$u"; curl -s -o /dev/null -m 8 -w '%{http_code}\n' "$u" || echo blocked
done
which dotnet docker psql git
docker info >/dev/null 2>&1 && echo "docker: ok" || echo "docker: unavailable"
```

The session can build and test in the container only if the .NET and NuGet lines answer (any HTTP
code other than a blocked/000 result). Otherwise it uses the GitHub Actions route (§2-A).

---

## 5. Minimum to start

1. The network allowlist includes the .NET and NuGet domains, **or** you accept CI-only builds.
2. The GitHub repo `ta3lom-store-api` exists, with `docs/` and `db/reference/` pushed, and the session has access.
3. Q-01 and the Option A/B decision are made.
4. The backend has reached S-16, **or** you choose "contracts first" (§2-E, option 2).
5. Webhook fixtures are captured from the sandbox and committed (can be done in the session's first hour if the provider dashboard allows it).
