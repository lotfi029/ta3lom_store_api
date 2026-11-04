# Store API runbook (edge model)

The store API runs on the shared ta3lom VPS **behind the edge Caddy** of `ta3lom-deploy` (the only process on 80/443,
F-D6). This folder starts PostgreSQL 18, the migration job and the API — **no proxy, no published port**. The edge
reaches the API over the external `edge` Docker network by its alias. Images come from GHCR; the VPS holds no source.

```
internet ─▶ edge Caddy (ta3lom-deploy, TLS, HSTS) ─▶ edge network ─▶ store-api:8080          (project ta3lom-store)
                                                               └──▶ store-api-staging:8080  (project ta3lom-store-staging)
                                                                     │  store network (private)
                                                                     └─ db (postgres:18), migrate (one-shot)
```

| | Production | Staging |
|---|---|---|
| Compose project | `ta3lom-store` | `ta3lom-store-staging` |
| Env file | `.env.production` | `.env.staging` |
| API host / edge alias | `store-api.ta3lom.sa` → `store-api` | `store-api-staging.ta3lom.sa` → `store-api-staging` |
| Web origin (CORS) | `https://store.ta3lom.sa` (+ `https://ta3lom.sa`, `https://www.ta3lom.sa` for the cart badge) | `https://store-staging.ta3lom.sa` |
| Image tag | `X.Y.Z` (git tag `vX.Y.Z`) | `staging-<sha>` |
| Moyasar | live keys (`sk_live_…`) | **test** keys (`sk_test_…`) |
| Secrets | `secrets/production/` | `secrets/staging/` |

Memory limits: db 512 MB, api 384 MB, migrate 256 MB per environment (≈ 0.9 GB each). The API and migrate containers
run read-only (tmpfs `/tmp`) as non-root.

## 1. First install (once per VPS)

1. **Edge network with a fixed subnet.** `ta3lom-deploy/edge/up.sh` creates `edge` with a fixed IPv4 subnet (e.g.
   `172.31.0.0/24`). Put the same value in `EDGE_SUBNET` of both env files: the API trusts `X-Forwarded-For/Proto` only
   from that subnet (per-IP rate limits and HSTS depend on it). If `edge` already exists without a fixed subnet, follow
   ta3lom-deploy's README ("recreate the edge network") during a short planned restart of the edge.
2. **Folders.** `/srv/ta3lom-store/deploy/` with `compose.yaml`, `backup.sh`, `restore-test.sh` from this folder, and
   `.env.production` / `.env.staging` copied from the `.example` files and edited (no secrets in them).
3. **Secrets.** `secrets/production/` and `secrets/staging/` exactly as in `secrets.md` (Moyasar keys included).
4. **GHCR pull access.** The packages stay private. On a dedicated machine user, create a **classic** token with
   `read:packages` only (GHCR does not accept fine-grained tokens), then as the deploy user:
   `read -rs T && printf '%s' "$T" | docker login ghcr.io -u <machine-user> --password-stdin; unset T`
   (prefer a credential helper; otherwise keep `~/.docker/config.json` at `0600`).
5. **DNS**: `store-api.ta3lom.sa` and `store-api-staging.ta3lom.sa` → the VPS. **Edge routes**: the store block of
   ta3lom-deploy (`conf/store.caddy`; a copy lives in the web repo `deploy/edge/store.caddy`) — body limit 21 MB on the
   API, HSTS on production names only, `X-Robots-Tag: noindex` on staging names, the webhook path not cached.
6. **First start** (staging first):
   ```bash
   cd /srv/ta3lom-store/deploy
   export STORE_VERSION=staging-<sha>
   docker compose --env-file .env.staging -p ta3lom-store-staging -f compose.yaml up -d --wait api
   curl -fsS https://store-api-staging.ta3lom.sa/health/ready
   docker compose --env-file .env.staging -p ta3lom-store-staging -f compose.yaml run --rm api create-admin --email <you> --name "<name>"
   ```
7. **Moyasar dashboard** (each environment): webhook `https://<API_DOMAIN>/v1/payments/webhooks/moyasar`, POST, the
   secret token = `Store__Payments__Moyasar__WebhookSecret`; enable mada, Visa, Mastercard, Apple Pay, STC Pay.

## 2. Deploy (automatic)

`.github/workflows/deploy.yml` starts only after **CI succeeded on the same commit**:

