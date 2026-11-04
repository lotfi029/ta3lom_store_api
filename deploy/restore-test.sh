#!/usr/bin/env bash
# Restore test (plan §5): restores the newest (or the given) dump into a THROWAWAY postgres:18 container and checks the
# schema and row counts. Touches nothing of the running environment. Run it monthly and after any backup change:
#   ./restore-test.sh /var/backups/ta3lom-store/ta3lom-store/daily/db-20261104T234000Z.dump
set -euo pipefail

DUMP="${1:-$(ls -1t "${BACKUP_DIR:-/var/backups/ta3lom-store}"/*/daily/db-*.dump 2>/dev/null | head -1)}"
[ -n "$DUMP" ] && [ -s "$DUMP" ] || { echo "restore-test: no dump found" >&2; exit 2; }
NAME="ta3lom-store-restore-test-$$"
cleanup() { docker rm -f "$NAME" >/dev/null 2>&1 || true; }
trap cleanup EXIT

docker run -d --name "$NAME" -e POSTGRES_PASSWORD=restore-test -e POSTGRES_USER=store -e POSTGRES_DB=ta3lom_store postgres:18 >/dev/null
for _ in $(seq 1 60); do docker exec "$NAME" pg_isready -U store -d ta3lom_store >/dev/null 2>&1 && break; sleep 1; done
docker exec -i "$NAME" pg_restore -U store -d ta3lom_store --no-owner --exit-on-error < "$DUMP"

q() { docker exec "$NAME" psql -U store -d ta3lom_store -tAc "$1"; }
for table in orders order_line payment donation_receipt customer project category; do
  echo "restore-test: store.$table = $(q "SELECT count(*) FROM store.$table") rows"
done
migrations="$(q 'SELECT count(*) FROM store.__ef_migrations_history')"
[ "${migrations:-0}" -gt 0 ] || { echo "restore-test: no migration history — not a store dump?" >&2; exit 1; }
gaps="$(q "SELECT count(*) FROM (SELECT series, year, max(number) - count(*) AS gap FROM store.donation_receipt GROUP BY series, year) g WHERE gap <> 0")"
[ "$gaps" = "0" ] || { echo "restore-test: receipt numbering has gaps" >&2; exit 1; }
echo "restore-test: OK ($DUMP, $migrations migrations, receipt numbers gapless)"
