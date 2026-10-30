# Oscillation and the offside striker: what the tick trace shows

Issue #177, free-ball spec [R7](../specs/free-ball-real-movement.md#engine-v13) (and R6). Engine v12,
no behaviour change. The R7 and R6 movement tasks read this file before they choose a mechanism.

## The instrument

- `MatchEngine.MovementSink` (`IMovementSink`, `Sim.Core/Match/Movement/MovementSink.cs`). It is
  null in production. When attached, it records one `MovementRecord` per tick for each man it watches:
  - the job the base brain asked for, and the job that moved him
    (`Spot`, `Support`, `Chase`, `Ball`, `Run`, `Offer`, `Cover`, ...);
  - the inputs of his target: team phase, possession, expansion, ball, the offside line, his phase
    spot before and after the onside hold;
  - the target handed to the legs, the velocity the legs wanted, the velocity after separation, and
    the velocity and position at the end of the tick.

  It only reads. `MovementSinkTests` pins the match hash with the sink attached and detached, even
  with all 22 men traced. The harness re-plays every traced match and checks the same thing.
- `.\tools\balance.ps1 -Scenario pitch -PitchMatches 100 -PitchTrace .\_to_delete\trace.csv -PitchTraceMatches 30`.
  The traced men are picked from data, per match:
  - the 3 top oscillators by the per-player metric of `OscillationAnalyzer` (≥ 10 min);
  - every `Striker`.

  For an oscillator, each reversal (two 1 s steps > 0.5 m, > 120° apart, the metric's own
  definition) gets the first cause that applies in its 2 s window: a job switch, a possession flip,
  the target itself reversing, a steady target (his legs), or a target moving without reversing.
  For a striker, each open-play possession tick in an offside position (§10.3: beyond the line + 0.3 m)
  is split by what sent him there. The CSV holds every tick of every traced man of the first match.

Baseline of this run (100 matches, seed 20260803):
- reversals: 1.345 per player-minute (band 0.870-1.529);
- per player: p50 1.244, p90 2.097, max 2.697, 34.5% of men above the band;
- striker offside position: 22.2% of possession (band 6.84-9.94).

## 1. Oscillation: the job is re-chosen from scratch and the target teleports

Top 3 oscillators of 30 matches: 15,406 reversals in 7,167 open-play minutes (2.15/min).

| cause in the 2 s window | share |
|---|---|
| a job switch | **72.3%** (Spot→Support 19.2, Support→Spot 11.2, Support→Chase 6.2, Ball→Spot 5.3, Ball→Chase 5.2, Run→Support 3.8, Spot↔Cover 4.5, Spot↔Chase 4.8, ...) |
| the target moving, same job | 17.8% (Support 13.3, Spot 4.6) |
| the target reversing, same job | 7.9% (Support 7.2) |
| target steady: legs, inertia, separation | **1.0%** |
| possession flip, same job | 0.6% |

The control: a job switch is in 72.3% of the reversing windows, but only in 38.1% of the 332,069
moving windows that do not reverse. The supporting run alone is in at least 60% of all
reversals: the Spot↔Support switches, the Support target moving or reversing, and Support→Chase/Run.

From the CSV of match 0 (4 traced men: 3 oscillators and 2 strikers, one of them both):

| switch | median jump of the target | back to the old job within 2 s |
|---|---|---|
| Spot→Support | 19.9 m | 28% |
| Support→Spot | 19.5 m | 27% |
| Spot→Chase | 13.7 m | **89%** |
| Spot→Cover | 12.7 m | **82%** |

- Each traced man switches job 14-19 times a minute. The median spell is 1.5-3.0 s for Support,
  1.7-2.3 s for Spot, and **0.3 s for Chase**.
- With the job unchanged, a Support target still jumps more than 3 m 340-800 times a match
  (median jump 9-23 m).

The mechanism is in `BaseBrain.Team.cs`.
- `UpdateSupport` empties `_supporters` on every 0.5 s rethink. It then picks the men with the best
  `_forward*4 - distance to the support spot` again, with no preference for the men already
  supporting, and leaves out whoever is the chaser at that moment.
- `_chaser` is `NearestToBall`, re-read every tick.
- `FindSupportSpot` is an argmax over a 5×3 grid, re-run every 1 s. Rows are 26 m apart and
  columns about 8 m apart, and every man on the run shares the one spot.

So the job and the target are re-read by a winner-takes-all rule with no hysteresis. Each switch
moves the target 13-20 m, often behind the man. Only 22-27% of the Spot↔Support switches came with
the same ball owner for the second before. The rest followed a pass or a turnover, so with about 970
passes a match every pass re-deals the attackers' jobs.

Excerpt: home winger, match 0, ticks 5690-5710. His phase spot is (87.7, 13.0); x and y are in dm,
v in cm/s.

