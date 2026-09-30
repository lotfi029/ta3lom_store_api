#!/usr/bin/env bash
# Regenerates store/database/02-data-dictionary.md from a database built from the EF migrations,
# with column notes taken from the comments in db/reference/001_store_schema.sql (database/03 §2).
set -euo pipefail
source "$(dirname "$0")/_scratch-db.sh"
migrate_script
create_migrated_db
python3 "$ROOT/db/reference/tools/gen_dictionary_step1.py" "$ROOT/db/reference/001_store_schema.sql" "$SCRATCH_DB" "$ROOT/artifacts/dict.json"
python3 "$ROOT/db/reference/tools/gen_dictionary_step2.py" "$ROOT/artifacts/dict.json" "$ROOT/store/database/02-data-dictionary.md" "${GENERATED_ON:-$(date -u +%Y-%m-%d)}"
echo "store/database/02-data-dictionary.md"
