#!/usr/bin/env bash
# Review 04 H-2: the deploy stack as it runs on the VPS — deploy/compose.yaml UNCHANGED (db + migrate + api), its env
# file, key-per-file secrets generated here with the permissions secrets.md prescribes (db_password 0400 owned by the
# postgres uid 999; the api folder owned by the app uid 1654) — then /health/ready through the edge network alias.
# Then (S-10/S-11) deploy/backup.sh against that stack: folders 0700, files 0600, and deploy/restore-test.sh must pass on
# the dump — and must FAIL once its counts file is tampered with.
# Run by CI (job `stack`) and locally:  bash deploy/stack-check.sh <api image> <migrate image>
# Staging mode: Production refuses Moyasar test keys, and no live key exists outside the VPS. Nothing here is a real
# secret: every value is random per run, lives in a temp dir and is removed on exit.
# STACK_MODE=showcase (deploy without payments): PRODUCTION with PAYMENTS_PROVIDER=none and CUSTOMER_SIGNIN=false and NO
# Moyasar or Sms__AppSid secret file — the API must start, report "mode":"showcase" on /health/ready and refuse a cart
# line with 409 store.payments_disabled. The backup/restore part runs in the default (live) mode only.
set -euo pipefail

API_IMAGE_REF=${1:?usage: stack-check.sh <api image:tag> <migrate image:tag>}
MIGRATE_IMAGE_REF=${2:?usage: stack-check.sh <api image:tag> <migrate image:tag>}
CURL_IMAGE=${CURL_IMAGE:-curlimages/curl:8.11.1}
export ALPINE_IMAGE=${ALPINE_IMAGE:-alpine:3} POSTGRES_IMAGE=${POSTGRES_IMAGE:-postgres:18}
SUBNET=${STACK_EDGE_SUBNET:-172.31.251.0/24}
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="$(mktemp -d)"
PROJECT=t3s-stack-check
SUDO=""
[ "$(id -u)" -eq 0 ] || SUDO="sudo"
CREATED_EDGE=""
STACK_MODE=${STACK_MODE:-live}
case "$STACK_MODE" in
  live) ENV_NAME=staging ;;
  showcase) ENV_NAME=production ;;
  *) echo "stack-check: STACK_MODE must be live or showcase" >&2; exit 2 ;;
esac

cleanup() {
  local rc=$?
  if [ "$rc" -ne 0 ]; then
    echo "stack-check: FAILED (exit $rc) — compose state and API log:" >&2
    docker compose --env-file "$WORK/.env" -p "$PROJECT" -f "$WORK/compose.yaml" ps -a >&2 || true
    docker compose --env-file "$WORK/.env" -p "$PROJECT" -f "$WORK/compose.yaml" logs --tail 60 migrate api >&2 || true
  fi
  docker compose --env-file "$WORK/.env" -p "$PROJECT" -f "$WORK/compose.yaml" down -v >/dev/null 2>&1 || true
  [ -n "$CREATED_EDGE" ] && docker network rm edge >/dev/null 2>&1 || true
  $SUDO rm -rf "$WORK"
  exit "$rc"
}
trap cleanup EXIT

# The external network the edge Caddy creates on the VPS.
if ! docker network inspect edge >/dev/null 2>&1; then
  docker network create --subnet "$SUBNET" edge >/dev/null
  CREATED_EDGE=1
fi
EDGE_SUBNET_NOW="$(docker network inspect -f '{{(index .IPAM.Config 0).Subnet}}' edge)"

cp "$HERE/compose.yaml" "$HERE/backup.sh" "$HERE/restore-test.sh" "$WORK/"
api_repo="${API_IMAGE_REF%:*}" api_tag="${API_IMAGE_REF##*:}"
migrate_repo="${MIGRATE_IMAGE_REF%:*}" migrate_tag="${MIGRATE_IMAGE_REF##*:}"
[ "$api_tag" = "$migrate_tag" ] || { echo "stack-check: the api and migrate images need the same tag (STORE_VERSION)" >&2; exit 2; }
sed -e "s|^COMPOSE_PROJECT_NAME=.*|COMPOSE_PROJECT_NAME=$PROJECT|" \
    -e "s|^STORE_VERSION=.*|STORE_VERSION=$api_tag|" \
    -e "s|^EDGE_SUBNET=.*|EDGE_SUBNET=$EDGE_SUBNET_NOW|" \
    -e "s|^SECRETS_DIR=.*|SECRETS_DIR=./secrets/$ENV_NAME|" \
    "$HERE/.env.$ENV_NAME.example" > "$WORK/.env"
printf 'API_IMAGE=%s\nMIGRATE_IMAGE=%s\n' "$api_repo" "$migrate_repo" >> "$WORK/.env"
if [ "$STACK_MODE" = showcase ]; then
  # What the owner sets on the VPS before Moyasar (README.md "Showcase mode").
  sed -i -e "s|^PAYMENTS_PROVIDER=.*|PAYMENTS_PROVIDER=none|" -e "s|^CUSTOMER_SIGNIN=.*|CUSTOMER_SIGNIN=false|" "$WORK/.env"
  grep -q "^PAYMENTS_PROVIDER=none" "$WORK/.env" && grep -q "^CUSTOMER_SIGNIN=false" "$WORK/.env" \
    || { echo "stack-check: the env example has no PAYMENTS_PROVIDER / CUSTOMER_SIGNIN line" >&2; exit 1; }
