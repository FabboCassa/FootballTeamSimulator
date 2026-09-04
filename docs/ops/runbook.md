# Live-ops runbook

Roadmap 10.3. What to do when the ladder is live and something needs looking at.

This is written to be read at 3am by whoever is on call, so it is short and in the order you would
actually need it. Anything that needs a paragraph of justification is in the code comments, not here.

---

## The four things that go wrong

| Symptom | Where you see it | What it means |
|---|---|---|
| The API does not answer | `GET /health` fails | The container is down or the host is gone. |
| **The ladder has stopped moving** | `worstFixtureLagSeconds` climbing | The Hangfire worker is starved, stopped, or the tick is throwing. The API stays perfectly healthy while this happens — that is why it is monitored rather than noticed. |
| Jobs are failing | `failedJobs > 0` | Look at the Hangfire dashboard (`/hangfire`, non-production only). |
| A tuning change did not reach everyone | `balanceRevision` differs from the stored revision | An instance has not reloaded. It should close itself within a minute; if it does not, that instance cannot reach the database. |

`tools/watchdog.ps1` checks exactly these four and nothing else. Run it on a schedule:

```powershell
.\tools\watchdog.ps1 -BaseUrl https://api.example.com -Email ops@example.com -Password $env:FTS_ADMIN_PW -Once
```

Exit code 0 = fine, 1 = something alerted. Pass `-Webhook https://hooks.slack.com/...` to get a message.

The lag threshold defaults to **600s**. The 9.6 load test measured the matchday job at 5.0s for 500
fixtures across 125 groups, so 600s is 120× its measured cost — if it trips, the job is not slow, it is
not running.

---

## Getting in

The dashboard is `web/admin.html`. It is **not published with the game** (`tools/deploy-web.ps1` skips it
unless you pass `-IncludeAdmin`). Run it locally:

```powershell
cd web
python -m http.server 8090
# then open http://localhost:8090/admin.html
```

The server must allow that browser origin, or the page cannot call it at all:

```
Cors__AllowedOrigins__0=http://localhost:8090
```

The page holds no secrets — the gate is the `admin` Identity role, checked on the server for every single
request. A signed-in account without the role gets **404**, not 403: there is no reason to confirm to a
curious player that an admin API exists.

### The first admin

There is no API path to it, deliberately. Register the account normally, then set on the server:

```
Admin__BootstrapEmail=ops@example.com
```

and restart. Startup puts that account in the role (idempotent, and it logs what it did). After that,
admins grant each other the role from the dashboard.

An admin cannot lock or demote **themselves** — both are refused, because undoing either would need a
second admin or a redeploy.

---

## Suspending an account

Dashboard → Accounts → search → **Lock**. You are asked for a reason; it goes in the audit trail.

What a lock actually does:

- sign-in returns **403 `account_locked`** (not 401 — a 401 would send the client into a refresh-and-retry
  loop that can never succeed);
- every refresh token the account holds is revoked, so the long-lived half of the session is dead
  immediately;
- **the access token they are already carrying stays valid until it expires** — up to 15 minutes by
  config. This is the one gap; it is small, bounded, and stated here rather than pretended away.

Their ranked seat is untouched: the coach stays in the division and their matches keep resolving on the
last lineup they submitted. Locking is not a way to remove someone from a season.

---

## Pushing a balance change

Dashboard → Balance. The textarea holds the **whole** config the server is simulating with. Edit it,
write a note, push.

Rules the server enforces, and why:

- **A push must carry every top-level section.** A fragment is refused with 400. `JsonSerializer` would
  happily accept `{"Match":{...}}` and hand back defaults for the other fourteen sections — a push that
  silently resets everything you did not mention is the worst failure this feature could have.
- **A push applies to what happens next.** Matches already resolved keep their stored reports, so replays
  are unaffected. A *live* match (8.6) being re-simmed from its seed will use the new numbers for the whole
  90' from the next input onwards — so do not push during a live window if you can avoid it.
- **Rollback moves forward.** Rolling back to revision 1 creates revision 3 carrying revision 1's numbers.
  History is never rewritten, so "what was live at 14:05?" is always a single `ORDER BY`.
