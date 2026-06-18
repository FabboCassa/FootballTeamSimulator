# Football Team Simulator — Roadmap

Phases are sequential; each task is small and **individually testable** (✅ = how you verify it). Single player first → full game loop offline → then backend & online. Status legend: `[ ]` todo · `[~]` in progress · `[x]` done.

---

## Phase 0 — Foundations
*Goal: repo, projects, pipeline. Boring but everything depends on it.*

- [x] **0.1 Repo & solution setup** — Monorepo structure per ARCHITECTURE.md §3; `Sim.Core`, `Sim.Core.Tests`, `Contracts` projects; .gitignore (Unity + .NET).
  ✅ `dotnet build` and `dotnet test` succeed from root.
- [x] **0.2 Unity project** — Unity 6 LTS project in `client/`, UI Toolkit set up, VContainer + UniTask installed, assembly definitions (App/Services/Presenters/Views/MatchView).
  ✅ Empty project opens, enters Play mode with a Boot scene logging "App started".
- [x] **0.3 Sim.Core → Unity pipeline** — Build script copies `Sim.Core.dll` + `Contracts.dll` into `Assets/Plugins/SimCore/`.
  ✅ A test MonoBehaviour calls a Sim.Core function and logs its result in Unity.
- [x] **0.4 Deterministic RNG** — PCG32 implementation in `Sim.Core.Random`, seedable, serializable state.
  ✅ Unit test: same seed ⇒ identical 10,000-number sequence; state save/restore mid-stream works.
- [x] **0.5 CI** — GitHub Actions: build + test Sim.Core on every push.
  ✅ Badge green on a test PR; a deliberately broken test fails the pipeline.

---

## Phase 1 — Domain & Match Engine v1
*Goal: a match can be simulated headless and the result is sensible.*

- [x] **1.1 Domain entities** — Player (attributes 1–100 per position role, age), Team, Squad, Club, Coach skeletons. Plain C#, serializable.
  ✅ Unit tests construct entities, round-trip serialize/deserialize.
- [x] **1.2 Player & league generation** — Seeded procedural generation: leagues of N clubs, squads of ~22 with realistic attribute/age/position distributions; name database.
  ✅ Test generates a 20-team league; histogram of attributes/ages matches expected curves (test asserts bounds).
- [x] **1.3 BalanceConfig** — All tunables in one JSON-loaded config object injected into every system.
  ✅ Changing a JSON value (e.g. home advantage) changes sim output; no tunables hard-coded (review).
- [x] **1.4 Match engine: result model** — Zone-based action resolution producing score + event timeline from lineups, attributes and a seed. No movement yet.
  ✅ Golden-master test: fixed seed ⇒ exact same report. 1,000-match harness: strong team beats weak ~70–80%, realistic score distribution (most common 1-0/1-1/2-1), no 15-0 outliers.
- [x] **1.5 Match engine: position stream** — Tick-based player/ball movement generation consistent with the event timeline (top-down coordinates).
  ✅ Test: ball position coincides with scorer's position at each goal tick; stream is deterministic per seed.
- [x] **1.6 Determinism cross-check** — Same sim run under .NET (server-like) and Unity IL2CPP.
  ✅ Identical MatchReport hash on both runtimes for 50 seeded matches.

---

## Phase 2 — Single Player Core Loop
*Goal: playable skeleton — start a career, advance days, see matches happen, finish a season.*

- [x] **2.1 App shell & navigation** — Boot → MainMenu → Hub with ScreenNavigator (screen stack), DI scopes (App/Game/Screen), message bus.
  ✅ Navigate between placeholder screens on desktop and phone aspect ratios; back button works.
- [x] **2.2 Career setup & save system** — New career: pick generated league + club; versioned gzip-JSON save/load via `ISaveRepository`.
  ✅ Create career, quit, relaunch, continue — state identical. Corrupted file shows friendly error, doesn't crash.
- [x] **2.3 Calendar & advance day** — `LocalClock`, fixture generation (double round-robin), advance-day button simulating due AI matches headless.
  ✅ Advance through a week: AI fixtures get results, league table updates correctly (points, GD tiebreakers).
- [x] **2.4 Squad screen** — Roster list with attributes, positions, age, value; lineup picker (formation slots, validity checks).
  ✅ Set a lineup, save, advance to match day — chosen XI is used in the sim.
