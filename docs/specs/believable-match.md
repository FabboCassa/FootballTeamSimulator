# Believable match: decisions, individual movement, readable ball

Status: approved
Repo: FabboCassa/FootballTeamSimulator   Base branch: main

## Goal
Engine v12 meets the aggregate realism bands, but a watched match still does not look like football (user recordings 2026-10-24 10:11 on v11 and 10:30 on v12, plus the `pitch` harness dump at seed 20260803).
- **Decisions are inverted.** In one match, a carrier is free (no defender within 3 m) and ≤ 12 m from goal, centrally, 11 times, and never shoots: 6 square passes, 4 passes back, 1 clearance. `V11ShotFromDistancePercent = 4200` multiplies a clear shot from outside the box by 42. Inside the box, a shot is worth ×0.75 and a body in the lane cuts it to 30% or 10%. The pass is priced on xT gain, the shot on scaled xG, so the two are not in the same unit.
- **The team moves as one block.** In 1 s windows with ≥ 6 outfielders moving > 1 m/s, the median direction agreement is 0.88, and 44% of windows are above 0.9. On video, the side slides back and forth like table football, and nobody runs against the team's movement.
- **The ball teleports.** It jumps > 8 m in one 0.2 s frame 31 times a match: restarts placed without transport, and passes faster than 40 m/s. A shot flies for 2 frames, stops short of the goal line, and reappears on the goal-kick spot. A keeper moves 1.3 m in the last second before a save.
- **The client is out of sync and jumps.** Toasts fire at `Minute × TicksPerMinute` (`MatchRenderer.cs:294`), not at the action's frame. Director cuts jump all 22 players with no transition.

Outcome: a match where the man on the ball makes the choice a real player would make, the 22 move as individuals with roles, and the ball and the keeper are never teleported. Each is measured against real tracking data and sourced statistics, and the existing realism, style and fast-model gates still hold.

## Scope
In:
- Movement reference bands from public tracking data, added to `docs/research/football-reference.md` and `RealismReference`.
- New harness metrics: movement feel, chance decisions, ball continuity, keeper reaction.
- Engine:
  - an expected-value action choice (shot vs pass vs carry in one unit);
  - individual movement (reaction delays, role jobs, curved runs);
  - ball continuity (speed caps, restart transport, shots that finish past the line or in the net);
  - keeper reaction and dive;
  - ball height in the stream.
- Absorbed balance issues: #120 (headed shots, crosses, direct speed), #127 (narrow AM + two-striker shapes, 5-4-1), #131 (Tiki-taka passes, Park the bus possession, Mid block), #135 (costly instruction levels).
- Client:
  - toasts and commentary on the action frame;
  - cut transitions;
  - readable shot, goal and save;
  - ball shadow for height.
- Harness tool: an explained HTML replay showing each carrier decision with its options and values.
- Engine v13, with the golden master moved once, and the fast model recalibrated.

Out:
- Camera zoom/follow, slow motion and goal replays (the user judged them not to be the problem).
- New player attributes, injuries, weather.
- A save-format change (the stream is a match artifact, not a save field).
- Third-party packages, and any engine outside Sim.Core.

## Users / flows
1. SP manager watches at 1x. Free in the box, a striker shoots. With a defender square in front of him and a mate unmarked at the far post, he squares it. From 25 m with a crowded box, he works it wide or recycles. Speculative long shots are rare and come from players with the appetite and the skill.
2. Out of possession, the block shifts, but players arrive at different moments: the near-side full-back steps, the far-side winger tucks in late, and the holding midfielder screens. In possession, the striker attacks the depth, the wingers hold the width, a full-back overlaps, and a midfielder arrives in the box. Runs bend around opponents.
3. A wide shot crosses the goal line and the ball stays out while a player fetches it for the goal kick. A saved shot shows the keeper moving across and diving toward where the ball crosses the line. A goal ends in the net.
4. The "Tiro parato" toast appears on the frame of the save, and the commentary line appears with it. A director cut is a short fade with its one-line summary.
5. The developer opens the harness HTML replay, filters "chances", and sees for every carrier decision the top options (shot xG, best passes with completion and value, carry) and the chosen one.

## Requirements

