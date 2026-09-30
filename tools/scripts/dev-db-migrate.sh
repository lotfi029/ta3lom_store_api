#!/usr/bin/env bash
# Applies artifacts/migrate.sql (idempotent) to the local dev database (PGDATABASE, default ta3lom_store_dev).
# Cloud/dev only: PostgreSQL < 18 gets the same uuidv7() shim the tests use (never part of a migration).
# Usage: tools/scripts/dev-db-migrate.sh [--regenerate] [--seed-catalogue]
#   --seed-catalogue  loads the reference catalogue (db/reference/002_store_seed.sql) when the DB has no products
set -euo pipefail
ROOT="$(git rev-parse --show-toplevel)"
DB="${PGDATABASE:-ta3lom_store_dev}"
REGEN=0; SEED=0
for arg in "$@"; do case "$arg" in --regenerate) REGEN=1 ;; --seed-catalogue) SEED=1 ;; esac; done
if [ "$REGEN" = 1 ] || [ ! -f "$ROOT/artifacts/migrate.sql" ]; then
  "$ROOT/tools/scripts/migrate-script.sh" >/dev/null
fi
if [ "$(psql -qAtd "$DB" -c "SHOW server_version_num")" -lt 180000 ] \
   && [ -z "$(psql -qAtd "$DB" -c "SELECT 1 FROM pg_proc WHERE proname = 'uuidv7'")" ]; then
  psql -v ON_ERROR_STOP=1 -qd "$DB" <<'SQL'
CREATE FUNCTION public.uuidv7() RETURNS uuid LANGUAGE sql VOLATILE PARALLEL SAFE AS $$
  SELECT encode(set_bit(set_bit(overlay(uuid_send(gen_random_uuid())
           PLACING substring(int8send((extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
           FROM 1 FOR 6), 52, 1), 53, 1), 'hex')::uuid
$$;
SQL
fi
PGOPTIONS='-c search_path=store,public -c client_min_messages=warning' \
  psql -v ON_ERROR_STOP=1 -qd "$DB" -f "$ROOT/artifacts/migrate.sql" >/dev/null
echo "migrated $DB"
if [ "$SEED" = 1 ] && [ "$(psql -qAtd "$DB" -c "SELECT count(*) FROM store.product")" = "0" ]; then
  PGOPTIONS='-c client_min_messages=warning' psql -v ON_ERROR_STOP=1 -qd "$DB" -f "$ROOT/db/reference/002_store_seed.sql" >/dev/null
  echo "seeded the reference catalogue ($(psql -qAtd "$DB" -c "SELECT count(*) FROM store.product") products)"
fi
