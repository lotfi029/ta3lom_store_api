# Cloud Session Start — files, environment, start prompt

**Date:** 30 September 2026 · **Plan:** [Cloud-build-plan.md](Cloud-build-plan.md) · **Repo:** `lotfi029/ta3lom_store_api`

The cloud session reads only what's in the GitHub repo. **Use a fresh clone for this (see [Fix-PR-7.md](Fix-PR-7.md)); PR #7 was made from an out-of-date copy.** Checked today on `main` (`26a0c15`), before the P1 docs PR.

---

## 1. Files that must be in the repo (`main`) before you start

### 1.1 Missing: add these first

| File in repo | Copy from (your PC) | Why |
|---|---|---|
| `store/sessions/Cloud-build-plan.md` | `D:\Freelance\ELAMS\Docs\store\sessions\Cloud-build-plan.md` | The run rules, order, defaults and prompts |
| `store/sessions/Architecture-alignment-DMS.md` | `D:\Freelance\ELAMS\Docs\store\sessions\Architecture-alignment-DMS.md` | DMS mapping + store (K) exceptions, used by S-02b |
| `store/sessions/Cloud-session-start.md` | this file | Reference for resumes |

```powershell
cd D:\Freelance\ELAMS\repos
git clone https://github.com/lotfi029/ta3lom_store_api.git StoreApi-new
cd StoreApi-new
git checkout -b docs/cloud-build-plan-v2
$d = "D:\Freelance\ELAMS\Docs\store\sessions"
Copy-Item "$d\Cloud-build-plan.md","$d\Architecture-alignment-DMS.md","$d\Cloud-session-start.md" store\sessions\
git add store/sessions/Cloud-build-plan.md store/sessions/Architecture-alignment-DMS.md store/sessions/Cloud-session-start.md
git diff --cached --stat  # exactly 3 files, only additions
git commit -m "Add cloud build plan, DMS alignment and start guide"
git push -u origin docs/cloud-build-plan-v2
```

Merge the PR on GitHub. Then `store/sessions/` on `main` must show 4 files (`S-01-plan-review.md` + the 3 new ones). `store/14-…` is not replaced: Cloud-build-plan.md already overrides plan 14's working method.

### 1.2 Already there (checked ✅), must stay

| Group | Files |
|---|---|
| Rules | `CLAUDE.md`, `README.md` |
| Build | `Ta3lom.Store.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitattributes`, `.gitignore`, `BannedSymbols.txt`, `BannedSymbols.Money.txt`, `.config/dotnet-tools.json` |
| Docs | `store/01`…`store/17`, `store/README.md`, `store/store-prototype.md`, `store/adr/` (ADR-S01…S08 + README), `store/database/01-03`, `store/sessions/S-01-plan-review.md` |
| Database | `db/reference/001_store_schema.sql`, `002_store_seed.sql`, `003_integrity_checks.sql`, `generated/001_store_schema.pg_dump.sql`, `tests/smoke_scenario.sql`, `tools/gen_dictionary_step1.py`, `step2.py` |
| Code | `src/Ta3lom.Store.{Api, Application, Contracts, Domain, Infrastructure}` (2 migrations: `InitialSchema`, `GrantPaymentEventColumnUpdate`) |
| Tests | `tests/Ta3lom.Store.{Domain.Tests, Application.Tests, Api.IntegrationTests, ArchitectureTests, Testing}` |
| Tools | `tools/scripts/` (`migrate-script`, `regenerate-data-dictionary`, `regenerate-schema-sql`, `_scratch-db`), `deploy/env/store.env.example`, `openapi/v1.json` |

### 1.3 Created by the session during the run (don't add them yourself)

`PROGRESS.md`, `DECISIONS.md`, `tools/smoke/S-nn.http`, `.github/workflows/ci.yml` (or `tools/ci/ci.yml`), `store/sessions/Part-1-report.md`, `src/Ta3lom.Store.SharedKernel/` (S-02b).

---

