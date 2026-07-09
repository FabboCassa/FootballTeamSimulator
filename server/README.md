# FTS Backend (Phase 7)

ASP.NET Core host (`Api`) + use-cases (`Application`) + EF Core / Redis (`Infrastructure`),
all running the same `Sim.Core` DLL as the client (server-authoritative, anti-cheat).

## Layout

- `Api/` — thin host: `/health` (liveness + Sim.Core version), `/health/ready`
  (PostgreSQL migrated + Redis reachable), and the `/auth/*` endpoints (7.2). Auto-applies EF
  migrations on startup; JWT bearer authentication configured from the `Jwt` config section.
- `Application/` — use-case contracts (`Auth/` DTOs + `IAuthService`).
- `Infrastructure/` — `FtsDbContext` (PostgreSQL + ASP.NET Core Identity), the core schema
  entities, the `Auth/` implementation (Identity + JWT + rotating refresh tokens), Redis
  multiplexer, health checks.
- `docker-compose.yml` — PostgreSQL 17, Redis 8, and the API.

## Auth (7.2)

ASP.NET Core Identity (users/passwords, `Guid` keys) + JWT access tokens + **rotating refresh
tokens** (only a SHA-256 hash is stored; the raw token is returned once). Single-player stays
offline-first — an account is only for the online phases. Endpoints:

| Method + path    | Body                                   | Result |
|------------------|----------------------------------------|--------|
| POST `/auth/register` | `{ email, password, displayName }` | `200` token pair + profile · `409` email in use · `400` weak password |
| POST `/auth/login`    | `{ email, password }`              | `200` token pair + profile · `401` invalid credentials |
| POST `/auth/refresh`  | `{ refreshToken }`                 | `200` new token pair (old refresh token revoked) · `401` invalid/expired |
| POST `/auth/logout`   | `{ refreshToken }`                 | `204` (revokes the refresh token) |
| GET  `/auth/me`       | — (Bearer access token)            | `200` `{ userId, email, displayName }` · `401` |

Password policy: ≥8 chars, upper + lower + digit (symbol optional). Access token lifetime
`Jwt:AccessTokenMinutes` (15), refresh `Jwt:RefreshTokenDays` (30).

**`Jwt:SigningKey` is a dev placeholder** in `appsettings.json` / `docker-compose.yml` — override
via the `Jwt__SigningKey` env var (min 32 bytes) in every real environment.

## Core schema (7.1)

`worlds → leagues → clubs → players`, plus `coaches` (nullable club, nullable owner user for
7.2 auth) and `transfers`. Players/clubs/coaches are unique per world (ARCHITECTURE §6.3).
Full `PlayerAttributes` are stored as `jsonb`; overall/potential/age/role/value are
denormalised columns for querying. Later tables (seasons, fixtures, match_reports, auctions,
bids, transactions, rankings, notifications) land as additive migrations in Phases 7.2 / 8.x.

## Migrations — generate before `docker compose up`

The auto-migrate on startup applies whatever migrations are compiled into the image, so any new
migration must be generated and committed **before** the next `docker compose up --build`.

```powershell
# from server/ — needs the EF tool (once): dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p Infrastructure -s Api   # 7.1 (once, if not done)
dotnet ef migrations add AddAuth       -p Infrastructure -s Api   # 7.2 Identity + auth tables
```

Each scaffolds `Infrastructure/Migrations/*` (offline — no database needed). Commit those files.
`AddAuth` is additive on top of `InitialCreate` (users/roles + coach_profiles + refresh_tokens).

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
dotnet test Api.Tests/Api.Tests.csproj   # health endpoint + full auth flow
```

Auth tests run the real HTTP pipeline against an in-memory SQLite DB (no PostgreSQL needed):
register → login → `/auth/me` → refresh (rotation) → logout, plus the failure cases.

### Try the auth flow by hand

```powershell
curl -X POST http://localhost:8080/auth/register -H "Content-Type: application/json" `
  -d '{"email":"me@example.com","password":"Password1","displayName":"Mister"}'
# → { accessToken, refreshToken, expiresInSeconds, profile:{ userId, email, displayName } }
curl http://localhost:8080/auth/me -H "Authorization: Bearer <accessToken>"
```
