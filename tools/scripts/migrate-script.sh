#!/usr/bin/env bash
# Produces artifacts/migrate.sql: the idempotent migration script the deploy job runs as store_owner
# before switching containers (database/03 §2). Never Database.Migrate() at start-up.
set -euo pipefail
source "$(dirname "$0")/_scratch-db.sh"
migrate_script
echo "artifacts/migrate.sql"
