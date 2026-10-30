# Free ball, real crosses, onside striker, purposeful movement

Status: approved
Repo: FabboCassa/FootballTeamSimulator   Base branch: main

## Goal
The user's 1x recording of 2026-10-30 11:39 (engine with #147–#153) looked better than before, but it still does not look like football:
- the ball still seems to teleport or change course mid-flight;
- the ball is "magnetised" to players and is never alone, so there are no long balls into space;
- no crosses are visible;
- players drift and oscillate without purpose;
- the striker stands offside for long stretches.

Real tracking (DFL open data, Köln–Bayern and Bochum–Leverkusen, 2022/23) and the engine (40-match `pitch` harness at seed 20260803, engine v12 + #147) were compared with the same scripts on 2026-10-30:

| Metric (ball in play) | Real (2 matches) | Engine |
|---|---|---|
| Ball with nobody within 1.5 m | 45.7–47.4% | 19.2% |
| Ball with nobody within 3 m | 25.6–26.3% | 14.9% |
| Fast free ball (> 5 m/s, nobody within 2 m) turning > 20° in 0.2 s | 1.0–1.2% (tracking noise) | 7.6% |
| Striker in an offside position, share of his team's possession | 7.7–8.0% | 22.3% |
| Crosses per match (events) | 32–33 | 74.9 counted, none visible |
| Passes per match (events) | 696–951 | 1273 |
| Players within 5 m of the ball | 1.64–1.79 | 1.78 |

Outcome: the ball lives on its own. It is kicked into space, it runs, it is contested, and it changes course only when something touches it. Crosses are lofted from the flank into the box and are visible at 1x. The striker plays on the last line. The 22 players move with a purpose, measured against a reference built from recent real top-league data.

The remaining issues of `believable-match.md` are absorbed here: #143, #144, #145, #146, #154, #155 and #156 close as superseded. Its balance gates (#120, #127, #131, #135) move to a later balance spec.

## Scope
In:
- Reference v2: real tracking and event data from recent top-league seasons, split by league and team strength, turned into bands in `docs/research/football-reference.md` and `RealismReference`.
- New harness metrics: ball freedom, mid-flight turns with their cause, pass-into-space share, ball height, high crosses, loose balls, aerial duels, striker offside position and oscillation.
- Engine v13:
  - a free ball with no magnetism, and an imperfect first touch;
  - passes into space, through balls, lofted long balls, aerial duels and second balls;
  - real crosses and box arrival;
  - a striker who holds the line and times his runs;
  - purposeful movement, absorbing the expected-value action choice, role jobs, curved runs and staggered re-reads of #143–#146.
- Client: ball interpolation along the physics path, height drawn from `BallZ`, and a dev overlay that detects rendered ball jumps.
- One engine version bump (v12 → v13), golden master moved once, and the fast model recalibrated (absorbs #156).

Out:
- Balance gates #120, #127, #131 and #135. They stay open for a later spec. Here the current bands only must not get worse.
- Camera zoom/follow, replays and slow motion.
- New player attributes, weather and injuries.
- A save-format change.
- Paid data, and any data that needs the user's registration (PFF and Hudl were considered and left out).

## Users / flows
1. SP manager watches at 1x. A centre-back plays a lofted switch to the far winger. The ball flies, lands ahead of him and runs, and he runs onto it. An opponent could have stepped in.
2. The winger reaches the byline area and crosses high into the box. The striker attacks the near post, the far winger the far post, and a midfielder arrives late. A header or a clearance follows, and the second ball is contested.
3. A tackle pokes the ball loose. It rolls free for a second, and the nearest players from both sides race to it.
4. The striker waits on the last defender's line and starts his run when the midfielder lifts his head with a lane. He is flagged offside a couple of times a match, not stuck beyond the line.
5. Players re-position with intent: the far-side full-back tucks in, the holding midfielder screens, and nobody drifts back and forth.
6. The developer runs the `pitch` harness. Every metric prints with its real band, and any free-ball turn without a cause is listed with its tick.

## Requirements

### Reference and measurement
- R1 Reference v2 from recent real data.
  - A script under `tools/` (Python + numpy, run by the agent) downloads into `tools/.tracking-data/` (git-ignored, never committed) and derives aggregates only. Sources, most recent first:
    - **Tracking:**
      - DFL/IDSSE open data, 7 matches, 2022/23: 2 Bundesliga (Köln–Bayern, Bochum–Leverkusen) and 5 2. Bundesliga. CC-BY 4.0, 25 fps, ball with height. figshare DOI 10.6084/m9.figshare.28196177.
      - SkillCorner open data, commit `1ed50b775f`: 9 top-five-league matches from 2019/20 (City–Liverpool ×2, Clásico ×2, Juventus–Inter ×2, Dortmund–Bayern ×2, PSG–Marseille). MIT, broadcast tracking at 10 fps. Ball frames that are off camera are excluded.
      - Metrica sample games 1–3 (already used).
    - **Events:**
      - Impect open data, Bundesliga 2023/24, 306 matches. If the `LICENSE.pdf` terms forbid publishing derived figures in a commercial product, it is dropped and the doc says so.
      - StatsBomb open data, the most recent seasons per league: Bundesliga 2023/24, Ligue 1 2021/22 and 2022/23, La Liga 2020/21. Premier League and Serie A have no open season newer than 2015/16, so 2015/16 is used for those two and marked as such. Attribution to StatsBomb.
  - Every source is split by league and by team tier: top 6, middle and bottom 6 of that season's final table. Matches are split by tier pairing (strong–strong, strong–weak, weak–weak).
  - Tracking metrics (ball in play, open play):
    - share of time with nobody within 1.5 m and within 3 m of the ball;
    - players within 5 m and 10 m of the ball;
    - share of time the ball is above 1 m (DFL only, since it is the only source with height);
    - fast free-ball turns > 20° per 0.2 s, as the noise floor;
    - striker offside-position share of his team's possession;
    - pass flight time p50/p90;
    - distance from the receiver to the ball's arrival point at release (the pass-into-space share is the share ≥ 3 m);
    - loose-ball episodes per match (ball free ≥ 1 s after a duel or a touch by the opponent);
    - the movement metrics of `believable-match` R1 (direction agreement, heading reversals, speed bands, accelerations), re-measured on all tracking sources.
  - Event metrics per team-match: passes, long passes (> 30 m) share, high passes share, high crosses, low crosses and cut-backs, through balls, aerial duels, offsides.
  - Each band is the min–max across the splits, widened by 10% of the range, or by ±5% of the value when the range is 0. The doc records each source's URL, licence, commit or DOI, and the script.

  (acceptance: the script reproduces the doc numbers; every new `RealismReference` constant cites its doc section, and a test asserts the constants match the doc table; the doc shows the per-league and per-tier split for every event metric.)
- R2 Harness metrics, each printed with its R1 band by the `pitch` scenario:
  - all R1 tracking and event metrics computed from the engine stream (height from `BallZ`);
  - a cause trace for every fast free-ball turn > 20°: tick, cause (touch, bounce, deflection, post, none), and the nearest player. A turn with cause "none" is listed as a defect;
  - the `cross` label is assigned by geometry: origin in the wide channel (≤ 13.84 m from the touchline) of the final third, arrival inside the penalty area. It is split into high (peak ≥ 2 m) and low/cut-back;
  - an oscillation metric: per player, target reversals > 120° within 2 s, per player-minute.

  (acceptance: a unit test on a scripted stream for each metric; the harness prints the full table; the engine–real table of the Goal section is reproduced by the harness and by the script.)

### Engine (v13)
- R3 A free ball, with no magnetism.
  - After a kick, the ball moves only by its physics: speed decay, bounce and `BallZ`.
  - A player changes the ball only through a touch event within reach: ≤ 1.0 m for the feet, ≤ 2.5 m height for a header, and keeper reach from the save model. Nothing pulls, snaps or steers the ball toward a player.
  - First touch: the ball leaves the receiver's foot with a control error that scales with ball speed and inversely with `Technique` (tunable). A poor touch can leave the ball loose.
  - Dribbling keeps the ball 0.5–2 m ahead of the carrier through touches, not glued to him.

  (acceptance:
  - unit tests: a ball passing 1.5 m from a player without a touch keeps its line; a low-Technique receiver of a fast pass loses control in more seeds than a high-Technique one;
  - harness: 0 free-ball turns with cause "none" outside director cuts; the ball-free shares and loose-ball episodes inside the R1 bands.)
- R4 Passes into space, long balls, aerial duels.
  - A pass can target a point ahead of a running receiver (a lead pass) or behind the defensive line (a through ball). The receiver runs onto it, and an opponent can reach it first.
  - Long balls and switches are lofted (`BallZ`). Where a high ball drops, an aerial duel resolves it from `Heading`, `Strength` and `Jumping`/height (existing attributes), and the second ball is contested.

  (acceptance: unit tests on a lead pass, a through ball and an aerial duel; harness: pass-into-space share, long-pass share, high-pass share, aerial duels and pass flight time inside the R1 bands.)
- R5 Real crosses and box arrival.
  - From the wide channel of the final third, a cross is lofted (peak ≥ 2 m) or driven low/cut back, chosen by the expected-value choice of R7.
  - On a cross, the striker attacks the near post, the far-side wide player the far post, and one central midfielder arrives late at the penalty spot.
  - Headed attempts come from crosses.

  (acceptance: a unit test on a scripted wide carrier, with the three box runs; harness: high crosses and low crosses per team-match inside the R1 bands.)
- R6 The striker holds the line and times his runs.
  - In possession, the striker positions on the second-last defender's line (≤ 0.5 m beyond it) while the ball is not playable to him.
  - He starts a run in behind when a team-mate on the ball has a lane to the space behind.
  - An offside run he cannot correct is still possible and is called.

  (acceptance: unit tests: a striker beyond the line steps back onside within 2 s when no pass is on; a run starts when a lane opens; harness: striker offside-position share and offsides per team-match inside the R1 bands.)
- R7 Purposeful movement. This absorbs #143–#146.
  - **Expected-value action choice:** as in `believable-match` R3, with the same scene tests. It is extended to lead passes, through balls, long balls and crosses.
  - **Role jobs:** in and out of possession, as in `believable-match` R4.
  - **Curved runs:** around opponents within 2 m of the straight line.
  - **Staggered re-reads:** each player re-reads his target on his own schedule.
  - Work starts with a tick-by-tick trace of a drifting or oscillating player in the current engine. #144's approach failed 3 times, so the mechanism is chosen from that trace, not assumed.

  (acceptance: the `believable-match` R3 and R4 unit tests; harness: direction agreement, heading reversals, speed bands, accelerations and the oscillation metric inside the R1 bands; free ≤ 12 m central → shot ≥ 80%.)
- R8 Closure.
  - The R2 bands of the previous specs hold: goals, shots, on target, accuracy, corners, fouls, cards, km, shape.
  - Passes per match move inside the band (today they are HIGH at 1273).
  - The style fingerprints and fast-model tolerances hold.
  - Generation time is ≤ +25% of v12.
  - The engine moves to v13 once, and the golden master is re-pinned in its 4 places after the user approves the harness.
  - Online version checks refuse v12 clients.

  (acceptance: the harness pasted by the user and judged together; the determinism tests and full `dotnet test` green.)

### Client
- R9 A readable ball.
  - The renderer interpolates the ball between stream frames along the physics path, not straight-line snapping.
  - Size and shadow follow `BallZ`, so lofted balls and crosses read as high at 1x.
  - A dev overlay (behind `DevFlags.OnlineTestTools` or a new dev flag) logs every rendered ball jump > 2 m between rendered frames, with the match tick.
  - No per-frame allocations.

  (acceptance: presenter tests on the interpolation and height scale; client type-check with 0 errors; the overlay logs 0 jumps outside fades in the user's Play-mode run.)
- R10 Play-mode acceptance. This replaces #157. The user records a 1x match, and the checklist is:
  - the ball is visibly alone (long balls, loose balls, passes into space);
  - no mid-flight course change without a touch;
  - at least 3 visible high crosses;
  - the striker is not camped offside;
  - no player oscillates;
  - the dev overlay reports 0 rendered jumps.

  (acceptance: the user's recording and checklist, judged together.)

## Tech decisions
- **Stack/libs:** no new packages. Sim.Core stays netstandard2.1, deterministic, with integer math and a single seeded RNG. All tunables live in `BalanceConfig`. The reference script is Python + numpy, like `tools/movement_reference.py` (extended, or a sibling script).
- **Data:**
  - Raw data stays in `tools/.tracking-data/` and is never committed. Only aggregates go into `docs/research/football-reference.md` and `RealismReference`.
  - Attribution: DFL (CC-BY 4.0), SkillCorner (MIT), StatsBomb, Impect (per its terms), and Metrica.
- **Structure:**
  - Ball touch and control live in `MatchBall` siblings.
  - Lead pass, through ball and aerial duel live in `V11Brain` siblings.
  - Striker line logic lives in `V11Positioning`.
  - Files ≤ ~300 lines.
- **Commands:**
  - Build: `dotnet build --nologo -v q`. Test: `dotnet test --nologo -v q`. Fast tier: `--filter 'Category!=Slow&Category!=Integration'`.
  - Harness: `.\tools\balance.ps1 -Scenario pitch [-PitchMatches N] [-PitchDump file.html]`.
  - Client: the Roslyn type-check in CLAUDE.md.
  - After any `shared/` change the user runs `.\tools\build-simcore.ps1`.
- **Workflow:** `/ba:auto merge`. Each verified PR is squash-merged after green CI. Base ref is `FootballSimulator/main` (`origin/main` is stale).

## Constraints / non-functional
- Architecture §4.1: pure, deterministic, no I/O. The renderer never changes a result.
- Every build or test runs at BelowNormal CPU priority. Agent prompts carry the exact command: `cmd //c start //b //belownormal //wait ...` (double slashes), or the PowerShell `PriorityClass` line.
- Balance-sensitive requirements (R3–R8) close only after the user pastes the harness output and judges it. Game-level tests use ≥ 64 seeds. Random-world tests remove the dice (bigger samples), never widen bands. Fragile tests are flagged.
- Mobile/WebGL: no per-frame allocations, and generation time ≤ +25%.
- Localisation: every new string in en + it.
- Runs end before 24:00 local time.

## Open risks
- Open tracking data is thin and partly old: 19 matches, and only 9 from the top five leagues outside Germany, all from 2019/20 big matches. Bands are widened by rule. Weak-team tracking comes only from 2. Bundesliga and Metrica.
- Premier League and Serie A event data is 2015/16, the newest open season for those leagues.
- The Impect licence may forbid published derived figures. The fallback is StatsBomb Bundesliga 2023/24.
- Broadcast tracking (SkillCorner) loses off-camera players and ball. Ball-freedom metrics read only frames where the ball and the players near it are tracked.
- Freeing the ball changes possession, passes and every downstream band. Several tuning rounds are likely.
- First-touch error and aerial duels may cost generation time. The +25% budget is the guard.

## Tasks (filled by /ba:issues)
| # | Title | Depends on | Requirements | Issue |
|---|-------|------------|--------------|-------|
| 1 | docs+tool: reference v2 tracking metrics (DFL, SkillCorner, Metrica) by league and tier | - | R1 | #172 |
| 2 | docs+tool: reference v2 event metrics (Impect BL 23/24, StatsBomb recent) by league and tier | 1 | R1 | #173 |
| 3 | harness: ball freedom, loose balls, pass flight, passes into space, ball height | 1 | R2 | #174 |
| 4 | harness: cause trace for free-ball turns and cross label by geometry | 2 | R2 | #175 |
| 5 | harness: striker offside-position share and oscillation metric | 1 | R2 | #176 |
| 6 | engine: tick-by-tick trace of a drifting/oscillating player and documented root cause | 5 | R7 | #177 |
| 7 | engine: free ball with no magnetism, touches only within reach | 4 | R3 | #178 |
| 8 | engine: imperfect first touch and dribbling by touches | 3, 7 | R3 | #179 |
| 9 | engine: lead passes and through balls | 8 | R4 | #180 |
| 10 | engine: lofted long balls, aerial duels and second balls | 8 | R4 | #181 |
| 11 | engine: expected-value action choice incl. lead passes, through balls, long balls, crosses | 9, 10 | R7 | #182 |
| 12 | engine: real crosses and box arrival | 4, 11 | R5 | #183 |
| 13 | engine: striker holds the line and times his runs | 5, 9 | R6 | #184 |
| 14 | engine: role jobs in possession | 6, 13 | R7 | #185 |
| 15 | engine: role jobs out of possession, curved runs, staggered re-reads | 6 | R7 | #186 |
| 16 | client: ball interpolation along the physics path and height from BallZ | - | R9 | #187 |
| 17 | client: dev overlay for rendered ball jumps | 16 | R9 | #188 |
| 18 | balance: v13 into the reference v2 bands with current bands held | 7, 8, 9, 10, 11, 12, 13, 14, 15 | R8 | #189 |
| 19 | engine v13: fast model recalibration, timing, golden master, online version | 18 | R8 | #190 |
| 20 | Play mode: record a 1x match and check the free-ball checklist (needs-human) | 17, 19 | R10 | #191 |
