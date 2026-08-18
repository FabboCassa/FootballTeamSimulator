# Deploying the FTS backend

Roadmap 10.4. This is the production counterpart to `server/docker-compose.yml`, which is and stays the
LOCAL DEV stack: plain HTTP, a dev signing key in the file, the dev endpoints on. Nothing in this
document points at that file.

The runbook (`docs/ops/runbook.md`) is for a server that is already up and misbehaving. This is for
standing one up. If you do not have a server yet, `docs/ops/hosting.md` comes first: what the machine
has to be (sized off the 9.6 measurements), what it costs, and the fresh-box bring-up down to the
firewall rules and the deploy key.

---

## What runs

Five containers on one host, from `server/docker-compose.prod.yml`:

| service | what it is | published |
|---|---|---|
| `caddy` | TLS termination, Let's Encrypt certificate, reverse proxy | 80, 443 |
| `api` | the player-facing API, `Jobs__Role=api` | nothing |
| `worker` | the Hangfire scheduler, `Jobs__Role=worker` | nothing |
| `postgres` | the database | nothing |
| `redis` | cache / presence | nothing |

Only Caddy is reachable from the internet. That is what makes the `X-Forwarded-For` reading in the 9.5
integrity signals and in the anonymous rate-limit bucket trustworthy: nothing can reach the API without
passing through a proxy that overwrites the header.

`api` and `worker` are the **same image** with a different `Jobs__Role`. One build, one migration path,
one version to reason about. The split is the 9.6 load test's standing recommendation — while the
scheduler shares a process with the API, matchday resolution and player requests compete for the same
CPU, and the measured cost per fixture climbed accordingly.

Migration ownership is explicit: `api` has `Database__AutoMigrate=true` and `worker` has it false and
waits for `api` to report healthy. Two processes never race the same migration.

---

## Standing it up

```powershell
cd server
cp .env.prod.example .env.prod      # then fill in EVERY value
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
```

Before that works:

1. **DNS.** `FTS_DOMAIN` must already resolve to this host, and ports 80 and 443 must reach it. Caddy
   proves control of the name over port 80; without that there is no certificate.
2. **Secrets.** `JWT_SIGNING_KEY` and `INTEGRITY_SIGNAL_SALT` must be real random values. The defaults
   that ship in `appsettings.json` are public — anyone with this repository can mint tokens against a
   server still using them. `tools\preflight-launch.ps1` tests exactly this and fails the launch on it.
3. **The first admin.** There is no API path to the first admin by design. Register an account through
   the game, put its email in `ADMIN_BOOTSTRAP_EMAIL`, and restart: the role is granted at startup,
   idempotently, and logged. Clear the variable once a second admin exists.
4. **Push (optional).** Put the Firebase service-account key at `server/secrets/fcm.json` (gitignored,
   never in the image) and set `FCM_ENABLED=true`. Both `api` and `worker` mount it — the outbid and
   kickoff pushes are sent from background jobs, so the worker needs it too.

Then:

```powershell
.\tools\preflight-launch.ps1 -BaseUrl https://<FTS_DOMAIN> `
    -AdminEmail <the bootstrap account> -AdminPassword <...> `
    -WebOrigin https://<the web build's origin> `
    -EnvFile .\server\.env.prod -BackupPath .\backups
```

Exit code 0 or the launch does not happen. What it cannot settle by itself it prints as a MANUAL list
rather than passing quietly.

---

## Verifying by hand

```powershell
# the public name is the API, not the scheduler
curl https://<FTS_DOMAIN>/health          # -> status ok, role "api", the version being shipped
curl https://<FTS_DOMAIN>/health/ready    # -> Healthy (PostgreSQL migrated, Redis answering)

# the worker is alive but is not an API
docker compose -f docker-compose.prod.yml exec worker curl -s localhost:8080/health   # role "worker"
docker compose -f docker-compose.prod.yml exec worker curl -s -o /dev/null -w "%{http_code}" localhost:8080/auth/me   # 404

# the dev doors are gone
curl -X POST https://<FTS_DOMAIN>/internal/dev/test-league   # 404
curl https://<FTS_DOMAIN>/hangfire                            # 404
```

`role` on `/health` is the quickest way to tell an API replica from the scheduler without shelling in.

---

## Scaling

`docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --scale api=3` — the api role
holds no scheduling state and Caddy load-balances across the replicas, taking any that fails `/health`
out of rotation.

**The worker stays at 1.** The recurring jobs are minutely and the ranked calendar tick is not designed
to be raced by a second scheduler. If one worker ever stops being enough, the fix is Hangfire queues,
not a second copy of the same recurring registrations.

A balance push reaches every replica within a minute through `BalanceReloadJob`, and `/admin/metrics`
reports the revision of the instance that answered, so skew is visible rather than silent.

---

## Upgrading

1. `.\tools\set-version.ps1 -Version x.y.z -BumpBuild`, commit, build.
2. Set `FTS_VERSION` in `.env.prod` to the same value — the image is tagged with it, so a rollback is
   `docker compose up` with the previous tag rather than a rebuild from a guessed commit.
3. `docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build`.
4. `.\tools\preflight-launch.ps1 -BaseUrl https://<FTS_DOMAIN>` — the version check is the point: it
   compares what answers with `tools/version.json`.

**Take a backup first** (`.\tools\backup-db.ps1`). A migration is the one deployment step that is not
trivially reversible.

---

## Backups

`tools\backup-db.ps1` and `tools\restore-db.ps1` work INSIDE the database container over the local
socket, so they need no published port and no password. Nothing about that changes here — the prod
stack simply makes it the only option, which was already the better one.

Schedule the backup, and read one back into a side database periodically. A backup nobody has restored
is a hope, not a backup; the preflight says so as a MANUAL item every time it runs.

---

## What is still not automated

- **A second host.** Everything above is one machine. Losing it loses the ladder until the backup is
  restored somewhere else.
- **Log shipping.** Caddy logs JSON to stdout and the app logs to stdout; `docker compose logs` is the
  whole story today.
- **Alerting.** `tools\watchdog.ps1 -Once` has an exit code and an optional webhook, but something has
  to run it on a schedule. That something is not in this repository.
