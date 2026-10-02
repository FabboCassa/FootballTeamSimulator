# Football reference data

Reference for `docs/specs/real-match-and-playing-styles.md` (R1). It backs the R2 neutral realism
bands held in `shared/Sim.Core/Match/Analysis/RealismReference.cs`, the R3 tier gap, the R9 passing
chains, the R17 formation catalogue and the R18/R19 style catalogue.

Rules:
- Every figure has a source `[n]`, a link to the URL in [Sources](#sources).
- A figure not published for a league is **n/a**. Nothing is estimated. A figure marked
  **derived** is computed from sourced figures, and the arithmetic is given.
- Primary season: 2024-25. R1 asks for the latest complete season. 2025-26 is complete too (its
  last matches in the five top leagues were played on 16-24 May 2026 [1]), but most providers here
  were reached as complete-season figures for 2024-25 only: the FBref/Opta squad tables [4], Opta
  Analyst's league and team figures (for 2025-26 only part-season articles were reached: [7] after
  50 matches, [9] after 70, [8] after 210), CIES ball in play [21] and Bundesliga distance [23][24].
  The spec's R2 starting values are also Opta Analyst 2024-25 figures. One season keeps the doc's
  figures comparable with each other. Where a complete 2025-26 figure was reached it is shown
  alongside: football-data [1] in §2.1 and Understat PPDA [2] in §2.4. §6 checks every band set
  from [1] against both seasons.
- A figure from an article that does not cover a full season carries its cut-off (matches,
  matchday or date) as the source states it; where the source states none, "as of" the article
  date.
- "my computation" means a script run on the raw public download of that source.

## 1. Definitions

Providers define the same word differently. A number is only comparable on its own scale.

### 1.1 Sequence vs possession
- **Opta sequence** [11]: a passage of play owned by one team. It ends with a defensive action, a
  stoppage or a shot. Opta Analyst, FBref (to 2025) and the Premier League use it.
- **StatsBomb possession** [19]: longer than an Opta sequence, because it survives brief opponent
  touches. Passes per possession, 10+ possessions and direct speed from StatsBomb are **not on the
  Opta scale**. They are used here only as ratios against the same dataset's league average.
- **Harness rule:** sequences follow Opta's definition, made exact by the conventions of §1.5.

### 1.2 Direct speed, high turnover, start distance (Opta [11])
- **Direct speed**: upfield progress divided by sequence time. Opta defines it per sequence
  [11], so its team figures are means of per-sequence ratios. The PL 2024-25 ratio of averages, 12.6 m over 10.4 s = 1.21 m/s
  (**derived** 12.6 ÷ 10.4) [7], is therefore not Opta's direct speed.
- **High turnover**: a possession that starts in open play within 40 m of the opponent's goal.
- **Start distance**: mean distance from a team's own goal at which its open-play sequences start.

### 1.3 PPDA scales
- **Opta PPDA** [11]: opposition passes allowed outside the pressing team's defensive third,
  divided by the pressing team's defensive actions in that area.
- **Understat PPDA** [2]: uses the opponent's 60% of the pitch. It reads **lower** than Opta PPDA
  for the same team. Same-period pair, 2024-25 full season: Bournemouth 8.53 Understat [2] vs 9.9
  Opta [15], 14% lower (**derived** 1 − 8.53 ÷ 9.9 = 0.138). Nottingham Forest 14.22 Understat (full
  season) [2] vs "16" Opta (to 10 Feb 2025) [16] points the same way, but its periods differ, so it
  gives the direction only, not a size.
- **Harness rule:** PPDA uses the **Understat zone** (`RealismReference.PpdaZoneFraction` = 0.60).
  The R2 band (9-14) and the R19 PPDA anchors (Gegenpress ≤ 9, Park the bus ≥ 16) are read on this
  scale: no 2024-25 PL side was below 9.9 on the Opta scale [15]. The exact pass set, action set
  and aggregation are in §1.5.

### 1.4 Other conventions
- Shots: [1] defines its columns only as "HS = Home Team Shots" and "HST = Home Team Shots on
  Target"; it does not say whether blocked shots are counted. Its 2024-25 totals sit close to
  FBref's, which include blocked shots: Serie A 24.06 [1] vs 23.97 with 6.45 blocked [4], LaLiga
  23.69 [1] vs 23.44 with 5.92 blocked [4]; without blocked shots FBref's totals would be 17.52 in
  both leagues (**derived** 23.97 − 6.45 and 23.44 − 5.92). So [1]'s shots are read as including
  blocked shots; this is a derived reading, not stated by [1]. On target excludes blocked shots by
  definition (§1.5).
- Crosses: FBref/Opta counts [4] may include crossed set pieces. StatsBomb's cross flag is
  narrower and gives 11.9-12.7 per team (2015-16) [19].
- Long ball: FBref > 30 yd [4]; StatsBomb ≥ 35 yd (my cut) [19].

### 1.5 Harness definitions

What the harness computes for each R2 band (§5). The sources define a sequence, direct speed and
PPDA only in the words of §1.1-§1.3. Every detail marked **(convention)** is a harness convention:
a choice no source gives, fixed here so that #71-#73 compute each band the same way from an event
stream. `RealismReference` carries the same text.

Counting basis: "per match" counts both teams, "per team" is the per-match count ÷ 2. A ratio is
pooled, total numerator ÷ total denominator over every match and both teams, unless the row says
"mean over sequences".

Building blocks:
- **Touch**: any contact of a player with the ball while it is in play.
- **Attempted pass** (convention): every deliberate kick, header or throw by a player that is not
  a shot: ground and long passes, through balls, crosses, clearances, and every restart delivery
  (kick-off, goal kick, throw-in, free kick, corner) that is not a shot. A keeper's save, a block
  and a deflection are not passes. **Completed**: the next touch is by a teammate, with no stoppage
  before it (a pass that goes out or is caught offside is incomplete).
- **Sequence** (Opta [11]; boundaries by convention): the run of touches by one team. It starts
  with that team's first touch after the ball came from the opponent, after a shot, or at a
  restart. It ends at the first of: any touch by an opponent (a deflection or a keeper save
  included; if the ball comes back, a new sequence starts), a shot, or a stoppage (ball out of
  play, foul, offside, goal, end of a half).
- **Open-play sequence** (convention): a sequence whose first touch is in play (not a restart), or
  is a throw-in or a goal kick. A sequence that starts with a kick-off, a corner, a free kick of
  any kind, a penalty or any other restart is a set-piece sequence and is left out of every
  sequence metric and of PPDA.

R2 metrics:
- **Goals per match**: every goal on the scoreboard, penalties and own goals included, as [1]
  counts them.
- **Shots per match** (convention): every attempt to score, blocked shots and penalties
  included. Own goals are not shots.
- **On target (excl. blocked), % of shots**: shots that end as a goal or a keeper save ÷ all shots.
  A shot blocked by an outfield player is never on target.
- **Blocked, % of shots**: shots stopped by an outfield player of the defending team (not the
  keeper) ÷ all shots.
- **Inside the box, % of shots**: shots struck from inside the opponent's penalty area ÷ all
  shots. A shot struck on the area's line is inside (convention).
- **Headed shots per match**: shots struck with the head.
- **Keeper save rate**: keeper saves ÷ (keeper saves + goals from shots); penalties included, own
  goals excluded. This is StatsBomb's measure in §2.2 [19]. **Inside / outside the box**: the
  same ratio, split by where the shot was struck (as in "inside the box").
- **Passes attempted per team**: all attempted passes ÷ (2 × matches).
- **Pass accuracy**: completed passes ÷ attempted passes.
- **Passes per open-play sequence**: attempted passes inside open-play sequences ÷ number of
  open-play sequences, sequences with no pass included; this equals the mean over sequences. The
  throw-in or goal kick that starts a sequence is its first attempted pass, and the pass that ends
  a sequence (intercepted or out) counts; both follow from the definitions above.
- **10+ pass open-play sequences per team**: open-play sequences with at least 10 attempted passes
  (counted as in the row above) ÷ (2 × matches).