- [x] **2.5 League screen** — Table, fixtures/results, top scorers.
  ✅ Data matches simulated results; updates after each advance.
- [x] **2.6 Match day (instant)** — User match simulates with chosen lineup; result + event summary screen.
  ✅ Play 5 matches; results reflect lineup strength (bench the XI ⇒ visibly worse outcomes over a season harness).
- [x] **2.7 Season rollover** — End of season: final standings, promotion/relegation between two generated divisions, new fixtures, player ages +1.
  ✅ Finish a full season (can be auto-advanced); correct teams promoted/relegated; new season starts clean.

🏁 **Milestone: vertical slice — a full season is playable end-to-end.**

---

## Phase 3 — Match Experience
*Goal: watching matches is fun; tactics matter.*

- [x] **3.1 Match renderer** — Top-down 2D pitch, circle players/ball, playback of position stream, speed controls (1x/2x/4x/skip), event toasts, score/clock HUD.
  ✅ Watch a match start-to-finish at all speeds on desktop + phone; events match the report; 60fps, no GC spikes (profiler).
- [x] **3.2 Tactics system (Sim.Core)** — Formations + instructions, counter-matrix, tactic familiarity accumulation, role-fit.
  ✅ Harness: counter-tactic gives measurable edge (~55–60% vs equal squads, config-tunable); familiar tactic beats freshly-switched identical tactic; no tactic >55% win rate vs all others across 1,000 matches.
- [x] **3.3 Tactics screen** — Formation editor, instruction toggles, familiarity display, opponent info panel.
  ✅ Change tactics → next match behaves accordingly (verify via harness stats and in-match shape).
- [x] **3.4 In-match interaction** — Pause for substitutions/tactic changes; engine re-sims from current tick.
  ✅ Sub a striker on while losing — re-sim from that tick changes the remainder; replay of final report is consistent.
- [x] **3.5 Pre-match plans** — Conditional rules ("if losing at 60' → mentality attacking; tired player → sub"). Foundation for online AI-delegation.
  ✅ Plan triggers correctly in 10 harness scenarios; executes when match is skipped/unwatched.

---

## Phase 4 — Living Players
*Goal: form, morale, fitness, development — realistic, never frustrating.*

- [x] **4.1 Condition model (Sim.Core)** — Form (mean-reverting walk), morale (playing time/results/chemistry), fitness (minutes/rest), all feeding match performance with capped maluses.
  ✅ Harness over 3 seasons: no player stuck in bad form >6 matches; performance floor ≥70% ability; rotation measurably outperforms fixed XI fatigue-wise.
- [x] **4.2 Condition UI** — Form arrows, morale faces, fitness bars on squad/lineup screens; tooltips explaining *why* (transparency = anti-frustration). Also made condition LIVE (chosen with user): matches simulated condition-aware + whole-world daily evolution, uniform starting fitness with stamina-driven drain, and within-match fatigue with half-time recovery (MatchFatigueAt90Permille 60). Calibration accepted: 2.59 g/m, draws 21.3%, golden master unchanged; 118 tests green.
  ✅ Every low value shows a cause in its tooltip; bench a tired player ⇒ fitness recovers visibly.
- [ ] **4.3 Training system** — Weekly team focus + individual focuses; affects development and tactic familiarity; simple schedule UI.
  ✅ Two identical save seeds, different training → diverging attributes after a season (harness assert).
- [ ] **4.4 Development & aging** — Potential, growth curves, monthly dev tick, modifiers (minutes, training, facilities, performances); decline capped.
  ✅ 10-season harness: youth with minutes grow, benched youth grow less, 33-year-olds decline gently; no attribute collapse possible.
- [ ] **4.5 Player support actions** — Praise/encourage/rest conversations (lightweight), morale effects with cooldowns.
  ✅ Actions move morale as configured; spamming is ineffective (cooldown works).

---

## Phase 5 — Market, Scouting & Career
*Goal: the management fantasy — buy, sell, scout, build, progress as a coach.*

- [ ] **5.1 Valuation model (Sim.Core)** — Price from attributes/age/potential/form/contract/league; periodic re-pricing.
  ✅ Harness: prices correlate with ability & age curve; young stars cost more than equal-ability 30-year-olds; no negative/absurd prices across 10k players.