- **Other instances catch up within a minute** (`fts-balance-reload`, a recurring job). Check
  `balanceRevision` on `/admin/metrics` to confirm.

World *generation* is deliberately **not** affected: `WorldFactory` still builds worlds from the balance
embedded in the build, so a seed keeps generating the same world forever and the golden master
`0xABC7B41DC6F258C2` stays meaningful. The push covers the live knobs — match, condition, development,
market, finance, tactics.

If a pushed revision turns out to be unreadable, every instance logs it and **stays on the balance it
already has**. It never falls back to defaults mid-season.

---

## Backups

```powershell
.\tools\backup-db.ps1                                   # dump + verify + prune (14 days)
.\tools\backup-db.ps1 -OutDir D:\backups -KeepDays 30
```

The script does not just dump — it reads the archive back with `pg_restore --list` and **fails** if the
table of contents is empty or unreadable. A backup nobody has read back is a hope, not a backup. It also
warns if the archive does not mention `users` / `ranked_coaches` / `balance_revisions`, which is what a
dump against the wrong database looks like.

Schedule it nightly. Keep the archives somewhere that is not the database host.

### The restore drill

Do this monthly. It takes two minutes and it is the only thing that turns backups into a recovery plan:

```powershell
.\tools\restore-db.ps1 -Archive .\backups\fts-fts-20260817-0300.dump
```

It restores into `fts_restore_test` (a side database, never the live one) and prints row counts for
accounts, ranked coaches, ranked fixtures, the audit trail and the highest balance revision. Compare them
against `/admin/metrics` on the running server. If they are wildly apart, the backup is stale or partial —
find out now, not during an incident.

Restoring over the live database requires naming it **and** `-Force`, and it warns and pauses first.

---

## Deploy-time reminders carried in from earlier phases

- ~~**Run the Hangfire worker on its own instance** (9.6)~~ — **DONE (10.4).** `Jobs__Role=api` /
  `Jobs__Role=worker` on the same image; `server/docker-compose.prod.yml` runs both. `GET /health`
  reports the role, so an operator can tell an API replica from the scheduler without shelling in.
  Scale `api` freely; the worker stays at ONE — the recurring jobs are minutely and the calendar tick
  is not designed to be raced.
- ~~**TLS in front of the API**~~ — **DONE (10.4).** Caddy terminates it in the production stack and
  renews by itself; nothing but Caddy publishes a port. See `docs/ops/deploy.md`.
- **Re-confirm p95 on a staging host** with the load generator off-box (9.6). `POST /ranked/today/confirm`
  is the heaviest endpoint and the first thing to look at. Still open — and now worth re-measuring with
  the worker split out, since the 9.6 numbers were taken with both in one process.
- `ReplayJson` stores the full `MatchReport` including the position stream (~180KB per fixture, ~108MB per
  ladder matchday). It is derived deterministically from seed + lineups, so the client could regenerate it
  — a standing optimisation candidate if storage or tick cost ever bites (9.6).

---

## Smoke test

After a deploy:

```powershell
.\tools\smoke-admin.ps1 -BaseUrl https://api.example.com -AdminEmail ops@example.com -AdminPassword ...
```

It walks the whole surface: the 404-for-non-admins property, metrics, closing and reopening a world,
locking an account and proving it cannot sign in, a balance push, a refused fragment, a rollback, and that
every one of those left an audit line. Exit code 0 = all checks passed.

Before a RELEASE (rather than after any deploy) run the launch preflight as well:

```powershell
.\tools\preflight-launch.ps1 -BaseUrl https://api.example.com -AdminEmail ops@example.com `
    -AdminPassword ... -WebOrigin https://<web origin> -EnvFile .\server\.env.prod -BackupPath .\backups
```

It asserts the section-1 blockers of `docs/store/release-checklist.md`: TLS + HSTS + the http redirect,
the shipped version, `role`, readiness, nine dev routes all 404, that no signing key from this repository
is accepted, CORS, and the age of the newest backup. Exit code 0 or the release does not go out.
