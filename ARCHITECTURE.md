# Football Team Simulator — Architecture

> A lightweight, cartoonish football-manager-like game. Focus: transfers, market, tactics/lineup, and coach career progression. No injury micromanagement or technical clutter. Single player (advance days at your own pace) + online multiplayer (public ranked real-time leagues and private friend leagues).

---

## 1. High-Level Overview

```
┌────────────────────────────────────────────────────────────┐
│                       UNITY CLIENT (C#)                    │
│  Platforms: WebGL (browser) · Steam (Win/macOS) · iOS/And  │
│                                                            │
│  ┌──────────┐ ┌──────────┐ ┌───────────┐ ┌─────────────┐   │
│  │ UI Layer │ │ Game     │ │ Match     │ │ Network     │   │
│  │ (MVP)    │ │ Services │ │ Renderer  │ │ Layer       │   │
│  └────┬─────┘ └────┬─────┘ └─────┬─────┘ └──────┬──────┘   │
│       └────────────┴──────┬──────┘               │         │
│                  ┌────────▼────────┐             │         │
│                  │  SIM.CORE (DLL) │             │         │
│                  │  shared library │             │         │
│                  └─────────────────┘             │         │
└──────────────────────────────────────────────────┼─────────┘
                                          HTTPS / WebSocket
┌──────────────────────────────────────────────────┼─────────┐
│                  ASP.NET CORE BACKEND            ▼         │
│  ┌──────────┐ ┌──────────┐ ┌───────────┐ ┌─────────────┐   │
│  │ REST API │ │ SignalR  │ │ Workers   │ │ SIM.CORE    │   │
│  │          │ │ Hubs     │ │ (Hangfire)│ │ (same DLL)  │   │
│  └────┬─────┘ └────┬─────┘ └─────┬─────┘ └─────────────┘   │
│       └────────────┴──────┬──────┘                         │
│              ┌────────────▼───────────┐                    │
│              │ PostgreSQL  ·  Redis   │                    │
│              └────────────────────────┘                    │
└────────────────────────────────────────────────────────────┘
```

**The single most important architectural decision:** all game logic (match simulation, player development, market pricing, form/morale models) lives in **`Sim.Core`**, a pure C# class library (.NET Standard 2.1) with **zero Unity dependencies**. The Unity client uses it for single player; the ASP.NET server uses the *same DLL* for online (server-authoritative, cheat-proof, zero logic duplication).

---

## 2. Tech Stack

| Layer | Technology | Rationale |
|---|---|---|
| Client engine | Unity 6 LTS, C#, IL2CPP | One codebase → WebGL + Steam + mobile |
| Client UI | UI Toolkit (UXML/USS) | Responsive layouts for phone + desktop, performant, data-binding |
| Client DI | VContainer | Lightweight DI, kills singletons/god classes |
| Shared logic | `Sim.Core` (.NET Standard 2.1) | Runs in Unity and on server, unit-testable with plain NUnit |
| Backend | ASP.NET Core 8 (REST + SignalR) | C# everywhere, SignalR for live matches & auctions |
| Background jobs | Hangfire | Scheduled match simulation, market windows, season rollover |
| Database | PostgreSQL + EF Core | Relational fits leagues/players/transfers; free |
| Cache / realtime state | Redis | Auction state, live match sessions, leaderboards (sorted sets) |
| Auth | ASP.NET Identity + JWT; OAuth (Google/Apple) later | Required for mobile stores anyway |
| Push notifications | Firebase Cloud Messaging (+ APNs) | Auction alerts, match reminders |
| Local save (SP) | JSON (gzip) via `ISaveRepository` | Simple, debuggable; abstracted so backend can swap in cloud save |
| Hosting | Docker → any VPS/cloud; scale later | Start cheap, the architecture doesn't care |
| Serialization | MessagePack-CSharp | Compact for WebSocket + saves; JSON for REST debugging |

