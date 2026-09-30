#!/usr/bin/env bash
# pg_dump of a database built from the EF migrations → db/reference/generated/001_store_schema.pg_dump.sql.
# db/reference/001_store_schema.sql stays the hand-written, commented reference; SchemaParityTests keeps the two
# identical, so a model change edits 001 in the same PR. This dump is for DBA diffs and reviews.
set -euo pipefail
source "$(dirname "$0")/_scratch-db.sh"
migrate_script
create_migrated_db
mkdir -p "$ROOT/db/reference/generated"
pg_dump --schema-only --schema=store --no-owner --no-privileges --exclude-table='store.__ef_migrations_history' "$SCRATCH_DB" \
  | grep -v '^\\restrict\|^\\unrestrict' > "$ROOT/db/reference/generated/001_store_schema.pg_dump.sql"
echo "db/reference/generated/001_store_schema.pg_dump.sql"
