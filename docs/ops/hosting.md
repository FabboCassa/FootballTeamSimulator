# Getting a host

Roadmap 10.4. `docs/ops/deploy.md` assumes a server exists. This is how to get one, and what it has to
be. Prices were checked in August 2026 and move — treat them as the right order of magnitude, not as
quotes.

---

## What the machine actually has to do

Not a guess: the 9.6 load test measured it.

- **The matchday job resolves 500 fixtures across 125 groups in 5.0 seconds** on an idle server — 10ms
  per fixture, against a 60-second window. Twelve times inside budget.
- The ugly numbers in that phase (1.6s per fixture, p95 754ms at 1,000 coaches) were **one laptop being
  the load generator, the API, PostgreSQL and Redis at once**, serving 1,079 req/s at its ceiling. That
  is a measurement of the laptop, not of the software.
- A real ranked world is 56 seats = 7 groups of 8 = **28 fixtures per matchday**. The load test's 500
  was eighteen worlds' worth, fired within the same second.

So for an open beta this is a small workload. The sizing below is about **headroom and disk**, not
throughput.

### Disk is the one that grows

`ReplayJson` stores the full `MatchReport` including the position stream: **~180 KB per fixture**
(9.6 measured it). Per world:

| | fixtures | replay storage |
|---|---|---|
| one matchday | 28 | ~5 MB |
| one 14-matchday season | 392 | ~70 MB |
| one world, a year of daily matchdays | ~10,200 | **~1.8 GB** |

Ten worlds running for a year is ~18 GB, plus the database's own overhead and whatever local backups you
keep. That is the number to watch, and it is why `ReplayJson` is a standing optimisation candidate
(9.6 carried it forward: the stream is derived deterministically from seed + lineups, so the client
could regenerate it instead of downloading it).

---

## The spec

**Start here: 4 vCPU / 8 GB RAM / 80 GB NVMe.**

Why not smaller: five containers share the box, and PostgreSQL wants real RAM to keep the working set in
cache. `api` and `worker` are two .NET processes; the matchday tick is a CPU spike on top of whatever
the players are doing. 2 vCPU / 4 GB will run it, and will also be the thing you blame when a tick and a
backup overlap.

Why not bigger: the measured job cost says you are nowhere near needing it. Scale when the watchdog
says so (`worstFixtureLagSeconds` climbing), not before — and the first move then is `--scale api=N`,
which the stack already supports.

**Where:** an EU region. Latency from Italy is the small reason; the store data-safety forms are the
real one — "where is player data stored" has a much shorter answer when everything is in one
jurisdiction.

**Roughly what it costs** (August 2026, shared-vCPU tier, EU): a 4 vCPU / 8 GB box is around
**€7–14 per month** depending on provider and on whether the price you are quoted includes VAT and the
IPv4 address. Published figures for the same Hetzner plans differed by nearly 2x between two comparison
sites while this was being written, so read the provider's own page before believing any of it. A `.com`
domain is around **$10–11 per year** at an at-cost registrar.

**Any provider works.** The requirements are exactly: Docker, a public IPv4, ports 80 and 443 reachable,
and ≥ 4 GB RAM. Nothing in the stack is provider-specific.

---

## Order of operations

The one that catches people is the admin bootstrap: `ADMIN_BOOTSTRAP_EMAIL` promotes an account that
must **already exist**. So the sequence is up → register → set it → restart, not the other way round.

### 1. Domain and DNS

Point an `A` record at the server's IPv4:

```
api.<yourdomain>    A    <server ip>
```

**If your DNS is behind Cloudflare, set that record to "DNS only" (grey cloud), not proxied.** Caddy
proves control of the name over port 80 to get its certificate, and a proxy in front intercepts that
exchange. You can move it behind a proxy later, deliberately, once the certificate exists.

Wait for it to resolve before starting the stack — `nslookup api.<yourdomain>` from your own machine.
Let's Encrypt rate-limits failed attempts, so a stack started too early costs you a wait.

### 2. The server

Fresh Debian or Ubuntu LTS. As root, once:

```bash
adduser fts && usermod -aG sudo fts
# copy your SSH key to the new user, then disable password login:
sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
systemctl restart ssh

ufw allow 22/tcp && ufw allow 80/tcp && ufw allow 443/tcp && ufw --force enable

curl -fsSL https://get.docker.com | sh
usermod -aG docker fts
```

If the provider has its own firewall (Hetzner Cloud Firewall, AWS security groups), open 22/80/443
there too — `ufw` does not know about it.

### 3. The code

The repository is private, so the server needs read access: a GitHub **deploy key** (read-only, one
repo) is the right shape.

```bash
ssh-keygen -t ed25519 -C "fts-prod" -f ~/.ssh/id_ed25519 -N ""
cat ~/.ssh/id_ed25519.pub      # -> GitHub repo Settings > Deploy keys > Add, read-only
git clone git@github.com:<you>/FootballTeamSimulator.git
cd FootballTeamSimulator/server
```

### 4. Secrets

```bash
cp .env.prod.example .env.prod
openssl rand -base64 48      # JWT_SIGNING_KEY
openssl rand -base64 48      # INTEGRITY_SIGNAL_SALT
openssl rand -base64 32      # POSTGRES_PASSWORD
nano .env.prod               # fill FTS_DOMAIN, FTS_ACME_EMAIL, the three above; leave ADMIN_BOOTSTRAP_EMAIL empty for now
chmod 600 .env.prod
```

**Back `.env.prod` up somewhere off the server** — same place as the Android keystore. Losing
`POSTGRES_PASSWORD` with the volume intact is recoverable; losing it with only a `pg_dump` is not.

### 5. Up

```bash
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
docker compose -f docker-compose.prod.yml logs -f caddy    # watch the certificate arrive
curl https://api.<yourdomain>/health                        # -> role "api"
```

The first build takes a few minutes (it compiles Sim.Core + the server inside the image).

### 6. The first admin

Register an account — through the game client, or directly:

```bash
curl -X POST https://api.<yourdomain>/auth/register \
  -H "content-type: application/json" \
  -d '{"email":"you@example.com","password":"...","displayName":"..."}'
```

Then put that email in `ADMIN_BOOTSTRAP_EMAIL` and restart:

```bash
nano .env.prod
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d
docker compose -f docker-compose.prod.yml logs api | grep "Granted the admin role"
```

### 7. The preflight

From **Windows**, not the server — the script is PowerShell and it is supposed to look at the
deployment from outside, the way a player would:

```powershell
.\tools\preflight-launch.ps1 -BaseUrl https://api.<yourdomain> `
    -AdminEmail you@example.com -AdminPassword <...> `
    -WebOrigin https://<web build origin>
```

Green, and 10.4a is done. It will still list the manual items — reading a backup back, the integrity
salt — because it cannot settle those from outside.

### 8. Before you tell anyone the address

```bash
.\tools\backup-db.ps1        # and restore it once into a side database
.\tools\watchdog.ps1 -Once   # then put it on a schedule
```

---

## What this setup is not

- **It is one machine.** Losing it loses the ladder until a backup is restored elsewhere. Fine for a
  beta, and worth saying out loud rather than discovering.
- **Backups sit on the same disk** until you move them. A backup that dies with the server is not one.
- **The 9.6 p95 has not been re-measured on a real host** with the generator off-box. That is still
  open, and this server is the place to finally do it — now with the worker split out, which the
  original numbers did not have.