### Reference and measurement
- R1 Movement reference from real tracking data.
  - Source: Metrica Sports open sample games (3 matches, 25 fps). If their licence forbids deriving published figures, use SkillCorner open data instead. Raw data is downloaded by a script under `tools/` and is never committed. Only derived aggregates go into the doc.
  - Metrics, per outfield player or per team, open play only:
    - direction agreement R in 1 s windows (≥ 6 outfielders moving > 1 m/s): median, and share of windows > 0.9;
    - heading reversals (> 120°, both 1 s steps > 0.5 m/s) per player-minute;
    - speed-band shares: < 0.2, 0.2-2, 2-4, 4-5.5, 5.5-7 and > 7 m/s;
    - accelerations > 2 m/s² per player-minute;
    - ball speed p50, p90 and p99 for passes and for shots.
  - Each band is the min-max across the reference matches, widened by 10% of the range, or by ±5% of the value when the range is 0.
  - The doc records the source URL, the licence, the script and the commit of the data used.

  (acceptance: the script reproduces the numbers in the doc; every new `RealismReference` constant cites its doc section, and a test asserts the constants match the doc table.)
- R2 Feel metrics in the realism harness, each printed with its band:
  - the R1 movement metrics;
  - ball jumps > 8 m in one frame;
  - frames where the ball is placed at a restart with no player transport;
  - shot flight: share of wide shots whose ball crosses the goal line, and share of goals whose ball ends ≥ 1 m behind the line;
  - keeper displacement between shot release and the shot crossing the line;
  - the chance-decision table: carriers ≤ 16 m from goal centre with |y − 34 m| ≤ 10 m, split by "free" (no defender within 3 m) and by action taken: shot, pass forward/square, pass back, carry, clear, lost.

  (acceptance: unit tests on scripted streams for each metric; the harness prints the full table.)

### Engine (v13)
- R3 Expected-value action choice. Shot, pass, cross and carry are priced in one unit, the expected goal value of the possession.
  - **Shot:** xG × (1 − block probability from bodies in the lane, from the existing `ShotBlock` model) + the expected value of the rebound or corner.
  - **Pass/cross:** completion probability (execution × lane × receiver safety) × the value at the receiver, minus (1 − completion) × the opponent's value at the loss point. The value at the receiver is the greater of his own shot value after reception and the xT of his spot.
  - **Carry:** the same with the carry's keep probability.
  - `V11ShotFromDistancePercent`, `V11ShotValuePercent`, `V11ShotBlockerPercent` and `V11ShotCloseBlockerPercent` are removed as decision multipliers. Style axes (shooting appetite, directness, final-third approach) act as bounded biases on the same unit, with the middle level as identity.

  (acceptance:
  - unit tests on scripted scenes: free at ≤ 12 m central with the keeper on his line → shot in 100% of cases; a defender square in the lane and a mate unmarked at ≤ 6 m central → pass; 25 m out, clear lane, crowded box, normal appetite → no shot in ≥ 70% of seeds;
  - harness: free ≤ 12 m central → shot ≥ 80%; unpressed carrier ≤ 16 m central → pass back ≤ 15%; the R2 "shots inside the box" band of the previous spec (60-72%) holds.)
