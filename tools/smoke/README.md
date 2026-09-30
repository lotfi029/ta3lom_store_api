# Smoke checks (Cloud-build-plan §2.7)

Plain `.http` files (VS Code REST Client / JetBrains HTTP client), one per session (`S-nn.http`) and one per
milestone (`M2.http` … `M7.http`). They are **not** tests: each session calls every new endpoint once
(success path + one business error) against a local API started with

```bash
tools/scripts/dev-db-migrate.sh --regenerate      # idempotent script → ta3lom_store_dev
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/Ta3lom.Store.Api
```

`@base` is `http://localhost:5080`. Recorded results live in PROGRESS.md.