## 2. Cloud environment variables

Set these in the cloud environment's **Environment variables** field (`KEY=value`, one per line). None of them is a secret: they point at a throw-away PostgreSQL inside the container, and the app's other dev keys are already in `appsettings.Development.json`.

```env
ASPNETCORE_ENVIRONMENT=Development
DOTNET_NOLOGO=1
DOTNET_CLI_TELEMETRY_OPTOUT=1
DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
STORE_TEST_DB=external
STORE_TEST_DB_CONNECTION=Host=localhost;Port=5432;Username=postgres;Password=postgres
ConnectionStrings__Store=Host=localhost;Port=5432;Database=ta3lom_store_dev;Username=postgres;Password=postgres;Search Path=store,public
Store__Seed__RunOnStartup=true
PGHOST=localhost
PGPORT=5432
PGUSER=postgres
PGPASSWORD=postgres
PGDATABASE=ta3lom_store_dev
```

| Variable | Used by |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `dotnet run` for the smoke checks. Development maps `/docs` and the `/dev/*` endpoints |
| `DOTNET_*` | Quieter, faster CLI |
| `STORE_TEST_DB`, `STORE_TEST_DB_CONNECTION` | The existing integration tests run against local PostgreSQL 16 (no Docker in the cloud) |
| `ConnectionStrings__Store` | The API during smoke checks (adds the password that `appsettings.Development.json` leaves out) |
| `Store__Seed__RunOnStartup` | Seeds settings and templates when the API starts |
| `PG*` | `psql`, the integrity checks and `tools/scripts/regenerate-data-dictionary.sh` |

**Not needed now:** `Store__Pii__MobileHashKeyBase64` and `Store__DataProtection__KeysPath` (dev values are in `appsettings.Development.json`), SMS/SMTP keys (sinks), payment keys (out of scope). **Never add real secrets here.**

### Setup script (environment "Setup script" field)

```bash
#!/usr/bin/env bash
set -e
sudo apt-get update -y
sudo apt-get install -y dotnet-sdk-10.0 postgresql
sudo pg_ctlcluster 16 main start || sudo service postgresql start
sudo -u postgres psql -c "ALTER USER postgres PASSWORD 'postgres';"
sudo -u postgres psql -tc "SELECT 1 FROM pg_database WHERE datname='ta3lom_store_dev'" | grep -q 1 \
  || sudo -u postgres psql -c "CREATE DATABASE ta3lom_store_dev;"
```

### Other environment settings

| Setting | Value |
|---|---|
| Repository | `lotfi029/ta3lom_store_api` with **push** access |
| Network | At least `github.com`, `api.nuget.org`, `www.nuget.org`, `globalcdn.nuget.org`, Ubuntu apt mirrors (or "all domains") |
| Permissions | Auto-accept edits and commands, if offered (a prompt pauses the run) |

---

## 3. Start prompt (paste as the first message)

It prepares the environment and then starts building in the same turn, without waiting for you.