---

## 3. Repository Layout (Monorepo)

```
FootballTeamSimulator/
├── ARCHITECTURE.md
├── ROADMAP.md
├── shared/
│   ├── Sim.Core/              # ALL game rules. No UnityEngine. No I/O.
│   │   ├── Domain/            # Entities: Player, Team, Club, League, Season...
│   │   ├── Match/             # Deterministic match engine
│   │   ├── Market/            # Valuation, AI transfer behaviour, auctions
│   │   ├── Development/       # Growth/decline, training effects
│   │   ├── Condition/         # Form, morale, fitness models
│   │   ├── Tactics/           # Tactic definitions, counter-matrix, familiarity
│   │   ├── Career/            # Coach reputation, job offers, objectives
│   │   ├── Generation/        # Procedural players/leagues (seeded)
│   │   └── Random/            # Seeded deterministic RNG (PCG32)
│   ├── Sim.Core.Tests/        # NUnit. Determinism + balance tests
│   └── Contracts/             # DTOs shared client↔server (requests, events)
├── client/                    # Unity project
│   └── Assets/
│       ├── Plugins/SimCore/   # Sim.Core.dll + Contracts.dll (built artifact)
│       ├── Scripts/
│       │   ├── App/           # Bootstrap, DI scopes, scene flow
│       │   ├── Services/      # SaveService, ApiClient, ClockService...
│       │   ├── Presenters/    # MVP presenters per screen
│       │   ├── Views/         # UI Toolkit views (dumb, no logic)
│       │   └── MatchView/     # Top-down 2D match renderer
│       ├── UI/                # UXML/USS, themes
│       └── Data/              # ScriptableObjects: balance configs, names DB
├── server/
│   ├── Api/                   # ASP.NET Core host (controllers, SignalR hubs)
│   ├── Application/           # Use cases / handlers (league mgmt, auctions)
│   ├── Infrastructure/        # EF Core, Redis, FCM, Hangfire jobs
│   └── Api.Tests/
├── tools/                     # Balance simulator CLI, data import scripts
└── .github/workflows/         # CI: build Sim.Core, run tests, build clients
```

**Build flow:** `Sim.Core` is compiled by CI (or a local script) and the DLL is copied into `client/Assets/Plugins/SimCore/`. Unity never compiles game-rules source directly → guarantees client and server run identical logic.

---

## 4. Sim.Core — The Game Rules Library

### 4.1 Design rules (non-negotiable)

1. **Pure & deterministic.** Same inputs + same seed ⇒ identical output, on every platform. No `DateTime.Now`, no floats in critical paths where platform differences matter (use fixed-point or carefully constrained float ops), no statics holding state.
2. **No I/O, no Unity, no network.** Sim.Core computes; hosts (client/server) persist and display.
3. **Small classes, single responsibility.** Systems communicate via plain data, not inheritance trees. Target: no file > ~300 lines.
4. **Everything balance-related is data.** Tunable values live in `BalanceConfig` (loaded from JSON/ScriptableObject), never hard-coded — enables live tuning without client updates.

### 4.2 Domain model (core entities)

```
Club ── Team ── Squad(Player[]) ── Player
 │                                   ├─ Attributes (per-position skills, 1–100)
 │                                   ├─ Condition { Form, Morale, Fitness }
 │                                   ├─ Development { Potential, GrowthCurve }
 │                                   └─ ContractInfo / MarketValue
 ├─ Facilities { Stadium, TrainingGround, ScoutingDept, Academy }
 ├─ Finances { Budget, WageBill, Income/Expenses }
 └─ Coach (the user or AI) { Reputation, History, Specialties }

League ── Season ── Fixture[] ── MatchResult
LeaguePyramid: promotion/relegation between divisions, fixed team counts
```

### 4.3 Match engine (v1 — simple, top-down)

