#!/usr/bin/env bash
# Nightly backup of one store environment (plan §5): pg_dump -Fc of the database + tar of the api-keys and api-files
# volumes, kept 14 daily / 8 weekly (Fridays). Review 04 S-10: every folder 0700 and every file 0600 from the start (the
# dump holds donor data, the key ring decrypts tokens). S-11: `counts-<stamp>.txt` lists each table's row count taken
# IN THE SAME SNAPSHOT as the dump (pg_export_snapshot + pg_dump --snapshot), so restore-test.sh can require equality.
# Calls an optional offsite hook (same pattern as ta3lom-deploy):
#   BACKUP_DIR=/var/backups/ta3lom-store OFFSITE_HOOK=/srv/ta3lom-deploy/offsite.sh ./backup.sh .env.production
# Cron (root or the deploy user in the docker group), 02:40 Riyadh:
#   40 2 * * * cd /srv/ta3lom-store/deploy && ./backup.sh .env.production >> /var/log/ta3lom-store-backup.log 2>&1
set -euo pipefail
umask 077

ENV_FILE="${1:-.env.production}"
cd "$(dirname "$0")"
[ -f "$ENV_FILE" ] || { echo "backup: $ENV_FILE not found" >&2; exit 2; }
# shellcheck disable=SC1090
PROJECT="$(grep -E '^COMPOSE_PROJECT_NAME=' "$ENV_FILE" | cut -d= -f2 | cut -d' ' -f1)"
[ -n "$PROJECT" ] || { echo "backup: COMPOSE_PROJECT_NAME missing in $ENV_FILE" >&2; exit 2; }
BACKUP_DIR="${BACKUP_DIR:-/var/backups/ta3lom-store}/$PROJECT"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
DAILY="$BACKUP_DIR/daily"
WEEKLY="$BACKUP_DIR/weekly"
mkdir -p "$DAILY" "$WEEKLY"
chmod 0700 "$BACKUP_DIR" "$DAILY" "$WEEKLY"
ALPINE_IMAGE="${ALPINE_IMAGE:-alpine:3}"
TABLES="orders order_line payment refund payment_event donation_receipt customer project project_option category setting outbox_message"

compose() { docker compose --env-file "$ENV_FILE" -p "$PROJECT" -f compose.yaml "$@"; }

# 1. Database: custom format, compressed. A REPEATABLE READ session exports its snapshot; the dump and the row counts
#    both read that snapshot, so the counts describe exactly what the dump holds.
coproc PSQL { compose exec -T db psql -U store -d ta3lom_store -qtAX -v ON_ERROR_STOP=1; }
echo "BEGIN ISOLATION LEVEL REPEATABLE READ; SELECT pg_export_snapshot();" >&"${PSQL[1]}"
IFS= read -r -t 30 SNAPSHOT <&"${PSQL[0]}" || { echo "backup: no snapshot exported" >&2; exit 1; }
compose exec -T db pg_dump -U store -d ta3lom_store -Fc --snapshot="$SNAPSHOT" > "$DAILY/db-$STAMP.dump"
[ -s "$DAILY/db-$STAMP.dump" ] || { echo "backup: empty database dump" >&2; exit 1; }
: > "$DAILY/counts-$STAMP.txt"
for table in $TABLES; do
  echo "SELECT '$table ' || count(*) FROM store.$table;" >&"${PSQL[1]}"
  IFS= read -r -t 30 line <&"${PSQL[0]}" || { echo "backup: no count for $table" >&2; exit 1; }
  echo "$line" >> "$DAILY/counts-$STAMP.txt"
done
echo "COMMIT;" >&"${PSQL[1]}"
psql_in=${PSQL[1]}
exec {psql_in}>&-
wait "$PSQL_PID" || true

# 2. Volumes: the Data Protection key ring and the uploaded/private files (receipts).
for volume in api-keys api-files; do
  docker run --rm -v "${PROJECT}_${volume}:/data:ro" -v "$DAILY:/backup" "$ALPINE_IMAGE" \
    sh -c "umask 077 && tar -czf '/backup/${volume}-$STAMP.tar.gz' -C /data ."
done
chmod 0600 "$DAILY/"*"-$STAMP"*

# 3. Weekly copies on Fridays (Riyadh week ends Friday/Saturday).
if [ "$(TZ=Asia/Riyadh date +%u)" = "5" ]; then
  cp -p "$DAILY/db-$STAMP.dump" "$DAILY/counts-$STAMP.txt" "$DAILY/api-keys-$STAMP.tar.gz" "$DAILY/api-files-$STAMP.tar.gz" "$WEEKLY/"
fi

# 4. Retention: 14 daily and 8 weekly sets.
prune() { local dir="$1" keep="$2"; for prefix in db counts api-keys api-files; do
  # `|| true`: an empty folder (no weekly set before the first Friday) is not an error under pipefail.
  { ls -1t "$dir/$prefix-"* 2>/dev/null || true; } | tail -n +"$((keep + 1))" | xargs -r rm -f; done; }
prune "$DAILY" 14
prune "$WEEKLY" 8

# 5. Offsite (optional): the hook receives the folder with today's files.
if [ -n "${OFFSITE_HOOK:-}" ]; then
  "$OFFSITE_HOOK" "$BACKUP_DIR" "$STAMP"
fi

echo "backup: $PROJECT $STAMP ok ($(du -sh "$DAILY" | cut -f1) daily)"
