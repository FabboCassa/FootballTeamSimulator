namespace Sim.Core.Config
{
    /// <summary>
    /// THE single home for every gameplay tunable (ARCHITECTURE.md 4.1 rule 4).
    /// Plain POCO: hosts load/override it from JSON (server can push updates,
    /// the client ships it embedded). Code defaults below ARE the baseline balance.
    /// Sim.Core never reads files - it only receives this object.
    /// </summary>
    public sealed class BalanceConfig
    {
        /// <summary>Bumped whenever a change invalidates stored seeds/replays.</summary>
        public int Version { get; set; } = 1;

        public GenerationBalance Generation { get; set; } = new GenerationBalance();
        public MatchBalance Match { get; set; } = new MatchBalance();
        public SeasonBalance Season { get; set; } = new SeasonBalance();
        public TacticsBalance Tactics { get; set; } = new TacticsBalance();
        public ConditionBalance Condition { get; set; } = new ConditionBalance();
        public DevelopmentBalance Development { get; set; } = new DevelopmentBalance();
        public SupportBalance Support { get; set; } = new SupportBalance();
        public MarketBalance Market { get; set; } = new MarketBalance();
        public TransferBalance Transfer { get; set; } = new TransferBalance();
    }

    /// <summary>
    /// Tunables for the valuation model (task 5.1) — the transfer price of a player from
    /// his attributes, age, potential, form, contract and league level (ARCHITECTURE.md
    /// §4.7). Pure and deterministic: <see cref="Market.ValuationModel"/> uses integer math
    /// and NO RNG, so a player's price is a stable function of his state (re-priced on a
    /// host cadence, not flickering daily).
    ///
    /// Shape of the model (structural patterns live in ValuationModel; only magnitudes here):
    ///   base = ValueUnitPerRatingCubed × (valueRating − RatingValueFloor)^ValueExponent,
    /// where valueRating = overall + a youth-scaled share of the remaining potential headroom
    /// (so young stars are dearer than equal-ability veterans). The base is then scaled by
    /// permille multipliers for age (value fades after the prime), short-term form, contract
    /// length (an expiring deal discounts the fee — he can leave cheap) and league level, and
    /// finally clamped to [<see cref="MinValue"/>, <see cref="MaxValue"/>] and rounded — so a
    /// price is always positive and never absurd (the 5.1 acceptance).
    ///
    /// Scale is calibrated against the wage anchor (WeeklyWage = overall² × WageFactor): a
    /// prime ~70-overall player lands near €15M, a ~90 top talent near €50M, squad filler a
    /// few hundred k. Currency units are abstract "game money".
    /// </summary>
    public sealed class MarketBalance
    {
        // --- Base value curve (convex in a potential-adjusted rating) ---
        /// <summary>Currency per (valueRating − RatingValueFloor)^ValueExponent. Tuned so a prime 70-overall ≈ €15M and the curve is steeply convex (top talents cost far more).</summary>
        public long ValueUnitPerRatingCubed { get; set; } = 230;
        /// <summary>Rating at/below which the base value is ~0 (only the MinValue floor remains). Amplifies the dynamic range so weak players are cheap and stars are dear.</summary>
        public int RatingValueFloor { get; set; } = 30;
        /// <summary>Exponent on the rating excess (3 = cubic convexity).</summary>
        public int ValueExponent { get; set; } = 3;
        /// <summary>Upper clamp on the potential-adjusted value rating before the curve (guards the base against overflow / runaway premiums).</summary>
        public int ValueRatingCap { get; set; } = 110;

        // --- Potential premium (young players priced toward their ceiling) ---
        /// <summary>Percent of the remaining headroom (potential − overall), itself scaled by the youth age curve, that is added to the value rating. 60 = a young prospect is valued well above his current overall; a peaked player gets none (his age-growth factor is 0).</summary>
        public int PotentialWeightPercent { get; set; } = 60;

        // --- Elite premium (the fat top tail: a few phenoms cost an order of magnitude more) ---
        // Real markets (Transfermarkt) are extremely top-heavy: the masses sit at a few M, top
        // stars at tens of M, and a handful of phenoms at €150-230M. Our overall scale is
        // compressed (top ~76), so the cubic base alone tops out too low — this premium makes the
        // rare elite value rating (high overall AND/OR a young high-potential prospect) explode,
        // while leaving everyone below the threshold untouched.
        /// <summary>Value rating above which the elite premium starts to apply (~the top division's quality). Below it the base curve is used as-is.</summary>
        public int EliteValueThreshold { get; set; } = 72;
        /// <summary>Extra value per value-rating point above the elite threshold, in 1/1000 (110 = +11% per point → a phenom is worth multiples of a merely good player). The MaxValue cap still bounds the very tip.</summary>
        public int ElitePremiumPermillePerPoint { get; set; } = 110;

        // --- Age value multiplier (resale value fades after the prime) ---
        /// <summary>Age from which transfer value starts to decline (independent of the development decline onset — a 30-year-old at his peak ability is still worth less than a 24-year-old).</summary>
        public int ValueDeclineOnsetAge { get; set; } = 29;
        /// <summary>Value lost per year beyond the onset age, in 1/1000 (90 = −9% per year).</summary>
        public int ValueDeclinePerMillePerYear { get; set; } = 90;
        /// <summary>Floor on the age multiplier, in 1/1000 (250 = a veteran never drops below 25% of his prime value on age alone).</summary>
        public int ValueAgeFloorPermille { get; set; } = 250;

        // --- Short-term form (small swing; the cached value is otherwise stable) ---
        /// <summary>Max value swing from form at the form extremes, in 1/1000 (40 = ±4% between cold and hot form around neutral 50).</summary>
        public int FormValueSwingPermille { get; set; } = 40;

        // --- Contract length (an expiring deal discounts the fee) ---
        /// <summary>Seasons remaining at/above which a contract carries no discount (full value).</summary>
        public int ContractFullSeasons { get; set; } = 3;
        /// <summary>Value multiplier at zero seasons remaining, in 1/1000 (350 = a player in his final months fetches ~35% of value — he can leave on a free soon). Scales linearly up to 1000 at ContractFullSeasons.</summary>
        public int ContractExpiringFloorPermille { get; set; } = 350;

        // --- League level (top-flight players priced higher) ---
        /// <summary>Value discount per division below the top flight, in 1/1000 (150 = −15% per division down).</summary>
        public int LeagueLevelDiscountPermille { get; set; } = 150;
        /// <summary>Floor on the league multiplier, in 1/1000 (400 = the lowest divisions still retain 40% of top-flight pricing).</summary>
        public int LeagueLevelFloorPermille { get; set; } = 400;

        // --- Guardrails (no negative/absurd prices — the 5.1 acceptance) ---
        /// <summary>Hard floor: every player is worth at least this much (keeps prices positive even for the weakest).</summary>
        public long MinValue { get; set; } = 25_000;
        /// <summary>Hard ceiling: an absurdity guard so no combination of multipliers produces a silly price.</summary>
        public long MaxValue { get; set; } = 250_000_000;
        /// <summary>Final prices are rounded to the nearest multiple of this (tidy display). Must divide MinValue/MaxValue.</summary>
        public long ValueRoundingUnit { get; set; } = 5_000;
    }

    /// <summary>
    /// Tunables for the transfer market (task 5.2, ARCHITECTURE.md §4.7) — club budgets,
    /// AI personalities, squad-need analysis and offer/counteroffer negotiation. Built on
    /// top of the 5.1 <see cref="Market.ValuationModel"/> (which prices the player); these
    /// magnitudes shape how the AI BUYS and SELLS around that price.
    ///
    /// Pure and deterministic: <see cref="Market.NegotiationModel"/> uses integer math and
    /// NO RNG (a negotiation is a stable function of its inputs); only the world-orchestration
    /// in <see cref="Market.TransferMarket"/> draws from a seeded RNG (window seed) for
    /// buyer/target ordering, so a window replays identically. The match engine never touches
    /// any of this → golden masters/replays are unaffected (opt-in by being called).
    ///
    /// Budgets are minimal-but-real until full finances land at 5.5: seeded once per season
    /// from squad strength + division, debited on a buy, credited on a sale.
    /// </summary>
    public sealed class TransferBalance
    {
        // --- Budget seeding (a club's transfer kitty for the season) ---
        /// <summary>Strength at/below which a club's seeded budget is ~0 (only the floor remains). Mirrors the valuation rating floor so weak clubs are poor and strong clubs rich.</summary>
        public int BudgetStrengthFloor { get; set; } = 35;
        /// <summary>Currency per (clubStrength − BudgetStrengthFloor)² / 100. Tuned so a top club can afford a couple of marquee signings per window (clubs spend best-target-first, so a bigger kitty = more deals, not pricier ones): the budget — not need count — was the live throttle holding the window to ~1 signing/club.</summary>
        public long BudgetUnitPerStrengthSquared { get; set; } = 6_000_000;
        /// <summary>Budget discount per division below the top flight, in 1/1000 (200 = −20% per division down).</summary>
        public int BudgetLeagueDiscountPermille { get; set; } = 200;
        /// <summary>Floor on the league budget multiplier, in 1/1000 (300 = lower divisions still get 30% of the top-flight kitty).</summary>
        public int BudgetLeagueFloorPermille { get; set; } = 300;
        /// <summary>Hard floor on a seeded budget so even the smallest club can do some business.</summary>
        public long MinBudget { get; set; } = 250_000;

        // --- Squad-need analysis (who to buy / who is sellable) ---
        /// <summary>A role is a "need" if its best player rates below (squad standard − this), or the role is below its template depth. NEGATIVE = ambition: the club shops roles up to |value| points ABOVE its own standard, so it tries to improve nearly every position and the REAL gate becomes "does an affordable upgrade actually exist". Calibration: 4→2→0 gave 2→7→23 transfers (cross-role coverage keeps most roles near standard, so a positive threshold starves the market); negative widens the pool toward the 50-150 band, with the per-club signing cap (×40 clubs) as the hard ceiling and budget the live throttle.</summary>
        public int NeedQualityGapPoints { get; set; } = -3;
        /// <summary>A signing must rate at least (club's current best in the role + this) to be bought. 0 = a below-standard role may be filled by an at-least-equal player (not a downgrade). This widens the pool of mutually-beneficial trades — in a closed world the count of genuine upgrades is intrinsically limited (~47 with +1), so 0 lifts the window into the 50-150 band; the cap still bounds the top.</summary>
        public int UpgradeMinPoints { get; set; } = 0;
        /// <summary>Minimum squad size; a club never sells below this (keeps a legal squad).</summary>
        public int MinSquadSize { get; set; } = 18;
        /// <summary>A club keeps at least this many players per role (template depth is the target; this is the hard floor before a sale is refused).</summary>
        public int MinPerRoleDepth { get; set; } = 1;

        // --- Negotiation (multi-round offer/counteroffer) ---
        /// <summary>Max negotiation rounds before the parties give up (each side responds in turn).</summary>
        public int MaxNegotiationRounds { get; set; } = 4;
        /// <summary>Buyer's opening offer as a permille of the seller's asking price (850 = opens at 85%).</summary>
        public int OpeningOfferPermille { get; set; } = 850;
        /// <summary>Seller accepts immediately if the offer is at least this permille of the asking price (970 = within 3%).</summary>
        public int SellerAcceptPermille { get; set; } = 970;
        /// <summary>Seller rejects outright (no counter) if the offer is below this permille of asking (600 = a lowball under 60% is waved away).</summary>
        public int SellerWalkAwayPermille { get; set; } = 600;
        /// <summary>When countering, the seller concedes this permille of the gap between its last ask and the offer (400 = moves 40% toward the buyer each round → convergence).</summary>
        public int SellerConcessionPermille { get; set; } = 400;
        /// <summary>When countering, the buyer closes this permille of the gap between its last offer and the seller's ask, capped by its max price (500 = meets halfway).</summary>
        public int BuyerConcessionPermille { get; set; } = 500;

        // --- Asking-price shaping (importance + personality) ---
        /// <summary>Asking-price premium in 1/1000 for a player who is a regular starter (best XI) — a club only parts with the spine at a premium over plain value (1250 = asks 125%). Lowered from 1500 (task 5.3 feedback: 150% felt too high and pushed fair-value offers under the lowball band, so the user only ever saw rejections; at 125% a near-value offer now lands in the counter band).</summary>
        public int StarterAskPremillePermille { get; set; } = 1250;
        /// <summary>Asking discount in 1/1000 for a clearly surplus player a club wants off the books (900 = priced at 90% to move him).</summary>
        public int SurplusAskPermille { get; set; } = 900;
        /// <summary>A starter is only ever sold if the offer reaches this permille of his plain value — the "never sells its best XI for peanuts" guard (1150 = a 15% premium minimum; still clearly not peanuts, but a determined buyer CAN prise him away). Lowered from 1300 with the ask above (task 5.3 feedback); still well under BuyerMaxValuePermille so a wanted starter stays reachable.</summary>
        public int StarterMinSalePermille { get; set; } = 1150;
        /// <summary>Floor on ANY AI sale as a permille of the player's plain value — no peanuts even for squad players (800 = never below 80% of value).</summary>
        public int MinSalePermille { get; set; } = 800;

        // --- Buyer willingness ---
        /// <summary>A buyer will pay up to this permille of a target's value before walking (1450 = up to 145% of valuation, budget permitting). Must sit ABOVE StarterMinSalePermille so a wanted starter is actually reachable; cheap squad/surplus deals still settle near value because the negotiation converges on the (much lower) asking price, not on this ceiling.</summary>
        public int BuyerMaxValuePermille { get; set; } = 1450;

        // --- Window throughput ---
        /// <summary>Max players one club signs in a single window (keeps activity sane and budgets meaningful). The primary throttle on total window volume once needs are generous.</summary>
        public int MaxSigningsPerClubPerWindow { get; set; } = 3;
        /// <summary>Hard safety cap on total transfers processed in one window (guards runaway loops).</summary>
        public int MaxTransfersPerWindow { get; set; } = 400;
        /// <summary>Seasons remaining written onto a transferred player's contract on signing.</summary>
        public int SignedContractSeasons { get; set; } = 4;
    }

    /// <summary>
    /// Tunables for the lightweight player support actions (task 4.5) — the coach's
    /// morale levers from ARCHITECTURE.md §4.4. Five conversations (praise, encourage,
    /// motivate, criticize, rest) nudge a player's <see cref="Domain.PlayerCondition"/>;
    /// the effect is context-sensitive (structural, in SupportActionModel) and each
    /// action is on a per-player cooldown so spamming is ineffective.
    ///
    /// Anti-frustration ("challenge, not chaos"): every move is small and capped by the
    /// 0..100 condition clamp, the upside saturates near the morale ceiling (so repeated
    /// praise yields nothing), and the one negative action (criticize) has a bounded
    /// downside that is telegraphed by the condition UI. These actions are opt-in: the
    /// match engine and SeasonProgressor never call them, so golden masters/replays are
    /// unaffected. Magnitudes live here; the "when does it land" context patterns are
    /// structural and live in SupportActionModel.
    /// </summary>
    public sealed class SupportBalance
    {
        // --- Morale magnitudes (points at full contextual effect, before the 0..100 clamp) ---
        /// <summary>Praise: morale lift for a job well done. Best when there is room to lift and recent form is good; ~0 on a maxed-morale or out-of-form player.</summary>
        public int PraiseMoraleBoost { get; set; } = 8;
        /// <summary>Encourage: a pick-me-up. Strongest when morale is low, tapering to ~0 as morale rises.</summary>
        public int EncourageMoraleBoost { get; set; } = 10;
        /// <summary>Motivate: a challenge/push. Small morale lift, peaks for a mid-morale player (the maxed have nothing to prove, the broken need encouragement first).</summary>
        public int MotivateMoraleBoost { get; set; } = 5;
        /// <summary>Criticize: the stick. Morale hit that lands harder on an already-fragile (low-morale) player and gentler on a confident one.</summary>
        public int CriticizeMoraleHit { get; set; } = 8;
        /// <summary>Rest: a breather. Small flat morale lift on top of the fitness recovery.</summary>
        public int RestMoraleBoost { get; set; } = 3;

        // --- Secondary form/fitness magnitudes ---
        /// <summary>Motivate: small form nudge (the push sharpens focus), scaled by the same context as its morale lift.</summary>
        public int MotivateFormNudge { get; set; } = 3;
        /// <summary>Criticize: a form "wake-up" spark that a confident (high-morale) player converts well; near-zero for a fragile one.</summary>
        public int CriticizeFormSpark { get; set; } = 4;
        /// <summary>Rest: fitness recovered, scaled by how tired the player is (~0 when already fresh).</summary>
        public int RestFitnessRecovery { get; set; } = 15;

        // --- Cooldowns (calendar days) per action, per player: an action is blocked until this many days pass ---
        /// <summary>Days before praise can be used again on the same player.</summary>
        public int PraiseCooldownDays { get; set; } = 14;
        /// <summary>Days before encourage can be used again on the same player.</summary>
        public int EncourageCooldownDays { get; set; } = 14;
        /// <summary>Days before motivate can be used again on the same player.</summary>
        public int MotivateCooldownDays { get; set; } = 14;
        /// <summary>Days before criticize can be used again on the same player (longer — a stern word loses its weight if overused).</summary>
        public int CriticizeCooldownDays { get; set; } = 21;
        /// <summary>Days before rest can be used again on the same player.</summary>
        public int RestCooldownDays { get; set; } = 21;
    }

    /// <summary>
    /// Tunables for the training-driven development model (task 4.3). A weekly team
    /// focus (plus optional per-player individual focus) biases which skills grow;
    /// growth is gated by the player's hidden <see cref="Domain.PlayerDevelopment.Potential"/>
    /// (no growth once overall reaches it), and players at/above potential drift gently
    /// downward — so the whole world improves OR declines, never stagnates.
    ///
    /// Anti-frustration (ARCHITECTURE.md §4.4/§4.5): every decline is capped (a player
    /// never falls more than <see cref="DeclineFloorPoints"/> below his potential), and
    /// the chosen focus protects the skills you drill. Magnitudes only live here; the
    /// per-focus skill weight patterns are structural and live in TrainingModel.
    ///
    /// The richer age-curve / minutes / facilities / performance modifiers and the
    /// monthly cadence arrive in task 4.4 — 4.3 is the training foundation that 4.4
    /// builds on, the way 4.1 preceded the 4.2 wiring.
    /// </summary>
    public sealed class DevelopmentBalance
    {
        // --- Growth (players below their potential) ---
        /// <summary>
        /// Per-week +1 probability (in 1/1000) for a skill carrying a focus weight of 100,
        /// at full headroom. A single-focus skill (weight ~90) then gains a few points a
        /// season; a Balanced week spreads a smaller gain across every skill.
        /// </summary>
        public int GrowthPerMillePerWeight { get; set; } = 130;
        /// <summary>Upper clamp on a single skill's weekly +1 probability (1/1000), so stacked focuses can't run away.</summary>
        public int GrowthMaxPerMille { get; set; } = 250;
        /// <summary>Headroom (potential − overall) at or above which growth runs at full rate; below it growth slows linearly toward the cap.</summary>
        public int HeadroomScaleCap { get; set; } = 10;

        // --- Decline (players at or above their potential) ---
        /// <summary>Per-week −1 probability (in 1/1000) for an unprotected skill once a player has no headroom left. Gentle by design.</summary>
        public int DeclinePerMille { get; set; } = 35;
        /// <summary>A skill counts as "drilled" (and so decline-protected) when its combined training weight reaches this threshold — above the all-round baseline (~40), below a real emphasis (60+).</summary>
        public int DeclineProtectionWeightThreshold { get; set; } = 55;
        /// <summary>How much a drilled skill's decline probability is reduced, in percent (drilling a skill keeps it sharp longer).</summary>
        public int DeclineProtectionPercent { get; set; } = 70;
        /// <summary>A player's overall never declines more than this many points below his potential (anti-frustration cap on ageing/decline).</summary>
        public int DeclineFloorPoints { get; set; } = 8;

        // --- Tactic familiarity (the "affects tactic familiarity" half of 4.3) ---
        /// <summary>Familiarity points a club gains for its current tactic per week of Tactical team focus (host applies it to the stored level; 0 for any other focus).</summary>
        public int TacticalFocusFamiliarityGainPerWeek { get; set; } = 8;

        // ===================== Development & aging (task 4.4) =====================
        // The age curve and per-player modifiers ride the SAME weekly cadence as the
        // 4.3 training tick (chosen with the user) rather than a separate monthly tick.
        // Magnitudes only live here; the per-role peak/onset offsets are structural and
        // live in Development.AgeCurve (like the tactic +/- patterns in TacticModifiers).

        // --- Age growth curve (young players grow, fading to zero at the role peak) ---
        /// <summary>At or below this age the age curve never slows growth (youth grow at full speed before any taper).</summary>
        public int GrowthYouthFullAge { get; set; } = 19;
        /// <summary>Base age at which the age-growth factor reaches zero (per-role offsets shift it; ARCHITECTURE.md §4.5 "peak ~27").</summary>
        public int GrowthPeakAge { get; set; } = 27;

        // --- Age decline curve (older players decline, faster with age, position-dependent) ---
        /// <summary>Base age at which ageing decline begins (per-role offsets shift it; "decline after ~30").</summary>
        public int DeclineOnsetAge { get; set; } = 30;
        /// <summary>Per-week −1 probability (1/1000) per skill for a player exactly at his decline-onset age (gentle by design). Scales up with age and down with drilling.</summary>
        public int AgeDeclineBasePerMille { get; set; } = 45;
        /// <summary>Extra age-decline multiplier (1/1000) added per year beyond the onset age (120 = +12% of the base rate per year).</summary>
        public int AgeDeclineRampPerMillePerYear { get; set; } = 120;
        /// <summary>Upper clamp on the age-decline multiplier (1/1000), so the very old still decline gently — no cliff (anti-collapse).</summary>
        public int AgeDeclineMaxMultiplierPermille { get; set; } = 1800;
        /// <summary>Ageing never drives a player's overall below this percent of his potential — a hard anti-collapse floor (a pot-80 veteran bottoms out near 44).</summary>
        public int AgeDeclineFloorPercentOfPotential { get; set; } = 55;

        // --- Growth modifiers (all neutral = 1000/no change; default callers stay 4.3-identical) ---
        /// <summary>Growth speed (1/1000) for a player who gets no minutes; full minutes = 1000. Benched youth still develop, just slower ("youth need games").</summary>
        public int MinutesGrowthFloorPermille { get; set; } = 350;
        /// <summary>Facility level (0–100) at which the training ground neither helps nor hinders growth. Real levels arrive with facilities (task 5.5); until then hosts pass neutral.</summary>
        public int FacilityNeutralLevel { get; set; } = 50;
        /// <summary>Growth swing (1/1000) from facilities at the level extremes (300 = ±30% at level 0 vs 100).</summary>
        public int FacilityGrowthSwingPermille { get; set; } = 300;
        /// <summary>Performance rating (0–100) that neither helps nor hinders growth. Hosts can feed match ratings or use form/condition as a proxy.</summary>
        public int PerformanceNeutralRating { get; set; } = 50;
        /// <summary>Growth swing (1/1000) from performances at the rating extremes (good games accelerate growth, poor ones slow it).</summary>
        public int PerformanceGrowthSwingPermille { get; set; } = 300;
    }

    /// <summary>
    /// Tunables for the condition model (task 4.1): form, morale and fitness, and
    /// how they feed match performance. See ARCHITECTURE.md §4.4 — "challenge, not
    /// chaos": every malus is capped and dynamics are gentle and self-correcting.
    ///
    /// Identity invariant: at neutral condition (Form = FormNeutral,
    /// Morale = MoraleNeutral, Fitness = 100) the performance multiplier is exactly
    /// 1.0, so a squad of neutral players plays byte-identically to the
    /// pre-condition engine (proven by test). Condition is opt-in on the engine, so
    /// the default match path is unchanged regardless.
    /// </summary>
    public sealed class ConditionBalance
    {
        // --- Performance multiplier (feeds match ratings; capped maluses) ---
        /// <summary>Hard floor: a player never performs below this % of his ability, however bad his condition.</summary>
        public int PerformanceFloorPercent { get; set; } = 70;
        /// <summary>Ceiling for a player on hot form and full fitness.</summary>
        public int PerformanceCapPercent { get; set; } = 108;
        /// <summary>Max performance swing from form, in 1/1000 (±6% at the form extremes).</summary>
        public int FormSwingPermille { get; set; } = 60;
        /// <summary>Max performance swing from morale, in 1/1000 (±5% at the morale extremes).</summary>
        public int MoraleSwingPermille { get; set; } = 50;
        /// <summary>Performance loss at zero fitness, in 1/1000 (−20%; fitness only ever reduces, never boosts).</summary>
        public int FitnessSwingPermille { get; set; } = 200;

        // --- Fitness dynamics (drains with minutes, recovers with rest) ---
        /// <summary>Fitness lost for a full 90 minutes played (scaled by actual minutes).</summary>
        public int FitnessDrainPer90Minutes { get; set; } = 24;
        /// <summary>Fitness recovered per rested day (capped at 100). Calibrated so a weekly cycle of full matches slowly accumulates fatigue.</summary>
        public int FitnessRecoveryPerDay { get; set; } = 3;
        /// <summary>Stamina value at which the drain is unmodified (the neutral pivot).</summary>
        public int StaminaNeutral { get; set; } = 50;
        /// <summary>How much stamina bends the fitness drain, in 1/1000 at the stamina extremes (400 = ±40%): low stamina tires faster, high stamina slower. Everyone starts at full fitness, so stamina is the differentiator — how fast they drop.</summary>
        public int StaminaDrainSwingPermille { get; set; } = 400;

        // --- Within-match fatigue (opt-in: only when the engine's applyMatchFatigue is on) ---
        /// <summary>A neutral-stamina side's rating fade by the 90th minute, in 1/1000 (60 = −6%). Both sides tiring equally is scale-invariant (no net goal change); the effect is RELATIVE — a fresher / higher-stamina side gains the edge, especially late. Tuned down from 80 to keep draws near the historic ~24% while still making condition matter.</summary>
        public int MatchFatigueAt90Permille { get; set; } = 60;
        /// <summary>Fade recovered at the half-time break, in 1/1000 (20 = +2%): the second half restarts a little fresher, then tiredness builds again. "A bit, but not too much."</summary>
        public int HalfTimeRecoveryPermille { get; set; } = 20;

        // --- Form dynamics (bounded, mean-reverting walk) ---
        /// <summary>Neutral form; the walk reverts toward it so cold streaks always end.</summary>
        public int FormNeutral { get; set; } = 50;
        /// <summary>Uniform random step applied to form each match, ± this many points.</summary>
        public int FormRandomStep { get; set; } = 5;
        /// <summary>Mean reversion per match, in 1/1000 of the gap to neutral (350 = 35% of the gap pulled back).</summary>
        public int FormReversionPermille { get; set; } = 350;
        /// <summary>Form nudge from the match result (win +, loss −) for players who featured.</summary>
        public int FormResultNudge { get; set; } = 3;

        // --- Morale dynamics (playing time + results, decays to neutral) ---
        /// <summary>Neutral morale; morale decays toward it when nothing happens.</summary>
        public int MoraleNeutral { get; set; } = 50;
        /// <summary>Morale gained for featuring in a match.</summary>
        public int MoralePlayBonus { get; set; } = 2;
        /// <summary>Morale lost for being left out of a match.</summary>
        public int MoraleBenchPenalty { get; set; } = 3;
        /// <summary>Morale gained on a win.</summary>
        public int MoraleWinBonus { get; set; } = 4;
        /// <summary>Morale lost on a loss.</summary>
        public int MoraleLossPenalty { get; set; } = 4;
        /// <summary>Morale drift toward neutral per rested day.</summary>
        public int MoraleDecayPerDay { get; set; } = 1;
    }

    /// <summary>
    /// Tunables for the tactics system (task 3.2). All effects are percent deltas
    /// applied to a side's attack/midfield/defense before the chance model.
    ///
    /// Two design invariants keep the ladder fair (acceptance: no tactic &gt; 55%
    /// win rate across the field):
    ///   - self-effects are trade-offs: every bonus on one rating is paid for by an
    ///     equal malus on another, so no instruction is strictly better in a vacuum;
    ///   - counter contributions use zero-sum patterns (each option's effect summed
    ///     over a uniform field of opponents is zero), so no tactic gains on average.
    /// Magnitudes live here; the +/- patterns (who counters whom) are structural and
    /// live in TacticModifiers.
    /// </summary>
    public sealed class TacticsBalance
    {
        // --- Self-effects (trade-offs) ---
        /// <summary>Attacking: +attack / -defense (Defensive mirrors it).</summary>
        public int MentalitySwingPercent { get; set; } = 8;
        /// <summary>High press: +attack / -defense — win it high, leave space behind (Low mirrors it).</summary>
        public int PressingSwingPercent { get; set; } = 6;
        /// <summary>Fast: +attack / -defense — direct and committed (Slow mirrors it).</summary>
        public int TempoSwingPercent { get; set; } = 5;
        /// <summary>Wide: +attack / -midfield — the only possession-moving axis (Narrow mirrors it).</summary>
        public int WidthSwingPercent { get; set; } = 5;

        // --- Counter-matrix (zero-sum patterns) ---
        /// <summary>Press vs tempo: fast tempo gains attack through a high press, which is left exposed at the back.</summary>
        public int CounterPressVsTempoPercent { get; set; } = 6;
        /// <summary>My mentality vs their tempo: a high line is punished by fast play, rewarded vs slow.</summary>
        public int CounterMentalityVsTempoPercent { get; set; } = 5;
        /// <summary>My width vs their width: cyclic edge (Wide&gt;Narrow&gt;Normal&gt;Wide).</summary>
        public int CounterWidthPercent { get; set; } = 5;

        // --- Familiarity ---
        /// <summary>Familiarity scale; full familiarity = no penalty.</summary>
        public int FamiliarityMax { get; set; } = 100;
        /// <summary>Effectiveness malus (attack &amp; defense) at zero familiarity.</summary>
        public int UnfamiliarPenaltyPercent { get; set; } = 12;
        /// <summary>Familiarity gained each match a tactic is used (capped at max).</summary>
        public int FamiliarityGainPerMatch { get; set; } = 20;

        // --- Guardrails ---
        /// <summary>Lower clamp for any tactic rating multiplier (percent of base).</summary>
        public int MinMultiplierPercent { get; set; } = 70;
        /// <summary>Upper clamp for any tactic rating multiplier (percent of base).</summary>
        public int MaxMultiplierPercent { get; set; } = 130;
    }

    /// <summary>Tunables for the season calendar and league table.</summary>
    public sealed class SeasonBalance
    {
        /// <summary>Career day of matchday 1.</summary>
        public int FirstMatchDay { get; set; } = 7;

        /// <summary>Days between consecutive matchdays.</summary>
        public int DaysBetweenRounds { get; set; } = 7;

        public int PointsForWin { get; set; } = 3;
        public int PointsForDraw { get; set; } = 1;

        /// <summary>Clubs promoted/relegated between adjacent divisions at season end.</summary>
        public int PromotedRelegatedCount { get; set; } = 3;
    }

    /// <summary>Tunables for procedural league/player generation.</summary>
    public sealed class GenerationBalance
    {
        // --- Club strength hierarchy ---
        public int TopClubStrength { get; set; } = 72;
        public int BottomClubStrength { get; set; } = 52;
        /// <summary>Random jitter (+/-) applied to each club's baseline strength.</summary>
        public int ClubStrengthJitter { get; set; } = 2;

        /// <summary>How much weaker each lower division is (applied to top/bottom strength).</summary>
        public int DivisionStrengthStep { get; set; } = 14;

        // --- Player skills ---
        /// <summary>Random jitter (+/-) applied to each player's target overall around the club baseline.</summary>
        public int PlayerTargetNoise { get; set; } = 6;
        /// <summary>Random jitter (+/-) applied to every individual skill.</summary>
        public int SkillNoise { get; set; } = 8;
        public int OutfieldGoalkeepingMin { get; set; } = 2;
        public int OutfieldGoalkeepingMax { get; set; } = 15;

        // --- Ages ---
        public int MinAge { get; set; } = 17;
        public int MaxAge { get; set; } = 36;
        /// <summary>Relative weight of each age from MinAge upward. Peak in the mid-20s.</summary>
        public int[] AgeWeights { get; set; } =
            { 2, 3, 4, 6, 7, 8, 9, 9, 9, 8, 8, 7, 6, 5, 4, 3, 2, 1, 1, 1 };

        // --- Development potential ---
        /// <summary>Age at which growth headroom reaches zero.</summary>
        public int PeakAge { get; set; } = 27;
        /// <summary>Potential headroom granted per year below PeakAge.</summary>
        public int HeadroomPerYear { get; set; } = 2;
        public int HeadroomNoiseMin { get; set; } = -3;
        public int HeadroomNoiseMax { get; set; } = 6;

        // --- Fresh-career condition ---
        public int MoraleMin { get; set; } = 50;
        public int MoraleMax { get; set; } = 70;
        public int FitnessMin { get; set; } = 95;
        public int FitnessMax { get; set; } = 100;

        // --- Contracts ---
        /// <summary>WeeklyWage = overall^2 * WageFactor.</summary>
        public int WageFactor { get; set; } = 4;
        public int ContractSeasonsMin { get; set; } = 1;
        public int ContractSeasonsMax { get; set; } = 4;
    }

    /// <summary>Tunables for the match engine (consumed from task 1.4 onward).</summary>
    public sealed class MatchBalance
    {
        /// <summary>Strength bonus for the home side, in percent of team strength.</summary>
        public int HomeAdvantagePercent { get; set; } = 6;

        /// <summary>Probability that any given minute produces an attacking action (~25 per match).</summary>
        public double ActionChancePerMinute { get; set; } = 0.28;

        /// <summary>
        /// Scales how often a chance becomes a goal (calibrated for ~2.6-2.8 goals/match).
        /// Note: generated defenses rate slightly above attacks (role-weight inflation),
        /// so the equal-teams ratio r sits just below 0.5 - this value compensates.
        /// </summary>
        public double GoalCoefficient { get; set; } = 0.92;

        /// <summary>Exponent applied to midfield ratings when deriving possession. Higher = dominance matters more.</summary>
        public int PossessionSharpness { get; set; } = 2;

        /// <summary>Exponent applied to attack/defense ratio per chance. Higher = quality gaps matter more.</summary>
        public int ChanceSharpness { get; set; } = 3;

        /// <summary>Of the chances that do not score, how many are saves (vs off target). Cosmetic.</summary>
        public int SavedShareOfFailedChancesPercent { get; set; } = 55;

        /// <summary>Relative shooting weight per PositionRole (enum order: GK, CB, FB, DM, CM, AM, W, ST).</summary>
        public int[] ScorerWeightsByRole { get; set; } = { 0, 2, 3, 4, 8, 16, 18, 30 };

        // --- Position stream (task 1.5) — distances in decimetres (Pitch coords) ---

        /// <summary>Position snapshots per match minute (4 = one frame every 15 sim-seconds).</summary>
        public int TicksPerMinute { get; set; } = 4;

        /// <summary>Formation anchor X per PositionRole, in 1/1000 of pitch length (home side; away is mirrored).</summary>
        public int[] FormationAnchorXPermilleByRole { get; set; } = { 40, 180, 200, 340, 440, 540, 620, 720 };

        /// <summary>Y margin from the touchline for wide roles (FullBack, Winger).</summary>
        public int WideRoleYMarginDm { get; set; } = 80;

        /// <summary>Y margin from the touchline for central roles.</summary>
        public int CentralRoleYMarginDm { get; set; } = 170;

        /// <summary>How far from the attacked goal line shots are taken.</summary>
        public int ShotSpotGoalDistanceDm { get; set; } = 140;

        /// <summary>Max lateral offset of a shot spot from the goal's centre line.</summary>
        public int ShotSpotHalfWidthDm { get; set; } = 160;

        /// <summary>Ball wanders via random waypoints roughly this many ticks apart between events.</summary>
        public int BallWaypointIntervalTicks { get; set; } = 12;

        /// <summary>Max random deviation of the ball from its waypoint-to-waypoint path.</summary>
        public int BallNoiseDm { get; set; } = 50;

        /// <summary>Max distance a player covers per tick while repositioning.</summary>
        public int PlayerMaxStepDmPerTick { get; set; } = 70;

        /// <summary>Random per-tick jitter applied to each player position.</summary>
        public int PlayerNoiseDm { get; set; } = 15;

        /// <summary>How strongly outfield players shift along X toward the ball (percent of ball offset from centre).</summary>
        public int PlayerBallPullXPercent { get; set; } = 30;

        /// <summary>How strongly outfield players drift along Y toward the ball (percent of distance).</summary>
        public int PlayerBallPullYPercent { get; set; } = 20;

        /// <summary>Ball pull percent override for goalkeepers (they mostly hold their line).</summary>
        public int GoalkeeperBallPullPercent { get; set; } = 6;

        /// <summary>Ticks before an event during which the shooter sprints toward the shot spot.</summary>
        public int ShooterApproachTicks { get; set; } = 3;
    }
}
