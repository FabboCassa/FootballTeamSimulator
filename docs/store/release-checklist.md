# Release checklist

Run through this on a release day. It is ordered so the cheap checks fail first.

---

## 0. Before anything

- [ ] `.\tools\build-simcore.ps1` — the client is running the current Sim.Core.
- [ ] `dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj` — 216 green, golden master
      `0xCDEA5A2F7B9E5CF6` unchanged.
- [ ] `cd server && dotnet test Api.Tests/Api.Tests.csproj` — green, and
      `[server-determinism] 0xCDEA5A2F7B9E5CF6` matches the client's.
- [ ] `.\tools\set-version.ps1 -Check` — every target agrees with `tools/version.json`.
- [ ] Bump if this is a new build: `.\tools\set-version.ps1 -Version x.y.z -BumpBuild`.
- [ ] Commit `ProjectSettings.asset`, `Api.csproj` and `version.json` together.

---

## 1. Blockers that apply to every store

These are not per-platform paperwork; without them a submission is refused or a launch is
irresponsible.

- [x] **Account deletion** — in-app (Account → Delete account) and the public page
      `web/delete-account.html`, both on `POST /auth/account/delete` (Roadmap 10.2a).
      Still to do at deploy time: publish the page and set `Cors__AllowedOrigins__0` to the
      site's origin, or the browser form cannot call the API.
- [ ] **HTTPS in front of the API.** The compose stack is plain HTTP; production needs TLS
      termination before any store form can claim encryption in transit.
- [ ] **Privacy policy published at a stable URL** (not a document in a repository).
- [ ] **`Integrity__SignalSalt` overridden** with a real secret in the production
      environment — the default in `appsettings.json` is a placeholder.
- [ ] **`Jwt__SigningKey` overridden** with a real >= 32-byte secret.
- [ ] **Dev endpoints off.** `Dev:ExposeSeedEndpoints`, `Ranked:ExposeInternalEndpoints`,
      `Simulation:ExposeInternalEndpoints`, `Jobs:ExposeDashboard` and
      `Jobs:ExposeTestEndpoint` must all be false — or the environment must be Production,
      which already gates them. Verify by hitting `/internal/dev/test-league` and
      `/hangfire` on the deployed instance and getting 404.
- [ ] **Hangfire worker on its own instance**, not inside the API process (carried forward
      from the 9.6 load test — while they share a process, matchday resolution competes
      with player requests).
- [ ] **Database backup** taken and a restore actually tested.
- [ ] `GET /health` on the deployed instance reports the version being shipped.

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
      `store-copy.md`.
- [ ] Age rating questionnaire completed.
- [ ] The build is on a beta branch, not `default`, until it has been played end to end.

Known: the Windows standalone currently uses the **Mono** scripting backend, not IL2CPP —
the IL2CPP toolchain broke against VS 2026 during 6.5. Determinism is unaffected (Sim.Core
is integer maths), but IL2CPP is worth revisiting before a paid release for startup and
runtime performance.

## 3. Google Play

- [ ] **Target API level 36 (Android 16).** From 31 August 2026 new apps and updates must
      target it. `AndroidTargetSdkVersion` is currently `0` (auto = highest installed), so
      this depends on which SDK platform Unity has installed — check it explicitly, and
      install API 36 through Unity Hub / the SDK manager if it is missing.
- [ ] Release keystore created and **backed up off the machine**; `tools/keystore.local.ps1`
      filled in from the example.
- [ ] Play App Signing enabled at first upload (it is the safety net if the keystore is
      ever lost).
- [ ] `.\tools\release-android.ps1 -BumpBuild` produces a signed `.aab`.
- [ ] Internal testing track first; install from Play on a real device and play a match.
- [ ] Data Safety form completed from `data-safety.md`.
- [ ] Content rating questionnaire (IARC) completed.
- [ ] Store listing: icon 512, feature graphic 1024x500, at least 2 phone screenshots.
- [ ] Privacy policy URL entered.
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
- [ ] TestFlight build installed and played before submitting for review.

## 5. Web (Cloudflare Pages)

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
