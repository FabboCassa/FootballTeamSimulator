# Multi-stage build for the FTS API. Build context is the REPO ROOT (see docker-compose)
# because the server references the shared Sim.Core / Contracts projects under shared/.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer-cached) — copy just the project files the Api graph needs.
COPY shared/Sim.Core/Sim.Core.csproj shared/Sim.Core/
COPY shared/Contracts/Contracts.csproj shared/Contracts/
COPY server/Application/Application.csproj server/Application/
COPY server/Infrastructure/Infrastructure.csproj server/Infrastructure/
COPY server/Api/Api.csproj server/Api/
RUN dotnet restore server/Api/Api.csproj

# Copy the sources and publish.
COPY shared/ shared/
COPY server/ server/
RUN dotnet publish server/Api/Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl for the compose container healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
