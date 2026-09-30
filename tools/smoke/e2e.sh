#!/usr/bin/env bash
# Close-out 7.2: the full smoke scenario from an EMPTY database (tools/smoke/e2e.py), repeatable.
# Drops and recreates $E2E_DB (default ta3lom_store_e2e — never the dev database), migrates it (idempotent script + reference
# catalogue), creates the first admin with the CLI, runs the built API on it (Development, dev sinks) and runs the scenario.
# Needs: a built solution (dotnet build), psql with PGHOST/PGUSER/PGPASSWORD, ConnectionStrings__Store pointing at the dev DB
# (the database name is swapped), nothing else listening on $PORT (default 5080).
set -euo pipefail
ROOT="$(git rev-parse --show-toplevel)"
E2E_DB="${E2E_DB:-ta3lom_store_e2e}"
PORT="${PORT:-5080}"
[ "$E2E_DB" != "ta3lom_store_dev" ] || { echo "refusing to drop the dev database"; exit 2; }
DLL="$ROOT/src/Ta3lom.Store.Api/bin/Debug/net10.0/Ta3lom.Store.Api.dll"
export ConnectionStrings__Store="$(echo "${ConnectionStrings__Store:?set ConnectionStrings__Store}" | sed -E "s/Database=[^;]+/Database=$E2E_DB/")"
export PGDATABASE="$E2E_DB" ASPNETCORE_ENVIRONMENT=Development

psql -qd postgres -c "DROP DATABASE IF EXISTS \"$E2E_DB\" WITH (FORCE)" -c "CREATE DATABASE \"$E2E_DB\""
"$ROOT/tools/scripts/dev-db-migrate.sh" --regenerate --seed-catalogue
PW=$(dotnet "$DLL" create-admin --email e2e-admin@smoke.test --name "E2E Admin" | sed -n 's/.*shown once): //p')

LOG="$ROOT/artifacts/e2e-api.log"
ASPNETCORE_URLS="http://localhost:$PORT" Store__PublicBaseUrl="http://localhost:$PORT" \
  dotnet "$DLL" --contentRoot "$ROOT/src/Ta3lom.Store.Api" > "$LOG" 2>&1 &
API=$!
trap 'kill $API 2>/dev/null || true' EXIT
for _ in $(seq 1 60); do curl -sf "http://localhost:$PORT/health/ready" >/dev/null && break; sleep 1; done
sleep 3                                                    # the start-up seed (settings, templates) runs in the background

python3 "$ROOT/tools/smoke/e2e.py" --base "http://localhost:$PORT" --admin-email e2e-admin@smoke.test --admin-password "$PW"
echo "API log: $LOG ($(grep -c '"@l":"Error"' "$LOG" || true) error lines)"
