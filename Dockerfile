# syntax=docker/dockerfile:1
# Two images from one build:
#   docker build --target api     -t ta3lom-store-api .       the web host (+ the create-admin CLI)
#   docker build --target migrate -t ta3lom-store-migrate .   psql + the idempotent migration script (no Database.Migrate())
# CI (deploy.yml) builds both with --build-arg VERSION/GIT_SHA and pushes them to GHCR; the VPS only pulls (F-D7).
# Smoke: docker run --rm ta3lom-store-api self-check   (renders a receipt PDF: QuestPDF natives + embedded Arabic font)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt BannedSymbols.Money.txt Ta3lom.Store.slnx ./
COPY .config/ .config/
COPY src/ src/
# Optional build secret `build_ca`: an extra CA bundle for networks that inspect TLS (never set in CI; build stage only,
# nothing of it reaches the shipped images): docker build --secret id=build_ca,src=/path/ca.pem …
RUN --mount=type=secret,id=build_ca,required=false \
    if [ -s /run/secrets/build_ca ]; then cp /run/secrets/build_ca /usr/local/share/ca-certificates/build_ca.crt && update-ca-certificates; fi \
 && dotnet tool restore \
 && dotnet restore src/Ta3lom.Store.Api/Ta3lom.Store.Api.csproj
RUN dotnet publish src/Ta3lom.Store.Api/Ta3lom.Store.Api.csproj -c Release --no-restore -o /out/api \
 && dotnet ef migrations script --idempotent -p src/Ta3lom.Store.Infrastructure -s src/Ta3lom.Store.Infrastructure \
      -o /out/migrate.sql

FROM postgres:18 AS migrate
ARG VERSION=dev
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="ta3lom-store-migrate" \
      org.opencontainers.image.description="ta3lom store API — idempotent migration job" \
      org.opencontainers.image.source="https://github.com/lotfi029/ta3lom_store_api" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.licenses="UNLICENSED"
COPY --from=build /out/migrate.sql /migrate.sql
# psql only: run as the image's non-root `postgres` user (no server is started).
USER postgres
# PGHOST, PGDATABASE, PGUSER come from compose; the password from the db_password secret file.
CMD ["sh", "-c", "PGPASSWORD=\"$(cat /run/secrets/db_password)\" psql -v ON_ERROR_STOP=1 -q -f /migrate.sql"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
ARG VERSION=dev
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="ta3lom-store-api" \
      org.opencontainers.image.description="ta3lom store API (.NET 10)" \
      org.opencontainers.image.source="https://github.com/lotfi029/ta3lom_store_api" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.licenses="UNLICENSED"
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /srv/store/keys /srv/store/files \
 && chown -R app:app /srv/store
WORKDIR /app
COPY --from=build /out/api .
# Read-only root filesystem compatible: the app writes only to the two volumes and /tmp (a tmpfs in compose).
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
USER app
EXPOSE 8080
VOLUME ["/srv/store/keys", "/srv/store/files"]
HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=3 CMD curl -fsS http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Ta3lom.Store.Api.dll"]