- **Tick-based discrete simulation**, ~2–4 ticks/sim-second, 90' compressed to ~3–5 real minutes when watched (instant when skipped).
- Players = circles with position, velocity, simple steering toward role-based target zones; ball = circle with owner or trajectory. **No physics engine** — pure math in Sim.Core.
- Outcome model: zone-based action resolution (build-up → chance creation → shot) where probabilities derive from: player attributes, form, fitness, morale, tactic fit, tactic-vs-tactic matrix, tactic familiarity, home advantage, randomness (seeded).
- Output = `MatchReport`: final score, event timeline (goals, cards, key chances), stats, **and a replayable position stream** the client renders top-down. Because the sim is deterministic, online clients only need `(seed, lineups, tactics, events)` to re-render the match identically — tiny payloads.
- Live interaction (online real-time matches / SP watched matches): pause-points (substitutions, tactic changes) are injected as **inputs at a given tick**; the engine re-simulates from that tick. Pre-made plans ("if losing at 60', switch to 4-2-4") are just scheduled inputs; AI fallback generates inputs automatically.

### 4.4 Condition model (realistic but not frustrating)

Anti-frustration principles baked into the math:
- **Form** (short-term, per player): bounded random walk nudged by performance ratings. Mean-reverting → cold streaks always end. Visible as a simple arrow/emoji, not a hidden trap.
- **Morale** (mental health): driven by playing time, results, transfers, team chemistry. Decays slowly toward neutral; user always has levers (play him, praise via "player support" actions, team talks).
- **Fitness** (physical health): drains with minutes, recovers with rest/rotation. **No injuries in v1** — the cost of a tired player is reduced performance, never unavailability.
- **Guardrails:** every negative modifier is capped (e.g. a player never performs below ~70% of his ability), at most N players can be in poor form simultaneously, and tooltips always explain *why* a value is low → challenge without chaos.

### 4.5 Development model

- Each player has hidden `Potential` and an age-based growth curve (peak ~27, decline after ~30, position-dependent).
- Modifiers: training focus & facility level, minutes played (youth need games), performance ratings, coach specialty. Poor usage/training ⇒ slow growth or mild decline — but decline rates are capped (anti-frustration).
- Deterministic monthly "development tick".

### 4.6 Tactics system

- Tactic = formation + instruction set (mentality, width, pressing, tempo, build-up style).
- **Counter-matrix:** instruction pairings give contextual bonuses/maluses (e.g. high press vs slow build-up = bonus; high line vs counter-attack = risk). Data-driven, tunable.
- **Familiarity:** each team accumulates familiarity per tactic with use/training; switching constantly costs effectiveness. Players also have role-fit.
- Designed so there is **no single dominant tactic** — effectiveness is contextual (opponent, familiarity, squad fit), which is what rewards tactical vision in ranked play.

### 4.7 Market & valuation

- `ValuationModel`: price from attributes, age, potential (as scouted), form, contract length, league level, demand. Re-priced periodically → realistic dynamics.
- **Single player:** AI clubs with personalities (seller/hoarder/youth-focused), budgets, squad needs; negotiation = offer/counteroffer rounds. Scouting reveals attribute ranges with accuracy based on scout level (uncertainty lives in *knowledge*, not in the sim).
- **Online:** no duplicate players within a competition. Market = **fixed auction windows** (season start + mid-season) with English auctions, server-resolved, push notifications on outbid. Free-agent pool + waiver rules handled server-side using the same `ValuationModel` for minimum prices.

---

## 5. Unity Client Architecture

### 5.1 Patterns

- **MVP (Model-View-Presenter)** per screen. Views (UI Toolkit) are dumb: they expose events and render view-models. Presenters hold screen logic and talk to services. Models come from Sim.Core / Contracts.
- **DI with VContainer:** `AppScope` (global services) → `GameScope` (active career) → `ScreenScope`. No singletons, no `FindObjectOfType`, no god `GameManager`.
- **Async:** UniTask everywhere (WebGL-safe, no threads required).
- **Events:** lightweight typed event bus (`IMessageBroker`) for cross-screen signals (e.g. `DayAdvanced`, `TransferCompleted`). No static event spaghetti.