- **main** → images `:staging-<sha>` (+ `:<sha>`), built, `self-check` (renders a receipt PDF in the image), pushed;
  then on the VPS `docker compose … pull migrate api && docker compose … up -d --wait api` for staging, then a smoke of
  `/health/ready`.
- **tag `vX.Y.Z`** → images `:X.Y.Z`, same steps for production after the `production` environment reviewer approves.
- Missing `*_SSH_*` secrets → the deploy step is skipped with a warning and prints the commands (never a red run).

GitHub settings: environments `staging` and `production` (required reviewer; tags `v*` only); secrets per environment
`STAGING_SSH_HOST|USER|KEY|KNOWN_HOSTS` and `PRODUCTION_…`; optional variables `STAGING_DEPLOY_DIR`,
`PRODUCTION_DEPLOY_DIR` (default `/srv/ta3lom-store/deploy`).

The migration job (`migrate`) runs the idempotent script before every API start; it never drops anything (expand →
migrate → contract). Never run a migration's `Down` in production — roll forward.

## 3. Rollback

Actions → **deploy** → *Run workflow* — **for production choose "Use workflow from: the tag"** (the environment only
allows `v*`). Environment + an earlier tag: production accepts only `X.Y.Z`, staging only `staging-<sha>`, and the
image's `org.opencontainers.image.version` label must equal the tag. By hand on the VPS:
`STORE_VERSION=<tag> docker compose --env-file .env.production -p ta3lom-store -f compose.yaml up -d --wait api`.
A rollback across a migration keeps the newer schema (expand-only migrations stay compatible).

## 4. Backups and restore test

```bash
# nightly, 02:40 Riyadh (cron of the deploy user)
40 2 * * * cd /srv/ta3lom-store/deploy && BACKUP_DIR=/var/backups/ta3lom-store OFFSITE_HOOK=/srv/ta3lom-deploy/offsite.sh ./backup.sh .env.production >> /var/log/ta3lom-store-backup.log 2>&1
50 2 * * * cd /srv/ta3lom-store/deploy && BACKUP_DIR=/var/backups/ta3lom-store ./backup.sh .env.staging >> /var/log/ta3lom-store-backup.log 2>&1
# monthly restore test into a throwaway container (row counts, migrations, gapless receipt numbers)
0 4 1 * * cd /srv/ta3lom-store/deploy && ./restore-test.sh >> /var/log/ta3lom-store-backup.log 2>&1
```

`backup.sh`: `pg_dump -Fc` + tar of the `api-keys` (Data Protection key ring) and `api-files` (uploads and private
receipt PDFs) volumes; 14 daily / 8 weekly kept; `OFFSITE_HOOK` copies them off the VPS (ta3lom-deploy's `offsite.sh`).
The `secrets/` folders are backed up by the owner offline.

## 5. Operations

- Logs: `docker compose --env-file .env.production -p ta3lom-store -f compose.yaml logs --tail 200 api` (no PII: the
  RedactingEnricher masks mobiles, e-mails and tokens).
- Health: `/health/live` (process), `/health/ready` (database + outbox: dead letters or overdue messages → Degraded).
- Jobs (Quartz, Riyadh time): reconcile payments and expire unpaid orders every 5 minutes; OTP/idempotency pruning
  hourly; guest carts, integrity checks, retention nightly/weekly.
- Payment webhook: `POST /v1/payments/webhooks/moyasar` (anonymous, verified by the shared secret, re-fetches the
  payment before settling). A failed verification is recorded and answered 401 — check the secret in the dashboard.
- Key rotation: see `secrets.md`. Moyasar keys: replace the file, `up -d api`; the webhook secret also in the dashboard.

## Go-live checklist (owner)

- [ ] Edge network recreated with the fixed subnet; `EDGE_SUBNET` set in both env files.
- [ ] Secrets for both environments (Moyasar **live** keys in production, **test** keys in staging).
- [ ] Moyasar: association account, live keys, webhook URL + secret, payment methods enabled.
- [ ] GHCR read token on the VPS; GitHub environments and secrets.
- [ ] DNS for the four store names; edge store block deployed.
- [ ] First staging deploy, a sandbox donation, its refund; then the first production tag and a **live 1-SAR donation
      and its refund**.
- [ ] Backups in cron; the first restore test run.
- [ ] Receipt settings: licence number, texts (Q-02 VAT note, Q-11 receipt format), stamp image.
