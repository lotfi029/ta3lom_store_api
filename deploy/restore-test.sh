#!/usr/bin/env bash
# Restore test (plan §5, review 04 S-11 — strict): restores the newest (or the given) dump into a THROWAWAY postgres:18
# container and FAILS on anything unexpected: a restore error, no migration history, a table whose row count differs
# from the backup's counts-<stamp>.txt (taken in the dump's own snapshot by backup.sh), a missing counts file (unless
# --no-counts, for dumps older than this check), or a gap in the receipt numbers. Touches nothing of the running
# environment. Run it monthly and after any backup change:
#   ./restore-test.sh /var/backups/ta3lom-store/ta3lom-store/daily/db-20261104T234000Z.dump
set -euo pipefail

REQUIRE_COUNTS=1
if [ "${1:-}" = "--no-counts" ]; then REQUIRE_COUNTS=0; shift; fi
DUMP="${1:-$(ls -1t "${BACKUP_DIR:-/var/backups/ta3lom-store}"/*/daily/db-*.dump 2>/dev/null | head -1)}"
[ -n "$DUMP" ] && [ -s "$DUMP" ] || { echo "restore-test: no dump found" >&2; exit 2; }
COUNTS="$(dirname "$DUMP")/counts-$(basename "$DUMP" .dump | sed 's/^db-//').txt"
if [ ! -s "$COUNTS" ] && [ "$REQUIRE_COUNTS" = 1 ]; then
  echo "restore-test: $COUNTS missing (backup.sh writes it; --no-counts only for older dumps)" >&2; exit 2
fi
POSTGRES_IMAGE="${POSTGRES_IMAGE:-postgres:18}"
NAME="ta3lom-store-restore-test-$$"
cleanup() { docker rm -f "$NAME" >/dev/null 2>&1 || true; }
trap cleanup EXIT

docker run -d --name "$NAME" -e POSTGRES_PASSWORD=restore-test -e POSTGRES_USER=store -e POSTGRES_DB=ta3lom_store "$POSTGRES_IMAGE" >/dev/null
# Over TCP: the image's first-start init server listens on the socket only, and stops again before the real one starts.
for _ in $(seq 1 60); do docker exec "$NAME" pg_isready -h 127.0.0.1 -U store -d ta3lom_store >/dev/null 2>&1 && break; sleep 1; done
docker exec -i "$NAME" pg_restore -U store -d ta3lom_store --no-owner --exit-on-error < "$DUMP"

q() { docker exec "$NAME" psql -U store -d ta3lom_store -v ON_ERROR_STOP=1 -tAXc "$1"; }
migrations="$(q 'SELECT count(*) FROM store.__ef_migrations_history')"
[ "${migrations:-0}" -gt 0 ] || { echo "restore-test: no migration history — not a store dump?" >&2; exit 1; }

failures=0
if [ -s "$COUNTS" ]; then
  while read -r table expected; do
    [ -n "$table" ] || continue
    actual="$(q "SELECT count(*) FROM store.$table")"
    if [ "$actual" = "$expected" ]; then
      echo "restore-test: store.$table = $actual rows (as backed up)"
    else
      echo "restore-test: store.$table = $actual rows, the backup had $expected" >&2
      failures=$((failures + 1))
    fi
  done < "$COUNTS"
fi
gaps="$(q "SELECT count(*) FROM (SELECT series, year, max(number) - count(*) AS gap, min(number) AS first FROM store.donation_receipt GROUP BY series, year) g WHERE gap <> 0 OR first <> 1")"
[ "$gaps" = "0" ] || { echo "restore-test: receipt numbering has gaps" >&2; failures=$((failures + 1)); }
[ "$failures" -eq 0 ] || { echo "restore-test: FAILED ($failures problem(s))" >&2; exit 1; }
echo "restore-test: OK ($DUMP, $migrations migrations, every count as backed up, receipt numbers gapless)"
