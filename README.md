# ta3lom store API

ASP.NET Core (.NET 10) Minimal API for the donations store of ta3lom.sa: a public catalogue of donation projects and
categories, a donation cart, donor sign-in by SMS code, checkout into `pending_payment` orders, and the staff admin.
PostgreSQL 18, EF Core 10. Payments, receipts and settlement are not part of this release.

## Layout

```
src/    SharedKernel ← Domain ← Application ← Infrastructure ← Api (composition root)
tests/  Domain.Tests · Application.Tests · Api.IntegrationTests (Testcontainers postgres:18) · ArchitectureTests · Testing
```

## Run

```bash
dotnet tool restore
export ConnectionStrings__Store="Host=localhost;Database=ta3lom_store;Username=postgres;Password=<local password>"
dotnet ef database update -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure   # local database only
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Ta3lom.Store.Api
```

Development uses DEV ONLY keys, sends SMS and e-mail to sinks (`GET /dev/sms`, `GET /dev/mail`), seeds three categories
and six demo projects, and serves OpenAPI at `/openapi/v1.json` with Scalar at `/docs`. Health: `GET /health/live`,
`GET /health/ready`.

Deployments apply migrations with the idempotent script, never at start-up:

```bash
dotnet ef migrations script --idempotent -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure -o migrate.sql
```

## Create the first admin

```bash
dotnet src/Ta3lom.Store.Api/bin/Release/net10.0/Ta3lom.Store.Api.dll create-admin --email admin@ta3lom.sa --name "Store admin"
```

It prints a one-time password once. The first sign-in must change it and enrol an authenticator (TOTP).

## Configure

Environment variables (`__` separates sections) or files under `/run/secrets`. Every environment except Development
refuses to start when a key is missing, shorter than 32 bytes or equal to its DEV ONLY value.

| Variable | Notes |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production`, `Staging` or `Development` |
| `ConnectionStrings__Store` | `Host=…;Database=ta3lom_store;Username=store_app;Password=…;Search Path=store,public` (the extensions live in `public`; start-up refuses a search path without it outside Development) |
| `Store__PublicBaseUrl`, `Store__WebBaseUrl` | API host and web store host (`https://store.ta3lom.sa`) |
| `Store__Cors__Origins__0…` | Allowed web origins (default: store and store-staging) |
| `Store__DataProtection__KeysPath` | Persistent key ring (TOTP secrets, guest cart cookie) |
| `Store__Seed__RunOnStartup` | Settings and message templates (idempotent) |
| `Store__Jwt__CustomerSigningKeyBase64`, `Store__Jwt__StaffSigningKeyBase64` | 32+ random bytes each, different |
| `Store__Otp__PepperBase64` | OTP hash pepper |
| `Store__Tokens__StatusKeyBase64` | HMAC key of the order status token |
| `Store__Hashing__KeyBase64` | HMAC key for IP and recipient hashes |
| `Store__Sms__Provider`, `Store__Sms__AppSid`, `Store__Sms__SenderId` | `sink` or `unifonic` |
| `Store__Email__Mode`, `__Host`, `__Port`, `__User`, `__Password`, `__From`, `__StaffAlerts` | `sink` or `smtp` |
| `Store__Files__Root`, `Store__Files__PublicBaseUrl` | Uploaded media |
| `Store__SiteRebuild__Repository`, `Store__SiteRebuild__Token`, `Store__SiteRebuild__EventType` | `repository_dispatch` to the web repo after catalogue changes; required in Production, empty elsewhere = logged no-op |
| `Store__Telemetry__OtlpEndpoint` | Optional OpenTelemetry exporter |

Real values never go into the repository.

## Test

```bash
dotnet build Ta3lom.Store.slnx -warnaserror
dotnet format Ta3lom.Store.slnx --verify-no-changes
dotnet test --solution Ta3lom.Store.slnx          # needs Docker for Testcontainers postgres:18
```

CI also enforces line coverage (Domain ≥ 90 %, Application ≥ 80 %) and fails on High/Critical vulnerable packages.