- **Direct speed (open play)**: mean over open-play sequences, each weighted 1, of progress ÷
  duration (Opta's form, §1.2). Convention: progress is the signed distance along the pitch length
  toward the opponent's goal from the ball's position at the sequence's first touch to its position
  at the sequence's last touch, and may be negative; duration is the match time between those two
  touches; sequences shorter than 1 s (single-touch sequences included) are left out, because the
  ratio is undefined at 0 s and unbounded near it.
- **PPDA**, for each pressing team (convention, one pass set):
  - zone: the 60% of the pitch length nearest the opponent's own goal (`PpdaZoneFraction`);
  - numerator: the opponent's attempted passes started inside the zone that belong to an opponent
    open-play sequence, the throw-in or goal kick that starts one included. Passes in set-piece
    sequences are not counted. This is the pass set of "passes per open-play sequence", restricted
    to the zone;
  - denominator: the pressing team's tackles (won or lost), interceptions and fouls committed,
    located inside the zone, made while the opponent owns an open-play sequence, the action that
    ends that sequence included;
  - aggregation: Σ numerator ÷ Σ denominator over every match and both pressing teams, not the
    mean of per-match or per-team ratios. The league figures of §2.4 are built the same way [2].
- **Crosses per team** (convention): attempted passes started in the last third of the pitch
  length from outside the width of the penalty area and aimed at a point inside the opponent's
  penalty area, completed or not, ÷ (2 × matches). Corner kicks and throw-ins are not crosses
  (corners have their own band); free-kick deliveries that meet the rule are.
- **Corners per match**: corner kicks taken.
- **Fouls per match**: fouls the referee whistles, those giving a penalty included (convention).
  Offsides are not fouls.
- **Ball in play**: minutes the ball is in play, added time included, mean over matches.
- **Distance per outfield player** (convention): total distance covered by a team's outfield
  players in a match, substitutes included, keepers excluded, ÷ 10, then the mean over
  team-matches. This mirrors the derivation of the 10.0 km reference (99.9 km per team-match
  ÷ 10) [27].

## 2. League averages, 2024-25, per match

Both teams unless stated "per team".

### 2.1 League core events

From every match row of each season file [1] (my computation). Save rate is **derived** as
1 − goals ÷ SoT. Goals include penalties and own goals, while §1.5 excludes own goals, so this
column is not on the §1.5 save-rate scale and sets no band limit (§6).

| League | Matches | Goals | Shots | SoT | SoT % of shots | Fouls | Corners | Save rate (derived) |
|---|---|---|---|---|---|---|---|---|
| Serie A | 380 | 2.56 | 24.06 | 8.07 | 33.6 | 25.00 | 9.31 | 68.3% |
| Premier League | 380 | 2.93 | 25.92 | 9.10 | 35.1 | 22.07 | 10.30 | 67.8% |
| LaLiga | 380 | 2.62 | 23.69 | 8.14 | 34.3 | 24.73 | 9.52 | 67.8% |
| Bundesliga | 306 | 3.13 | 25.80 | 9.24 | 35.8 | 21.42 | 9.64 | 66.0% |
| Ligue 1 | 306 | 2.98 | 24.69 | 9.31 | 37.7 | 24.02 | 9.36 | 68.0% |
| Eredivisie | 306 | 2.99 | 26.25 | 9.18 | 35.0 | 20.50 | 9.93 | 67.5% |
| Primeira Liga | 306 | 2.57 | 23.63 | 8.17 | 34.6 | 28.05 | 9.52 | 68.6% |
| Serie B | 380 | 2.46 | 25.68 | 8.17 | 31.8 | 29.68 | 9.02 | 69.8% |
| Championship | 552 | 2.45 | 23.65 | 7.72 | 32.6 | 22.80 | 10.19 | 68.2% |
| 2. Bundesliga | 306 | 3.02 | 26.89 | 9.35 | 34.8 | 23.70 | 10.37 | 67.7% |

Source for every cell: [1]. Bundesliga: 306 matches were played, and goals average over all 306.
The Union Berlin v Bochum row (14 Dec 2024) has goals but no match statistics, so shots, SoT,
fouls, corners and the derived save rate average over the other 305 matches.

Cross-checks:
- Understat 2024-25 [2], my computation. Goals per match: PL 2.93, LaLiga 2.62, Bundesliga 3.13,
  Serie A 2.56, Ligue 1 2.98. Shots per match: PL 25.91, LaLiga 23.76, Bundesliga 25.88,
  Serie A 24.22, Ligue 1 24.92.
- 2025-26, complete season [1] (my computation; every match row has statistics). §6 checks the
  bands set from [1] against these figures too:

| League | Matches | Goals | Shots | SoT % | Fouls | Corners |
|---|---|---|---|---|---|---|
| Serie A | 380 | 2.43 | 24.66 | 32.7 | 25.35 | 8.82 |
| Premier League | 380 | 2.75 | 25.00 | 33.5 | 21.64 | 10.00 |
| LaLiga | 380 | 2.69 | 24.98 | 34.7 | 25.16 | 9.67 |
| Bundesliga | 306 | 3.24 | 26.51 | 36.0 | 20.85 | 9.75 |
| Ligue 1 | 306 | 2.82 | 24.85 | 34.8 | 24.10 | 9.59 |
| Eredivisie | 306 | 3.18 | 28.23 | 36.8 | 21.75 | 10.45 |
| Primeira Liga | 306 | 2.68 | 24.17 | 34.1 | 27.51 | 9.43 |
| Serie B | 380 | 2.56 | 26.17 | 32.2 | 29.87 | 9.54 |
| Championship | 552 | 2.61 | 25.24 | 32.3 | 21.48 | 10.35 |
| 2. Bundesliga | 306 | 2.93 | 26.69 | 33.9 | 24.58 | 10.04 |

### 2.2 Shot mix and keeper

2024-25 per league unless stated (n/a = not published in any source reached):

| League | Blocked % of shots | Inside the box % of shots | Headed shots / match | Save rate inside box | Save rate outside box |
|---|---|---|---|---|---|
| Serie A | 26.9 (6.45 of 23.97) [4] | n/a | 4.23 (2024-25 as of 18 Oct 2024) [5] | n/a | n/a |
| Premier League | n/a | 68.3 (**derived** 100 − 31.7, to 31 Mar 2025) [10] | 4.1 (as of 18 Oct 2024; [5] does not name the season) [5] | n/a | n/a |
| LaLiga | 25.3 (5.92 of 23.44) [4] | n/a | 4.03 (2023-24) [5] | n/a | n/a |
| Bundesliga | n/a | n/a | 4.19 (2024-25 as of 18 Oct 2024, "on track to average over 4.0") [5] | n/a | n/a |
| Ligue 1 | n/a | n/a | ≤ 3.68 in every season after 2016-17, as of 18 Oct 2024 [5] | n/a | n/a |
| Eredivisie | n/a | n/a | n/a | n/a | n/a |
| Primeira Liga | n/a | n/a | n/a | n/a | n/a |
| Serie B | n/a | n/a | n/a | n/a | n/a |
| Championship | n/a | n/a | n/a | n/a | n/a |
| 2. Bundesliga | n/a | n/a | n/a | n/a | n/a |

Top-five aggregates:
- Shots from inside the box, top-five mean: 51.4% in 2006-07, 65.4% in 2023-24, 66.4% in 2024-25
  as of 18 Oct 2024 ("just a few matchdays in") [5]. PL 2024-25 to 31 Mar 2025: 31.7% from outside,
  a record low; 8.3 outside-box shots per match [10].
- Conversion, PL 2024-25 to 31 Mar 2025: 14.7% inside the box, 4.2% outside [10]. Five-season
  mean including 2024-25 as of 18 Oct 2024: Bundesliga 12.2%, Ligue 1 11.6%, PL 11.2%, Serie A
  10.8%, LaLiga 10.7% [5].
- Headed goals 2024-25: Bundesliga 15.8% of goals (0.5 per match), 2. Bundesliga 16.8% [42].
- Average shot distance 2024-25: PL 16.6 yd, LaLiga 17.9 yd, Serie A 17.6 yd [4].

StatsBomb open data, full 2015-16 seasons, plus Leverkusen's 34 Bundesliga matches of 2023-24
[19] (my computation). Shot-mix numbers are comparable with Opta; pass counts and the rows built on
StatsBomb possessions (passes before a shot, "From Counter") are not (§1.1).

| Metric | PL 15-16 | LaLiga 15-16 | Serie A 15-16 | Ligue 1 15-16 | Leverkusen matches 23-24 |
|---|---|---|---|---|---|
| Matches | 380 | 380 | 380 | 377 | 34 |
| Shots per match | 26.07 | 24.13 | 26.31 | 22.79 | 26.94 |
| On target, excl. blocked (% of shots) | 32.3 | 35.2 | 31.5 | 34.7 | 37.2 |
| Blocked (% of shots) | 29.1 | 22.7 | 25.3 | 22.6 | 26.4 |
| Inside the box (% of shots) | 59.0 | 61.2 | 53.8 | 58.6 | 63.1 |
| Headed shots per match | 4.23 | 3.93 | 3.74 | 4.14 | 4.24 |
| Keeper save rate, saved ÷ (saved + goals), penalties incl. (%) | 69.1 | 68.6 | 69.8 | 69.9 | 67.7 |
| Save rate, shots from inside the box (%) | 60.7 | 61.2 | 60.8 | 61.9 | 60.6 |
| Save rate, shots from outside the box (%) | 86.4 | 87.5 | 86.4 | 86.4 | 87.8 |
| Open-play shots after 0-1 passes in the possession (%) | 23.0 | 24.2 | 21.8 | 24.7 | 19.3 |
| ... after exactly 2 passes (%) | 11.8 | 13.3 | 12.1 | 12.3 | 10.5 |
| ... after 3+ passes (%) | 65.3 | 62.6 | 66.1 | 63.0 | 70.2 |
| Open-play shots "From Counter" (%) | 4.8 | 4.6 | 4.5 | 4.9 | 5.1 |
| Passes per team (StatsBomb scale) | 485 | 481 | 492 | 484 | 577 |
| Pass accuracy (%) | 76.6 | 75.8 | 77.4 | 77.1 | 85.0 |
| Long-ball share, ≥ 35 yd (%) | 15.9 | 16.2 | 15.0 | 15.1 | 9.7 |
| Crosses per team (StatsBomb flag) | 12.6 | 12.6 | 12.7 | 11.9 | 10.1 |

StatsBomb definitions [19]: on target = outcome Goal / Saved / Saved To Post; inside the box =
x ≥ 102 and 18 ≤ y ≤ 62 on the 120 × 80 yd pitch; passes before a shot = passes by the shooting
team in the same possession, open-play shots only.

### 2.3 Passing

FBref/Opta 2024-25 squad tables [4], my computation. Only the PL and LaLiga tables are complete in
the archived captures; later captures have the Opta columns blank.

| League | Passes / team | Pass accuracy | Long-ball share (> 30 yd) | Crosses / team | Possession range (lowest - highest) |
|---|---|---|---|---|---|
| Serie A | n/a | n/a | n/a | 17.2 [4] | 38.7 Verona - 59.4 Inter [4] |
| Premier League | 486.7 [4]; Opta 446.7 (**derived** 893.4 per match ÷ 2) [7] | 81.4% [4] | 13.1% [4] | 17.6 [4] | 40.6 Ipswich - 61.3 Man City [4] |
| LaLiga | 477.9 [4] | 80.1% [4] | 14.8% [4] | 18.1 [4] | 39.8 Espanyol - 68.3 Barcelona [4] |
| Bundesliga | n/a | n/a | n/a | 18.2 [4] | 40.0 Union - 67.9 Bayern [4] |
| Ligue 1 | n/a | n/a | n/a | 17.2 [4] | 40.4 Nantes - 68.0 PSG [4] |
| Eredivisie | n/a | n/a | n/a | 18.2 [4] | 42.9 Almere - 67.5 PSV [4] |
| Primeira Liga | n/a | n/a | n/a | 17.3 [4] | n/a |
| Serie B | n/a | n/a | n/a | n/a | n/a |
| Championship | n/a | n/a | n/a | 17.9 [4] | n/a |
| 2. Bundesliga | n/a | n/a | n/a | 18.5 [4] | n/a |

More:
- Team range of passes per team: PL 378 Forest - 643 Man City; LaLiga 340 Getafe - 677
  Barcelona [4].
- Long-pass completion: PL 51.5%, LaLiga 52.6% [4].
- PL long-ball share on Opta's count (**derived**, long balls ÷ passes): 93.4 / 893.4 = 10.5% in
  2024-25, full season [7][8]; 99.6 / 873.3 = 11.4% in 2025-26 after 210 matches (16 Jan 2026) [8].
- PL pass accuracy on Opta: 82.6% in 2025-26 after 50 matches (five matchdays, 24 Sep 2025),
  "third highest on record" [7].
- Goal kicks 2024-25: LaLiga 50.7% launched, mean length 41.7 yd; Serie A 40.6% launched,
  35.5 yd [4].

### 2.4 Sequences and pressing

PPDA, Understat scale [2] (my computation: league total of opponent passes ÷ league total of
defensive actions):

| League | 2024-25 | 2025-26 | 2023-24 | 2014-15 | Lowest PPDA side 24-25 | Highest PPDA side 24-25 |
|---|---|---|---|---|---|---|
| Premier League | 11.34 | 11.40 | 11.84 | 9.94 | Bournemouth 8.53 | Everton 14.28 |
| LaLiga | 11.24 | 11.01 | 11.14 | 8.21 | Barcelona 6.48 | Espanyol 15.51 |
| Bundesliga | 13.05 | 13.11 | 13.08 | 8.66 | Bayern 8.99 | St. Pauli 17.15 |
| Serie A | 11.87 | 11.89 | 12.50 | 8.98 | Bologna 7.54 | Parma 16.32 |
| Ligue 1 | 11.65 | 11.98 | 11.51 | 9.09 | PSG 7.29 | Nantes 16.88 |
| Eredivisie, Primeira Liga, Serie B, Championship, 2. Bundesliga | n/a | n/a | n/a | n/a | n/a | n/a |

Source for every cell: [2]; every season column covers every match of a complete season. League
means of other seasons quoted in §3.1, same aggregation and source [2]: 2015-16 PL 9.41, LaLiga
8.24, Bundesliga 9.14, Serie A 8.74; 2019-20 PL 11.19, Serie A 10.58. Opta
scale, 2024-25, not comparable with the Understat cells:
Bournemouth lowest in the PL at 9.9, full season [15]; Forest highest at about 16, to 10 Feb 2025
[16]; LaLiga to 29 Nov 2024, Getafe 10.2, with only Real Sociedad (9.4) and Barcelona (8.9) lower
[12].

Opta sequence metrics. League means for 2024-25 are **n/a** in every league: not published in
text. Published team and older league figures:

| Metric | Figures |
|---|---|
| Passes per sequence | PL 2024-25 to 15 Apr 2025: Man City 5.1 highest, Southampton 4.4 second [6]; Man City 5.12 over the full season [7]; Forest 2.8 lowest, to 10 Feb 2025 [16]. LaLiga: Getafe 2.1 in 2024-25 to 29 Nov 2024, lowest in the top five [12]; league mean 2.4 in 2008-09 and 3.2 in 2020-21 [18]. Bundesliga: Bielefeld 2.3 and Bayern 4.05 in 2021-22 to 3 Feb 2022 [13]; Leverkusen 5.01 in 2023-24, league maximum [17]. PL 2025-26 after 50 matches (five matchdays, 24 Sep 2025): Brentford 2.84, fewest [7] |
| Direct speed | PL 2024-25 to 15 Apr 2025: Forest 2.1 m/s fastest, Man City 1.4 m/s slowest [6] (Forest also 2.1 m/s to 10 Feb 2025 [16]). Brighton 1.60 m/s over De Zerbi's "almost two" PL seasons (2022-24), only City, Burnley and Wolves slower [14]. LaLiga: Getafe 2.36 m/s in 2024-25 to 29 Nov 2024, fastest in the top five [12]; league mean 2.2 m/s in 2008-09 and 1.6 m/s in 2020-21 [18]. Bundesliga: Leverkusen slowest in 2023-24 at 1.71 m/s [17]; Dortmund slowest in 2021-22 to 3 Feb 2022 at 1.45 [13] |
| 10+ pass sequences | PL 2024-25 to 15 Apr 2025: Man City 656 (17.3 per match, **derived** 656 ÷ 38), Forest 192 (5.1 per match, **derived** 192 ÷ 38) [6]. Brighton under De Zerbi (2022-24): 1,256, second to City's 1,585 [14] |
| Start distance | PL 2024-25 to 15 Apr 2025: Ipswich 39.5 m (lowest), Forest 39.6 m, Man City 46.2 m (highest) [6] |
| High turnovers (both teams) | PL 16.7 in 2023-24 [7], 14.6 in 2024-25 [7][8] (full seasons); 2025-26: 11.5 after 50 matches (24 Sep 2025) [7], 13.3 after Matchday 21 (16 Jan 2026) [8]. Top-five mean 11.3 in 2014-15, 14.72 in 2023-24; four-season means including 2024-25 as of 18 Oct 2024: PL 15.9, Bundesliga 14.9, Ligue 1 14.7, LaLiga 14.2, Serie A 13.5 [5] |
| Shot-ending fast breaks | 2024-25 as of 18 Oct 2024: Bundesliga 2.1, Ligue 1 1.86, PL 1.77, LaLiga 1.66, Serie A 1.5 per match [5] |
| PL sequence time and progress | 2024-25, full season: 10.4 s and 12.6 m on average [7]. 2025-26: 9.6 s and 12.1 m after 50 matches (24 Sep 2025) [7]; 9.5 s and 12.1 m after 210 matches (16 Jan 2026) [8] |

Caveat on "÷ 38": [6] is dated 15 Apr 2025 and says Forest and Man City had "six games
remaining", so its season totals cover fewer than 38 matches, and every per-match figure derived
from [6] by ÷ 38 (here and in §3.1, §3.2) is a lower bound.

### 2.5 Ball in play and distance

Ball in play per match (min:s). The CIES figures [21] are "2024/25" as published on 21 May 2025;
[21] states no cut-off, and the PL, Serie A and LaLiga seasons ended on 25 May 2025 [1], after that
date. Gazzetta's Serie A 54:50 is the full 2024-25 season ("lo scorso campionato") [20]; Opta's PL
56:59 is the full season ("last season", 13 Oct 2025) [9].

| League | 2024-25 | Source |
|---|---|---|
| Serie A | 53:36 (CIES); 54:50 (Gazzetta) | [21]; [20] |
| Premier League | 54:36 (CIES); 56:59 (Opta) | [21]; [9] |
| LaLiga | 53:24 | [21] |
| Bundesliga | 55:06 | [21] |
| Ligue 1 | 56:40 | [21] |
| Eredivisie | 56:42 | [21] |
| Primeira Liga | 50:44 | [21] |
| Serie B | n/a | - |
| Championship | n/a | - |
| 2. Bundesliga | n/a | - |

More:
- PL (Opta): 54:45 (2021-22), 54:49 (2022-23), 58:11 (2023-24), 56:59 (2024-25), 55:00
  (2025-26 after 70 matches, seven matchdays, 13 Oct 2025) [9].
- Three-season mean, from the start of 2022-23 to 18 Oct 2024: Ligue 1 56:35, PL 56:33,
  Bundesliga 56.8% of match time; LaLiga lowest; Serie A 36 s more than LaLiga [5].
- Serie A: 55:17 (2023-24), 52:55 (2025-26, first matchdays up to the September 2025
  international break; article of 12 Sep 2025) [20].
- LaLiga 2024-25 team extremes, full season (article of 27 May 2025, after the last matchday on
  25 May [1]): Real Madrid 59:40, Barcelona 55:58, Getafe 49:36 [22].

Distance:

| League | Figure | Source |
|---|---|---|
| Bundesliga | 117.0 km per team-match incl. subs and GK (112.8 Bochum - 120.3 St. Pauli), **derived**: season total ÷ 34 | [23] |
| 2. Bundesliga | 115.3 km per team-match incl. subs and GK (111.9 - 120.0), **derived**: season total ÷ 34 | [24] |
| Other eight leagues, per outfield player | n/a | - |

- 31 leagues, 2020-21 (SkillCorner): 99.9 km outfield per team-match, about 10.0 km per outfield
  player (**derived** 99.9 ÷ 10); LaLiga highest at 103.7 km; midfielders 10.6 km, centre-backs
  9.2 km per match [27].
- PL 2024-25 per-90 extremes (≥ 1,500 min), after 29 matchweeks (25 Mar 2025): Kulusevski
  12.3 km, Murillo 8.5 km [26].

## 3. Style profiles

Every R18 style has a definition and example teams (catalogue below) and the example teams'
sourced figures (§3.1). §3.2 checks R19's four anchor styles against real data. This section sets
no direction, strength or threshold of its own: the only thresholds in it are R19's anchors, quoted
from the spec.

Periods and scales. Every club figure in §3.1 carries its season or the date its source covers, its
scale and its source. Sources that stop mid-season: [12] covers LaLiga 2024-25 to 29 Nov 2024, [16]
the PL 2024-25 to 10 Feb 2025, [6] the PL 2024-25 to 15 Apr 2025 (six games remaining), [7] the PL
2025-26 after 50 matches (five matchdays, 24 Sep 2025), [8] the PL 2025-26 after 210 matches
(Matchday 21, 16 Jan 2026). [2], [4], [15] and [17] cover full seasons; [19] covers the seasons of the table below. Two figures
for one club that differ in period or scale are **not comparable** with each other, and §3.1 says so
wherever it quotes both.

League references: PL 2024-25 full season about 487 passes per team (486.7, §2.3) [4], Understat
PPDA 11.34 [2], long-ball share 13.1% [4], crosses 17.6 per team [4]. StatsBomb 2015-16 league means
(StatsBomb scale, §1.1): passes 481-492, pass accuracy 75.8-77.4%, long 15.0-16.2% (§2.2), passes per possession
4.5-4.85, 10+ possessions 8.6-9.7, direct speed 2.0-2.1 m/s, start 33-35 m [19].

StatsBomb team rows (2015-16 unless stated) [19], Understat PPDA for the same season [2]. Every
column but PPDA is computed from StatsBomb events, so "Poss %", "pp-poss", "10+" and "SB direct" are
on the StatsBomb scale (§1.1). A club quoted elsewhere in this doc for another season, or on the
Opta/FBref scale (Barcelona, Real Madrid, Man City, Getafe, Leverkusen), has other figures there;
they are not comparable with its row here.

| Team (season) | Poss % | Passes | Acc % | Long % | Crosses | pp-poss | 10+ / team | SB direct m/s | Start m | Understat PPDA |
|---|---|---|---|---|---|---|---|---|---|---|
| Barcelona 15-16 | 66.2 | 677 | 86.1 | 10.8 | 12.0 | 6.86 | 17.4 | 1.55 | 36.6 | 5.66 |
| PSG 15-16 | 67.5 | 769 | 87.7 | 9.1 | 13.5 | 8.28 | 23.9 | 1.51 | 34.9 | 6.32 |
| Napoli (Sarri) 15-16 | 62.5 | 708 | 84.4 | 9.3 | 13.7 | 6.60 | 19.0 | 1.74 | 38.1 | 7.81 |
| Real Madrid 15-16 | 57.2 | 613 | 84.6 | 12.7 | 18.0 | 5.84 | 15.2 | 1.86 | 36.7 | 8.86 |
| Juventus 15-16 | 55.5 | 567 | 83.3 | 12.4 | 13.3 | 5.85 | 13.5 | 1.69 | 34.8 | 8.52 |
| Arsenal 15-16 | 57.6 | 598 | 82.6 | 11.3 | 12.5 | 6.12 | 14.2 | 1.82 | 34.3 | 8.35 |
| Man City 15-16 | 57.0 | 588 | 81.3 | 11.2 | 14.2 | 6.03 | 14.0 | 1.73 | 37.1 | 7.78 |
| Tottenham (Pochettino) 15-16 | 56.8 | 543 | 78.7 | 14.8 | 11.3 | 5.18 | 12.2 | 1.83 | 36.7 | 6.44 |
| Liverpool (Klopp, from Oct) 15-16 | 56.7 | 565 | 79.2 | 12.4 | 13.5 | 5.46 | 11.8 | 1.82 | 36.6 | 7.48 |
| Leverkusen (Schmidt) 15-16 | 52.8 | 480 | 73.5 | 15.3 | 11.4 | 4.52 | 8.2 | 2.13 | 34.7 | 5.93 |
| Atlético Madrid 15-16 | 48.5 | 506 | 76.4 | 13.5 | 13.1 | 4.94 | 11.6 | 2.05 | 36.6 | 8.42 |
| Leicester 15-16 | 42.5 | 397 | 68.9 | 19.8 | 13.6 | 3.99 | 5.7 | 2.44 | 35.4 | 9.57 |
| Stoke 15-16 | 49.4 | 470 | 77.1 | 16.5 | 10.6 | 4.76 | 9.6 | 2.02 | 34.0 | 9.94 |
| West Brom (Pulis) 15-16 | 39.9 | 368 | 67.9 | 21.4 | 12.2 | 3.81 | 4.8 | 2.36 | 35.7 | 11.41 |
| Sunderland 15-16 | 41.3 | 383 | 68.9 | 21.9 | 8.7 | 3.90 | 5.1 | 2.38 | 35.2 | 11.64 |
| Crystal Palace 15-16 | 45.7 | 416 | 72.1 | 20.0 | 14.7 | 4.16 | 6.1 | 2.26 | 34.1 | 9.03 |
| Getafe 15-16 | 46.1 | 423 | 73.8 | 17.9 | 11.1 | 3.99 | 6.8 | 2.20 | 34.0 | 9.38 |
| Carpi 15-16 | 38.5 | 371 | 67.2 | 20.1 | 11.3 | 3.33 | 3.0 | 2.80 | 33.3 | 9.96 |
| Frosinone 15-16 | 39.6 | 349 | 68.1 | 21.0 | 11.0 | 3.31 | 3.0 | 2.69 | 34.2 | 8.57 |
| Leverkusen (Alonso) 23-24 | 61.8 | 713 | 88.1 | 7.2 | 12.9 | 8.44 | 20.2 | 1.36 | 37.6 | 12.69 |

Style catalogue (R18). The last column quotes R19's anchor bands from the spec; the other styles
have no anchor, and no other threshold is set here.

| # | Style | Definition | Example teams | R19 anchor (quoted from the spec) |
|---|---|---|---|---|
| 1 | Tiki-taka | Short-pass possession, many players near the ball, patient circulation, press to win the ball back | Barcelona 15-16, Barcelona 24-25, PSG 15-16 | possession 58-70%, passes 600-780, passes per sequence ≥ 5.0 |
| 2 | Positional play | Fixed zones and width, superiority behind each line, slow build to break lines (Guardiola) | Man City 24-25, Bayern 24-25 | - |
| 3 | Gegenpress | Immediate counter-press after a loss, short vertical attacks after a regain | Leverkusen (Schmidt) 15-16, Liverpool (Klopp) 19-20, Bournemouth 24-25 | PPDA ≤ 9, high turnovers ≥ 1.4x Balanced |
| 4 | Vertical tiki-taka | Possession-based but always looking forward; quick vertical combinations (Sarri, Alonso, De Zerbi) | Napoli (Sarri) 15-16, Leverkusen (Alonso) 23-24 | - |
| 5 | Control possession | Keep the ball to control tempo and risk; low-risk passing, fewer runners | Real Madrid 24-25, Juventus 15-16 | - |
| 6 | Wing play | Width from wingers and full-backs, final third reached wide, many crosses | Fulham, Bournemouth, Osasuna, Girona 24-25 | - |
| 7 | Route one | Long balls to a target forward, second balls, few passes per move | West Brom (Pulis) 15-16, Getafe (Bordalás) 24-25, Everton 24-25 | long-ball share ≥ 18%, passes per sequence ≤ 3.0 |
| 8 | Direct | Forward passing as soon as possible, on the ground or long as needed; not necessarily a low block | Brentford 24-25 and 25-26 | - |
| 9 | Fluid counter | Concede the ball, regain mid or low, counter with combinations and several runners | Leicester 15-16 | - |
| 10 | Direct counter | Low or mid block, then the fastest possible long or vertical ball into space | Nottingham Forest 24-25 | - |
| 11 | Mid block | Engage around halfway, compact 4-4-2 or 4-5-1, deny central access | Atlético Madrid (Simeone) 15-16; WC 2022 mid blocks | - |
| 12 | Catenaccio | Deep, man-tight defending with a spare man (libero), counters; modern sense: deep five-man line and patient counters | Inter (Mourinho) 09-10; modern proxy Torino 15-16 | - |
| 13 | Park the bus | Low block with ten men behind the ball, no press, ball conceded | Forest 24-25, St. Pauli 24-25, Parma 24-25 | possession ≤ 40%, PPDA ≥ 16, average defensive line ≤ 32 m from own goal |
| 14 | Man-oriented press (Gasperini) | Man-for-man marking across the pitch, 1v1 at the back, aggressive duels | Atalanta 19-20, 23-24, 24-25 | - |
| 15 | Balanced | League average on every axis | PL 2024-25 league means (§2) | - |

Extra styles found, not in R18:
- **Relational play** (Diniz's Fluminense, 2023 Libertadores): players cluster around the ball, no
  fixed zones, overloads on one side. Club figures: n/a.
- **High line + offside trap** (Flick's Barcelona 24-25): Understat PPDA 6.48, lowest in LaLiga [2];
  68.3% poss (FBref) [4].

### 3.1 Example-team figures per style

Each entry lists the example teams' figures with period, scale and source, beside the league
reference of the same scale and season where one was reached. Ratios are **derived** as shown.
StatsBomb figures (pp-poss, 10+, SB direct, start, crosses, long ≥ 35 yd) stand beside StatsBomb
league means only, never beside Opta or FBref figures.

1. **Tiki-taka.** Barcelona 15-16 [19]: 677 passes, 66.2% poss, pp-poss 6.86, 10+ 17.4, SB direct
   1.55, crosses 12.0 vs LaLiga 12.6 (0.952x), long 10.8% vs LaLiga 16.2%, start 36.6 m. PSG 15-16
   [19]: 769 passes, 67.5% poss, pp-poss 8.28, 10+ 23.9, SB direct 1.51, crosses 13.5 vs Ligue 1
   11.9 (1.13x), long 9.1% vs Ligue 1 15.1%, start 34.9 m. Barcelona 24-25 full season (FBref) [4]:
   677 passes, 68.3% poss, long 9.4% (> 30 yd) vs LaLiga 14.8%; Understat PPDA 6.48 vs LaLiga 11.24,
   0.58x [2]. Barcelona's two pass counts share a value but not a season or a scale (StatsBomb 15-16,
   FBref 24-25): not comparable.
2. **Positional play.** Man City 24-25, Opta, to 15 Apr 2025 [6]: 5.1 pass/seq, the highest in the
   PL; 656 10+ sequences, 17.3 per match (**derived** 656 ÷ 38, a lower bound, §2.4); 1.4 m/s, the
   slowest in the PL; start 46.2 m, the highest in the PL; 300 high turnovers, 7.9 per match
   (**derived** 300 ÷ 38, a lower bound) vs PL 14.6 ÷ 2 = 7.3 per team [8], 1.08x. Man City 24-25 full
   season (FBref) [4]: 643 passes vs about 487, 61.3% poss, long 8.3% vs 13.1%; Understat PPDA 12.09
   vs PL 11.34 [2]. Bayern 24-25 full season: 67.9% poss (FBref) [4]; Understat PPDA 8.99 vs
   Bundesliga 13.05 [2].
3. **Gegenpress.** Understat PPDA [2]: Leverkusen 15-16 5.93 vs Bundesliga 9.14, 0.65x; Liverpool
   19-20 8.01 vs PL 11.19, 0.72x; Bournemouth 24-25 8.53 vs PL 11.34, 0.75x. Bournemouth 24-25 full
   season, Opta [15]: PPDA 9.9, the lowest in the PL, and 68 shot-ending high turnovers, the most in
   the PL; its 8.53 (Understat) and 9.9 (Opta) are on different scales (§1.3). Bournemouth 24-25,
   Opta, to 15 Apr 2025 [6]: 298 high turnovers, 7.8 per match (**derived** 298 ÷ 38, a lower bound)
   vs PL 7.3 per team [8], 1.07x. Bournemouth 24-25 full season (FBref) [4]: crosses 21.9 vs 17.6, long
   16.7% vs 13.1%. Leverkusen 15-16 [19]: 480 passes, 52.8% poss, pp-poss 4.52, 10+ 8.2, SB direct
   2.13, long 15.3%, start 34.7 m.
4. **Vertical tiki-taka.** Napoli (Sarri) 15-16 [19]: 708 passes, 62.5% poss, pp-poss 6.60, 10+
   19.0, SB direct 1.74, long 9.3% vs Serie A 15.0%, crosses 13.7 vs Serie A 12.7 (1.08x), start
   38.1 m; Understat PPDA 7.81 vs Serie A 8.74, 0.89x [2]. Leverkusen (Alonso) 23-24 full
   season, Opta [17]: 62.1% poss, 5.01 pass/seq (the Bundesliga maximum), 1.71 m/s (the slowest in
   the Bundesliga), 367 high turnovers, 10.8 per match, the most in the Bundesliga (1.44x or 1.59x the
   league mean per team, §3.2); Understat PPDA 12.69 vs Bundesliga 13.08, 0.97x [2]. Leverkusen's StatsBomb
   row for the same season (61.8% poss, pp-poss 8.44, SB direct 1.36 m/s) is on the StatsBomb scale:
   not comparable with the Opta figures of [17].
5. **Control possession.** Real Madrid 24-25 full season (FBref) [4]: 637 passes, 60.4% poss, 88.2%
   accuracy, long 10.7% vs LaLiga 14.8%, crosses 15.2 vs 18.1; Understat PPDA 11.11 vs LaLiga 11.24
   [2]. Juventus 15-16 [19]: 567 passes, 55.5% poss, pp-poss 5.85, 10+ 13.5, SB direct 1.69, long
   12.4% vs Serie A 15.0%, crosses 13.3 vs Serie A 12.7 (1.047x), start 34.8 m.
6. **Wing play.** Crosses per team, 24-25 full season (FBref) [4]: Fulham 23.5 ÷ 17.6 = 1.34x (the
   highest in the PL), Bournemouth 21.9 ÷ 17.6 = 1.24x, Osasuna 21.6 ÷ 18.1 = 1.19x, Girona 21.2 ÷
   18.1 = 1.17x. Bournemouth's other figures are in 3; no other figure for these examples is quoted.
7. **Route one.** West Brom (Pulis) 15-16 [19]: 368 passes, 39.9% poss, 10+ 4.8 vs PL 9.7, SB direct
   2.36 vs 2.0-2.1, long 21.4% (≥ 35 yd) vs PL 15.9%, crosses 12.2 vs PL 12.6 (0.97x), start 35.7 m.
   Getafe (Bordalás) 24-25, Opta, to 29 Nov 2024 [12]: 294.9 passes per match and 2.1 pass/seq, both
   the fewest in the top five leagues; direct speed 2.36 m/s, the fastest in the top five; PPDA 10.2,
   with only Real Sociedad (9.4) and Barcelona (8.9) lower in LaLiga. Getafe 24-25 full season
   (FBref) [4]: 340 passes, the fewest in LaLiga; long 22.8% (> 30 yd). Not comparable: Getafe's 294.9
   and 340 passes differ in period (to 29 Nov vs full season) and in count (FBref's PL mean is 486.7
   passes per team, Opta's 446.7, §2.3); Getafe's 2.36 m/s (Opta, 24-25) and West Brom's SB direct
   2.36 (StatsBomb, 15-16) only share a value. Everton 24-25 full season: long 19.4% (> 30 yd) [4];
   Understat PPDA 14.28, the highest in the PL, vs 11.34 [2].
8. **Direct.** Brentford 25-26, Opta, after 50 matches (five matchdays, 24 Sep 2025) [7]: 2.84
   pass/seq, the fewest in the PL.
   Brentford 24-25 full season (FBref) [4]: 450 passes vs about 487, long 16.0% vs 13.1%.
9. **Fluid counter.** Leicester 15-16 [19]: 397 passes, 42.5% poss, pp-poss 3.99, 10+ 5.7, SB direct
   2.44 vs PL 2.01, long 19.8% vs PL 15.9%, crosses 13.6 vs PL 12.6 (1.08x), start 35.4 m; counters
   7.8% of open-play shots vs PL 4.8%. Understat PPDA 9.57 vs PL 9.41 [2].
10. **Direct counter.** Nottingham Forest 24-25, Opta, to 10 Feb 2025 [16]: 39.5% poss, the lowest in
    the PL; 2.8 pass/seq, the fewest; PPDA 16, the highest; 2.1 m/s, the fastest; 32 fast-break
    shots, behind only Liverpool (45) and Chelsea (36). Forest 24-25, Opta, to 15 Apr 2025 [6]: 2.1
    m/s, the fastest; 192 10+ sequences, 5.1 per match (**derived** 192 ÷ 38, a lower bound) vs Man
    City 17.3; start 39.6 m, next to Ipswich's 39.5 m (the lowest; City 46.2 m the highest). Forest
    24-25 full season: 378 passes, the fewest in the PL, long 16.3% vs 13.1% (FBref) [4]; Understat
    PPDA 14.22 vs PL 11.34 [2]. Not comparable: PPDA 16 (Opta zone, to 10 Feb) and 14.22 (Understat
    zone, full season, §1.3); possession 39.5% (Opta, to 10 Feb) and FBref's full-season table, whose
    lowest PL side is Ipswich at 40.6% [4], so Forest's full-season FBref possession is at least 40.6%.
11. **Mid block.** Atlético 15-16 [19]: 506 passes vs LaLiga 481 (1.052x), 48.5% poss, pp-poss 4.94,
    10+ 11.6, SB direct 2.05, crosses 13.1 vs LaLiga 12.6 (1.04x), long 13.5% vs LaLiga 16.2%, start
    36.6 m; Understat PPDA 8.42 vs LaLiga 8.24 (1.02x), 0.47 goals conceded per match [2]. WC 2022:
    mid-block share Morocco 38%, France 37%, Argentina 32%, Spain 9%; mid-block size 40.1 m wide ×
    27.2 m long, tournament mean [35].
12. **Catenaccio.** Inter (Mourinho) 09-10: 4-2-1-3, "bus" in the 2010 Champions League semi-final
    [38]. Torino 15-16 (modern proxy): start 31.3 m [19], lower than every row of the StatsBomb team
    table above (its lowest is Carpi at 33.3 m); Understat PPDA 10.71 vs Serie A 8.74, 1.23x [2].
13. **Park the bus.** Forest 24-25: see 10. St. Pauli 24-25 full season: Understat PPDA 17.15, the
    highest in the Bundesliga; 0.82 goals scored and 1.21 conceded per match [2]. Parma 24-25 full
    season: Understat PPDA 16.32 [2].
14. **Man-oriented press (Gasperini).** Atalanta 23-24 (Europa League vs Liverpool): 37 of 50
    defensive moments in the opposition half used the man-to-man shape [41]. Understat PPDA [2]:
    19-20 7.76 vs Serie A 10.58 (0.73x), with 2.58 goals scored per match; 24-25 11.28 vs Serie A
    11.87 (0.950x). Matches with Atalanta under Gasperini averaged 3.1 goals, both teams, over his
    nine seasons [46]: a different measure from the 2.58 scored in 19-20, not comparable.
15. **Balanced.** The league means of §2.

### 3.2 R19 anchors against real data

- **Tiki-taka.** Possession 58-70% and passes 600-780: Barcelona 24-25, full season (FBref) [4],
  68.3% and 677, inside both ranges. FBref's pass count is not shown to be the harness's either: the
  PL's FBref and Opta means differ, 486.7 vs 446.7 (§2.3). Barcelona 15-16 and PSG 15-16 (66.2% /
  677 and 67.5% / 769) [19] are StatsBomb possessions and pass counts, not the harness scale;
  direction only (§1.1): as ratios to their leagues' StatsBomb means, 677 ÷ 481 = 1.41x (LaLiga) and
  769 ÷ 484 = 1.59x (Ligue 1) (**derived**), so they show more passing than the league, not whether
  600-780 holds. Passes per sequence ≥ 5.0: no tiki-taka example has a published Opta figure; the highest
  published team figures are Man City 5.1 (PL 24-25 to 15 Apr 2025) [6] and Leverkusen 5.01
  (Bundesliga 23-24) [17], so ≥ 5.0 sits at the very top of the real range.
- **Gegenpress.** PPDA ≤ 9 holds for the lowest-PPDA side of every top-five league on the
  Understat scale (6.48-8.99, 2024-25 full season, §2.4) [2], while no 2024-25 PL side was below 9.9
  on Opta's [15]: it holds only on the harness's Understat zone (§1.3). The zone is shared, but
  Understat's action set is not stated and the harness's is a convention (§6), so this is a check on
  Understat's figures, not yet on the harness's own PPDA. High turnovers ≥ 1.4x
  Balanced: Leverkusen 2023-24 had 10.8 per match [17]. [5] (18 Oct 2024) gives the Bundesliga's
  league mean as falling "from 15.04 to 13.61 per game" without naming the seasons; next to its
  Serie A line ("12.76 per game last season and 11.74 in 2024-25") the likelier reading is 15.04 for
  2023-24 and 13.61 for 2024-25 as of 18 Oct 2024. Per team (**derived**): 15.04 ÷ 2 = 7.52, 10.8 ÷ 7.52 = 1.44x;
  on the other reading (13.61 for 2023-24), 13.61 ÷ 2 = 6.8, 10.8 ÷ 6.8 = 1.59x. Leverkusen
  2023-24 meets 1.4x on either reading, but it is a vertical tiki-taka example. The gegenpress example Bournemouth reaches 1.07x
  and Man City (positional play) 1.08x, both lower bounds (§3.1). The anchor is therefore met by one
  published side, not shown for the style's own examples (§8).
- **Route one.** Long-ball share ≥ 18%: Getafe 22.8% and Everton 19.4% (FBref 24-25, > 30 yd) [4],
  West Brom 21.4% (StatsBomb 15-16, ≥ 35 yd, my cut) [19]. West Brom's share is StatsBomb data, not
  the harness scale; direction only (§1.1): 21.4 ÷ 15.9 = 1.35x the PL's StatsBomb mean
  (**derived**). Getafe and Everton meet 18% on FBref's > 30 yd count only: the PL's FBref and Opta
  long-ball shares differ, 13.1% vs 10.5% (§2.3), and no source fixes the anchor's scale. Passes per sequence ≤ 3.0: Getafe 2.1 (Opta,
  LaLiga 24-25 to 29 Nov 2024) [12]; Forest (direct counter) 2.8 (Opta, PL 24-25 to 10 Feb 2025)
  [16] and Brentford (direct) 2.84 (Opta, PL 25-26 after 50 matches, 24 Sep 2025) [7] are also
  under it.
- **Park the bus.** Possession ≤ 40%: Forest 39.5% (Opta, PL 24-25 to 10 Feb 2025) [16]. On FBref's
  full-season 2024-25 tables the lowest side of each top-five league sits at 38.7-40.6% [4], and the
  PL's lowest is Ipswich at 40.6%, so Forest's full-season FBref figure is at least 40.6%: the two
  Forest figures differ in period and provider and are not comparable (§3.1, 10). PPDA ≥ 16 on Understat's
  figures (the harness's zone; action set as in Gegenpress above): St. Pauli 17.15 and Parma 16.32 meet it (full season) [2]; Forest's "16"
  is Opta, to 10 Feb 2025 [16], and its full-season Understat figure is 14.22 [2]. Average line
  ≤ 32 m from own goal: the WC 2022 keeper line height of 13.1 m plus the keeper-to-line gap of
  20.4 m gives 33.5 m as the tournament's mean back line (**derived**: 13.1 + 20.4) [36], so ≤ 32 m
  is a deeper-than-average block.

## 4. Formation catalogue

Position codes: GK, RB/LB full-backs, CB, RWB/LWB wing-backs, DM, CM, AM, RM/LM wide midfielders,
RW/LW wingers, SS second striker, ST. Definitions from [38]; in/out-of-possession shapes from
[38][39][40][47]. Shapes marked *(typical)* are standard coaching descriptions with no source
stating them; they are not measured data. The "Usually paired with" column is *(typical)* in every
row: no source states the pairings.

| Formation | 11 slots | In possession | Out of possession | Usually paired with *(typical)* | Example (season) |
|---|---|---|---|---|---|
| 4-4-2 | GK, RB, CB, CB, LB, RM, CM, CM, LM, ST, ST | 2-4-4 / 4-2-4, full-backs high *(typical)* | 4-4-2 block, two banks of four sliding [39] | Mid block, Direct, Route one, Wing play, Direct counter | Atlético (Simeone) [39]; Milan (Sacchi) 1988-95 [38] |
| 4-4-2 diamond | GK, RB, CB, CB, LB, DM, RCM, LCM, AM, ST, ST | 2-3-3-2, central overload [39] | 4-3-1-2 / narrow 4-4-2 [39] | Control possession, Tiki-taka, Vertical tiki-taka | Milan 2002-03 (listed under 4-3-1-2 in [38]) |
| 4-1-2-1-2 | GK, RB, CB, CB, LB, DM, RCM, LCM, AM, ST, ST ([38] treats it as the diamond; kept as the *narrow* variant, shuttlers inside) | 2-3-3-2 *(typical)* | 4-3-1-2 *(typical)* | As the diamond | As the diamond |
| 4-4-1-1 | GK, RB, CB, CB, LB, RM, CM, CM, LM, SS, ST | 4-2-3-1-like / 2-4-4 *(typical)* | 4-4-1-1 [40] | Mid block, Direct counter, Fluid counter | Man Utd (Ferguson) *(typical)* |
| 4-3-3 (4-1-2-3) | GK, RB, CB, CB, LB, DM, RCM, LCM, RW, ST, LW | 2-3-5 / 3-2-5 (full-back inverts or DM drops) [38] | 4-5-1 / 4-1-4-1 mid block, or 4-3-3 high press [39] | Positional play, Tiki-taka, Gegenpress, Vertical tiki-taka, Control possession | Man City 2022-23 [38]; Liverpool (Klopp) [39] |
| 4-3-3 holding (4-2-1-3) | GK, RB, CB, CB, LB, DM, DM, CM, RW, ST, LW | 2-4-4 / 4-2-4 *(typical)* | 4-4-1-1 / 4-5-1 *(typical)* | Mid block, Direct counter, Catenaccio | Inter (Mourinho) 2009-10 [38] |
| 4-2-3-1 | GK, RB, CB, CB, LB, DM, DM, RAM, AM, LAM, ST | 2-3-5 / 3-2-5 / 2-4-4 [38][40] | 4-4-2 / 4-4-1-1 [40] | Balanced, Gegenpress, Control possession, Wing play, Mid block | Most common modern shape [38] |
| 4-1-4-1 | GK, RB, CB, CB, LB, DM, RM, CM, CM, LM, ST | 2-3-5 *(typical)* | 4-1-4-1 / 4-5-1 mid or low block *(typical)* | Mid block, Control possession, Gegenpress | Spain / Guardiola variants *(typical)* |
| 4-5-1 | GK, RB, CB, CB, LB, RM, CM, CM, CM, LM, ST | 4-3-3 *(typical)* | 4-5-1 low block [47] | Park the bus, Mid block, Direct counter | [38] |
| 4-3-2-1 (Christmas tree) | GK, RB, CB, CB, LB, RCM, CM, LCM, AM, AM, ST | 2-3-2-3 *(typical)* | 4-3-2-1 / 4-5-1 *(typical)* | Control possession, Fluid counter | Milan (Ancelotti) 2006-07 [38] |
| 4-2-2-2 (magic rectangle) | GK, RB, CB, CB, LB, DM, DM, AM, AM, ST, ST | 2-2-2-4, full-backs give width *(typical)* | 4-4-2 *(typical)* | Fluid counter, Direct, Vertical tiki-taka | Brazil and France 1982-86, Pellegrini's Real Madrid [38] |
| 4-3-1-2 | GK, RB, CB, CB, LB, RCM, CM, LCM, AM, ST, ST | 2-3-3-2, full-backs give width [38] | 4-3-1-2 / 4-3-2-1 *(typical)* | Control possession, Vertical tiki-taka, Direct counter | Porto 2003-04, Chelsea 2009-10 [38] |
| 4-1-3-2 | GK, RB, CB, CB, LB, DM, RM, AM, LM, ST, ST | 2-1-3-4 *(typical)* | 4-1-3-2 → 4-4-2 *(typical)* | Gegenpress, Direct, Wing play | Lobanovskyi [38] |
| 4-2-4 | GK, RB, CB, CB, LB, CM, CM, RW, ST, ST, LW | 2-2-6 *(typical)* | 4-4-2, wingers drop *(typical)* | Wing play, Route one, Direct | Brazil 1958 [38] |
| 4-6-0 | GK, RB, CB, CB, LB, DM, CM, CM, RAM, AM (false 9), LAM | 2-3-5 with no fixed striker [38] | 4-5-1 / 4-1-4-1 *(typical)* | Tiki-taka, Positional play | Spain Euro 2012 (false 9) *(typical)* |
| 3-5-2 | GK, RCB, CB, LCB, RWB, CM, DM, CM, LWB, ST, ST | 3-1-4-2 / 3-5-2, wing-backs high [39] | 5-3-2 [39] | Catenaccio, Mid block, Direct counter, Wing play | Bilardo's Argentina [38]; Inter (Conte/Inzaghi) *(typical)* |
| 3-4-3 | GK, RCB, CB, LCB, RWB, CM, CM, LWB, RW, ST, LW | 3-4-3 / 3-2-5, front three narrow as 10s [39] | 5-4-1 / 5-2-3 *(typical)* | Gegenpress, Man-oriented press, Wing play | Chelsea (Conte) 2016-17 [38] |
| 3-4-2-1 | GK, RCB, CB, LCB, RWB, CM, CM, LWB, AM, AM, ST | 3-2-5 / 3-4-3 [38] | 5-4-1 [38] | Man-oriented press, Mid block, Direct counter | Atalanta (Gasperini) 2023-24 UEL [41] |
| 3-4-1-2 | GK, RCB, CB, LCB, RWB, CM, CM, LWB, AM, ST, ST | 3-4-1-2 → 3-2-5 *(typical)* | 5-3-2 / 5-2-1-2 *(typical)* | Man-oriented press, Direct, Catenaccio | Celtic (O'Neill) 2003 [38] |
| 3-1-4-2 | GK, RCB, CB, LCB, DM, RWB, CM, CM, LWB, ST, ST | 3-1-4-2, wing-backs high *(typical)* | 5-3-2 *(typical)* | Control possession, Wing play | Variant of 3-5-2 (no source) |
| 3-5-1-1 | GK, RCB, CB, LCB, RWB, CM, CM, CM, LWB, SS, ST | 3-4-2-1-like *(typical)* | 5-3-1-1 / 5-4-1 *(typical)* | Mid block, Park the bus | Variant of 3-5-2 [38] |
| 5-3-2 | GK, RWB, RCB, CB, LCB, LWB, CM, CM, CM, ST, ST | 3-5-2 when wing-backs push [38] | 5-3-2 [38] | Catenaccio, Park the bus, Direct counter | Inter (Herrera) 1960s; Brazil 2002 [38] |
| 5-4-1 | GK, RWB, RCB, CB, LCB, LWB, RM, CM, CM, LM, ST | 3-6-1, attacking full-backs [38] | 5-4-1 low block [38] | Park the bus, Catenaccio, Direct counter | Greece Euro 2004 [38] |
| 5-2-3 | GK, RWB, RCB, CB, LCB, LWB, CM, CM, RW, ST, LW | 3-4-3 / 3-2-5 *(typical)* | 5-2-3 → 5-4-1 *(typical)* | Direct counter, Fluid counter | Defensive reading of 3-4-3 [38] |
| 5-2-1-2 | GK, RWB, RCB, CB, LCB, LWB, CM, CM, AM, ST, ST | 3-4-1-2 *(typical)* | 5-3-2 *(typical)* | Catenaccio, Direct counter, Route one | Variant of 5-3-2 / 3-4-1-2 [38] |
| 3-2-4-1 (box) | GK, CB, CB, CB, DM, DM, RW, AM, AM, LW, ST | 3-2-4-1 box [38] | 4-4-2 / 4-2-3-1 [38] | Positional play | Man City 2022-23 [38] |
| 5-2-2-1 | GK, RWB, RCB, CB, LCB, LWB, CM, CM, AM, AM, ST | 3-4-2-1 *(typical)* | 5-2-2-1 / 5-4-1 [38] | Direct counter, Mid block | [38] |

The last two are common shapes missing from the R17 list. Shape references for R4/R6:
- Team length 31-46 m, width 35-48 m, both larger in possession; mean team area about 900 m² [34].
- WC 2022 mid-block compactness: width 40.1 m × length 27.2 m [35].
- WC 2022 keeper line height 13.1 m (2018: 11.8 m); keeper to defensive line 20.4 m [36][37].

## 5. RealismReference bands

`shared/Sim.Core/Match/Analysis/RealismReference.cs`. Equal-strength top-tier sides, Balanced
style, harness definitions of §1.5. **Top-five envelope** = the lowest and highest 2024-25 league
figure among Serie A, PL, LaLiga, Bundesliga and Ligue 1, on the band's own scale. For the bands
set from football-data [1] the complete 2025-26 envelope is given too, and a changed bound must
hold against both seasons (§6). It is n/a when a league is missing or the scale is not
established; the "other reference" column then gives context only and never sets a limit.

| Band (constant) | R2 start | Band | Top-five envelope | Other reference (context only) | Backing section |
|---|---|---|---|---|---|
| Goals per match (`Goals`) | 2.5-3.2 | 2.5-3.2 | 2.56 (Serie A) - 3.13 (Bundesliga) [1]; 2025-26: 2.43 (Serie A) - 3.24 (Bundesliga) [1] | - | §2.1, §6 |
| Shots per match (`Shots`) | 23-28 | **23-27.9** | 23.69 (LaLiga) - 25.92 (PL) [1]; 2025-26: 24.66 (Serie A) - 26.51 (Bundesliga) [1] | Understat 2024-25 23.76-25.91 [2] | §2.1, §6 |
| On target excl. blocked, % of shots (`OnTargetPercent`) | 30-38 | 30-38 | 33.6 (Serie A) - 37.7 (Ligue 1) [1]; 2025-26: 32.7 (Serie A) - 36.0 (Bundesliga) [1] | StatsBomb 15-16 31.5-35.2 [19] | §2.1, §2.2, §6 |
| Blocked, % of shots (`BlockedPercent`) | 20-30 | 20-30 | n/a (3 leagues missing) | LaLiga 25.3, Serie A 26.9 [4]; StatsBomb 15-16 22.6-29.1 [19] | §2.2 |
| Inside the box, % of shots (`InsideBoxPercent`) | 60-72 | 60-72 | n/a (4 leagues missing) | PL 68.3 (**derived** 100 − 31.7) to 31 Mar 2025 [10]; top-five mean 66.4 as of 18 Oct 2024 [5] | §2.2 |
| Headed shots per match (`HeadedShots`) | 3-5 | 3-5 | n/a (Serie A and Bundesliga only 2024-25 as of 18 Oct 2024, PL season not named, LaLiga only 2023-24, Ligue 1 only a bound) | Serie A 4.23, Bundesliga 4.19 (2024-25 as of 18 Oct 2024); PL 4.1 (as of 18 Oct 2024, season not named); LaLiga 4.03 (2023-24); Ligue 1 ≤ 3.68 [5] | §2.2 |
| Keeper save rate % (`SaveRatePercent`) | 65-75 | 65-75 | n/a (scale differs, §6) | derived 1 − goals ÷ SoT 66.0-68.3 [1], own goals included, not the §1.5 scale; StatsBomb 15-16 68.6-69.9 [19] | §2.1, §2.2 |
| Save rate inside the box % (`SaveRateInsideBoxPercent`) | 55-68 | 55-68 | n/a | StatsBomb 15-16 60.7-61.9 [19] | §2.2 |
| Save rate outside the box % (`SaveRateOutsideBoxPercent`) | 78-90 | 78-90 | n/a | StatsBomb 15-16 86.4-87.5 [19] | §2.2 |
| Passes attempted per team (`PassesPerTeam`) | 380-520 | 380-520 | n/a (3 leagues missing) | PL 486.7, LaLiga 477.9 (FBref) [4]; PL 446.7 (Opta) [7] | §2.3 |
| Pass accuracy % (`PassAccuracyPercent`) | 78-86 | 78-86 | n/a (3 leagues missing) | PL 81.4, LaLiga 80.1 [4] | §2.3 |
| Passes per open-play sequence (`PassesPerSequence`) | 3.0-5.0 | 3.0-5.0 | n/a (no league mean) | teams 2.1 (Getafe, LaLiga to 29 Nov 2024) [12] - 5.1 (Man City, PL to 15 Apr 2025) [6] | §2.4 |
| 10+ pass open-play sequences per team (`TenPlusSequencesPerTeam`) | 6-14 | 6-14 | n/a (no league mean) | PL teams 5.1-17.3, to 15 Apr 2025 (÷ 38, lower bounds, §2.4) [6] | §2.4 |
| Direct speed, open play, m/s (`DirectSpeed`) | 1.3-1.8 | 1.3-1.8 | n/a (no league mean) | PL teams 1.4-2.1, to 15 Apr 2025 [6] | §2.4, §8 |
| PPDA, Understat zone (`Ppda`) | 9-14 | 9-14 | n/a (action set not established, §6) | Understat leagues 11.24-13.05 [2] | §1.3, §1.5, §2.4 |
| Crosses per team (`CrossesPerTeam`) | 12-20 | 12-20 | n/a (scale not established, §6) | FBref 17.2-18.2 [4]; StatsBomb 15-16 11.9-12.7 [19] | §1.4, §2.3 |
| Corners per match (`Corners`) | 9-11 | 9-11 | 9.31 (Serie A) - 10.30 (PL) [1]; 2025-26: 8.82 (Serie A) - 10.00 (PL) [1] | - | §2.1, §6 |
| Fouls per match (`Fouls`) | 20-27 | 20-27 | 21.42 (Bundesliga) - 25.00 (Serie A) [1]; 2025-26: 20.85 (Bundesliga) - 25.35 (Serie A) [1] | - | §2.1, §6 |
| Ball in play, min (`BallInPlayMinutes`) | 53-59 | 53-59 | 53.40 (LaLiga, CIES 53:24, as published 21 May 2025) - 56.98 (PL, Opta 56:59, full season) [21][9] | CIES only: 53:24-56:40 = 53.40-56.67 [21] | §2.5 |
| Distance per outfield player, km (`DistancePerOutfieldPlayerKm`) | 9.5-11.5 | 9.5-11.5 | n/a | 31 leagues 2020-21: about 10.0 (**derived** 99.9 ÷ 10) [27] | §2.5 |

## 6. Band changes vs spec R2

Rule (spec R1): a band may differ from its R2 starting value only when narrowed or shifted to the
top-five envelope ±5%. Applied here as:
- **narrowing**: a bound moves inward but never past its limit (new min ≤ envelope low × 0.95,
  new max ≥ envelope high × 1.05), so the band still holds every top-five league figure ±5%;
- **shifting**: a bound moves outward to at most its limit (the same envelope ±5%);
- **rounding**: a narrowed bound is rounded outward (away from the band's centre), a shifted bound
  inward, so that the rounded value never passes the limit;
- **no envelope, no change**: where the envelope is n/a (§5), no limit exists and the band stays
  at R2. Team-level or other-season figures never stand in for the envelope. Any doubt about
  scale counts as n/a.

The rule allows a change; it does not require one. A band was changed only where a narrowing was
proposed during research and the envelope is on the band's own scale.

Envelope figures are top five only. For the bands set from football-data [1] (goals, shots, on
target, corners, fouls) both complete seasons are checked, 2024-25 and 2025-26 (§2.1), and a
changed bound must meet its limit in both, that is the stricter of the two.

| Band | Old → new | Envelope and limit | Check | Source |
|---|---|---|---|---|
| Shots per match | 23-28 → 23-27.9 (max narrowed; min unchanged) | 2024-25: 23.69 (LaLiga) - 25.92 (PL), max limit 25.92 × 1.05 = 27.216. 2025-26: 24.66 (Serie A) - 26.51 (Bundesliga), max limit 26.51 × 1.05 = 27.836 | 27.9 ≥ 27.836 ≥ 27.216 (step 0.1, rounded outward); 27.9 < 28, so still a narrowing | [1]; [1]'s shots read as blocked-inclusive (§1.4), as §1.5 counts them; Understat's 2024-25 envelope 23.76-25.91 [2] differs from [1]'s by at most 0.07 per match at its ends; per league the gap reaches 0.23 (Ligue 1 24.69 vs 24.92, §2.1) |

Check of the other [1] bands against both seasons. These bounds are the R2 starting values,
unchanged, so the rule sets no limit on them; the arithmetic is given so that any later change
starts from it (low × 0.95 / high × 1.05):

| Band | 2024-25 envelope → limits | 2025-26 envelope → limits | Every top-five mean inside the band? |
|---|---|---|---|
| Goals 2.5-3.2 | 2.56 - 3.13 → 2.432 / 3.287 | 2.43 - 3.24 → 2.309 / 3.402 | 2024-25 yes; 2025-26 no: Serie A 2.43 < 2.5, Bundesliga 3.24 > 3.2 |
| Shots 23-27.9 (min) | 23.69 → 22.506 | 24.66 → 23.427 | yes, both seasons |
| On target 30-38% | 33.6 - 37.7 → 31.92 / 39.585 | 32.7 - 36.0 → 31.065 / 37.8 | yes, both seasons |
| Corners 9-11 | 9.31 - 10.30 → 8.845 / 10.815 | 8.82 - 10.00 → 8.379 / 10.5 | 2024-25 yes; 2025-26 no: Serie A 8.82 < 9 |
| Fouls 20-27 | 21.42 - 25.00 → 20.349 / 26.25 | 20.85 - 25.35 → 19.808 / 26.618 | yes, both seasons |

Goals and corners are kept at R2 although two 2025-26 league means fall just outside: the rule
allows shifting a bound outward to its limit but does not require it (§8).

Spec R2 note "Opta Analyst 2024-25: shots 24-27.4": the same two figures appear in [5] (18 Oct
2024, "a few matchdays in"): LaLiga 24.0 shots per match, the other four leagues "all beyond 26",
the Bundesliga 27.4. They are early-season figures, not complete seasons; the complete-season
top-five means are 23.69-25.92 (2024-25) and 24.66-26.51 (2025-26) [1]. The max 27.9 holds both
seasons + 5% and also covers the early-season 27.4.

Considered and kept at the R2 starting value:
- **Crosses per team 12-20** (15-20 proposed). The FBref count [4] may include crossed set pieces
  (§1.4), while the harness counts crosses with corner kicks and throw-ins excluded (§1.5), so the
  FBref figures 17.2-18.2 are not shown to be on the harness scale; StatsBomb's narrower flag reads
  11.9-12.7 (2015-16) [19]. Scale not established: envelope n/a, 12-20 kept.
- **Keeper save rate 65-75%** (65-72 proposed). 2024-25 is only available derived as 1 − goals ÷
  SoT, 66.0 (Bundesliga) - 68.3 (Serie A) [1], whose goals include own goals (§2.1): not on the
  scale of §1.5, envelope n/a, so 75 stays (no envelope, no change). Context only, since another
  season never stands in for the envelope: the only figures on the §1.5 definition are StatsBomb
  2015-16 (saved ÷ (saved + goals), penalties included, §2.2) [19], max 69.9 (Ligue 1), and
  69.9 × 1.05 = 73.395 ≈ 73.4 would not allow 72 either.
- **PPDA 9-14** (10-14 proposed). Understat's zone gives 11.24 (LaLiga) - 13.05 (Bundesliga) in
  2024-25 and 11.01 (LaLiga) - 13.11 (Bundesliga) in 2025-26 [2]; min limits 11.24 × 0.95 = 10.678
  and 11.01 × 0.95 = 10.460, so 10 would pass the arithmetic. But the harness's action set
  (§1.5) is a convention, and Understat's own set is not stated in the sources reached: scale not
  established, 9-14 kept.
- **On target 30-38%** (32-38 suggested). Min limits 33.6 × 0.95 = 31.92 (2024-25) and 32.7 ×
  0.95 = 31.065 (2025-26); a narrowed min must stay ≤ the limit, so 32 does not qualify and 30
  stays.
- **Ball in play 53-59 min** (53-58 proposed). Max limit 56.98 × 1.05 = 59.83 (Opta PL), or
  56.67 × 1.05 = 59.50 on CIES alone; a narrowed max must stay ≥ the limit, so 58 does not
  qualify and 59 stays.
- **Inside the box 60-72%** (62-72 proposed), **headed shots 3-5** (3.5-4.5 suggested), **save rate
  inside the box 55-68%** (57-66 proposed), **save rate outside the box 78-90%** (82-90 proposed):
  envelope n/a (§5), kept.
- **Passes per sequence 3.0-5.0** (3.0-4.0 proposed), **10+ pass sequences 6-14**, **direct speed
  1.3-1.8 m/s** (1.5-2.0 proposed): no 2024-25 league mean is published (§2.4), envelope n/a,
  kept. Direct speed: see §8.
- **Goals, blocked, passes per team, pass accuracy, corners, fouls, distance**: no change
  proposed, kept at R2. Goals and corners: see the 2025-26 check above.

## 7. Other requirements

- **R3, quality tier.** 2024-25, second tier vs top tier [1]: goals Championship 2.45 vs PL 2.93,
  Serie B 2.46 vs Serie A 2.56, 2. Bundesliga 3.02 vs Bundesliga 3.13; SoT% 32.6 vs 35.1, 31.8 vs
  33.6, 34.8 vs 35.8; fouls Serie B 29.68 vs Serie A 25.00. R3's goals target 2.3-2.9 holds for
  two of the three second tiers in both complete seasons: Championship 2.45 (2024-25) and 2.61
  (2025-26), Serie B 2.46 and 2.56. 2. Bundesliga is above it in both: 3.02 (2024-25) and 2.93
  (2025-26) [1] (§8). Ball in play, pass accuracy and long-ball share for the second tiers are n/a.
  Primeira Liga's 50:44 ball in play sits 2:40-5:56 under the top five's 53:24-56:40
  (**derived**: 53:24 − 50:44 and 56:40 − 50:44, CIES, "2024/25" as published on 21 May 2025, §2.5)
  [21].
- **R9, passing chains.** StatsBomb 2015-16 [19]: open-play shots after 3+ passes 62.6-66.1%, after
  0-1 passes 21.8-24.7%. These count passes in StatsBomb possessions, not the harness scale;
  direction only (§1.1). What they show: in every league reached most open-play shots follow 3+
  passes and fewer than a quarter follow 0-1, the direction R9 asks for. What they cannot show:
  whether R9's thresholds (≥ 40%, ≤ 35%, ≤ 25% without rebounds and high turnovers) hold on the
  harness's Opta-style sequence. A possession survives brief opponent touches (§1.1), so it holds
  the sequence that ends with the shot and can only count as many passes before it or more: on the
  harness scale the 3+ share can be lower and the 0-1 share higher than these figures. No Opta
  league figure for passes before a shot was reached: n/a. Counter shots ("From Counter", also a
  StatsBomb possession pattern, direction only)
  4.5-4.9% of open-play shots [19]; Opta shot-ending fast breaks 1.5-2.1 per match in 2024-25 as of 18 Oct 2024 [5].
- **Movement (R4, R6, R8).** Max acceleration 0-5 m in pro players 4.98 ± 0.26 m/s² (faster group),
  4.39 ± 0.23 (slower) [33]. PL 2024-25 top speeds after 29 matchweeks (25 Mar 2025) run from 29.4 km/h
  (Bernardo Silva, the slowest outfield player) to 37.1 km/h (Van de Ven) [26]; Bundesliga 2024-25
  full season max 37.16 km/h [25]. Purposeful
  movement 40.6 ± 10.0% of match time [45]. Goalkeepers cover 5,611 ± 613 m per match [32].

## 8. Open notes for the user

- **Direct speed band looks low.** Team-level data reaches well above the R2 band
  1.3-1.8 m/s: PL 2024-25 teams to 15 Apr 2025 run from 1.4 (Man City, slowest) to 2.1 m/s
  (Forest, fastest) [6], and Getafe reached 2.36 m/s in LaLiga 2024-25 to 29 Nov 2024 [12]; the
  Bundesliga's slowest side in 2023-24 was already at 1.71 m/s [17]. This suggests the band is low, but with no published 2024-25 league
  mean the spec rule cannot move the band (§6). Raising it needs a spec change.
- **Gegenpress high-turnover anchor.** R19's ≥ 1.4x Balanced is met by Leverkusen 2023-24 (1.44x
  or 1.59x, depending on the season of [5]'s league mean), a vertical tiki-taka example, but is not
  shown for the gegenpress examples (Bournemouth ≥ 1.07x, a lower bound) (§3.2). #94 may need it
  judged with the user.
- **Goals and corners in 2025-26.** On the complete 2025-26 season [1], Serie A's 2.43 goals and
  8.82 corners per match and the Bundesliga's 3.24 goals fall just outside the R2 bands 2.5-3.2 and
  9-11, which hold every 2024-25 top-five mean (§6). The rule would allow shifting those bounds
  outward up to their limits (goals 2.309-3.402, corners min 8.379); whether to do so is left to
  the user.
- **R3 goals target vs 2. Bundesliga.** R3 asks for 2.3-2.9 goals per match between second-tier
  sides. The Championship (2.45, 2.61) and Serie B (2.46, 2.56) sit inside it in 2024-25 and
  2025-26, but 2. Bundesliga sits above it in both, 3.02 and 2.93 [1] (§7). The target fits two of
  the three second tiers reached, not all; whether to keep it is left to the user (#73, #83).
- **R19 directions per style.** No per-metric direction table is given; R19's directions are left
  to #94, read from §3.1 with the user.

## Sources

- [1] football-data.co.uk match files 2024-25 and 2025-26 (also `I1, SP1, D1, F1, N1, P1, I2, E1, D2` and `/2526/`): https://www.football-data.co.uk/mmz4281/2425/E0.csv ; column notes: https://www.football-data.co.uk/notes.txt
- [2] Understat league data (my computation; also `/La_liga/`, `/Bundesliga/`, `/Serie_A/`, `/Ligue_1/`, seasons 2014-2025, i.e. 2014-15 to 2025-26): https://understat.com/league/EPL/2024
- [4] FBref (Opta) 2024-25 squad tables, Wayback captures 2025-08 to 2025-12 (also `passing_types`, `misc`, `possession`, `shooting`, `keepersadv`, `defense` for comps 9, 12, 11, 20, 13, 23, 32, 10, 33): https://web.archive.org/web/20251116222941/https://fbref.com/en/comps/9/2024-2025/passing/2024-2025-Premier-League-Stats
- [5] Opta Analyst, "How Do Playing Styles Change Across the Top European Leagues?" (18 Oct 2024): https://theanalyst.com/articles/playing-styles-top-five-european-leagues-stats
- [6] Opta Analyst, "Analysing Premier League Playing Styles in 2024-25" (15 Apr 2025; "six games remaining"): https://theanalyst.com/articles/analysing-premier-league-playing-styles-2024-25
- [7] Opta Analyst, "More Long Balls, Fewer High Turnovers... Are Premier League Teams Going More Direct?" (24 Sep 2025; after 50 matches): https://theanalyst.com/articles/premier-league-2025-26-more-direct-football
- [8] Opta Analyst, "Premier League Tactical Trends... 2025-26" (16 Jan 2026; after 210 matches): https://theanalyst.com/articles/premier-league-teams-still-more-direct-2025-26
- [9] Opta Analyst, "Ball in Play: Are We Seeing Less Football in the Premier League This Season?" (13 Oct 2025; after 70 matches): https://theanalyst.com/articles/premier-league-ball-in-play-are-we-seeing-less-football-2025-26
- [10] Opta Analyst, "Finding Their Range: Where Premier League Teams Are Shooting From in 2024-25" (31 Mar 2025): https://theanalyst.com/articles/premier-league-2024-25-shot-data
- [11] Opta Analyst, "Opta Football Stats Definitions": https://theanalyst.com/articles/opta-football-stats-definitions
- [12] Opta Analyst, "The Curious Tactics of José Bordalás and Getafe" (29 Nov 2024): https://theanalyst.com/articles/getafe-jose-bordalas-tactics-la-liga
- [13] Opta Analyst, "Bundesliga: How Does Each Team Play?" (3 Feb 2022): https://theanalyst.com/articles/bundesliga-how-does-each-team-play
- [14] Opta Analyst, "What is Roberto De Zerbi's Style of Play..." (4 Apr 2026): https://theanalyst.com/articles/roberto-de-zerbi-tottenham-style-of-play-tactics-stats
- [15] Opta Analyst, "One Stat to Sum Up Each Premier League Club's 2024-25 Season" (29 May 2025): https://theanalyst.com/articles/premier-league-one-stat-every-club-2024-25-season
- [16] Premier League / Opta, "Analysis: Why Wood is the unlikely star for fast-attacking Forest" (10 Feb 2025): https://www.premierleague.com/en/news/4243624
- [17] Opta Analyst, "Xabi Alonso Can Be a Success at Chelsea..." (20 May 2026; Leverkusen 2023-24): https://theanalyst.com/articles/xabi-alonso-chelsea-manager-bayer-leverkusen-real-madrid-stats
- [18] "Technical and tactical evolution of the offensive team sequences in LaLiga between 2008 and 2021", Biology of Sport 2024 (Mediacoach/Opta): https://pmc.ncbi.nlm.nih.gov/articles/PMC10955746/
- [19] StatsBomb Open Data (my computation; competitions 2/27, 11/27, 12/27, 7/27, 9/281): https://github.com/statsbomb/open-data
- [20] ilNapolista citing Gazzetta dello Sport, Serie A effective time (12 Sep 2025): https://www.ilnapolista.it/2025/09/serie-a-allarme-tempo-effettivo-si-gioca-meno-che-in-qualsiasi-altro-top-campionato/
- [21] Sindicato dos Jogadores citing Observatório do Futebol (CIES), effective playing time 2024-25 (21 May 2025): https://sjogadores.pt/?pt=news&op=OP_SHOW_DETAIL&id=13901
- [22] El Diario Alerta, "El Real Madrid lidera el tiempo efectivo de juego en LaLiga 2024-2025" (27 May 2025): https://www.eldiarioalerta.com/articulo/futbol/real-madrid-lidera-tiempo-efectivo-juego-laliga-ea-sports-2024-2025/20250527200216562372.html
- [23] Bundesliga club distance 2024-25: https://www.bundesliga.com/en/bundesliga/stats/clubs/distance/2024-2025
- [24] 2. Bundesliga club distance 2024-25: https://www.bundesliga.com/en/2bundesliga/stats/clubs/distance/2024-2025
- [25] Bundesliga, "The five fastest players from the 2024/25 season" (30 May 2025): https://www.bundesliga.com/en/bundesliga/news/five-fastest-players-2024-25-bahoya-openda-conteh-holtmann-burke-32466
- [26] Premier League / Opta Analyst, "Who tops the 2024/25 Premier League running charts?" (25 Mar 2025; after 29 matchweeks): https://www.premierleague.com/en/news/4272290
- [27] CIES Football Observatory Monthly Report 68 (SkillCorner, 2020-21): https://football-observatory.com/IMG/sites/mr/mr68/en/
- [32] Di Salvo et al. 2008, J Sports Med Phys Fitness 48(4):443-6: https://pubmed.ncbi.nlm.nih.gov/18997646/
- [33] Loturco et al. 2019, PLOS ONE (max acceleration 0-5 m): https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0216806
- [34] Rico-González et al. 2022, Biol Sport 39(1):110-114: https://pubmed.ncbi.nlm.nih.gov/35173369/
- [35] FIFA Training Centre, "Controlling the game without the ball: the mid-block and compactness" (WC 2022): https://www.fifatrainingcentre.com/en/fwc2022/technical-and-tactical-analysis/controlling-the-game-without-the-ball--the-mid-block-and-compactness.php
- [36] FIFA Training Centre, "Goalkeeper height and connection with the defensive line": https://www.fifatrainingcentre.com/en/fwc2022/technical-and-tactical-analysis/goalkeeper-height-and-connection-with-the-defensive-line.php
- [37] FIFA Training Centre, "Technical and tactical overview": https://www.fifatrainingcentre.com/en/fwc2022/technical-and-tactical-analysis/technical-tactical-overview.php
- [38] Wikipedia, "Formation (association football)": https://en.wikipedia.org/wiki/Formation_(association_football)
- [39] Coaches' Voice, "Formations: football tactics explained": https://learning.coachesvoice.com/cv/formations-football-tactics-explained-best-most-used/
- [40] Coaches' Voice, "The 4-2-3-1: football tactics explained": https://learning.coachesvoice.com/cv/the-4-2-3-1-football-tactics-pochettino-guardiola-flick-southgate/
- [41] UEFA, "Europa League performance insights: How Atalanta's high block disrupted Liverpool's build-up play": https://www.uefa.com/uefaeuropaleague/news/028c-1aa8b95a0bda-bbbf978b7f3f-1000--europa-league-performance-insights-how-atalanta-s-high-b/
- [42] Bundesliga, "Where the Bundesliga was best in Europe in 2024/25" (12 Jun 2025): https://www.bundesliga.com/en/bundesliga/news/how-germany-compares-to-europe-s-other-top-leagues-2024-25-goals-attendance-32632
- [45] Bloomfield, Polman, O'Donoghue 2007, J Sports Sci Med 6:63-70: https://www.jssm.org/hf.php?id=jssm-06-63.xml
- [46] Opta Analyst, "Gian Piero Gasperini Leaves Atalanta: A Nine-Year Legacy" (4 Jun 2025): https://theanalyst.com/articles/gian-piero-gasperini-leaves-atalanta-stats
- [47] Coaches' Voice, "The 4-5-1 formation: football tactics explained": https://learning.coachesvoice.com/cv/4-5-1-formation-football-tactics/

[1]: https://www.football-data.co.uk/mmz4281/2425/E0.csv
[2]: https://understat.com/league/EPL/2024
[4]: https://web.archive.org/web/20251116222941/https://fbref.com/en/comps/9/2024-2025/passing/2024-2025-Premier-League-Stats
[5]: https://theanalyst.com/articles/playing-styles-top-five-european-leagues-stats
[6]: https://theanalyst.com/articles/analysing-premier-league-playing-styles-2024-25
[7]: https://theanalyst.com/articles/premier-league-2025-26-more-direct-football
[8]: https://theanalyst.com/articles/premier-league-teams-still-more-direct-2025-26
[9]: https://theanalyst.com/articles/premier-league-ball-in-play-are-we-seeing-less-football-2025-26
[10]: https://theanalyst.com/articles/premier-league-2024-25-shot-data
[11]: https://theanalyst.com/articles/opta-football-stats-definitions
[12]: https://theanalyst.com/articles/getafe-jose-bordalas-tactics-la-liga
[13]: https://theanalyst.com/articles/bundesliga-how-does-each-team-play
[14]: https://theanalyst.com/articles/roberto-de-zerbi-tottenham-style-of-play-tactics-stats
[15]: https://theanalyst.com/articles/premier-league-one-stat-every-club-2024-25-season
[16]: https://www.premierleague.com/en/news/4243624
[17]: https://theanalyst.com/articles/xabi-alonso-chelsea-manager-bayer-leverkusen-real-madrid-stats
[18]: https://pmc.ncbi.nlm.nih.gov/articles/PMC10955746/
[19]: https://github.com/statsbomb/open-data
[20]: https://www.ilnapolista.it/2025/09/serie-a-allarme-tempo-effettivo-si-gioca-meno-che-in-qualsiasi-altro-top-campionato/
[21]: https://sjogadores.pt/?pt=news&op=OP_SHOW_DETAIL&id=13901
[22]: https://www.eldiarioalerta.com/articulo/futbol/real-madrid-lidera-tiempo-efectivo-juego-laliga-ea-sports-2024-2025/20250527200216562372.html
[23]: https://www.bundesliga.com/en/bundesliga/stats/clubs/distance/2024-2025
[24]: https://www.bundesliga.com/en/2bundesliga/stats/clubs/distance/2024-2025
[25]: https://www.bundesliga.com/en/bundesliga/news/five-fastest-players-2024-25-bahoya-openda-conteh-holtmann-burke-32466
[26]: https://www.premierleague.com/en/news/4272290
[27]: https://football-observatory.com/IMG/sites/mr/mr68/en/
[32]: https://pubmed.ncbi.nlm.nih.gov/18997646/
[33]: https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0216806
[34]: https://pubmed.ncbi.nlm.nih.gov/35173369/
[35]: https://www.fifatrainingcentre.com/en/fwc2022/technical-and-tactical-analysis/controlling-the-game-without-the-ball--the-mid-block-and-compactness.php
[36]: https://www.fifatrainingcentre.com/en/fwc2022/technical-and-tactical-analysis/goalkeeper-height-and-connection-with-the-defensive-line.php
[37]: https://www.fifatrainingcentre.com/en/fwc2022/technical-and-tactical-analysis/technical-tactical-overview.php
[38]: https://en.wikipedia.org/wiki/Formation_(association_football)
[39]: https://learning.coachesvoice.com/cv/formations-football-tactics-explained-best-most-used/
[40]: https://learning.coachesvoice.com/cv/the-4-2-3-1-football-tactics-pochettino-guardiola-flick-southgate/
[41]: https://www.uefa.com/uefaeuropaleague/news/028c-1aa8b95a0bda-bbbf978b7f3f-1000--europa-league-performance-insights-how-atalanta-s-high-b/
[42]: https://www.bundesliga.com/en/bundesliga/news/how-germany-compares-to-europe-s-other-top-leagues-2024-25-goals-attendance-32632
[45]: https://www.jssm.org/hf.php?id=jssm-06-63.xml
[46]: https://theanalyst.com/articles/gian-piero-gasperini-leaves-atalanta-stats
[47]: https://learning.coachesvoice.com/cv/4-5-1-formation-football-tactics/
