# ta3lom store API

ASP.NET Core (.NET 10) Minimal API for the donations store of ta3lom.sa: a public catalogue of donation projects and
categories, a donation cart, donor sign-in by SMS code, checkout into `pending_payment` orders, and the staff admin.
PostgreSQL 18, EF Core 10. Payments, receipts and settlement are not part of this release.

## Layout

```
src/    SharedKernel ← Domain ← Application ← Infrastructure ← Api (composition root)
tests/  Domain.Tests · Application.Tests · Api.IntegrationTests (Testcontainers postgres:18) · ArchitectureTests · Testing
```

## Run locally

Needs the .NET 10 SDK and Docker. The API listens on `http://localhost:5080` (what the store web expects).

```bash
docker compose -f compose.dev.yaml up -d --wait                  # postgres:18 on 127.0.0.1:5440 (container t3s-pg)
dotnet tool restore
export ConnectionStrings__Store="Host=127.0.0.1;Port=5440;Database=ta3lom_store_dev;Username=postgres;Password=postgres;Search Path=store,public"
dotnet ef database update -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure   # uses ConnectionStrings__Store
dotnet run --project src/Ta3lom.Store.Api                          # Development, http://localhost:5080
```

Development uses DEV ONLY keys, sends SMS and e-mail to sinks (`GET /dev/sms`, `GET /dev/mail`), seeds three categories
and six demo projects, allows the web on `http://localhost:4200`, `:4201` and `:4320`, and serves OpenAPI at
`/openapi/v1.json` with Scalar at `/docs`. Health: `GET /health/live`, `GET /health/ready`. The store web repo's
`npm run api:up` does all of the above in one step.

Deployments apply migrations with the idempotent script, never at start-up (the `migrate` image of the `Dockerfile` runs it):

```bash
dotnet ef migrations script --idempotent -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure -o migrate.sql
```

## Deploy

Docker on one server: `deploy/compose.yaml` (PostgreSQL 18, migration job, API, Caddy with TLS and the static store
web). Runbook and go-live checklist: [deploy/README.md](deploy/README.md); secrets: [deploy/secrets.md](deploy/secrets.md).

## Create the first admin

```bash
dotnet src/Ta3lom.Store.Api/bin/Release/net10.0/Ta3lom.Store.Api.dll create-admin --email admin@ta3lom.sa --name "Store admin"
```

It prints a one-time password once. The first sign-in must change it and enrol an authenticator (TOTP).

## Configure

Environment variables (`__` separates sections) or one file per setting under `/run/secrets` (`STORE_SECRETS_DIR`;
file name = variable name, content = value; files win). Every environment except Development
refuses to start when a key is missing, shorter than 32 bytes or equal to its DEV ONLY value.

| Variable | Notes |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production`, `Staging` or `Development` |
| `ConnectionStrings__Store` | `Host=…;Database=ta3lom_store;Username=store_app;Password=…;Search Path=store,public` (the extensions live in `public`; start-up refuses a search path without it outside Development) |
| `Store__PublicBaseUrl`, `Store__WebBaseUrl` | API host and web store host (`https://store.ta3lom.sa`) |
| `Store__Cors__Origins__0…` | Allowed web origins (default: `https://store.ta3lom.sa`; Production refuses staging and plain http) |
| `Store__DataProtection__KeysPath` | Persistent key ring (TOTP secrets, guest cart cookie) |
| `Store__Seed__RunOnStartup` | Settings and message templates (idempotent) |
| `Store__Jwt__CustomerSigningKeyBase64`, `Store__Jwt__StaffSigningKeyBase64` | 32+ random bytes each, different |
| `Store__Otp__PepperBase64` | OTP hash pepper |
| `Store__Tokens__StatusKeyBase64` | HMAC key of the order status token |
| `Store__Hashing__KeyBase64` | HMAC key for IP and recipient hashes |
| `Store__Sms__Provider`, `Store__Sms__AppSid`, `Store__Sms__SenderId` | `sink` or `unifonic`; Production requires `unifonic` with AppSid and sender id |
| `Store__Email__Mode`, `__Host`, `__Port`, `__User`, `__Password`, `__From`, `__StaffAlerts` | `sink` or `smtp`; Production requires `smtp` with host, user and password |
| `Store__Files__Root`, `Store__Files__PublicBaseUrl` | Uploaded media (Staging and Production refuse a root that cannot be written) |
| `Store__SiteRebuild__Repository`, `Store__SiteRebuild__Token`, `Store__SiteRebuild__EventType` | `repository_dispatch` to the web repo after catalogue changes; required in Production, empty elsewhere = logged no-op |
| `Store__Telemetry__OtlpEndpoint` | Optional OpenTelemetry exporter |

Real values never go into the repository.

## Errors

Every error is an RFC 9457 problem document with a stable `code` (e.g. `cart.total_exceeded`) and, for `validation.failed`,
`errors` keyed by field. Messages (`title`, `detail`, validation messages) stay English; clients map by `code` and field,
never by message text.

## Test

```bash
dotnet build Ta3lom.Store.slnx -warnaserror
dotnet format Ta3lom.Store.slnx --verify-no-changes
dotnet test --solution Ta3lom.Store.slnx          # needs Docker for Testcontainers postgres:18
```

CI also enforces line coverage (Domain ≥ 90 %, Application ≥ 80 %) and fails on High/Critical vulnerable packages.
