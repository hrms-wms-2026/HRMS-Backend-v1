# Container image for the Crownus API (src/ONEVO.Api).
# Build from the repository root:   docker build -t crownus-api .
# Run notes, settings and health URLs: docs/deployment/container.md

# ---- build ---------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only, so the restore layer is cached until a
# package reference changes.
COPY src/ONEVO.Domain/ONEVO.Domain.csproj                 src/ONEVO.Domain/
COPY src/ONEVO.Application/ONEVO.Application.csproj       src/ONEVO.Application/
COPY src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj src/ONEVO.Infrastructure/
COPY src/ONEVO.Api/ONEVO.Api.csproj                       src/ONEVO.Api/
RUN dotnet restore src/ONEVO.Api/ONEVO.Api.csproj -r linux-x64

COPY src/ src/
RUN dotnet publish src/ONEVO.Api/ONEVO.Api.csproj \
        -c Release -r linux-x64 --self-contained false --no-restore \
        -p:UseAppHost=false \
        -o /app/publish

# ---- runtime -------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl is only for the HEALTHCHECK below; the runtime image has no HTTP client.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# One plain HTTP port. TLS ends at the load balancer, which must send X-Forwarded-* headers
# (configure ForwardedHeaders:KnownProxies / KnownNetworks for it).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1
EXPOSE 8080

# Non-root user shipped with the .NET images (uid 1654).
USER $APP_UID

# Liveness only: /health has no dependency checks. Use /health/ready for load-balancer readiness.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl --fail --silent --output /dev/null http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "ONEVO.Api.dll"]
