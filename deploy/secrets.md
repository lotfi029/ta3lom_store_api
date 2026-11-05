# Secrets (production and staging)

Secrets live on the VPS only, in `deploy/secrets/<environment>/` (git-ignored), one folder per compose project
(`SECRETS_DIR` in `.env.production` / `.env.staging`). Never commit them, paste them into a chat or an issue, or bake
them into an image. The API reads one file per setting from `/run/secrets` (`SecretFilesSetup`): the **file name is
the setting** with `__` between sections, the **content is the value** (a trailing newline is ignored).

```
deploy/secrets/production/            (deploy/secrets/staging/ has the same files with its own values)
  db_password                                  # PostgreSQL password of the `store` user (db + migrate)
  api/
    ConnectionStrings__Store                   # Host=db;Database=ta3lom_store;Username=store;Password=<db_password>;Search Path=store,public
    Store__Jwt__CustomerSigningKeyBase64       # 32+ random bytes, base64
    Store__Jwt__StaffSigningKeyBase64          # 32+ random bytes, base64, different from the customer key
    Store__Otp__PepperBase64                   # 32+ random bytes, base64
    Store__Tokens__StatusKeyBase64             # 32+ random bytes, base64
    Store__Hashing__KeyBase64                  # 32+ random bytes, base64
    Store__Sms__AppSid                         # Unifonic AppSid
    Store__Email__Password                     # SMTP password of SMTP_USER
    Store__SiteRebuild__Token                  # GitHub fine-grained token: Contents read/write on the web repo (repository_dispatch)
    Store__Payments__Moyasar__SecretKey        # Moyasar SECRET key: sk_live_… in production, sk_test_… in staging
    Store__Payments__Moyasar__WebhookSecret    # the webhook "secret token" set in the Moyasar dashboard (≥ 16 chars, random)
```

The publishable key (`pk_…`) is not used: the donor pays on Moyasar's hosted invoice page, so no key reaches the web.

Generate them once per environment on the server:

```bash
cd deploy
ENV=production   # or staging
mkdir -p "secrets/$ENV/api" && umask 077
openssl rand -base64 32 | tr -d '/+=' > "secrets/$ENV/db_password"
printf 'Host=db;Database=ta3lom_store;Username=store;Password=%s;Search Path=store,public' "$(cat "secrets/$ENV/db_password")" > "secrets/$ENV/api/ConnectionStrings__Store"
for k in Jwt__CustomerSigningKeyBase64 Jwt__StaffSigningKeyBase64 Otp__PepperBase64 Tokens__StatusKeyBase64 Hashing__KeyBase64; do
  openssl rand -base64 48 > "secrets/$ENV/api/Store__$k"
done
openssl rand -hex 32 > "secrets/$ENV/api/Store__Payments__Moyasar__WebhookSecret"   # paste the same value in the Moyasar dashboard
read -rs MOYASAR_KEY && printf '%s' "$MOYASAR_KEY" > "secrets/$ENV/api/Store__Payments__Moyasar__SecretKey"; unset MOYASAR_KEY
read -rs SMS && printf '%s' "$SMS" > "secrets/$ENV/api/Store__Sms__AppSid"; unset SMS
read -rs SMTP && printf '%s' "$SMTP" > "secrets/$ENV/api/Store__Email__Password"; unset SMTP
read -rs GH && printf '%s' "$GH" > "secrets/$ENV/api/Store__SiteRebuild__Token"; unset GH
# Review 04 H-2 — owners and modes (umask 077 above made everything root-only):
sudo chown 999:999 "secrets/$ENV/db_password" && sudo chmod 0400 "secrets/$ENV/db_password"   # postgres + migrate run as uid 999
sudo chown -R 1654:1654 "secrets/$ENV/api"   # the API image runs as user `app` (uid 1654)
sudo chmod 0500 "secrets/$ENV/api" && sudo find "secrets/$ENV/api" -type f -exec chmod 0400 {} +
```

## Showcase (deploy without payments)

While the env file says `PAYMENTS_PROVIDER=none` and `CUSTOMER_SIGNIN=false` (README.md "Showcase mode"), these
files are **not needed** — skip them (the `read -rs MOYASAR_KEY` and `read -rs SMS` lines above) and do not create
empty ones:

```
  api/
    Store__Payments__Moyasar__SecretKey        # not needed in showcase
    Store__Payments__Moyasar__WebhookSecret    # not needed in showcase
    Store__Sms__AppSid                         # not needed in showcase (and SMS_SENDER_ID may stay empty)
```

Every other file above is still required (database, the five keys, SMTP password, site-rebuild token). When payments
are switched on, write the three files first, then change the env file and `up -d api`.

CI proves these exact steps on every push: job `stack` (`deploy/stack-check.sh`, once live and once with
`STACK_MODE=showcase` without the three files above) generates the files this way, starts
db + migrate + api from `compose.yaml` and waits for `/health/ready`. A `db_password` left root-only fails there with
`cat: /run/secrets/db_password: Permission denied` in the migrate log.

- The API refuses to start when a key is missing, shorter than 32 bytes, equal to a DEV ONLY value, when the customer
  and staff keys are equal, when the Moyasar secret key is missing or of the wrong mode (live keys only in Production,
  test keys elsewhere), or when the webhook secret is missing, shorter than 16 characters or a DEV ONLY value.
- **Moyasar dashboard** (per environment): Settings → Webhooks → add `https://<API_DOMAIN>/v1/payments/webhooks/moyasar`,
  method POST, the secret token = the content of `Store__Payments__Moyasar__WebhookSecret`, events: payment paid, failed,
  voided, refunded (or all).
- **Keep a backup** of `secrets/` and of the `api-keys` volume (Data Protection key ring) in a safe place
  (`deploy/backup.sh` archives the volume; the secrets folder is backed up by the owner, offline).
- Rotating a key: replace the file, then `docker compose --env-file .env.$ENV -p <project> -f compose.yaml up -d api`.
  Rotating the webhook secret: write the new file, restart the API, then update the dashboard (Moyasar retries a refused
  delivery five times; reconciliation settles anything missed).
