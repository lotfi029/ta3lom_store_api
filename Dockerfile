# syntax=docker/dockerfile:1
# Two images from one build:
#   docker build --target api     -t ta3lom-store-api .       the web host (+ the create-admin CLI)
#   docker build --target migrate -t ta3lom-store-migrate .   psql + the idempotent migration script (no Database.Migrate())
# deploy/compose.yaml builds both.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt BannedSymbols.Money.txt Ta3lom.Store.slnx ./
COPY .config/ .config/
COPY src/ src/
RUN dotnet tool restore \
 && dotnet restore src/Ta3lom.Store.Api/Ta3lom.Store.Api.csproj
RUN dotnet publish src/Ta3lom.Store.Api/Ta3lom.Store.Api.csproj -c Release --no-restore -o /out/api \
 && dotnet ef migrations script --idempotent -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure \
      -o /out/migrate.sql

FROM postgres:18 AS migrate
COPY --from=build /out/migrate.sql /migrate.sql
# PGHOST, PGDATABASE, PGUSER come from compose; the password from the db_password secret file.
CMD ["sh", "-c", "PGPASSWORD=\"$(cat /run/secrets/db_password)\" psql -v ON_ERROR_STOP=1 -q -f /migrate.sql"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /srv/store/keys /srv/store/files \
 && chown -R app:app /srv/store
WORKDIR /app
COPY --from=build /out/api .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
USER app
EXPOSE 8080
VOLUME ["/srv/store/keys", "/srv/store/files"]
HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=3 CMD curl -fsS http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Ta3lom.Store.Api.dll"]
