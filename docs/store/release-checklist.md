# Release checklist

Run through this on a release day. It is ordered so the cheap checks fail first.

---

## 0. Before anything

Most of this section and the per-store artwork rules are now MACHINE-CHECKED too (Roadmap 10.4b):

```powershell
.\tools\preflight-store.ps1                                  # everything
.\tools\preflight-store.ps1 -Platform Play -SiteUrl https://<site>
```

It settles the version agreement, the Android target SDK, the signing setup, every image's exact
pixel size and alpha channel, the Steam ids, and whether the published legal pages still say DRAFT.
Exit code 0 or the submission is not ready.

- [ ] `.\tools\build-simcore.ps1` — the client is running the current Sim.Core.
- [ ] `dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj` — 216 green, golden master
      `0xABC7B41DC6F258C2` unchanged (engine v6, engine rework phase 3).
- [ ] `cd server && dotnet test Api.Tests/Api.Tests.csproj` — green, and
      `[server-determinism] 0xABC7B41DC6F258C2` matches the client's.
- [ ] `.\tools\set-version.ps1 -Check` — every target agrees with `tools/version.json`.
      (`.\tools\preflight-store.ps1` asserts the same thing, and was written because they did
      not agree: `ProjectSettings.asset` said 1.0 while `version.json` said 0.1.0.)
- [ ] Bump if this is a new build: `.\tools\set-version.ps1 -Version x.y.z -BumpBuild`.
- [ ] Commit `ProjectSettings.asset`, `Api.csproj` and `version.json` together.

---

## 1. Blockers that apply to every store

These are not per-platform paperwork; without them a submission is refused or a launch is
irresponsible.

Most of this section is now MACHINE-CHECKED. Stand the production stack up per
`docs/ops/deploy.md`, then:

```powershell
.\tools\preflight-launch.ps1 -BaseUrl https://<domain> -AdminEmail <admin> -AdminPassword <...> `
    -WebOrigin https://<web build origin> -EnvFile .\server\.env.prod -BackupPath .\backups