### 5.2 Key services (interfaces — swappable per mode)

| Interface | Single player impl | Online impl |
|---|---|---|
| `IGameClock` | `LocalClock` (advance on demand) | `ServerClock` (real-time) |
| `ISaveRepository` | `LocalJsonSave` | `CloudSave (API)` |
| `IMatchService` | `LocalMatchService` (runs Sim.Core) | `RemoteMatchService` (receives report, re-renders) |
| `IMarketService` | `LocalMarketService` (AI clubs) | `AuctionApiService` (SignalR) |
| `ILeagueService` | `LocalLeagueService` | `LeagueApiService` |

**This is how SP and MP share ~90% of the client:** screens depend on interfaces; the game-mode scope binds the right implementations. The UI doesn't know whether the league is local or remote.

### 5.3 Scenes & flow

```
Boot → MainMenu → ┬ SP: CareerSetup → Hub (persistent scene)
                  └ MP: Login → LeagueBrowser/Create → Hub
Hub (single scene, UI Toolkit screen stack):
  Squad · Tactics · Training · Market/Scouting · League/Fixtures
  · Club/Facilities · Career · Inbox
Watch match: a UI Toolkit screen pushed onto the stack (see §5.4)
```

Navigation = a `ScreenNavigator` managing a stack of UI Toolkit screens (cheap, instant, mobile-friendly). The whole client is code-built UI Toolkit (no per-screen UXML/scene assets), so the match renderer (3.1) ships as another screen in this stack drawn with the painter2D vector API, rather than a separate additively-loaded Unity scene as earlier sketched — same Boot scene, no editor wiring, fully Play-mode testable. Decision #2 (Unity + UI Toolkit) is unchanged.

### 5.4 Match renderer

- Top-down 2D pitch; players/ball as cartoon circles (faces/kits later). Plays back the `MatchReport` position stream; interpolates between sim ticks. Implemented in `client/.../MatchView/MatchRenderer.cs` as a `VisualElement` drawn with the UI Toolkit painter2D API: the pitch dm-space (1050×680) is letterboxed into the element, playback advances in wall-clock time scaled by speed, no per-frame allocations.
- Time controls (1x/2x/4x/skip), event toasts, live tactic panel that injects inputs (SP: re-sim locally; MP: send to server).
- Renderer is pure presentation — it can never change a result.

### 5.5 Performance & platform constraints (WebGL is the binding constraint)

- WebGL: no threads → sim runs sliced across frames via UniTask when needed; target build < 50 MB compressed; addressables for art.
- Mobile: 60fps UI, battery-friendly (no per-frame allocations; object pooling in match renderer; UI Toolkit retained-mode).
- Asset style: flat vector-ish cartoon art = tiny textures, one atlas, cheap everywhere.

---

## 6. Backend Architecture

### 6.1 Structure (Clean-ish, pragmatic)

- `Api` — thin controllers + SignalR hubs (`MatchHub`, `AuctionHub`), JWT auth, rate limiting.
- `Application` — use-case handlers (CreateLeague, PlaceBid, SubmitMatchPlan, AdvanceSeason). Orchestrates Sim.Core + persistence. No business rules here that belong in Sim.Core.
- `Infrastructure` — EF Core (PostgreSQL), Redis, Hangfire, FCM. Repositories behind interfaces.

### 6.2 Online competition model

