# Deploying the store (API + web) on one server

One Linux VPS with Docker runs everything through `deploy/compose.yaml`:

| Service | What it does |
|---|---|
| `db` | PostgreSQL 18, data in the `pg-data` volume, no published port |
| `migrate` | one-shot: applies the idempotent migration script (`Dockerfile` target `migrate`), then exits |
| `api` | the store API on `api:8080` (`Dockerfile` target `api`), starts only after `migrate` succeeded; key ring and uploaded files in the `api-keys` and `api-files` volumes |
| `caddy` | TLS (Let's Encrypt) for `store-api.ta3lom.sa` → `api`, and the static store web for `store.ta3lom.sa` |

> **The store web goes live only with R5-Payments (D-W2).** The web's `npm run build:prod` refuses to build while
> `paymentsEnabled` is `false`. Until then the API can run here on its own (staff admin, catalogue preparation), and
> `store.ta3lom.sa` answers 404.

## 1. Server, once

1. A VPS with Docker Engine and the compose plugin; ports 80 and 443 open; nothing else listening on them.
2. DNS: `A`/`AAAA` records for `store-api.ta3lom.sa` and `store.ta3lom.sa` pointing at the server.
3. Clone this repository (e.g. `/srv/ta3lom_store_api`) and create the settings:

   ```bash
   cd /srv/ta3lom_store_api/deploy
   cp .env.example .env          # edit: ACME_EMAIL, SMS_SENDER_ID, SMTP_USER, STORE_VERSION …
   ```

4. Create the secrets as described in [secrets.md](secrets.md) (`deploy/secrets/`, git-ignored). Back them up.
5. Create the web folder: `sudo mkdir -p /srv/store-web/site` (or set `WEB_DIR` in `.env`).

## 2. Deploy or update the API

```bash
cd /srv/ta3lom_store_api && git pull
cd deploy
docker compose build                      # images ta3lom-store-api / ta3lom-store-migrate, tag $STORE_VERSION
docker compose up -d                      # db → migrate → api → caddy
docker compose ps                         # migrate: exited (0); api: healthy
docker compose logs -f api                # JSON logs (no personal data)
```

`migrate` runs on every `up`; the script is idempotent and only applies missing migrations. If it fails, the API
is not started (or keeps the old container). A migration is never rolled back in production; fix forward with a new one.

Check:

```bash
curl -fsS https://store-api.ta3lom.sa/health/ready                 # 200
curl -fsS https://store-api.ta3lom.sa/v1/catalog/categories        # JSON
```

The API refuses to start (see `docker compose logs api`) when a secret is missing, too short or a DEV ONLY value,
when SMS is not Unifonic with AppSid + sender id, when SMTP is incomplete, when `Store:SiteRebuild` is missing, or when
the key ring or files volume is not writable.

## 3. First admin, once

```bash
docker compose run --rm --no-deps api create-admin --email admin@ta3lom.sa --name "Store admin"
```

It prints a one-time password once. At the first sign-in the admin enrols an authenticator (TOTP) and changes the
password. Then create the catalogue (categories, projects, media) in the admin.

## 4. Deploy or update the store web (after R5-Payments)

On a machine with Node 24, in the web repo (`lotfi029/ta3lom_store_web`):

```bash
npm ci
PRERENDER_API_URL=https://store-api.ta3lom.sa/v1 npm run build:prod     # needs the live API with ≥ 1 published project
rsync -a --delete dist/store-web/browser/ server:/srv/store-web/site/
rsync -a deploy/caddy-headers.conf server:/srv/store-web/caddy-headers.conf
ssh server 'cd /srv/ta3lom_store_api/deploy && docker compose exec caddy caddy reload --config /etc/caddy/Caddyfile'
```

`caddy-headers.conf` (CSP, HSTS, `no-referrer` on order pages) stays owned by the web repo: its build checks every
inline-script hash against it, so it must ship together with the build it belongs to.

## 5. Backups and operations

- **Database:** `docker compose exec db pg_dump -U store -Fc ta3lom_store > ta3lom_store-$(date +%F).dump` daily, kept
  off the server. Restore: `pg_restore -U store -d ta3lom_store --clean`.
- **Volumes:** `api-keys` (Data Protection key ring — losing it breaks enrolled authenticators and guest carts) and
  `api-files` (uploaded media). Back them up with the database.
- **Secrets:** `deploy/secrets/`, kept in a password manager or vault.
- **Client IPs:** the API trusts `X-Forwarded-For` only from the compose network (`172.30.0.0/24`, inside the
  configured `172.16.0.0/12`). Keep Docker's default iptables port publishing; with `userland-proxy` or an extra proxy
  in front, every donor would share one IP for the rate limits.
- **Staging:** set `STORE_ENVIRONMENT=Staging`, other domains in `.env`, and the staging origin
  (`https://store-staging.ta3lom.sa`) as `WEB_DOMAIN`. Staging accepts the SMS and e-mail sinks, but `/dev/sms` is not
  mapped there, so sign-in by SMS code needs Unifonic configured.

## Go-live checklist

- [ ] DNS for both hosts points at the server; `https://store-api.ta3lom.sa/health/ready` is 200 with a valid certificate.
- [ ] All secrets created from fresh random bytes (never a DEV ONLY value) and backed up; `.env` filled.
- [ ] Unifonic: approved sender id in `SMS_SENDER_ID`, AppSid secret; a test code reaches a real phone.
- [ ] SMTP: `SMTP_USER` + password secret; a template test mail arrives (admin → message templates).
- [ ] `Store__SiteRebuild__Token`: fine-grained GitHub token for the web repo; the web repo has a workflow that handles
      the `store-catalog-changed` dispatch (rebuild + publish).
- [ ] First admin created, authenticator enrolled, password changed; other staff created with their roles.
- [ ] Catalogue: categories and at least one published project with media.
- [ ] Database backup job and volume backups in place; one restore tried.
- [ ] **R5-Payments merged in API and web, `paymentsEnabled` true** — only then build and publish the store web (D-W2).
