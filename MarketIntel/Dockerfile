# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer-cached) using only the project files the API needs.
COPY src/MarketIntel.Shared/MarketIntel.Shared.csproj src/MarketIntel.Shared/
COPY src/MarketIntel.Api/MarketIntel.Api.csproj src/MarketIntel.Api/
RUN dotnet restore src/MarketIntel.Api/MarketIntel.Api.csproj

# Copy the rest and publish.
COPY src/MarketIntel.Shared/ src/MarketIntel.Shared/
COPY src/MarketIntel.Api/ src/MarketIntel.Api/
RUN dotnet publish src/MarketIntel.Api/MarketIntel.Api.csproj -c Release -o /app/publish --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
# Basic container healthcheck against the readiness endpoint.
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
    CMD wget -qO- http://localhost:8080/api/ready || exit 1
ENTRYPOINT ["dotnet", "MarketIntel.Api.dll"]