- **Private leagues:** invite code, configurable (real-time like public, or "advance when all ready"). Creator picks size (e.g. 8–20 clubs, mirroring real league sizes).
- **Public ranked:** server-managed league pyramid per "world". Fixed team counts per division; **promotion/relegation between seasons**; seasonal rewards; global Elo-style coach ranking (Redis sorted sets). Each season = fresh squads via seeded draft/auction → fairness, no snowballing; **coach ranking is the persistent progression**.
- **Daily loop ≤ 10 min:** set training, review market, confirm lineup/plan. Matches run at a fixed scheduled time: both online → live control via SignalR; otherwise pre-made plan or AI executes. Result + replay available afterwards.
- **Match resolution:** Hangfire job at kickoff loads both clubs' inputs, runs Sim.Core with a server seed, stores `MatchReport`, pushes notifications. Live matches keep a session in Redis and accept tactic inputs at pause-points until full-time, then finalize.
- **Auctions:** fixed windows (start + mid-season) with per-player timers (anti-sniping: bid in last 30s extends timer), `AuctionHub` broadcasts bids, FCM notifies outbid users, Hangfire settles. All state in Redis with Postgres write-behind.

### 6.3 Data model (server, main tables)

`users`, `coach_profiles`, `worlds`, `leagues`, `league_members(club)`, `seasons`, `fixtures`, `match_reports(jsonb)`, `players` (per-world instances — uniqueness enforced per world), `contracts`, `auctions`, `bids`, `transactions`, `rankings`, `notifications`.

### 6.4 Fairness & anti-cheat

- Server-authoritative: clients never report results; they only submit *inputs* (lineups, plans, live tactic changes). Server simulates.
- Server seed revealed after the match → results verifiable by re-running Sim.Core locally (free anti-tamper + builds trust).
- Input deadlines enforced server-side; AI takes over for absent players so nobody is punished by an opponent's no-show.

---

## 7. Single Player Specifics

- Whole world simulated locally with Sim.Core (user league in full; other divisions in light mode). Advance day-by-day or to next match at will.
- Difficulty levels = AI quality + user club budget/expectations + market aggressiveness. **Never** cheating AI or hidden penalties.
- Coach career: reputation from results vs board expectations; job offers from other clubs (mid/end of season); can be sacked; long-term legacy stats.
- Save system: versioned, gzip JSON snapshots + migration support.

---

## 8. Cross-Cutting Concerns

- **Testing:** Sim.Core ≥ 80% coverage; golden-master determinism tests (fixed seed ⇒ exact expected report, run on Windows/Linux/IL2CPP in CI); balance harness in `tools/` simulating 1000 seasons to detect dominant tactics or broken pricing.
- **Config/balance:** one `BalanceConfig` JSON, versioned; server can push updates; SP ships it embedded.
- **Localization:** string tables from day one (IT/EN).
- **Telemetry (online):** match outcomes, tactic pick rates, auction prices → balance dashboards.
- **CI/CD:** GitHub Actions — test Sim.Core on every push; Unity builds (WebGL/Win/Android) via game-ci; server Docker image deploy.

---

## 9. Key Decisions Log

| # | Decision | Why |
|---|---|---|
| 1 | Shared deterministic Sim.Core DLL | Zero logic duplication, anti-cheat, replays for free |
| 2 | Unity + UI Toolkit | 3 platforms incl. WebGL, responsive UI for mobile+desktop |
| 3 | MVP + VContainer DI | No god classes, testable presenters |
| 4 | ASP.NET Core + SignalR + Hangfire | C# end-to-end, scheduled sims, live auctions/matches |
| 5 | Replay = (seed + inputs), not video/streams | Tiny payloads, deterministic re-render |
| 6 | Online seasons reset squads; ranking persists | Fairness in ranked, no pay/grind snowball |
| 7 | No injuries; fatigue/form with capped maluses | "Challenge, not chaos" design pillar |
| 8 | Offline-first SP (no account needed) | SP must work without servers; servers added in later phases |
| 9 | Localization: flat key→text JSON per language in `Resources/Localization/`; views get a translate delegate from presenters | Translator-friendly files; views stay dumb without referencing Services; missing keys fall back to EN then render the key (visible in playtests) |