- [ ] **5.2 Transfer windows & AI clubs** — Club personalities, budgets, squad-need analysis; offer/counteroffer negotiation; AI↔AI transfers too.
  ✅ Season harness: AI completes 50–150 sensible transfers/window; AI never sells its best XI for peanuts; user negotiation flow works end-to-end.
- [ ] **5.3 Market & negotiation UI** — Search/filters, shortlist, offer dialog, transfer news feed in Inbox.
  ✅ Buy and sell a player end-to-end; budget updates; player appears/disappears from squads correctly.
- [ ] **5.4 Scouting** — Scout levels, assignments, attribute ranges narrowing with scouting accuracy; hidden potential estimation.
  ✅ Unscouted player shows wide ranges; after N weeks of scouting ranges narrow around true values (test asserts).
- [ ] **5.5 Club: facilities & finances** — Stadium/training/scouting/academy upgrade tiers; income (gate, prize, sponsors) & expenses (wages); budget from board.
  ✅ Upgrade training ground ⇒ measurable dev-speed increase (harness); finances balance correctly over a season; bankruptcy impossible but overspending blocks signings.
- [ ] **5.6 Coach career** — Board expectations/objectives, reputation, sackings, job offers from other clubs, career history screen.
  ✅ Overachieve ⇒ better offers arrive; underachieve persistently ⇒ sacked with warning beforehand; accepting an offer moves you with sensible squad/budget at new club.
- [ ] **5.7 Difficulty levels** — Easy/Normal/Hard: AI quality, board patience, starting budget, market aggressiveness. No cheating AI.
  ✅ Win-rate harness shows clear separation between difficulties with identical user behaviour.

🏁 **Milestone: complete single-player game (alpha).**

---

## Phase 6 — Polish & Platforms (SP Beta)
*Goal: looks cartoonish, runs everywhere, fun to play.*

- [ ] **6.1 Art pass** — Cartoon style: kits, player tokens (faces later), pitch, UI theme, club color/logo generator.
  ✅ Visual review on phone + desktop; build size within target.
- [ ] **6.2 UX pass** — Onboarding/tutorial, Inbox notifications hub, confirmations, empty states, localization IT/EN *(string-table infrastructure + IT/EN tables already in place since Phase 2)*.
  ✅ A new player reaches their first match without external help (playtest).
- [ ] **6.3 WebGL build** — Compressed build, loading screen, save persistence (IndexedDB), performance pass.
  ✅ Plays a full match day in Chrome/Firefox/Safari, <50 MB download, no freezes.
- [ ] **6.4 Mobile builds** — Android/iOS, touch targets, safe areas, battery check.
  ✅ Full season playable on a mid-range phone; 60fps UI; no overheating in 30-min session.
- [ ] **6.5 Desktop/Steam build** — Windows build, keyboard/mouse niceties, Steam-ready packaging (store assets later).
  ✅ Clean run on Windows; alt-tab/resize safe.

🏁 **Milestone: SP beta on all three platforms — playtest with friends.**

---

## Phase 7 — Backend Foundations
*Goal: server skeleton with auth, ready to host worlds. (Can start in parallel with Phase 6.)*

- [ ] **7.1 Server solution & Docker** — Api/Application/Infrastructure projects, PostgreSQL + Redis via docker-compose, EF migrations, health endpoint.
  ✅ `docker compose up` ⇒ API healthy, DB migrated.
- [ ] **7.2 Auth & accounts** — Register/login (email + JWT), coach profile; client Login screen and `ApiClient` service.
  ✅ Register + login from Unity client (all 3 platforms); token refresh works; wrong password handled gracefully.
- [ ] **7.3 Sim.Core on server** — Server references the same Sim.Core; match-simulation endpoint (internal) producing MatchReport.
  ✅ Same seed+inputs ⇒ byte-identical report client vs server (extends test 1.6).
- [ ] **7.4 Hangfire jobs & FCM** — Job scheduler wired; push notification service with device registration.
  ✅ Scheduled test job fires on time; test push received on Android device and as web push.

---

## Phase 8 — Private Online Leagues
*Goal: play a full season with friends — the simpler online mode first.*

- [ ] **8.1 League lifecycle** — Create league (size, mode: real-time or all-ready), invite codes, join/leave, server-seeded world generation (unique players per world).
  ✅ Two accounts create/join a league; both see the same generated squads; no duplicate players in world.
