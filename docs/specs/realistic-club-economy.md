# Realistic club economy by nation, division and club stature

Status: approved
Repo: FabboCassa/FootballTeamSimulator   Base branch: main

## Goal
Today a club's money depends only on its division number: a top flight in Iceland earns what the
Premier League earns, and inside one league clubs differ only through stadium tier and prize money.
The market also only covers the playable divisions of the user's own nation. The goal is a
world economy that reads like real football: the big five are richer than the rest, the big clubs
of a league are richer than its small clubs, lower divisions are clearly poorer, and a Serie B
club cannot buy or pay the players a Serie A club buys. Money is calibrated against real
2023-25 figures (Deloitte ARFF, DFL, EFL, UEFA ECFIL), with distances deliberately compressed so
lower divisions stay playable.

### Real reference figures (average revenue per club)
| Nation | Tier 1 | Tier 2 | Tier 3 |
|---|---|---|---|
| England | ~€405M | Championship ~€47M (wages ~93%) | League One ~€10.5M |
| Germany | ~€236M | 2. Bundesliga ~€58M | — |
| Spain | ~€205M | Hypermotion ~€20-25M | — |
| Italy | ~€150M (Inter/Juve >€500M) | Serie B wage bills €5-33M | Serie C a few €M |
| France | ~€122M | Ligue 2 budgets €9-15M | — |
| Others | Brazil ~€68M, MLS ~€76M, Scotland ~€31M | | |

Real ratio tier2/tier1 is 10-25%; this spec compresses it to ~35% (tier 3 ~12%).

## Scope
In:
- Nation wealth multiplier driven by a per-nation economic reputation.
- Division wealth multiplier (compressed real ratios).
- Persistent per-club stature driving intra-league wealth, evolving slowly.
- Revenue, wages, starting cash and transfer budgets derived from nation × division × stature.
- Wages set by the paying club; a player asks more to join a richer club.
- Player market value depends on the league (nation + division) he plays in.
- Worldwide transfer market: every club in the world (playable, background, data-only) buys and sells.
- Simplified estimated finances for data-only clubs.
- Prestige-based transfer refusal.
- Single-player career and online private leagues (server).
- Balance harness checks for every acceptance band below.

Out:
- Ranked ladder (keeps flat equal budgets and flat auction start price).
- Migration of existing saves (old careers are refused; the user restarts).
- Loans, release clauses, agent fees, parachute payments, FFP rules, club bankruptcy.
- Real club names or real per-club data.
- Match engine changes (golden master must not move).

## Users / flows
1. Career setup: the user picks a nation/division/club; the world is generated with stature per
   club and seeded finances for every club in the world.
2. Hub/Club screen: the user sees revenue, wages, cash and transfer budget consistent with his
   club's nation, division and stature.
3. Market: the user browses players from any club in the world; buys are limited by budget,
   by the wage the player demands from his club, and by prestige refusal (reason shown).
4. AI windows: every club in the world trades in the two season windows.
5. Season end: stature moves slightly with finish/promotion/relegation/title; promotion and
   relegation change revenue immediately through the division multiplier.
6. Online private league: draft keeps equal squads and equal starting budget; from then on
   income and wages follow each club's stature and results.

## Requirements
- R1 Nation wealth. Each nation has an `EconomicReputation` (defaults to its sporting
  reputation; atlas overrides so the curve can order Italy below Germany/Spain). A continuous
  integer curve maps it to a tier-1 revenue multiplier. (acceptance: harness prints mean tier-1
  revenue per nation; relative to England = 100%: Spain and Germany 55-75%, Italy 42-58%,
  France 38-52%, Portugal/Netherlands/Brazil 18-32%, Scotland/Switzerland 8-16%, a nation with
  economic reputation 50 at 2-6%; strictly monotone in economic reputation.)
- R2 Absolute anchor. (acceptance: England tier-1 mean club revenue €340-460M; Italy tier-1
  €170-230M; Italy tier-2 €55-85M; Italy tier-3 €18-30M.)
- R3 Division ratio. (acceptance: for every nation with ≥2 tiers, mean tier-2 revenue is 28-42%
  of tier 1 and mean tier-3 revenue is 9-15% of tier 1.)
- R4 Club stature. Every club has a persistent stature (0-100) assigned at generation,
  correlated with but not equal to squad strength. (acceptance: within every tier-1 league with
  ≥16 clubs, richest club revenue is 2.4-3.6× the league mean and poorest is 0.3-0.5×; Spearman
  rank correlation stature↔revenue ≥ 0.9; two clubs with equal strength but different stature
  have different revenue.)
- R5 Stature evolution. Stature changes only at season end, capped per season, from finish vs
  expectation, promotion/relegation and titles. (acceptance: a club winning its league every
  season needs ≥5 seasons to move from league-median stature to top-3 stature; no club's stature
  moves more than the configured cap in one season; deterministic for a given seed.)
- R6 Revenue model. Gate, commercial and prize income all scale by nation × division ×
  stature; data-only clubs receive an estimated annual revenue paid weekly, with no gate or prize.
  (acceptance: over a simulated season every club's revenue equals the sum of its components;
  data-only clubs' mean revenue is within ±15% of a playable club with the same nation, division
  and stature.)
