# Watchable match: new engine brain, broadcast director, commentary

Status: approved
Repo: FabboCassa/FootballTeamSimulator   Base branch: main

## Goal
A watched match today is unreadable. At 1x it runs about 9x real time and drops into a "TEMPO REALE" slow-motion around strikes (recording 2026-09-22: minutes 7'→10' in ~20 s). On the pitch, players drift without purpose and circle in midfield, and a carrier with an open goal often does not shoot. The outcome we want: at 1x a match takes about 10 real minutes (5 per half). Every action on screen plays at real speed (1x) or at most 2x real speed, and dead phases are cut by a broadcast-style director. The play is recognisable football, with team shape, build-up, runs, crosses, shots and set pieces, and a side commentary panel says clearly what happens. Substitutions, tactic changes and the new touchline shouts all have a measurable effect, and no tactic dominates.

## Scope
In:
- New decision layer ("brain") for `MatchSimulator`. It keeps ball physics, referee, fouls, offside, restarts, the integer determinism and the input/rule injection.
- Broadcast director in the client that replaces the current slow-motion director (`MatchRenderer`).
- Speed controls 1x / 2x / 4x / Skip. 0.5x and every automatic slow-motion are removed.
- Commentary side panel (Italian and English), including summaries of the cut phases.
- Touchline shouts: a new in-match input, usable live and in `PrematchPlan` rules.
- Online live screens (ranked and private league) move to the same director timeline and share one clock.
- Fast model recalibrated to the new engine, and it now reads instructions.
- 1,000-match harness extensions: realism bands, open-goal and circling metrics, preset tournament, effect measurements.
- Golden master moves once, to engine v11.

Out:
- New physics model, third-party packages, or any engine outside Sim.Core.
- Goal replays, on-pitch captions, pass/run overlays (not chosen; a later spec).
- Injuries, new player attributes, new formations.
- 3D or sprite art for players.

## Users / flows
1. SP manager watches a match at 1x. Kick-off. Build-up plays at 2x, and an attack into the final third drops to 1x. A sterile spell of possession is cut: the clock jumps and the panel logs "23'-27' Ulivara keep the ball in midfield". The match ends after about 10 minutes.
2. The manager pauses, makes a substitution or tactic change, or gives a shout. The engine re-simulates from that tick, and the effect shows in play and in the panel ("58' Shout: 'Press high!'").
3. The manager presses 2x / 4x to finish faster, or Skip to jump to the final result.
4. Online live match: every client derives the same director timeline from the deterministic report, so all viewers are at the same moment at the same wall-clock instant. The dev tools (bot autopilot + fast-forward) cover it.
5. Unwatched and background matches go through the fast model, whose distributions match the full engine.

## Requirements

### Engine brain (Sim.Core)
- R1 Team phases. Each side is always in exactly one phase: build-up, progression, final third, attacking transition, defensive transition, or set piece. The phase drives each player's target spot, derived from the formation shape, the phase and the instructions. (acceptance: a unit test asserts phase transitions on scripted positions; the harness reports time share per phase; every phase occupies >0% in every match of a 1,000-match run.)
- R2 Off-ball movement. Supporting players offer passing angles. Forwards make runs in behind, timed against the offside line. Full-backs overlap according to the width/mentality instructions. A man never sits over 25 m from his phase target for more than 5 s unless he is chasing or marking. (acceptance: the harness metric "off-target seconds per player" has a median ≤ 5 s per match; a unit test checks a run is triggered when a passing lane into the space behind exists.)
- R3 Action value from standard models. Pass, carry, cross, clear and shot values are computed from an expected-threat (xT) grid, a pitch-control estimate of lane and receiver safety, and an xG shot model. All are integer, deterministic and tunable in `BalanceConfig`. (acceptance: unit tests pin xT monotonicity (value rises toward goal) and xG ordering (6-yard central > edge of box > 30 m); the determinism test stays green.)
- R4 Open goal is taken. When the carrier is inside 20 m of goal with a clear shooting lane (no outfield defender in the ball-to-posts triangle), he shoots within 1.5 s in ≥ 90% of cases. The same holds when the keeper is also beaten, and a 1v1 is always resolved by a shot or a dribble past the keeper. (acceptance: the harness metric `openGoalShotRate` is ≥ 0.90 over 1,000 matches; a unit test on a scripted 1v1 position.)
- R5 No circling. The share of possessions lasting ≥ 20 s with net forward progress < 10 m and no pass into the final third is ≤ 15% of all possessions. (acceptance: the harness metric `sterilePossessionShare` is ≤ 0.15.)
- R6 Set pieces are visible and structured. Free kicks within 35 m form a wall and offer a direct-shot or crossed option. Corners use near-post, far-post and edge-of-box roles. Penalties have a visible run-up. Throw-ins and goal kicks follow the build-up instruction. (acceptance: unit tests on corner role assignment and the wall; harness counts free kicks shot directly vs crossed, and both are > 0.)
- R7 Realism bands (1,000-match harness, equal-strength neutral sides): goals 2.4–3.0 per match; shots 20–28; on target 30–40% of shots; corners 8–12; fouls 20–28; each side reaches the penalty area in open play ≥ 4 times per match on average. (acceptance: the user pastes the harness output and all bands are inside range.)

