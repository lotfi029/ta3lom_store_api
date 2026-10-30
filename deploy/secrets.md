# Production secrets

Secrets live on the server only, in `deploy/secrets/` (git-ignored). Never commit them, paste them into a chat or an
issue, or bake them into an image. The API reads one file per setting from `/run/secrets` (`SecretFilesSetup`): the
**file name is the setting** with `__` between sections, the **content is the value** (a trailing newline is ignored).

```
deploy/secrets/
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
```

Generate them once on the server:

```bash
cd deploy
mkdir -p secrets/api && umask 077
openssl rand -base64 32 | tr -d '/+=' > secrets/db_password
printf 'Host=db;Database=ta3lom_store;Username=store;Password=%s;Search Path=store,public' "$(cat secrets/db_password)" > secrets/api/ConnectionStrings__Store
for k in Jwt__CustomerSigningKeyBase64 Jwt__StaffSigningKeyBase64 Otp__PepperBase64 Tokens__StatusKeyBase64 Hashing__KeyBase64; do
  openssl rand -base64 48 > "secrets/api/Store__$k"
done
printf '%s' '<unifonic app sid>'   > secrets/api/Store__Sms__AppSid
printf '%s' '<smtp password>'      > secrets/api/Store__Email__Password
printf '%s' '<github token>'       > secrets/api/Store__SiteRebuild__Token
sudo chown -R 1654:1654 secrets/api   # the API image runs as user `app` (uid 1654)
```

- The API refuses to start when a key is missing, shorter than 32 bytes, equal to a DEV ONLY value, or when the customer
  and staff keys are equal.
- **Keep a backup** of `secrets/` and of the `api-keys` volume (Data Protection key ring) in a safe place. Losing the
  JWT keys signs everyone out; losing the key ring makes enrolled authenticators (staff TOTP) and guest carts unreadable.
- Rotating a key: replace the file, then `docker compose up -d api`.
