# Where the central chances come from

Issue #210, free-ball spec [R2, R7, R8](../specs/free-ball-real-movement.md#engine-v13). #182 prices
shot, pass and carry honestly with no multiplier. With that price alone the engine scored 3.42 goals a match
(band 2.4-3.0). This file records where the chances come from, what was tried, and what shipped.

## The instrument

`CentralZoneAnalyzer` (`Sim.Core/Match/Analysis`, the #206 spells) now labels each central-zone and
penalty-area spell by how the ball reached it. The `pitch` harness prints the labels under the
central-zone block.

- **Carry:** the spell began outside the zone (or the box) and he ran it in.
- **Through ball, cross, cut-back, pass:** a team-mate's pass, read by its geometry:
  - cut-back: struck from within the box's depth and played back at least 2 m;
  - cross: from the wide channel of the final third into the box (doc §11.3);
  - through ball: from behind the second-last opponent to a man not beyond him, taken beyond him
    (the `BallFreedomAnalyzer` rule);
  - pass: any other pass.
- **Second ball:** the other side kicked or touched it last (a clearance, a block, an interception).
- **Rebound:** his own side's shot, touched since only by the keeper or a blocker.
- **Won back:** taken off a man of the other side who held it.
- **Set piece:** the first kick after a restart.

For each label the harness prints, per team-match:
- the zone and box arrivals, and the share pressed on arrival (a defender within 3 m);
- the shots, goals and location xG of the spells it labels. Location xG uses the engine's
  `ExpectedGoals` with no pressure and neutral finishing.

Under the table it also prints:
- shots, goals and xG from open-play spells that never touched the box;
- goals off a set-piece taker's own shot;
- how the zone carriers got the ball, and how far out their carry began.

Run: `.\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -Seed <s>`, seeds 20260803, 1, 2, 3.
Every figure below is the mean of the four seeds.

## Before (main 471a7b2)

| source | zone | pressed % | box | shots | goals | xG |
|---|---|---|---|---|---|---|
| Carry | 14.40 | 64 | 21.54 | 7.13 | 0.94 | 1.45 |
| Through ball | 1.27 | 46 | 3.57 | 0.33 | 0.03 | 0.09 |
| Cross | 0.73 | 54 | 1.53 | 0.17 | 0.02 | 0.04 |
| Cut-back | 1.05 | 24 | 3.14 | 0.21 | 0.03 | 0.06 |
| Pass | 2.14 | 36 | 5.18 | 0.55 | 0.06 | 0.15 |
| Second ball | 1.77 | 77 | 3.16 | 0.32 | 0.03 | 0.08 |
| Set piece | 2.48 | 40 | 2.88 | 0.37 | 0.09 | 0.07 |
| Won back, rebound, other | 1.25 | - | 2.32 | 0.33 | 0.03 | 0.08 |
| **total** | **25.10** | 58 | **43.31** | 9.39 | 1.23 | 2.02 |
| outside the box | - | - | - | 4.71 | 0.10 | 0.17 |

The same table under #182's honest price (WIP 5e99e69 on a scratch branch):
- totals: zone 18.56, box 36.93, goals 1.35 from labelled spells;
- outside the box: 0.35 goals from 4.86 shots (xG 0.29).

**The largest source is the carry.** It gives 57% of zone spells and 76% of the goals from labelled
spells. A per-frame probe of the carries showed (seed 20260803, 200 matches; the arrival split on all four seeds):
- On arrival in the zone, 71% of carriers had no outfield defender goal-side of them within 10 m.
  Those carries gave 90% of the carry shots and 93% of the carry goals.
- 68% of the carries began with no defender inside the zone.
- Arrival speed was over 5 m/s in 62% of them.

The back line's floor is the edge of the box (`BackLineMinDepthDm` 165), and below a ball about 20 m
out the line stands level with it. So a central carrier who reaches the zone is level with the whole
line by construction.

**Where the honest price's extra goals come from.** Per team-match, against the current price:
- outside the box: +0.25. Same shot count; clear sights convert 7% against 2%;
- receptions in the zone (pass, through ball, cut-back, cross): +0.23. First-time shots;
- carries: -0.16.

## What was tried

Main = current price, shadow = #182's honest price. Goals per match, mean of 4 seeds x 200 matches.

| change | main | shadow | zone (main) | box (main) |
|---|---|---|---|---|
| none | 2.68 | 3.42 | 25.1 | 43.3 |
| line drops into the box to stay 3 m goal-side of the ball's depth, floor 6 m | 1.97 | 2.79 | 22.8 | 40.4 |
| same, gap 0, floor 11 m (no zone guard change) | 2.33 | 3.13 | 24.4 | 42.4 |
| same by the ball's distance to the goal centre (corners untouched) + zone guard 26 m | 2.29 | 3.02 | 23.4 | 40.8 |
| line drop by depth, gap 0, floor 11 m + zone guard 26 m | 2.22 | 2.87 | 23.6 | 41.1 |
| line drop by depth, gap 0, floor 15 m + zone guard 26 m | 2.39 | 3.08 | 24.0 | 41.6 |
| zone guard 23 / 26 m | 2.64 / 2.68 | 3.38 / 3.22 | 24.4 / 24.2 | 42.2 / 42.0 |
| zone guard 27 m | 2.61 | 3.38 | 24.1 | 41.7 |
| zone guard 32 m / 38 m | 2.55 / 2.61 | 3.22 / 3.14 | 23.6 / 23.2 | 40.9 / 40.2 |
| a covering man in the carrier's lane (30 m) | 2.58 | 3.45 | 25.2 | 43.3 |
| the goal-side presser engages at the zone (no stand-off) | 4.50 | - | 25.6 | 36.2 |
| the blocked carrier's speed cap also while he chases his own touch | 2.53 | - | 24.7 | 45.4 |

**Noise.** A change anywhere moves every later tick, so two settings 1 m apart can read 0.15 goals
apart. Zone guard 23-38 m gives the honest price 3.14-3.38, and it gives the current price 2.55-2.68.
The 27 m setting was therefore run again on 4 seeds x 800 matches:

| 800 matches a seed | main goals | main zone | main box | shadow goals | shadow shots |
|---|---|---|---|---|---|
| zone guard 20 m (before) | 2.72 | 25.09 | 43.15 | 3.44 | 29.7 |
| zone guard 27 m | 2.55 | 23.96 | 41.48 | 3.31 | 28.3 |

On the large sample the zone guard costs the current price as much as the honest one. The "no cost to
the current price" reading at 26 m on 200 matches was noise.

Notes on the rejected changes:
- **Engaging presser:** the presser is beaten in the duel and the carrier is through.
- **Line drop:**
  - It cuts carry chances, the largest source. But the current price takes 76% of its goals
    from carries, so it loses about twice what the honest price does.
  - Measured by the ball's depth, it also all but removed set-piece goals (0.09 to 0.01 a
    team-match).
  - A tested version also put the #206 presser outside his 3 m at 15 m (the
    `ACarrierNearingTheCentralZone_IsClosedTighterThanOneFarOut` test, 33 dm).