```
tick   job      tX   tY  sprint   vx    vy    x    y
5695   Spot     878  130   1     268  -412  851  291   sprinting to his wide spot
5696   Chase    926  362   1     281  -368  854  287   nearest to the ball for two ticks
5698   Spot     879  130   1     287  -368  860  280
5700   Support  838  325   0      68  -100  863  275   0.5 s rethink: the target jumps 20 m behind him
5703   Support  838  324   0      -6    18  864  275   ...and he reverses
5710   Support  782  299   0     -93    75  860  281   1 s re-grid: the support spot jumps again
```

There is also a second factor, which makes the reversal sharp. At tick 5700 the speed falls from
4.9 m/s to 1.2 m/s in one tick, while the acceleration limit is 0.5 m/s per tick. `PlayerSteering.Desired`
eases the pace inside `PlayerApproachDm` (15 m), and `Accelerate` caps the actual velocity at that
eased pace (and at the cruise pace when the sprint stops). So a man whose new target lands within 15 m brakes instantly and pivots in place instead
of turning on a curve.
- Changes in velocity above 0.75 m/s in one tick: 1.9% of the oscillators' ticks, median 3.45 m/s.
- Such a tick is in 46.7% of the reversing windows and in 32.5% of all 2 s windows.

Reaction delay (#144) is not the mechanism. Only 1.0% of the reversals come with a steady target:
the target moves and the man follows it. Staggered reaction delays would stagger the same jumps,
which is why #144 could not move the metric.

## 2. The striker camping offside: the onside hold binds a spot that is then replaced

Strikers of 30 matches: 23.5% of open-play possession ticks are in an offside position. The median
spell is 3.1 s.

| what sent him there | share of the offside ticks |
|---|---|
| target beyond the line, **Support** | **65.9%** |
| target beyond the line, Run (in behind) | 13.1% |
| target onside, Support: not coming back 6.5 / walking back (< 2 m/s) 5.1 / running back 2.8 | 14.4% |
| target beyond the line, Ball | 3.3% |
| other (Chase, Offer, Spot) | 3.3% |

For every offside tick where a support, run, offer or overlap target put him beyond the line, his own
phase spot was onside **100%** of the time.

The mechanism is in `V11Brain.Move`. `V11Positioning.Onside` holds only the phase spot, 1.6 m
inside the line. The support target that replaces it is never held onside:
- it is the shared grid spot of `FindSupportSpot`, and its scoring (pass safe, shooting range, ideal
  distance) knows nothing of offside;
- it is leashed 20 m (`V11SupportLeashDm`) from the onside spot.

The striker has the highest `_forward` score, so he is almost always one of the supporters. Two
smaller effects add to it:
- the run in behind keeps its target beyond the line for the whole `V11RunMs` (3 s), whether or not
  a pass comes;
- once his target is onside again, the approach easing makes him walk back. A man 3 m offside is
  about 4.6 m from a spot 1.6 m inside the line, so he returns at about 30% of his cruise.

Excerpt: away striker (attacking toward x = 0), match 0, ticks 3278-3328. The line is at 46-47 m and
his onside spot at about 48-49 m.

```
tick   job      lineX spotX   tX   tY  sprint   vx    vy    x    y  offside
3278   Support   458   474   447  340    0     -62    -6  480  345   0   support target 1 m beyond the line
3298   Support   468   484   447  340    0     -43    -6  470  345   0   the line steps up, he walks on
3303   Support   472   488   447  340    0     -37    -6  468  345   1   offside, still walking to it
3318   Support   475   491   447  340    0     -25    -6  464  344   1   2 s offside at 0.25 m/s
3320   Support   474   490   447   80    1     -25   -56  463  343   1   1 s re-grid: target jumps 26 m across
3328   Support   463   479   447   80    1     -25  -456  461  321   0   the line drops back to him
```

## What the movement tasks must change (yardstick: the harness oscillation metric)

1. **Commit to a job.** Two parts:
   - A man keeps a support, chase or cover job for a minimum spell. A challenger must beat him by a
     margin to take it.
   - The chaser and the supporters are re-chosen with the incumbents preferred, not re-dealt from
     scratch on every rethink and every pass.

   This is the role-jobs work of R7 (#185 in possession, #186 out of possession). It goes after the
   72% of reversals that have a job switch.
2. **A target that moves continuously.** The support spot must not be a grid argmax shared by every
   supporter. Each man's job target should follow from his own spot and the ball, and move
   continuously (re-read on his own schedule, as R7's staggered re-reads ask). This goes after the 20%
   of reversals where the Support target moves or reverses.
3. **Brake within a limit.** Only the wanted velocity should be eased, with the actual velocity braking
   at a bounded deceleration. On its own this does not remove a reversal, but it turns the pivot into
   a turn.
4. **Hold every target onside (R6, #184).** Two parts:
   - Apply the onside hold to the final target of a supporting, offering or overlapping man, not to
     the spot it replaces.
   - End the run in behind as soon as no pass is on, and run back to the line rather than walk.

   Together these go after the 66% + 13% + 14% of the striker's offside ticks.

Each change is judged by the per-player oscillation line (p50/p90/share above 1.529) and the
striker-offside line of `-Scenario pitch`, re-run with `-PitchTrace` to confirm that the cause it
targets has shrunk.
