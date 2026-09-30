# Shared helpers: build a throw-away database from the EF migrations.
# Connection: libpq variables (PGHOST, PGPORT, PGUSER, PGPASSWORD); needs psql, pg_dump and the .NET SDK.
set -euo pipefail
ROOT="$(git rev-parse --show-toplevel)"
SCRATCH_DB="store_scratch_$$"

migrate_script() {
  mkdir -p "$ROOT/artifacts"
  dotnet tool restore >/dev/null
  dotnet ef migrations script --idempotent \
    -p "$ROOT/src/Ta3lom.Store.Infrastructure" -s "$ROOT/src/Ta3lom.Store.Infrastructure" \
    -o "$ROOT/artifacts/migrate.sql" >/dev/null
}

create_migrated_db() {
  psql -v ON_ERROR_STOP=1 -qd postgres -c "CREATE DATABASE \"$SCRATCH_DB\""
  trap 'psql -qd postgres -c "DROP DATABASE IF EXISTS \"$SCRATCH_DB\" WITH (FORCE)" >/dev/null' EXIT
  # PostgreSQL < 18 has no built-in uuidv7(): the same shim the tests use (never part of a migration).
  if [ "$(psql -qAtd "$SCRATCH_DB" -c "SHOW server_version_num")" -lt 180000 ]; then
    psql -v ON_ERROR_STOP=1 -qd "$SCRATCH_DB" <<'SQL'
CREATE FUNCTION public.uuidv7() RETURNS uuid LANGUAGE sql VOLATILE PARALLEL SAFE AS $$
  SELECT encode(set_bit(set_bit(overlay(uuid_send(gen_random_uuid())
           PLACING substring(int8send((extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
           FROM 1 FOR 6), 52, 1), 53, 1), 'hex')::uuid
$$;
SQL
  fi
  PGOPTIONS='-c search_path=store,public -c client_min_messages=warning' psql -v ON_ERROR_STOP=1 -qd "$SCRATCH_DB" -f "$ROOT/artifacts/migrate.sql" >/dev/null
}
