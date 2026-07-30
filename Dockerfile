# Build/publish needs the full SDK; the final process needs only the ASP.NET runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy dependency descriptors first so Docker can reuse restore when only source changes.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/TradingRisk.Domain/TradingRisk.Domain.csproj src/TradingRisk.Domain/
COPY src/TradingRisk.Application/TradingRisk.Application.csproj src/TradingRisk.Application/
COPY src/TradingRisk.Infrastructure/TradingRisk.Infrastructure.csproj src/TradingRisk.Infrastructure/
COPY src/TradingRisk.Api/TradingRisk.Api.csproj src/TradingRisk.Api/
RUN dotnet restore src/TradingRisk.Api/TradingRisk.Api.csproj

COPY src/ src/
# Publish assembles the deployable layout; tests are built and run separately in CI.
RUN dotnet publish src/TradingRisk.Api/TradingRisk.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Multi-stage copy keeps compilers, NuGet caches, and source out of the runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# APP_UID is defined by Microsoft's Linux image; the service does not run as root.
USER $APP_UID
EXPOSE 8080
# Framework-dependent deployment starts the managed entry assembly with dotnet.
ENTRYPOINT ["dotnet", "TradingRisk.Api.dll"]
