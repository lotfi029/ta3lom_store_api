#!/usr/bin/env bash
# Nightly backup of one store environment (plan §5): pg_dump -Fc of the database + tar of the api-keys and api-files
# volumes, kept 14 daily / 8 weekly (Fridays). Calls an optional offsite hook (same pattern as ta3lom-deploy):
#   BACKUP_DIR=/var/backups/ta3lom-store OFFSITE_HOOK=/srv/ta3lom-deploy/offsite.sh ./backup.sh .env.production
# Cron (root or the deploy user in the docker group), 02:40 Riyadh:
#   40 2 * * * cd /srv/ta3lom-store/deploy && ./backup.sh .env.production >> /var/log/ta3lom-store-backup.log 2>&1
set -euo pipefail

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
umask 077

compose() { docker compose --env-file "$ENV_FILE" -p "$PROJECT" -f compose.yaml "$@"; }

# 1. Database: custom format, compressed, consistent snapshot (pg_dump uses one transaction).
compose exec -T db pg_dump -U store -d ta3lom_store -Fc > "$DAILY/db-$STAMP.dump"
[ -s "$DAILY/db-$STAMP.dump" ] || { echo "backup: empty database dump" >&2; exit 1; }

# 2. Volumes: the Data Protection key ring and the uploaded/private files (receipts).
for volume in api-keys api-files; do
  docker run --rm -v "${PROJECT}_${volume}:/data:ro" -v "$DAILY:/backup" alpine:3 \
    tar -czf "/backup/${volume}-$STAMP.tar.gz" -C /data .
done

# 3. Weekly copies on Fridays (Riyadh week ends Friday/Saturday).
if [ "$(TZ=Asia/Riyadh date +%u)" = "5" ]; then
  cp "$DAILY/db-$STAMP.dump" "$DAILY/api-keys-$STAMP.tar.gz" "$DAILY/api-files-$STAMP.tar.gz" "$WEEKLY/"
fi

# 4. Retention: 14 daily and 8 weekly sets.
prune() { local dir="$1" keep="$2"; for prefix in db api-keys api-files; do
  ls -1t "$dir/$prefix-"* 2>/dev/null | tail -n +"$((keep + 1))" | xargs -r rm -f; done; }
prune "$DAILY" 14
prune "$WEEKLY" 8

# 5. Offsite (optional): the hook receives the folder with today's files.
if [ -n "${OFFSITE_HOOK:-}" ]; then
  "$OFFSITE_HOOK" "$BACKUP_DIR" "$STAMP"
fi

echo "backup: $PROJECT $STAMP ok ($(du -sh "$DAILY" | cut -f1) daily)"