- **Lane guard and capped chase:** they did not move the zone.

## What shipped (with #182)

The owner folded #210 into #182: the instrument above, the honest price and the volume cut land
in one change, so main never ships the cut with the old price, nor the honest price without the
cut. The line drop is not in it: with the carry stopped further out it cost the open goals
(`OpenGoals_AreRareChances_NotEveryTouchNearTheBox`) and no longer paid for itself.

The cut, all in `MatchBalance` (each value's doc comment carries its measurement):
- `V11CarrierBlockedDm` 55 to 105: a defender goal-side within 10.5 m stops the carry (the largest
  source). Goals 3.13 at 55, 2.74 at 105.
- `V11PressStandOffPercent` 280 to 340: the presser jockeys instead of diving in and being beaten.
  Goals 3.06 at 280; fouls 27.1 to 23.9.
- `V11ZoneGuardRangeDm` 200 to 260: a pass met in front of the box is closed too. Goals 2.92 at 200.

And the decision side of #182, so the men who do reach the box shoot (believable-match R3):
- `V11NearGoalChooseRangeDm` 210: within 21 m a pressed or blocked man takes his dearest option
  instead of waiting for the tempo's release. Without it: free within 12 m shot 60%, pass-back 22%.
- `V11FreeShotRangeDm` 120 / `V11FreeShotPressurePermille` 600: a free man within 12 m is not held
  to the threat of the spot. Without it: free within 12 m shot 72%.
- `V11TempoHoldSpreadPercent` 30 to 60: keeps fast above normal above slow in passes.

Shipped readings (4 seeds x 200 matches):

| | 20260803 | 1 | 2 | 3 | mean | band |
|---|---|---|---|---|---|---|
| goals | 2.58 | 2.70 | 2.92 | 2.75 | 2.74 | 2.4-3.0 |
| shots | 21.8 | 22.8 | 25.6 | 23.7 | 23.5 | 20-30 |
| free within 12 m: shot % | 84.3 | 82.9 | 83.8 | 83.2 | 83.6 | >= 80 |
| free within 16 m: pass-back % | 7.1 | 7.8 | 8.0 | 7.3 | 7.6 | <= 15 |
| central-zone spells | 12.8 | 13.2 | 14.5 | 13.5 | 13.5 | 6.2-25.7 |
| penalty-area spells | 34.4 | 35.1 | 37.6 | 36.2 | 35.8 | 9.7-41.4 |

The sources under it (mean of the four seeds, per team-match):

| source | zone | pressed % | box | shots | goals | xG |
|---|---|---|---|---|---|---|
| Carry | 4.33 | 63 | 15.32 | 3.20 | 0.40 | 0.39 |
| Through ball | 1.35 | 53 | 4.08 | 0.73 | 0.11 | 0.16 |
| Cross | 0.86 | 54 | 1.60 | 0.45 | 0.06 | 0.10 |
| Cut-back | 0.69 | 32 | 2.06 | 0.51 | 0.07 | 0.08 |
| Pass | 2.06 | 47 | 5.33 | 1.23 | 0.15 | 0.23 |
| Second ball | 1.27 | 69 | 3.13 | 0.37 | 0.06 | 0.08 |
| Set piece | 2.32 | 40 | 2.69 | 1.00 | 0.19 | 0.16 |
| Won back, rebound, other | 0.66 | - | 1.61 | 0.33 | 0.06 | 0.06 |
| **total** | **13.52** | | **35.82** | 7.82 | 1.09 | 1.26 |
| outside the box | - | - | - | 3.82 | 0.27 | 0.24 |

The carry gives a third of the zone spells it gave on main (4.3 against 14.4) and a third of the
labelled goals (0.40 of 1.09), against 76% before.

Still open: passes per match read 1155 against the harness band's 1150 (main 1116); the fast tier's
own pass and tempo bands hold.
