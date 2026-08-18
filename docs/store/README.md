# Store kit — Football Team Simulator

Everything needed to submit the game to a storefront (Roadmap 10.2). Nothing here is
code: these are the texts, specifications and checklists the stores ask for, kept in the
repository so they version alongside the build they describe.

| File | What it is |
|---|---|
| `release-checklist.md` | The technical pre-flight, per platform. Start here on a release day. |
| `store-copy.md` | Names, taglines, short and long descriptions, feature bullets, keywords — English and Italian. |
| `asset-specs.md` | Exact pixel sizes and counts for every icon, screenshot and capsule image each store demands. |
| `privacy-policy.md` / `privacy-policy.it.md` | Privacy policy draft (EN / IT). **Needs a lawyer's read before publication.** |
| `eula.md` / `eula.it.md` | End-user licence agreement draft (EN / IT). **Same caveat.** |
| `data-safety.md` | The answers to Google Play's Data Safety form and Apple's App Privacy questionnaire, derived from what the server actually stores. |

The four legal documents are the SOURCE; `web/privacy-policy.html`, `web/privacy-policy.it.html`,
`web/eula.html` and `web/eula.it.html` are GENERATED from them by `tools\build-legal-pages.ps1`.
Edit the markdown and regenerate — never the HTML.

## The one-paragraph summary of where things stand

The game builds for four targets today (WebGL, Windows/macOS/Linux standalone, Android,
and an iOS Xcode export). One version number drives all of them — `tools/version.json`,
applied by `tools\set-version.ps1`. Steam is "Steam-ready": the depot scripts exist and
`tools\release-steam.ps1` will upload a build, but there is no App ID yet and the
Steamworks SDK is deliberately not integrated (no overlay, no achievements, no Steam
Cloud). The web build deploys to Cloudflare Pages through `tools\deploy-web.ps1`.

## What is NOT done and blocks an actual submission

These are external, cost money, or need a human decision — none of them can be written
into the repository:

1. **A Steamworks account** (100 USD one-off) and the resulting App ID + depot ids, to be
   filled into `tools/steam/steam.config.json`.
2. **A Google Play developer account** (25 USD one-off) and a release keystore (see
   `tools\keystore.local.example.ps1`).
3. **An Apple Developer Program membership** (99 USD/year) and a Mac with Xcode — Apple
   permits no other route to an .ipa.
4. **A hosted, publicly reachable privacy policy URL.** Both mobile stores require it as a
   link, not a document. The pipeline is built (10.4b): `tools\build-legal-pages.ps1` turns
   the four markdown documents into `web/*.html` and `tools\deploy-web.ps1 -PagesOnly`
   publishes them without needing a game build. What is still on you: a real contact
   address, a publication date, and the lawyer's read — the generator REFUSES to build a
   page that still says `[CONTACT EMAIL]`.
5. **Screenshots and capsule art.** `asset-specs.md` lists what is required; the game can
   produce the screenshots, the store-page artwork is a design job.
6. **A production backend.** The load test (9.6) carried forward two deployment
   requirements: run the Hangfire worker as its own instance rather than inside the API
   process, and re-confirm the p95 on a real host. Neither is a store requirement, but
   both are launch requirements — see `release-checklist.md`.
7. **An age rating.** Free questionnaires (IARC via Play, Steam's own, Apple's) — the
   answers are the same for all three and are drafted in `release-checklist.md`.
