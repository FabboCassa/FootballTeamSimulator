# FTS Backend (Phase 7)

ASP.NET Core host (`Api`) + use-cases (`Application`) + EF Core / Redis (`Infrastructure`),
all running the same `Sim.Core` DLL as the client (server-authoritative, anti-cheat).

## Layout

- `Api/` — thin host: `/health` (liveness + Sim.Core version) and `/health/ready`
  (PostgreSQL migrated + Redis reachable). Auto-applies EF migrations on startup.
- `Application/` — use-case handlers (empty until 7.2+).
- `Infrastructure/` — `FtsDbContext` (PostgreSQL), the core schema entities, Redis
  multiplexer, health checks.
- `docker-compose.yml` — PostgreSQL 17, Redis 8, and the API.

## Core schema (7.1)

`worlds → leagues → clubs → players`, plus `coaches` (nullable club, nullable owner user for
7.2 auth) and `transfers`. Players/clubs/coaches are unique per world (ARCHITECTURE §6.3).
Full `PlayerAttributes` are stored as `jsonb`; overall/potential/age/role/value are
denormalised columns for querying. Later tables (seasons, fixtures, match_reports, auctions,
bids, transactions, rankings, notifications) land as additive migrations in Phases 7.2 / 8.x.

## First-time setup — generate the initial migration (once)

The auto-migrate on startup applies whatever migrations are compiled into the image, so the
`InitialCreate` migration must be generated and committed **before** the first `docker compose up`.

```powershell
# from server/ — needs the EF tool (once): dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p Infrastructure -s Api
```

This scaffolds `Infrastructure/Migrations/*` (offline — no database needed). Commit those files.

## Run

```powershell
# from server/
docker compose up -d --build
# API on http://localhost:8080
curl http://localhost:8080/health          # {"status":"ok","simCore":"…"}
curl http://localhost:8080/health/ready     # Healthy once postgres+redis are up & migrated
docker compose ps                           # fts-api should read "healthy"
```

`docker compose up` brings up PostgreSQL + Redis, waits for them to be healthy, builds and
starts the API, and the API applies the migration on startup ⇒ **DB migrated**.

### Running the API locally instead (against compose infra)

```powershell
docker compose up -d postgres redis
cd Api && dotnet run          # uses the localhost connection strings in appsettings.json
```

## Tests

```powershell
dotnet test Api.Tests/Api.Tests.csproj   # health endpoint (runs under the Testing env, no DB)
```