```text
You are building the whole ta3lom store backend in this session. Do not stop until the entire plan is
finished. Plan 14 (store/14-backend-implementation-plan.md) says WHAT each session builds;
store/sessions/Cloud-build-plan.md says HOW this run works and overrides CLAUDE.md where they differ.
The architecture is my DMS repo (https://github.com/lotfi029/DMS — clone it read-only as the example),
as mapped in store/sessions/Architecture-alignment-DMS.md, keeping the store (K) exceptions.

STEP 0 — environment (no questions, fix what you can yourself):
- Make sure dotnet 10 SDK and PostgreSQL are installed and running (setup commands in
  store/sessions/Cloud-session-start.md §2), database ta3lom_store_dev exists, and the env vars of §2 are set
  (export them yourself if missing).
- Clone DMS read-only into a scratch folder.
- Run dotnet build Ta3lom.Store.slnx -warnaserror and dotnet test --solution Ta3lom.Store.slnx. Note the
  result in PROGRESS.md and go straight on.

THEN BUILD, in this order, without stopping between sessions:
S-RUN → S-02b → S-03a-lite → S-04 → S-05 → S-08 → S-09 → S-13 → S-15 → S-16 → S-17 → S-19 → S-20 →
S-21 → S-40 → S-41 → S-23 → S-24 → S-25 → S-26 → S-42 → S-43 → S-31 → S-33 → S-35 → S-45 →
close-out (Cloud-build-plan.md §4 Phase 7).

DON'T STOP — these rules are absolute:
1. Never end your turn to report, summarise, ask a question or wait for approval. Your reports are commits,
   PROGRESS.md and DECISIONS.md. Keep working until the Part 1 checklist in PROGRESS.md is fully ticked and
   store/sessions/Part-1-report.md is pushed. Only then write your final summary.
2. There are no stop conditions. When docs disagree or something is undecided, use the defaults in
   Cloud-build-plan.md §2.2–§2.4 (precedence 03 > 11 > database/ > 09 > 14), log the choice in DECISIONS.md,
   and continue.
3. If the build or the existing tests can't be made green after 3 honest attempts, revert that change set,
   mark the session "blocked" in PROGRESS.md with the error, continue with the next session whose
   dependencies are done (§4 dependency guard), and retry blocked sessions at the end.
4. If you can't merge a PR, don't wait: keep committing on the same branch.
5. If your context is compacted, re-read PROGRESS.md, DECISIONS.md and Cloud-build-plan.md §2, then
   continue from the "Next:" line. Never restart finished sessions.

LIMITS OF THE RUN:
- No new tests until the whole plan is built. Keep every existing test, the architecture tests and the
  schema-parity test green; adapt existing tests only when a refactor moves types. Keep
  ImplementedRules.txt empty. Regenerate openapi/v1.json when endpoints change.
- No hosting: no Dockerfile, no deploy, no VPS. CI only as S-03a-lite.
- No payment code (plan 14 §0): NullPaymentInitiator + POST /dev/orders/{ref}/confirm-payment.
- Never edit a merged migration (add a new one), never delete data outside the dev database, never add
  secrets, never force-push.

FOR EVERY SESSION:
a. Read its plan 14 section, the RULs it lists in store/03, its endpoints in store/11 §12, and the
   database/01 sections it names.
b. Build layer by layer (Domain → Application → Infrastructure → Api) in the DMS layout of
   Cloud-build-plan.md §3.2. Add new error codes to Domain/Errors, 11 §1.6 values, and both .resx files.
c. Smoke check (§2.7): build -warnaserror, format, existing tests, migrate + seed the dev DB, run the API and
   call every new endpoint once with curl (save to tools/smoke/S-nn.http). After S-16, S-21, S-23, S-25 and
   S-42 run db/reference/003_integrity_checks.sql — every query must return 0 rows.
d. Commit as "S-nn — title", push, update PROGRESS.md so its first line names the next step, push again,
   and start the next session immediately.

Start now with STEP 0, then S-RUN.
```

## 4. Resume prompt (only if the session ends before the plan is finished)

```text
Resume the ta3lom store backend run and do not stop until the entire plan is finished. Run STEP 0 of
store/sessions/Cloud-session-start.md §3 (environment only), then read CLAUDE.md,
store/sessions/Cloud-build-plan.md §2, PROGRESS.md and DECISIONS.md on the working branch (or main if it was
merged). Continue from the "Next:" line of PROGRESS.md with exactly the same rules as the start prompt in
Cloud-session-start.md §3: never end your turn to report or ask, no stop conditions, no new tests, no
hosting, no payment code, one commit set per S-nn, smoke check after each, PROGRESS.md always current.
Don't redo sessions marked done.
```

---

## 5. Order of your steps

1. Run the §1.1 commands and merge the docs PR.
2. Create or update the cloud environment with §2 (variables, setup script, repo access, network, auto-accept).
3. Open a new session on that environment and paste §3.
4. If it ends early, open a new session and paste §4.