```

Exit code 0 or the launch does not happen. What it cannot settle it prints as a MANUAL list rather
than passing quietly — the boxes below marked *(preflight)* are the ones it settles.

- [ ] **The shipped database size is decided by a MEASUREMENT, not a judgement** (Roadmap 11.1 →
      11.3). `DatabaseSizePreset.For`'s default is Medium because desktop numbers say it is
      comfortable — but the weakest targets decide it, and nobody has measured them. The tool
      exists and ships in development builds: **main menu → "Database bench (dev)"**
      (`DevFlags.WorldBench`), one preset per tap, printing generation time, managed heap, gzip
      save size, search-index build and a whole-world query, on screen and to the player log.
      Run it on a **WebGL build** and on a **mid-range Android phone**, for Small, Medium and
      Large, and compare against the desktop harness figures
      (`.\tools\balance.ps1 -Scenario world`: gen 2 / 7 / 20ms, save 186 / 559 / 1,372KB, a
      whole-world search 1.2 / 3.7 / 11.4ms). Two decisions come out of it: the default preset,
      and whether the search box needs a debounce (11.4ms per keystroke on desktop is 35-70ms on
      WebGL at Large). If a preset cannot be loaded on the weakest target, it must not be OFFERED
      there — a career's scope is immutable, so a player who picks Large on a phone is stuck with it.

- [x] **Account deletion** — in-app (Account → Delete account) and the public page
      `web/delete-account.html`, both on `POST /auth/account/delete` (Roadmap 10.2a).
      Still to do at deploy time: publish the page and set `Cors__AllowedOrigins__0` to the
      site's origin, or the browser form cannot call the API.
- [ ] **HTTPS in front of the API** *(preflight)*. `server/docker-compose.prod.yml` puts Caddy
      in front and it obtains/renews a Let's Encrypt certificate by itself; nothing else
      publishes a port. The preflight checks the certificate validates, that plain http
      redirects rather than serving the API, and that HSTS is set.
- [ ] **Privacy policy published at a stable URL** (not a document in a repository).
      The pipeline exists (Roadmap 10.4b): fill the placeholders in
      `docs/store/privacy-policy*.md` and `eula*.md`, run `.\tools\build-legal-pages.ps1`
      (it REFUSES while `[DATE]` / `[CONTACT EMAIL]` / the Draft banner are still there),
      then `.\tools\deploy-web.ps1 -PagesOnly -Deploy`. What is left is not code: a real
      contact address, a publication date, and a lawyer's read.
- [ ] **`Integrity__SignalSalt` overridden** with a real secret in the production
      environment — the default in `appsettings.json` is a placeholder. Not probeable from
      outside; the preflight checks the env file for it instead (`-EnvFile`).
- [ ] **`Jwt__SigningKey` overridden** with a real >= 32-byte secret *(preflight)*. The script
      mints a token with each signing key that ships in this repository and offers it to
      `/auth/me`: anything but a 401 and the launch stops there.
- [ ] **Dev endpoints off** *(preflight)*. `Dev:ExposeSeedEndpoints`,
      `Ranked:ExposeInternalEndpoints`, `Simulation:ExposeInternalEndpoints`,
      `Jobs:ExposeDashboard` and `Jobs:ExposeTestEndpoint` must all be false — or the
      environment must be Production, which already gates them (the prod compose sets both).
      The preflight walks all nine routes and requires 404 from every one.
- [ ] **Hangfire worker on its own instance** *(preflight)*, not inside the API process
      (carried forward from the 9.6 load test — while they share a process, matchday
      resolution competes with player requests). `Jobs__Role=api` / `Jobs__Role=worker` on the
      same image; `GET /health` reports which, and the preflight fails a public name that
      answers `worker` and warns on `both`.
- [ ] **Database backup** taken and a restore actually tested. The preflight checks the newest
      archive's age with `-BackupPath`; reading one BACK is manual and it says so every run.
- [ ] `GET /health` on the deployed instance reports the version being shipped *(preflight —
      compared against `tools/version.json`)*.
- [ ] **CORS** set to the web build's origin *(preflight, with `-WebOrigin`)*. Without it the
      public account-deletion page cannot submit, and that page is itself a store blocker.

---

## 2. Steam

- [ ] Steamworks account exists; App ID and depot ids filled into
      `tools/steam/steam.config.json`.
- [ ] `.\tools\build-desktop.ps1 -Platform All` succeeds (needs "Mac Build Support (Mono)"
      and "Linux Build Support (IL2CPP)" in Unity Hub).
- [ ] `.\tools\release-steam.ps1 -DryRun` — the VDFs resolve and every depot has content.
- [ ] `.\tools\release-steam.ps1 -SteamLogin <account> -Preview` — steamcmd validates.
- [ ] Upload for real, then **install from Steam on a clean machine** and play a match.
- [ ] Launch options configured on the partner site, one per OS; the Linux one must be
      marked executable.
- [ ] Store page: capsules and screenshots per `asset-specs.md`, copy from
      `store-copy.md`. Put the images under `store-assets/steam/` and let
      `.\tools\preflight-store.ps1 -Platform Steam` check every size for you.
- [ ] Age rating questionnaire completed.
- [ ] The build is on a beta branch, not `default`, until it has been played end to end.

Known: the Windows standalone currently uses the **Mono** scripting backend, not IL2CPP —
the IL2CPP toolchain broke against VS 2026 during 6.5. Determinism is unaffected (Sim.Core
is integer maths), but IL2CPP is worth revisiting before a paid release for startup and
runtime performance.

## 3. Google Play

- [x] **Target API level 36 (Android 16).** From 31 August 2026 new apps AND updates must
      target it (an extension to 1 November 2026 can be requested in the Play Console).
      `AndroidTargetSdkVersion` was `0` — auto, meaning "whatever SDK platform Unity happens
      to have installed", which is not a decision, it is a coin toss. Now pinned to `36`
      (Roadmap 10.4b). **You must still install SDK Platform 36** through Unity Hub or the
      Android SDK manager, or the build fails; confirm in Player Settings that Target API
      Level reads "Android 16.0 (API level 36)".
- [ ] Release keystore created and **backed up off the machine**; `tools/keystore.local.ps1`
      filled in from the example.
- [ ] Play App Signing enabled at first upload (it is the safety net if the keystore is
      ever lost).
- [ ] `.\tools\release-android.ps1 -BumpBuild` produces a signed `.aab`.
- [ ] Internal testing track first; install from Play on a real device and play a match.
- [ ] Data Safety form completed from `data-safety.md`.
- [ ] Content rating questionnaire (IARC) completed.
- [ ] Store listing: icon 512, feature graphic 1024x500, at least 2 phone screenshots —
      under `store-assets/play/`, verified to the pixel (and for the right alpha channel) by
      `.\tools\preflight-store.ps1 -Platform Play`.
- [ ] Privacy policy URL entered: `<site>/privacy-policy.html` (built by
      `.\tools\build-legal-pages.ps1`, staged by `.\tools\deploy-web.ps1` from `web/`).
- [ ] Account deletion URL entered: `<site>/delete-account.html` (staged by
      `.\tools\deploy-web.ps1` from `web/`).

## 4. App Store

- [ ] Apple Developer Program membership active.
- [ ] `.\tools\build-ios.ps1` exports cleanly (can be run from Windows as a smoke test).
- [ ] On a Mac: open the Xcode project, set the team and provisioning profile, Archive,
      upload to App Store Connect.
- [ ] App Privacy answers entered from `data-safety.md`.
- [ ] In-app account deletion present (Apple enforces this strictly).
- [ ] Screenshots at exactly 1320 x 2868 (iPhone 6.9"), and 2064 x 2752 if iPad is offered.
      Apple validates to the pixel, so put them under `store-assets/appstore/iphone/` and let
      `.\tools\preflight-store.ps1 -Platform AppStore` catch an off-by-one before a review cycle does.
- [ ] TestFlight build installed and played before submitting for review.

## 5. Web (Cloudflare Pages)

- [ ] **The legal pages can go up FIRST**, months before the game build is presentable:
      `.\tools\build-legal-pages.ps1` then `.\tools\deploy-web.ps1 -PagesOnly -Deploy -Branch pages`.
      Note that a Pages deployment REPLACES the site rather than merging into it, so once the
      game is live a `-PagesOnly` push to production would take it down — the script refuses
      that without `-Force`.
- [ ] `.\tools\deploy-web.ps1 -Build` — stages, prints the total size (budget: under 50 MB).
- [ ] Serve the staged folder locally over HTTP and check: the game loads, a career can be
      started, and a hard refresh keeps the save (IndexedDB).
- [ ] `.\tools\deploy-web.ps1 -Deploy` to a preview branch first.
- [ ] On the deployed URL, confirm in devtools that the build files come back with
      `content-encoding: br` and `cache-control: immutable`, and that `index.html` does not.
- [ ] Point the client's server URL at the production API, not localhost.
- [ ] Then deploy to production (`-Branch main`).

---

## 6. After the first live build

- [ ] Play one full ranked matchday cycle on production and confirm the recurring job
      resolves it within its window.
- [ ] Watch `GET /health/ready` and the error rate for the first hours.
- [ ] Keep the previous build's artefacts — a rollback is only easy if they still exist.
