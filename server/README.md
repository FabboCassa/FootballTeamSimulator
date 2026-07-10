# FTS Backend (Phase 7)

ASP.NET Core host (`Api`) + use-cases (`Application`) + EF Core / Redis (`Infrastructure`),
all running the same `Sim.Core` DLL as the client (server-authoritative, anti-cheat).

## Layout

- `Api/` — thin host: `/health` (liveness + Sim.Core version), `/health/ready`
  (PostgreSQL migrated + Redis reachable), the `/auth/*` endpoints (7.2), the internal
  `/internal/sim/*` dev endpoints (7.3), and the `/notifications/*` device API + dev-only
  `/hangfire` dashboard and `/internal/jobs/*` diagnostics (7.4). Auto-applies EF migrations on
  startup; JWT bearer authentication configured from the `Jwt` config section.
- `Application/` — use-case contracts (`Auth/`, `Simulation/`, `Notifications/` DTOs + interfaces).
- `Infrastructure/` — `FtsDbContext` (PostgreSQL + ASP.NET Core Identity), the core schema
  entities, the `Auth/` implementation (Identity + JWT + rotating refresh tokens), the
  `Notifications/` device store + FCM sender, the `Jobs/` Hangfire jobs, Redis multiplexer,
  health checks.
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

## Notifications & background jobs (7.4)

**Hangfire** schedules background/recurring work with durable **PostgreSQL** storage (its own
`hangfire` schema, created on startup — reuses the DB, jobs survive restarts). A recurring
`fts-heartbeat` job (once a minute) proves the scheduler fires; Phase 8's real jobs (kickoff match
resolution, auction settlement, season rollover) join it. Disabled under the `Testing`
environment (unit tests use SQLite, no live Postgres).

**Push** goes through **Firebase Cloud Messaging** behind `INotificationService`. It is
**config-gated**: with `Fcm:Enabled=false` (default) sends are logged + skipped, so the server
builds and runs with no Firebase project. To go live, set `Fcm__Enabled=true` and provide a
service-account key via `Fcm__CredentialsPath` (file) or `Fcm__CredentialsJson` (env/secret).
Invalid/expired tokens FCM reports are pruned so the device table self-heals.

| Method + path | Auth | Body / result |
|---|---|---|
| POST `/notifications/devices` | Bearer | `{ token, platform }` → `200` device dto (idempotent upsert per token) |
| GET  `/notifications/devices` | Bearer | `200` the account's devices |
| DELETE `/notifications/devices/{token}` | Bearer | `204` removed · `404` not found |

Dev-only (never mapped in Production; also behind `Jobs:ExposeDashboard` / `Jobs:ExposeTestEndpoint`):

| Path | What |
|---|---|
| GET `/hangfire` | Hangfire dashboard (permissive auth — dev only) |
| POST `/internal/jobs/heartbeat` | enqueue an immediate heartbeat → `{ jobId }` |
| POST `/internal/jobs/push/{userId}` | enqueue a push to an account through a job → `{ jobId }` |

`platform` = `0` Android · `1` iOS · `2` Web.

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
dotnet ef migrations add InitialCreate           -p Infrastructure -s Api   # 7.1 (once, if not done)
dotnet ef migrations add AddAuth                 -p Infrastructure -s Api   # 7.2 Identity + auth tables
dotnet ef migrations add AddDeviceRegistrations  -p Infrastructure -s Api   # 7.4 device_registrations
```

Each scaffolds `Infrastructure/Migrations/*` (offline — no database needed). Commit those files.
`AddAuth` is additive on top of `InitialCreate` (users/roles + coach_profiles + refresh_tokens);
`AddDeviceRegistrations` is additive on top of that (the `device_registrations` table). Hangfire
creates its own `hangfire` schema at runtime — no EF migration for it.

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
dotnet test Api.Tests/Api.Tests.csproj   # health + auth flow + sim determinism + notifications/jobs
```

Auth tests run the real HTTP pipeline against an in-memory SQLite DB (no PostgreSQL needed):
register → login → `/auth/me` → refresh (rotation) → logout, plus the failure cases. The 7.4
tests cover device registration (JWT-gated upsert/list/unregister), the FCM sender's
skip-when-unconfigured behaviour, the heartbeat job, and that the dashboard/enqueue endpoints are
absent under `Testing`.

### Try a push by hand (dev)

```powershell
# register a device token for your account (get <accessToken> from /auth/login)
curl -X POST http://localhost:8080/notifications/devices -H "Authorization: Bearer <accessToken>" `
  -H "Content-Type: application/json" -d '{"token":"<fcm-device-token>","platform":0}'
# enqueue a push through a background job (logs+skips unless Fcm__Enabled=true with credentials)
curl -X POST http://localhost:8080/internal/jobs/push/<userId> -H "Content-Type: application/json" `
  -d '{"title":"FTS","body":"Test push"}'
# watch it run at http://localhost:8080/hangfire
```

### Try the auth flow by hand

```powershell
curl -X POST http://localhost:8080/auth/register -H "Content-Type: application/json" `
  -d '{"email":"me@example.com","password":"Password1","displayName":"Mister"}'
# → { accessToken, refreshToken, expiresInSeconds, profile:{ userId, email, displayName } }
curl http://localhost:8080/auth/me -H "Authorization: Bearer <accessToken>"
```
