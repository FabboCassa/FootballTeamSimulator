# Real-looking match, then playing styles × formations

Status: approved
Repo: FabboCassa/FootballTeamSimulator   Base branch: main

## Goal
V11 is better than V10 but a watched match still does not look like football (user recording 2026-10-01, 32 s, minutes 10'→18').
- Players stand still or drift slowly, and the eleven bunch inside a 50 m band.
- A keeper stands at the edge of his box with the ball in midfield, and an outfield player sits on the touchline.
- Attacks are one pass and a shot. The keeper "never saves", because about two thirds of all shots come from the open-goal shortcut and most of them miss.
- The harness agrees: "off-target seconds per player" reads 33 s against a 5 s band, the only band of the old R7 set still out.

We want two outcomes.
- **Phase A:** a match whose movement, shape, passing chains, shot mix and keeper behaviour sit inside the ranges of real professional leagues, measured against a sourced reference. It is watched through a director that never looks slow-motion.
- **Phase B:** a tactical system where any of ~24 real formations combines with any of ~15 real playing styles, each style being an editable preset of ~14 instructions. Each style produces the statistical fingerprint of the real-world style it is named after.

## Scope
In:
- A sourced reference document of real football statistics: 10 leagues, playing-style profiles, formation catalogue.
- Phase A, engine: positioning and team shape, goalkeeper positioning and saves, off-ball movement and acceleration, passing chains, shot selection, blocked shots, restart shapes, and keeping players on the pitch.
- Phase A, client: director playback rates (1x = 1.3x real time on live play, dead time at twice the selected speed), a denser position stream, and a visible keeper save.
- New realism harness metrics and bands, a quality-tier effect, and a hardened tournament gate.
- Phase B: an expanded instruction model, a formation catalogue, a style catalogue, style-profile harness checks, the Tactics screen, AI club style choice, a save bump with migration, online engine-version checks, and the fast model reading the new instructions.
- Golden master moves to engine v12 at the end of Phase A, and to v13 at the end of Phase B only if the neutral match changes.

Out:
- 3D or sprite player art, goal replays, on-pitch captions or overlays.
- Player roles/duties per slot beyond what the formation catalogue defines (e.g. FM-style "inverted wing-back" duties); a later spec.
- New player attributes, injuries, weather, referees' personalities.
- Third-party packages; any engine outside Sim.Core.

## Users / flows
1. SP manager watches at 1x. Open play runs at 1.3x real time. A throw-in, goal kick or celebration runs at 2.6x. A long sterile spell is cut, and the commentary logs a one-line summary. The match lasts about 10 minutes and every goal, shot, card, penalty and substitution is shown.
2. On screen the defending side holds a compact block. The attacking side spreads its width, full-backs push on, midfielders show for the ball, and a move strings several passes before a cross, a through ball or a shot. Some shots are blocked, many are saved, and the keeper visibly moves across his goal.
3. (Phase B) In Tattiche the manager picks a formation (e.g. 4-1-4-1), then a style (e.g. Gegenpress), then tweaks single instructions (e.g. defensive line: very high). The match shows it: high recoveries, short sequences, a high line.
4. (Phase B) AI clubs field varied, squad-coherent styles across a league. Background matches (fast model) react to style and formation the same way the full engine does.
5. Online ranked/private matches carry the engine version; an outdated client is refused with a clear message. The dev tools (bot autopilot + fast-forward) still cover the flow.

## Requirements