### Tactics, changes, shouts
- R8 No dominant tactic. A round-robin between all tactic presets (formation + instruction set) at equal squads, ≥ 200 matches per pairing: no preset exceeds 55% of the available points, and each preset has at least one opponent against which it takes < 45%. (acceptance: harness output, judged with the user.)
- R9 Familiarity and fit matter. Same squad and tactic, familiarity 100 vs 0 gives ≥ +0.25 goal difference per match. Players in their natural role vs out of role gives ≥ +0.25. (acceptance: harness effect measurements.)
- R10 Substitutions matter. Replacing the 3 most tired outfield players at 60' with fresh equal-rated players gives ≥ +0.10 goal difference in minutes 60–90 compared with making no change. (acceptance: harness effect measurement.)
- R11 Touchline shouts. Five shouts:
  - "Press high": pressing up, fatigue up.
  - "Calm, keep the ball": tempo down, risk down.
  - "All forward": mentality up, defensive exposure up.
  - "Encourage": morale/composure up; weaker when repeated.
  - "Concentrate": fewer defensive errors, attack slightly more cautious.

  Each shout lasts 10 match minutes, has a 15-minute cooldown, and gets its magnitudes from `BalanceConfig`. Shouts are injected as inputs at a tick, like tactic changes, and are available in `PrematchPlan` rules. (acceptance: each shout has a measurable effect on its target metric in the harness, e.g. "Press high" raises high-ball-recoveries by ≥ 15%; no shout is net-positive on goal difference in every game state, which the harness shows per score state (leading / level / trailing); a unit test on cooldown and expiry.)

### Director and playback (client)
- R12 Broadcast director. The client builds a deterministic playback timeline from the `MatchReport` alone. Each segment is played at 1x, played at 2x, or cut:
  - 1x (real time): final-third possession, counter-attacks, set pieces in the attacking half, shots and their 3 s aftermath, goals, cards, penalties.
  - 2x: other open-play build-up and progression.
  - Cut: dead balls outside the attacking half, sterile possession, goal celebrations beyond 3 s.

  At 1x the total playback is 10 min ± 1 (5 ± 0.5 per half). Every goal, shot, card, penalty and substitution is shown. (acceptance: an EditMode/unit test over 200 reports asserts the duration and "all key events shown"; the user checks it in Play mode.)
- R13 Nothing ever plays slower than real time at 1x. The "TEMPO REALE" slow-motion badge and logic are removed. 2x and 4x scale the whole timeline, and Skip jumps to full time. (acceptance: a test asserts that no segment has a rate < 1.0 real-time at 1x; the 0.5x button no longer exists.)
- R14 A cut is visible. When time is cut, the clock advances and the panel logs a one-line summary of the cut span: possession side, zone, and any stoppage. (acceptance: a test that every cut span ≥ 1 match minute produces exactly one summary line.)
- R15 Commentary panel. A side panel lists events with the minute, an icon and a clear sentence built from the event chain (pass → cross → header → save). It highlights goals, cards and substitutions, holds the cut summaries and the shouts, scrolls, and has an it/en localisation. It follows the Phase 14 UI rules: `UiKit`, sizes in `FtsTheme.uss` with a `.fts--mobile` override. (acceptance: loc key coverage stays complete; a presenter test on the sentence builder for 6 chain types; Play-mode check by the user.)