- [ ] **8.2 Initial squad draft/auction** — Season-start squad assignment (seeded auction with budgets, or quick-draft option).
  ✅ 4-account test league: everyone gets a legal squad within budget; no duplicates.
- [ ] **8.3 Scheduled match resolution** — Kickoff jobs simulate fixtures from submitted lineups/plans; AI fallback for missing inputs; results + replay download.
  ✅ Test league plays 3 rounds: matches fire on schedule, absent player's AI fields a sensible XI, replays render identically for both users.
- [ ] **8.4 Daily management sync** — Training/lineup/plan submissions to server; server-authoritative condition & development ticks.
  ✅ Client and server state agree after a week of play (state-hash comparison endpoint).
- [ ] **8.5 Online auctions** — Auction windows with timers, AuctionHub live bids, anti-sniping extension, outbid push notifications, settlement job.
  ✅ 4 accounts bid live on one player: outbid notifications arrive, last-second bid extends timer, winner charged correctly, others refunded.
- [ ] **8.6 Live match control** — Both users connected at kickoff ⇒ SignalR session: pause-point tactic changes/subs processed server-side, state streamed.
  ✅ Two clients play live: a sub made by user A is reflected in B's view within 2s; disconnect mid-match falls back to plan/AI gracefully.
- [ ] **8.7 Private season end** — Final table, awards, season summary; rematch/new-season option.
  ✅ A complete private season finishes cleanly with 4 real users.

🏁 **Milestone: online beta with friends.**

---

## Phase 9 — Public Ranked Mode
*Goal: the competitive ladder.*

- [ ] **9.1 World & pyramid management** — Server-managed worlds with division pyramid, fixed team counts, matchmaking new players into openings/lowest division.
  ✅ 40 test accounts get placed; divisions stay at fixed size; waitlist/new-world spillover works.
- [ ] **9.2 Real-time season calendar** — Fixed match schedule (e.g. 1 matchday/day at user-friendly hours), market windows at season start + midpoint, notifications.
  ✅ Simulated 2-week season on staging runs unattended: all matchdays and windows fire correctly.
- [ ] **9.3 Coach ranking & seasonal reset** — Elo-style global ranking (Redis), seasonal rewards (trophies, cosmetics, titles), promotion/relegation applied, squads reset via new auction, ranking persists.
  ✅ Two seasons on staging: rankings update per results, P/R correct, season 2 starts with fresh fair squads.
- [ ] **9.4 ≤10-min daily loop audit** — Streamline daily flow (smart defaults, one-screen daily digest).
  ✅ Stopwatch playtest: typical day ≤ 10 min including market browsing.
- [ ] **9.5 Abuse & integrity** — Rate limits, multi-account heuristics, collusion flags (suspicious transfers), report function, input deadline enforcement.
  ✅ Scripted abuse scenarios (bid spam, lopsided friend-transfer) are blocked or flagged.
- [ ] **9.6 Load test** — Simulate 1,000 concurrent users, matchday spike, auction spike.
  ✅ p95 API < 300ms during spike; no lost bids; match jobs complete within window.

🏁 **Milestone: public ranked open beta.**

---

## Phase 10 — Launch & Live Ops
- [ ] **10.1 Balance pass from telemetry** — Tactic pick/win rates, market price sanity, difficulty curve; tune BalanceConfig.
- [ ] **10.2 Store readiness** — Steam page/build, Google Play/App Store submission, web hosting & CDN.
- [ ] **10.3 Live ops tooling** — Admin dashboard (worlds, users, balance push), backup/restore, monitoring & alerts.
- [ ] **10.4 Launch** 🚀

---

## Later / Ideas Backlog
Match renderer movement realism (possession-aware steering / less wandery off-ball positioning beyond the 1.5 stream + 3.1 smoothing) · National cups · continental competitions · player personalities & press conferences · cosmetic monetization (club customization) · spectator mode for friends' matches · seasonal events · replays sharing · cross-world tournaments · cloud saves for SP.

---

### Working agreement
- One task at a time; you test each ✅ before we tick it and move on.
- Anything that changes game feel goes through the balance harness before merging.
- ARCHITECTURE.md is updated whenever a decision in §9 changes.