- R7 Wages by paying club. A player's wage is set by the club that employs him (its wage
  structure = nation × division × stature), not only by his value; moving to a richer club raises
  his demand. (acceptance: the same player priced at a tier-1 Italian club earns ≥2× what he earns
  at a tier-2 Italian club; wage/revenue ratio medians: tier 1 55-70%, tier 2 72-88%, tier 3
  78-92%; zero clubs insolvent (board floor still applies).)
- R8 Player value by league. Market value includes a league multiplier from nation economic
  reputation and division. (acceptance: an identical player is worth more in England tier 1 than
  in Scotland tier 1 and more in Italy tier 1 than Italy tier 2; value stays within the existing
  [MinValue, MaxValue] clamp; 10k-player distribution printed.)
- R9 Budget realism. Transfer budgets come from cash + board grant scaled by the same factors.
  (acceptance: median tier-2 Italian budget is ≤25% of the median tier-1 Italian budget; in a
  simulated season no tier-2 club signs a player whose value exceeds the 90th percentile of
  tier-1 player values of its own nation.)
- R10 Worldwide market. All clubs in the world take part in AI windows and appear in the user's
  market search. (acceptance: after one window, transfers include at least one cross-nation deal
  and at least one data-only club as buyer or seller; money is conserved across the world.)
- R11 Prestige refusal. Prestige = f(league wealth, club stature). A player refuses a buying
  club whose prestige is below a threshold relative to his current club and his own level;
  players aged ≤21 or ≥32 and players outside the best XI have a lower threshold.
  (acceptance: a top player of a tier-1 top club refuses a tier-2 buyer with enough budget; the
  same buyer can sign a bench player from that club; the negotiation screen shows a localized
  refusal reason (en+it); AI transfers never violate the rule.)
- R12 Private leagues. Server private leagues keep equal draft budgets but apply stature-based
  income and wages on each resolved round. (acceptance: Api test — after the draft all budgets
  are equal; after N rounds two clubs with different stature have different revenue; ranked
  worlds are unchanged (existing ranked tests green).)
- R13 Save format. New save version; older careers are refused with a localized "incompatible
  save, start a new career" message. (acceptance: loading a pre-change save shows the message
  and does not crash; a new career saves and reloads with identical finances and stature.)
- R14 Determinism. All new models are pure, integer math, no new shared RNG streams in the match
  path. (acceptance: golden master `0x5EF1EDDAFA52BAFA` unchanged; two worlds from the same seed
  produce identical stature, finances and transfer records.)

## Tech decisions
- Stack/libs: `shared/Sim.Core` (netstandard2.1, no packages), Unity client, ASP.NET Core server.
- Tunables in `Config/BalanceConfig.cs` (`FinanceBalance`, `MarketBalance`, `TransferBalance`,
  new stature/prestige blocks); structural curves in code, magnitudes in config.
- Data/storage: `NationProfile.EconomicReputation`, `Club.Stature` persisted in the career save
  (version bump) and in the server `clubs` table (EF migration).
- Worldwide market uses `WorldPlayerIndex` for candidate search to meet the time budget.
- Build cmd: `.\tools\build-simcore.ps1`  Test cmd: `dotnet test` and
  `.\tools\balance.ps1 -Scenario economy` (new checks for R1-R10).

## Constraints / non-functional
- Performance: one worldwide transfer window on the Medium preset resolves in ≤2 s on desktop
  (measured in the harness); WebGL/mobile may take 3-5× behind a loading indicator.
- Determinism rules of Sim.Core (no `Math.Pow/Exp/Log`, no DateTime, only `Pcg32`).
- All UI strings localized en+it at parity.
- Views stay dumb (no Sim.Core refs).

## Open risks
- Compressed ratios still leave lower divisions thin: promoted clubs may be unable to compete;
  tune through the division multiplier, not by special cases.
- Worldwide market volume can explode or stall with ~1,000+ clubs; per-club signing caps may
  need retuning.
- Data-only clubs selling freely could drain the playable world of talent or flood it.
- Wage-by-club changes can shift the difficulty levers measured in 10.1; re-run
  `balance.ps1 -Scenario difficulty`.

## Tasks (filled by /ba:issues)
| # | Title | Depends on | Requirements | Issue |
|---|-------|------------|--------------|-------|
| 1 | Nation and division wealth multipliers | — | R1, R2, R3 | #1 |
| 2 | Persistent club stature and intra-league wealth spread | 1 | R4 | #2 |
| 3 | Season-end stature evolution | 2 | R5 | #3 |
| 4 | Estimated finances for every club incl. data-only | 2 | R6 | #4 |
| 5 | Wages set by the paying club | 2 | R7 | #5 |
| 6 | Player market value by league | 1 | R8 | #6 |
| 7 | Realistic transfer budgets | 4, 5 | R9 | #7 |
| 8 | Worldwide AI transfer market within 2 s | 4, 6, 7 | R10 | #8 |
| 9 | Prestige-based transfer refusal | 8 | R11 | #9 |
| 10 | Client: new save version, world-wide seeding, incompatible save message | 3, 4, 7 | R13 | #10 |
| 11 | Client: world-wide market search and refusal reason | 9, 10 | R10, R11 | #11 |
| 12 | Server: stature-based income for private leagues | 2, 5 | R12 | #12 |
| 13 | World-economy determinism and golden master guard | 3, 8 | R14 | #13 |