### Online and fast model
- R16 Online live screens compute the same director timeline and derive the displayed moment from the shared kickoff instant, which replaces `LiveSecondsPerMinute = 2f` and `BaseSecondsAt1x`. Two clients opened at different times show the same minute (±1 s). The dev tools (bot autopilot, fast-forward) keep working behind `DevFlags.OnlineTestTools`. (acceptance: a presenter test with two simulated clocks; a server/API test that the ranked flow is unchanged; the user's solo test with the dev tools.)
- R17 The fast model reproduces the full engine: mean goals within ±0.15, home-win/draw/away-win shares within ±3 pp, and instructions shift outcomes in the same direction as in the full engine. (acceptance: a harness comparison table over 1,000 matches.)

### Integrity
- R18 Determinism. Same seed + inputs ⇒ identical report on all platforms. The golden master moves once to engine v11 in the final task, after the user approves the harness. It is updated in `SimulationDeterminismTests.cs`, `SimulationService.cs`, `docs/ops/runbook.md` and `docs/store/release-checklist.md`. (acceptance: determinism tests green; a single commit changes the hash.)
- R19 Performance. Generating a full match on the user's machine takes no longer than +25% of the current engine's time (measured in the same harness run), and the WebGL-sliced generation still completes. (acceptance: harness timing line; a WebGL build smoke test by the user.)

## Tech decisions
- Stack/libs: no new packages. Sim.Core stays netstandard2.1, integer arithmetic, fixed iteration order, and a single seeded RNG. Models (implemented in-house, sources cited in code where non-obvious):
  - xT grid (Karun Singh), precomputed table in config.
  - Simplified pitch control (Spearman: time-to-intercept per player) for lane and receiver safety.
  - xG from distance, angle and pressure.
  - Utility-AI action selection and steering (Buckland).
- Structure: split `MatchSimulator.cs` (3,668 lines) into per-concern classes (phase, positioning, action valuation, set pieces, shouts) under `Match/Movement/`, each ≤ ~300 lines, as Architecture §4.1 requires.
- Data/storage: new tunables in `MatchBalance` / `BalanceConfig`. Shouts are a new `MatchInput` field, serialised in plans and in online inputs. There is no save bump unless a stored plan format changes; if it does, the save version is bumped with a migration that defaults to no shouts.
- Build cmd: `dotnet build --nologo -v q`   Test cmd: `dotnet test --nologo -v q`. Harness: `dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj --logger "console;verbosity=detailed"`. After a `shared/` change, run `.\tools\build-simcore.ps1`. The client is type-checked via Roslyn, and Play mode is run by the user.
- Models: this spec and planning on Opus 5.5. ba implementer and verifier on Opus; ba reviewer and auditor on Sonnet.

## Constraints / non-functional
- Architecture §4.1 rules: pure, deterministic, no I/O, balance as data.
- The renderer stays pure presentation. The director reads only the report and never changes a result.
- Mobile/WebGL: no per-frame allocations in the renderer or panel; the panel is pooled.
- Localisation: all new strings in en + it.
- Balance-sensitive steps (R7–R11, R17) close only after the user pastes the harness output and we judge it together. Statistically fragile tests are flagged as such.

## Open risks
- R8 (no dominant preset) and R7 bands pull against each other; several tuning rounds are likely.
- The new brain may exceed the R19 time budget. The fallback is to cache pitch control per phase and evaluate it every N ticks.
- Online clients on an old build would compute a different timeline. The online match must carry the engine version, and older clients are refused.
- A stream of 2 frames/s may be too coarse for smooth 1x playback of fast actions; the frame rate may need to rise, which grows the report payload.

## Tasks (filled by /ba:issues)
| # | Title | Depends on | Requirements | Issue |
|---|-------|------------|--------------|-------|
| 1 | Extract referee, restarts, set pieces | - | R18 | #28 |
| 2 | IMatchBrain seam + Brain selector | 1 | R18 | #29 |
| 3 | Realism harness metrics + bands | 2 | R4 R5 R7 R19 | #30 |
| 4 | xT, xG, pitch-control models | - | R3 | #31 |
| 5 | V11 team phase machine | 2 | R1 | #32 |
| 6 | V11 positioning, runs, overlaps | 5 | R2 | #33 |
| 7 | V11 action selection + open-goal rule | 4, 6 | R3 R4 R5 | #34 |
| 8 | V11 structured set pieces | 5 | R6 | #35 |
| 9 | Touchline shouts as inputs | 2 | R11 | #36 |
| 10 | Tactic tournament + effect measurements | 3, 9 | R8-R11 | #37 |
| 11 | Tune V11 to bands | 7, 8, 10 | R7-R11 R19 | #38 |
| 12 | Fast model recalibration + instructions | 11 | R17 | #39 |
| 13 | BroadcastDirector timeline | - | R12-R14 | #40 |
| 14 | Renderer on director timeline | 13 | R12 R13 | #41 |
| 15 | Commentary sentence builder | 9, 13 | R14 R15 | #42 |
| 16 | Commentary side panel | 14, 15 | R15 | #43 |
| 17 | Shouts UI | 9, 16 | R11 | #44 |
| 18 | Online live on director timeline | 13, 14 | R16 | #45 |
| 19 | V11 default, remove V10, golden master v11 | 11, 12 | R18 | #46 |