### Reference data
- R1 Reference document `docs/research/football-reference.md`. It contains:
  - Latest complete season averages per match for Serie A, Premier League, LaLiga, Bundesliga, Ligue 1, Eredivisie, Primeira Liga, Serie B, Championship and 2. Bundesliga. The metrics are those in R2, where published.
  - Style profiles for every style in R16, with real example teams and their per-match figures.
  - A formation catalogue with each formation's 11 slots and its in- and out-of-possession shape.
  - A source URL for every figure (Opta Analyst, FBref/StatsBomb, WhoScored, league sites, peer-reviewed papers). Figures not published for a league are marked "n/a" and are never invented.
  - The band constants used by the harness live in one class, `RealismReference`, with each constant commented with its doc section.

  (acceptance: the doc exists with sources for every number. The reviewer checks that every `RealismReference` constant matches the doc. A band may differ from the R2 starting value only when narrowed or shifted to the doc's top-5 envelope ±5%, and every such change is logged in the doc.)

### Phase A — realism (engine v12)
- R2 Neutral realism bands. 1,000 matches, equal-strength top-tier sides, Balanced style, 4-4-2 vs 4-4-2 and 4-3-3 vs 4-3-3. Starting values come from the top-5 leagues (Opta Analyst 2024-25: goals 2.5-3.5, shots 24-27.4, ball in play 54.5-56.8 min, save rate ~69%).

  | Metric | Band |
  |---|---|
  | Goals per match | 2.5-3.2 |
  | Shots per match | 23-28 |
  | On target (excl. blocked), % of shots | 30-38% |
  | Blocked, % of shots | 20-30% |
  | Shots inside the box, % of shots | 60-72% |
  | Headed shots per match | 3-5 |
  | Keeper save rate (on-target shots saved) | 65-75% |
  | Save rate, shots inside the box | 55-68% |
  | Save rate, shots outside the box | 78-90% |
  | Passes attempted per team | 380-520 |
  | Pass accuracy | 78-86% |
  | Passes per open-play sequence | 3.0-5.0 |
  | 10+ pass open-play sequences per team | 6-14 |
  | Direct speed (open play) | 1.3-1.8 m/s |
  | PPDA | 9-14 |
  | Crosses per team | 12-20 |
  | Corners per match | 9-11 |
  | Fouls per match | 20-27 |
  | Ball in play | 53-59 min |
  | Distance per outfield player | 9.5-11.5 km |

  (acceptance: the harness prints every metric and band. The user pastes the output, and all bands are in range.)
- R3 Quality tier effect. Two equal sides rated at the game's second-tier average, against two at top-tier average:
  - ball in play lower by ≥ 1.5 min;
  - pass accuracy lower by ≥ 3 pp;
  - long-ball share higher by ≥ 2 pp;
  - passes per sequence lower;
  - goals 2.3-2.9.

  (acceptance: a harness comparison over 1,000 matches per tier.)
- R4 Team shape.
  - **Out of possession:** in open play, a side's outfield length (deepest to highest outfield player) is 25-40 m in its own half and ≤ 45 m anywhere. Its width is 30-45 m.
  - **In possession:** in the opposition half, width is ≥ 45 m and at least one player is within 8 m of each touchline zone.
  - **Off-target time:** the existing metric "off-target seconds per player" has a median ≤ 5 s per match. It is 33 s today.

  (acceptance: harness metrics over 1,000 matches. A unit test on a scripted defensive phase asserts the block length.)
- R5 Players stay on the pitch. No outfield player is more than 1 m outside the touchlines or goal lines unless he is the throw-in taker, the corner taker, or carried there by the ball in play for < 2 s. (acceptance: a harness counter is 0 over 1,000 matches; a unit test on the touchline clamp.)
- R6 Keeper positioning.
  - **Open play:** the keeper stands on the line between the ball and his goal centre. His distance from the goal line is ≤ 6 m when the ball is within 35 m of his goal, and ≤ 18 m otherwise, more only to claim a through ball.
  - **Restarts:** on a goal kick he takes it or stands within 6 m of the line.

  (acceptance: a harness metric gives the share of open-play frames that break the rule, ≤ 1%. A unit test on keeper target positions for 5 ball positions.)
- R7 Keeper saves are real and visible. The save rates are those in R2. On ≥ 70% of saves the keeper's stream position comes within 1.5 m of the point where the shot crosses his goal line, or of the ball at contact. The renderer draws the keeper's movement and highlights the save. (acceptance: harness metric. A renderer/presenter test asserts the save highlight is emitted, and the user checks it in Play mode.)
- R8 Movement looks alive.
  - Base acceleration is 5-6 m/s², tunable.
  - In open play, an outfield player stands still (speed < 0.2 m/s) for ≤ 15% of his time.
  - Off-ball adjustment is continuous: the arrival deadband is replaced by speed easing, so players slow into their spot instead of stopping 3 m short.
  - Distance covered stays in the R2 band.

  (acceptance: harness metrics. A unit test on easing: a player 2 m from his target still moves toward it.)
- R9 Passing chains. Open-play shots preceded by ≥ 3 passes in the same sequence are ≥ 40% of open-play shots. Open-play shots preceded by 0-1 passes are ≤ 35%, and not counting rebounds or high turnovers, ≤ 25%. The passes-per-sequence and 10+ sequence bands in R2 hold. (acceptance: harness metrics. Bands confirmed or narrowed by the R1 doc.)
  - Amended by user decision 2026-10-10 (#82): the engine's ball is in play about 81 minutes (real 53-59), so the volume bands scale by 81/56: passes attempted per team 550-750 in the harness (R2's 380-520 stays the real-football reference) and at most 1,750 passes a match in `BallDecisionTests.Passes_ArriveLikeRealFootball` (was 1,200). #82 gates the volume row; the ratio rows (passes per sequence, 10+ sequences per team, shots after 0-1 and 3+ passes, pass accuracy 78-86%) are reported and move to #87.
- R10 Shot selection.
  - The open-goal shortcut fires only for a genuinely open goal: keeper beaten or out of position, lane clear, ≤ 20 m.
  - Shots from it are ≤ 15% of all shots.
  - A carrier outside the box with a blocking defender in the lane and a teammate free in the box passes or carries rather than shoots, in ≥ 70% of scripted cases.

  (acceptance: harness counters; a unit test on the scripted case. The `openGoalShotRate` ≥ 0.90 of the old R4 still holds.)
- R11 Restart shapes. On goal kicks played short the centre-backs split to the box corners and the full-backs go wide. On throw-ins, ≥ 2 teammates offer within 15 m. On corners and free kicks the old R6 structures still hold. (acceptance: unit tests on restart formations; harness count of short vs long goal kicks, both > 0.)
- R12 Director playback.
  - Live open play runs at 1.3x real time when 1x is selected.
  - Dead time runs at twice the selected speed: ball out of play, restarts being set up, celebrations, substitutions, injuries.
  - Sterile possession and the rest of the dead time are cut, with the cut summaries of the old R14, so that a 1x match lasts 10 ± 1 min.
  - 2x and 4x scale every rate, and Skip jumps to full time.
  - Every goal, shot, card, penalty and substitution is shown.
  - No segment ever plays slower than 1.3x real time at 1x.

  (acceptance: a test over 200 reports asserts the duration, the rates and that all key events are shown. The user checks it in Play mode.)
- R13 Smoother stream. Position stream frames go from 2 to 5 per match second (`StreamTicksPerFrame` 5 → 2). A full match report grows by ≤ 2.6x and stays within the online payload limit. (acceptance: a test on report size against the limit; online flow tests still green.)
- R14 Hardened tactic tournament gate. The old R8 gate (no preset > 55% points; each preset < 45% against at least one opponent) is evaluated as the mean over 3 seed sets. A single seed set was shown to be coin-flip fragile even on the base. (acceptance: harness output, judged with the user.)
- R15 Phase A closure.
  - The R2-R13 bands hold.
  - The old R8-R11 effect gates and R17 fast-model gates still hold.
  - Generation time is ≤ +25% of the v11 engine in the same run.
  - The golden master moves once to engine v12 in its five pinned places, after the user approves the harness.

  (acceptance: harness output plus determinism tests green.)

### Phase B — playing styles × formations
- R16 Expanded instruction model. ~14 instruction axes, each with 3-5 levels. The middle level is the identity, so a Balanced side reproduces the v12 neutral match.

  | Phase | Axes |
  |---|---|
  | In possession | mentality; passing directness; tempo; attacking width; build-up (play out from the back / mixed / goal-kick long); final-third approach (work into box / mixed / cross early); shooting (patient / normal / shoot on sight); dribbling (less / normal / more) |
  | Transitions | on losing the ball (counter-press / normal / regroup); on winning the ball (counter / normal / hold shape) |
  | Out of possession | line of engagement (low / mid / high); defensive line (deep / normal / high / very high); pressing intensity; marking (zonal / mixed / man-oriented); press direction (force outside / none / force inside) |

  The four v11 axes map onto these.

  (acceptance:
  - unit tests on the mapping;
  - the Balanced style plays bit-identically to v12;
  - for every axis, its extreme against neutral moves its target metric in the documented direction by a documented minimum, in a game-level test of the kind restored in #66.)
- R17 Formation catalogue. ~24 formations, each with 11 slots and in/out-of-possession shapes from R1: 4-4-2, 4-4-2 diamond, 4-4-1-1, 4-3-3, 4-3-3 holding, 4-2-3-1, 4-1-4-1, 4-5-1, 4-3-2-1, 4-1-2-1-2, 4-2-2-2, 4-3-1-2, 4-1-3-2, 4-2-4, 4-6-0, 3-5-2, 3-4-3, 3-4-2-1, 3-4-1-2, 3-1-4-2, 3-5-1-1, 5-3-2, 5-4-1, 5-2-3, 5-2-1-2. The final list is the R1 catalogue.

  (acceptance:
  - unit tests: every formation has 11 valid slots and a lineup can be selected;
  - harness: each formation with the Balanced style keeps the R2 core bands (goals, shots, passes) within ±15%;
  - a formation sweep against 4-4-2 Balanced: no formation takes > 60% of points, as a mean over 3 seed sets.)
- R18 Style catalogue. ~15 named styles, each an editable preset over R16: Tiki-taka, Positional play, Gegenpress, Vertical tiki-taka, Control possession, Wing play, Route one, Direct, Fluid counter, Direct counter, Mid block, Catenaccio, Park the bus, Man-oriented press (Gasperini), Balanced. Styles are independent of formation. Each style lists suggested formations, used by the AI and shown in the UI. (acceptance: unit tests that every style resolves to valid instructions, and that a tweaked style is stored as style + overrides.)
- R19 Style fingerprints. Each style plays against Balanced with the same formation and equal sides, over 500 matches. It must move ≥ 5 of the 7 fingerprint metrics in the real-world direction from R1, each by its documented minimum:
  1. passes per team;
  2. possession;
  3. passes per sequence;
  4. 10+ sequences;
  5. direct speed;
  6. PPDA;
  7. one of crosses, long-ball share or defensive-line height, as the style dictates.

  Four anchor styles also meet absolute bands taken from R1 example teams:
  - **Tiki-taka:** possession 58-70%, passes 600-780, passes per sequence ≥ 5.0.
  - **Gegenpress:** PPDA ≤ 9, high turnovers ≥ 1.4x Balanced.
  - **Route one:** long-ball share ≥ 18%, passes per sequence ≤ 3.0.
  - **Park the bus:** possession ≤ 40%, PPDA ≥ 16, average defensive line ≤ 32 m from own goal.

  (acceptance: harness table, judged with the user.)
- R20 No dominant style. The tournament gate of R14 (mean over 3 seed sets) runs over all styles, each in its first suggested formation, at ≥ 200 matches per pairing: no style takes > 55% of points, and each style takes < 45% against at least one opponent. (acceptance: harness output, judged with the user; flagged as long-running and statistically sensitive.)
- R21 Tactics screen. In Tattiche, the manager:
  - picks a formation from the catalogue;
  - picks a style, which shows its suggested formations;
  - tweaks any instruction (a tweak marks the style "custom");
  - sees a one-line description of each style and instruction.

  The screen follows the Phase 14 UI rules (`UiKit.StandardPage`, `BlockHead`, sizes in `FtsTheme.uss` with a `.fts--mobile` override), and all strings are in en + it. (acceptance: loc coverage complete; a presenter test on style → instructions → custom; Play-mode check by the user.)
- R22 AI clubs choose styles. Each AI club picks a formation and style that suit its squad (e.g. Route one or Direct counter when its best players are strong and fast forwards and its passers are weak; Tiki-taka only with strong passers) and its manager profile.
  - Across a generated league, no single style is used by > 30% of the clubs, and ≥ 6 different styles are in use.
  - Lower-tier leagues lean toward direct styles.

  (acceptance: a deterministic world-generation test on these distributions.)
- R23 Saves and online.
  - The save version is bumped, and a migration maps every stored v11 tactic (formation + 4 axes) to the nearest catalogue formation plus a Balanced-based style with overrides.
  - Prematch plans and touchline shouts keep working.
  - Online and ranked matches carry the engine version, and a client with another engine version is refused with a localized message.

  (acceptance: a migration test over fixture saves; an API test on version refusal; existing online/dev-tool flows green.)
- R24 Fast model reads styles and formations. Each style's goals for and against shift in the same direction as in the full engine, and outcome shares stay within the old R17 tolerances (±0.15 goals, ±3 pp). (acceptance: harness comparison table.)
- R25 Phase B closure.
  - R16-R24 hold.
  - Phase A bands hold for Balanced.
  - Generation time stays ≤ +25% of v11.
  - The golden master stays at v12 if the Balanced match is bit-identical. Otherwise it moves once to v13 after the user approves.

  (acceptance: harness + determinism tests.)

## Tech decisions
- **Stack/libs:** no new packages. Sim.Core stays netstandard2.1 with integer arithmetic, fixed iteration order and a single seeded RNG. Every tunable lives in `BalanceConfig`, and instruction tables read their middle entry as identity.
- **Structure:**
  - Phase A extends the V11 brain under `Match/Movement/`, with files ≤ ~300 lines.
  - Phase B adds `Tactics/Styles/` (style catalogue, instruction axes) and `Tactics/Formations/` (catalogue data).
  - `TacticInstructions` becomes the expanded model with a v11 mapping.
- **Data/storage:** the reference bands live in `RealismReference` (Sim.Core.Tests or Sim.Core analysis), sourced from `docs/research/football-reference.md`. The save bump and migration happen in Phase B only.
- **Research:** primary public sources (Opta Analyst, FBref/StatsBomb, WhoScored, league sites, papers). The first Phase A task writes the R1 doc.
- **Commands:**
  - Build: `dotnet build --nologo -v q`. Test: `dotnet test --nologo -v q`.
  - Harness: `dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj --logger "console;verbosity=detailed"` plus the explicit harness filters.
  - After any `shared/` change the user runs `.\tools\build-simcore.ps1`.
  - The client is type-checked via Roslyn; Play mode is run by the user.

## Constraints / non-functional
- Architecture §4.1: pure, deterministic, no I/O, balance as data. The renderer and director never change a result.
- Balance-sensitive requirements (R2-R4, R6-R10, R14-R20, R24) close only after the user pastes the harness output and judges it. Statistically fragile tests are flagged and sized (≥ 64 seeds for game-level instruction tests, 3 seed sets for tournament gates).
- Mobile/WebGL: no per-frame allocations in the renderer. The 5 fps stream must keep WebGL-sliced generation working.
- Localisation: every new string in en + it.
- Runs end before 24:00 local time (the user switches the PC off).

## Open risks
- Some fingerprint metrics (PPDA, direct speed, 10+ sequences) are published only for the top leagues. Bands for the other leagues rely on fewer sources, so R3 uses only widely published metrics.
- The R2 bands and the tournament gates pull against each other, so several tuning rounds are likely.
- R20 with ~15 styles is ~105 pairings × 200 × 3 seed sets. It is long-running and must run in parallel; the fallback is 100 per pairing for screening and full size only for the final candidate.
- A denser stream (R13) may exceed the online payload budget. The fallback is delta-encoded frames.
- The Phase B save bump touches every stored tactic, and the migration must be lossless for formation and the four v11 axes.

## Tasks (filled by /ba:issues)
| # | Title | Depends on | Requirements | Issue |
|---|-------|------------|--------------|-------|
| 1 | docs+test: football reference data and RealismReference bands | - | R1 | #70 |
| 2 | harness: shot, keeper and passing realism metrics | 1 | R2, R7, R9, R10 | #71 |
| 3 | harness: team shape, movement, pitch-bounds and keeper-position metrics | 1 | R4, R5, R6, R8 | #72 |
| 4 | harness: quality-tier comparison and 3-seed-set tournament gate | 1 | R3, R14 | #73 |
| 5 | engine: outfield players stay on the pitch | 3 | R5 | #74 |
| 6 | engine: keeper positioning between ball and goal | 3 | R6 | #75 |
| 7 | engine: zone-based save model and keeper reaching the ball | 2, 6 | R7 | #76 |
| 8 | engine: realistic acceleration and eased arrival instead of a deadband | 3 | R8 | #77 |
| 9 | engine: compact block out of possession, width in possession | 3, 8 | R4 | #78 |
| 10 | engine: structured goal-kick and throw-in shapes | 9 | R11 | #79 |
| 11 | engine: shots blocked by defenders in the lane | 2 | R2 | #80 |
| 12 | engine: open-goal shortcut only for a genuinely open goal | 2, 11 | R10 | #81 |
| 13 | engine: support angles and longer passing chains | 2, 9 | R9 | #82 |
| 14 | engine: quality tier shapes passing accuracy and directness | 4, 13 | R3 | #83 |
| 15 | engine: position stream at 5 frames per second within a size budget | - | R13 | #84 |
| 16 | director: 1.3x live play, dead time at twice the selected speed, 10-minute match | 15 | R12 | #85 |
| 17 | client: visible keeper save in the renderer | 7, 16 | R7 | #86 |
| 18 | balance: tune engine v12 into the realism bands | 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 | R2-R11, R14 | #87 |
| 19 | engine v12: fast model recalibration, timing and golden master | 18 | R15 | #88 |
| 20 | tactics: expanded instruction model with v11 mapping and neutral identity | 19 | R16 | #89 |
| 21 | engine: in-possession axes take effect | 20 | R16 | #90 |
| 22 | engine: transition and out-of-possession axes take effect | 20 | R16 | #91 |
| 23 | tactics: formation catalogue (~24 real formations) | 1, 20 | R17 | #92 |
| 24 | tactics: style catalogue as editable instruction presets | 20 | R18 | #93 |
| 25 | harness+balance: style fingerprints match their real-world styles | 21, 22, 24 | R19 | #94 |
| 26 | harness+balance: style tournament and formation sweep | 23, 25 | R17, R20 | #95 |
| 27 | saves: version bump and migration of v11 tactics | 23, 24 | R23 | #96 |
| 28 | online: engine version carried and outdated clients refused | 19 | R23 | #97 |
| 29 | world: AI clubs choose squad-coherent styles and formations | 23, 24 | R22 | #98 |
| 30 | client: Tactics screen with formation, style and instruction tweaks | 23, 24, 27 | R21 | #99 |
| 31 | fast model reads styles and formations; Phase B closure | 25, 26 | R24, R25 | #100 |