- R4 Individual movement.
  - **Reaction delay:** each outfielder re-reads his target after a delay of 150-600 ms, from `Positioning` (tunable). The delays are staggered so the eleven do not all retarget on the same tick.
  - **Role jobs in possession:** the striker attacks the depth on the last line; wingers hold the width; one full-back overlaps when the ball is on his side in the final two thirds; a central midfielder arrives in the box when a cross or cut-back is on; the holding midfielder stays behind the ball.
  - **Role jobs out of possession:** the near-side full-back steps to the wide carrier; the far side tucks in; the holding midfielder screens the passing lane to the striker.
  - **Curved runs:** a run's path bends around an opponent within 2 m of the straight line.
  - **Acceleration and speed caps:** these keep the previous spec's R8 easing. The speed bands follow R1.

  (acceptance:
  - unit tests: staggered delays; each role job on a scripted scene; curve around a blocker;
  - harness: direction agreement, reversals, speed-band shares and accelerations inside the R1 bands; the shape bands of the previous spec's R4 still hold.)
- R5 Ball continuity.
  - Ball speed per frame is capped per action kind at the R1 p99 for passes and shots, + 10%.
  - **Restarts:** the ball is carried, or rolled, to the restart spot at ≤ 8 m/s by the taker or the nearest player. It is never placed.
  - **Wide shots:** the ball crosses the goal line and stays beyond it for ≥ 1.5 s of match time before the restart transport starts.
  - **Goals:** the ball ends ≥ 1 m behind the line between the posts and stays there until the kick-off transport.
  - **Saves:** the ball goes to the keeper's hands, to a rebound, or out for a corner, with no jump.

  (acceptance: harness: 0 ball jumps > 8 m in one frame outside director cuts, and 0 placed restarts; unit tests on each restart transport and on the wide-shot and goal end positions.)
- R6 Keeper reaction.
  - From shot release, the keeper moves toward the predicted crossing point after 150-300 ms (from `Goalkeeping`), at dive speed for the last 2 m.
  - On ≥ 70% of saves, the keeper's stream position is within 1.5 m of the crossing point or of the ball at contact. This restates the previous spec's R7, now measured with the new displacement metric.
  - Save rates stay in the previous spec's R2 bands.

  (acceptance: harness metric; a unit test on a scripted shot to each corner.)
- R7 Ball height in the stream.
  - A `BallZ` array in decimetres, one value per frame, set from the ball model (lofted passes, crosses, high shots, headers).
  - The report grows by ≤ 15% over v12 and stays within the online payload limit used by the R13 test of the previous spec.

  (acceptance: a stream test on the height of a cross and a lofted pass; a report-size test.)
- R8 Absorbed balance gates. On the 1,000-match harness, with the other bands held:
  - **#120:** headed shots 3-5 a match, crosses 12-20 per team, direct speed 1.3-1.8 m/s;
  - **#127:** all 27 formations keep goals, shots and passes within ±15% of the R2 core;
  - **#131:** Tiki-taka passes 600-780 with passes per sequence ≥ 5.0; Park the bus possession ≤ 40%; Mid block moves ≥ 5/7 fingerprint metrics;
  - **#135:** every listed instruction level costs ≤ 8 pp against its neutral level in the screening tournament.

  The four issues close as superseded by this spec when it lands.

  (acceptance: harness output pasted by the user and judged together; the `ReportOnly` set in `StyleFingerprintHarnessTests` is empty.)
- R9 Closure.
  - The previous spec's R2 bands, R19 fingerprints, R20 tournament (mean over 3 seed sets) and R24 fast-model tolerances hold.
  - Generation time is ≤ +25% of v12 in the same run.
  - The golden master moves once to engine v13, in its pinned places (`SimulationDeterminismTests.cs`, `SimulationService.cs`, `docs/ops/runbook.md`, `docs/store/release-checklist.md`), after the user approves the harness.
  - Online engine-version checks refuse v12 clients.

  (acceptance: harness + determinism tests + full `dotnet test` green.)

### Client
- R10 Toasts and commentary on the action frame.
  - Every toast fires within 1 frame of its `BallAction` tick (shot, save, goal, miss, card, penalty, substitution), not at `Minute × TicksPerMinute`.
  - Commentary lines appear on their action frame.
  - The clock, the toast and the commentary show the same minute for the same moment.

  (acceptance: a Sim.Core playback test that maps every key event to its frame; a client type-check with 0 errors; a Play-mode check.)
- R11 Cut transitions. Every `PlaybackRate.Cut` boundary plays as a fade (≥ 0.3 s wall at 1x, scaled by 2x/4x) with the cut's one-line summary as a caption. No rendered frame interpolates positions across a cut. (acceptance: a `BroadcastPlayback` test that emits a transition at every cut boundary; Play-mode check.)
- R12 Readable shot, goal and save.
  - The shot line runs from the shooter to the ball.
  - Goal: the ball in the net, with a brief net highlight. Wide shot: the ball past the line.
  - Save: the keeper's dive is drawn toward the impact, plus the existing save highlight.
  - The ball's shadow and scale follow `BallZ`.
  - No per-frame allocations.

  (acceptance: a presenter/renderer test on the emitted save and goal moments; client type-check with 0 errors; Play-mode check.)

### Tooling
- R13 Explained HTML replay.
  - An optional decision sink on the brain records, for each carrier decision: tick, carrier, the top 3 options with kind, target, completion, value, and the choice.
  - The sink is null in production. Attaching it does not change the match hash.
  - `PitchDump` embeds the records, and the viewer shows them on the frame, with a "chances" filter (the R2 chance zone).

  (acceptance: a determinism test with and without the sink; a dump test that the records are present and ordered.)
- R14 Dev and Play-mode acceptance. The user records a 1x match in Play mode. The checklist:
  - a free striker in the box shoots;
  - at least one cut-back or through ball is visible;
  - full-backs overlap;
  - no ball or player teleports outside fades;
  - every save shows the keeper moving;
  - every toast is on its moment.

  (acceptance: the user's recording, judged together.)

## Tech decisions
- **Stack/libs:** no new packages. Sim.Core stays netstandard2.1 with integer arithmetic, fixed iteration order and a single seeded RNG. All tunables live in `BalanceConfig`, and instruction tables read their middle entry as identity.
- **Structure:**
  - The action valuation moves to an expected-value module under `Match/Movement/` (the old multipliers are deleted, not left dormant).
  - Role jobs live in `V11Positioning` / `V11Brain.Support` siblings.
  - Ball transport lives in `Restarts` / `MatchBall`.
  - Files ≤ ~300 lines.
- **Data:**
  - R1 tracking aggregates go in `docs/research/football-reference.md` and `RealismReference`.
  - The download/derive script lives in `tools/` (Python, run once by the agent; data not committed).
  - `BallZ` is packed like the other stream arrays.
- **Commands:**
  - Build: `dotnet build --nologo -v q`. Test: `dotnet test --nologo -v q`.
  - Harness: `dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj --logger "console;verbosity=detailed"` plus the realism/style filters, and `tools/BalanceHarness --scenario pitch` for the dump.
  - Client: Roslyn type-check (FTS.Views → FTS.MatchView → FTS.Presenters).
  - After any `shared/` change the user runs `.\tools\build-simcore.ps1`.
- **Workflow:** `/ba:auto` with `merge`: each verified PR is squash-merged to main after green CI, with no stacked chains.

## Constraints / non-functional
- Architecture §4.1: pure, deterministic, no I/O, balance as data. The renderer and the director never change a result.
- Every test or build run uses BelowNormal CPU priority.
- Balance-sensitive requirements (R3-R6, R8, R9) close only after the user pastes the harness output and judges it. Game-level tests use ≥ 64 seeds, and tournament gates use 3 seed sets. Statistically fragile tests are flagged.
- Mobile/WebGL: no per-frame allocations. WebGL-sliced generation keeps working, and generation time is ≤ +25%.
- Localisation: every new string in en + it.
- Runs end before 24:00 local time.

## Open risks
- Licence of the Metrica sample data (a fallback to SkillCorner open data is defined). Three matches is a thin reference, so the bands are widened by rule, not by taste.
- Re-pricing decisions moves every downstream band (shots, xG, goals, style fingerprints, tournament). Several tuning rounds are likely, and R8 adds four gates that were already hard to meet.
- Staggered reaction delays and role jobs may cost generation time; the +25% budget is the guard.
- `BallZ` adds payload; the ≤ 15% budget and the online limit are the guards.

## Tasks (filled by /ba:issues)
| # | Title | Depends on | Requirements | Issue |
|---|-------|------------|--------------|-------|
| 1 | docs+tool: movement reference bands from real tracking data | - | R1 | #138 |
| 2 | harness: movement feel metrics | 1 | R2 | #139 |
| 3 | harness: ball continuity, shot flight and keeper displacement metrics | 1 | R2 | #140 |
| 4 | harness: chance-decision table | - | R2 | #141 |
| 5 | tooling: decision sink and explained HTML replay | - | R13 | #142 |
| 6 | engine: expected-value action choice | 4, 5 | R3 | #143 |
| 7 | engine: staggered reaction delays per player | 2 | R4 | #144 |
| 8 | engine: role jobs in possession | 7 | R4 | #145 |
| 9 | engine: role jobs out of possession and curved runs | 7 | R4 | #146 |
| 10 | engine: ball speed caps and restart transport | 3 | R5 | #147 |
| 11 | engine: wide shots cross the line, goals end in the net | 3 | R5 | #148 |
| 12 | engine: keeper reaction and dive | 3, 11 | R6 | #149 |
| 13 | engine: ball height in the stream | - | R7 | #150 |
| 14 | client: toasts and commentary on the action frame | - | R10 | #151 |
| 15 | client: fade with caption at director cuts | - | R11 | #152 |
| 16 | client: readable shot, goal and save, ball shadow | 11, 12, 13 | R12 | #153 |
| 17 | balance: tune v13 into the R1/R2 bands with the #120 and #131 gates | 6, 8, 9, 10, 11, 12 | R3-R6, R8 | #154 |
| 18 | balance: narrow formations and costly instruction levels (#127, #135) | 17 | R8 | #155 |
| 19 | engine v13: fast model, timing, golden master, online version | 18 | R9 | #156 |
| 20 | Play mode: recording and checklist (needs-human) | 14, 15, 16, 19 | R14 | #157 |