fi

# Secrets exactly as secrets.md generates them (random; test-mode Moyasar key). Showcase: no Moyasar and no SMS file.
S="$WORK/secrets/$ENV_NAME"
mkdir -p "$S/api"
( umask 077
  openssl rand -base64 32 | tr -d '/+=\n' > "$S/db_password"
  printf 'Host=db;Database=ta3lom_store;Username=store;Password=%s;Search Path=store,public' "$(cat "$S/db_password")" > "$S/api/ConnectionStrings__Store"
  for k in Jwt__CustomerSigningKeyBase64 Jwt__StaffSigningKeyBase64 Otp__PepperBase64 Tokens__StatusKeyBase64 Hashing__KeyBase64; do
    openssl rand -base64 48 | tr -d '\n' > "$S/api/Store__$k"
  done
  if [ "$STACK_MODE" = live ]; then
    openssl rand -hex 32 > "$S/api/Store__Payments__Moyasar__WebhookSecret"
    printf 'sk_test_%s' "$(openssl rand -hex 16)" > "$S/api/Store__Payments__Moyasar__SecretKey"
    printf 'ci-%s' "$(openssl rand -hex 8)" > "$S/api/Store__Sms__AppSid"
  fi
  printf 'ci-%s' "$(openssl rand -hex 8)" > "$S/api/Store__Email__Password"
  printf 'ci-%s' "$(openssl rand -hex 8)" > "$S/api/Store__SiteRebuild__Token"
)
# secrets.md: db_password read by postgres and migrate (uid 999) only; the api folder by the app user (uid 1654).
$SUDO chown 999:999 "$S/db_password" && $SUDO chmod 0400 "$S/db_password"
$SUDO chown -R 1654:1654 "$S/api" && $SUDO chmod 0500 "$S/api" && $SUDO find "$S/api" -type f -exec chmod 0400 {} +

echo "stack-check: compose up (db → migrate → api), images $API_IMAGE_REF / $MIGRATE_IMAGE_REF"
docker compose --env-file "$WORK/.env" -p "$PROJECT" -f "$WORK/compose.yaml" up -d --wait --wait-timeout 180 db
docker compose --env-file "$WORK/.env" -p "$PROJECT" -f "$WORK/compose.yaml" up -d api
alias="$(sed -n 's/^EDGE_ALIAS=\([^ ]*\).*/\1/p' "$WORK/.env")"
for _ in $(seq 1 60); do
  if docker run --rm --network edge "$CURL_IMAGE" -fsS --max-time 3 "http://$alias:8080/health/ready" >/dev/null 2>&1; then
    echo "stack-check: http://$alias:8080/health/ready answered 200 — OK"
    ready="$(docker run --rm --network edge "$CURL_IMAGE" -sS "http://$alias:8080/health/ready")"
    echo "$ready"
    break
  fi
  sleep 2
done
[ -n "${ready:-}" ] || { echo "stack-check: /health/ready never answered 200" >&2; exit 1; }
case "$ready" in
  *"\"mode\":\"$STACK_MODE\""*) echo "stack-check: readiness reports mode $STACK_MODE — OK" ;;
  *) echo "stack-check: readiness does not report \"mode\":\"$STACK_MODE\"" >&2; exit 1 ;;
esac

if [ "$STACK_MODE" = showcase ]; then
  # Showcase: a donor action is refused before anything is written ("mode":"showcase" checked above).
  refused="$(docker run --rm --network edge "$CURL_IMAGE" -sS -o - -w ' %{http_code}' -X POST -H 'Content-Type: application/json' \
    -d '{"categoryId":"00000000-0000-0000-0000-000000000000","amount":"10.00"}' "http://$alias:8080/v1/cart/lines")"
  case "$refused" in
    *'"code":"store.payments_disabled"'*' 409') echo "stack-check: POST /v1/cart/lines → 409 store.payments_disabled — OK" ;;
    *) echo "stack-check: POST /v1/cart/lines should be 409 store.payments_disabled, got: $refused" >&2; exit 1 ;;
  esac
  echo "stack-check: OK — showcase stack healthy in Production without Moyasar or SMS secrets"
  exit 0
fi

echo "stack-check: backup.sh → restore-test.sh"
BACKUPS="$WORK/backups"
(cd "$WORK" && BACKUP_DIR="$BACKUPS" bash ./backup.sh .env)
bad_modes="$(find "$BACKUPS" \( -type d ! -perm 0700 \) -o \( -type f ! -perm 0600 \) | head -5)"
[ -z "$bad_modes" ] || { echo "stack-check: backup files/folders not 0600/0700: $bad_modes" >&2; exit 1; }
dump="$(ls -1t "$BACKUPS/$PROJECT/daily/"db-*.dump | head -1)"
bash "$WORK/restore-test.sh" "$dump"
counts="${dump%/*}/counts-$(basename "$dump" .dump | sed 's/^db-//').txt"
sed -i 's/^setting \([0-9]*\)$/setting 999999/' "$counts"
if bash "$WORK/restore-test.sh" "$dump" >/dev/null 2>&1; then
  echo "stack-check: restore-test.sh passed with a wrong row count — it must fail" >&2; exit 1
fi
echo "stack-check: OK — stack healthy, backup modes 0700/0600, restore strict"
