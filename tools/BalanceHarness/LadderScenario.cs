using Fts.Application.Ranked;
using Fts.Infrastructure.Ranked;
using Sim.Core.Random;

namespace Fts.BalanceHarness;

/// <summary>
/// Does the ranked ladder do its job? It has one: sort coaches by how good they are and let them move.
///
/// The rating maths and the pyramid shape are the SERVER's own (<see cref="EloModel"/> and
/// <see cref="RankedOptions"/> defaults), so what is measured here is what the ladder would do in
/// production. What the harness supplies is the part a database cannot: coaches with a real difference in
/// skill. Squads are re-equalised at every ranked season reset, so in the ladder a coach's edge is his own
/// - modelled here as a latent strength on the Elo scale, from which each match outcome is drawn (with a
/// home advantage and a draw rate matching the engine's own ~21-25%). Feeding full Sim.Core matches in
/// would only add variance around the same latent strength while making the run hours long.
///
/// The sweep is over SEAT OCCUPANCY, because the ladder's mobility depends on a free seat existing:
/// promotion and relegation are implemented as "find a free seat in the target tier, else stay put"
/// (RankedService.MoveToTierAsync), so a pyramid with no vacancies can freeze. That is a design question
/// worth a number rather than an opinion.
/// </summary>
internal static class LadderScenario
{
    private const double DrawBase = 0.28;   // draw share of an even match; matches the engine's ~21-25% overall
    private const double HomeAdvantage = 40; // Elo-equivalent points

    public static void Run(HarnessOptions opt, CheckList checks)
    {
        Console.WriteLine();
        Console.WriteLine("=== LADDER ===");

        var options = new RankedOptions();
        Console.WriteLine(
            $"[balance-ladder] pyramid: {options.TierCount()} tiers, groups {options.Tier1Groups}/" +
            $"{options.Tier2Groups}/{options.Tier3Groups} x {options.GroupSize} seats = {TotalSeats(options)} seats " +
            $"({PlaceableSeats(options)} placeable); K={options.EloKFactor}, season swing {options.SeasonEndPositionSwing}, " +
            $"P/R {options.PromotionSlots}/{options.RelegationSlots}");

        var fills = new List<int>(opt.LadderFillPercents.Length > 0 ? opt.LadderFillPercents : new[] { 75 });
        fills.Sort(); // ascending, so results[0] is the emptiest pyramid and results[^1] the fullest

        var results = new List<LadderRun>();
        foreach (int fill in fills)
        {
            LadderRun run = Simulate(opt, options, fill);
            results.Add(run);

            Console.WriteLine(
                $"[balance-ladder] fill {fill,3}% ({run.Coaches} coaches, {opt.LadderSeasons} seasons): " +
                $"rating p10 {run.P10} p50 {run.P50} p90 {run.P90} (min {run.Min}, max {run.Max}), " +
                $"mean drift {(run.MeanDrift >= 0 ? "+" : "")}{Fmt.N(run.MeanDrift, 0)}");
            Console.WriteLine(
                $"[balance-ladder] fill {fill,3}%: skill/rating rank correlation {Fmt.N(run.RankCorrelation, 2)}; " +
                $"tier moves {run.Moves} done / {run.Denied} denied for want of a free seat; " +
                $"{Fmt.Pct(run.NeverMovedShare)} of coaches never changed tier; " +
                $"top-decile coaches reaching tier 1: {Fmt.Pct(run.TopReachedTier1Share)}");
        }

        LadderRun open = results[0];                       // the most vacant pyramid of the sweep
        LadderRun full = results[^1];                      // the most crowded

        checks.Check(
            "the ladder sorts coaches by skill",
            open.RankCorrelation >= 0.5,
            $"skill/rating rank correlation {Fmt.N(open.RankCorrelation, 2)} at {open.FillPercent}% occupancy (floor 0.50)");

        bool drifts = false;
        foreach (LadderRun r in results) if (Math.Abs(r.MeanDrift) >= 150) drifts = true;
        checks.Check(
            "ratings neither inflate nor deflate",
            !drifts,
            $"largest mean drift {Fmt.N(LargestDrift(results), 0)} points over {opt.LadderSeasons} seasons (cap 150)");

        bool floorHeld = true;
        foreach (LadderRun r in results) if (r.Min < options.MinRating) floorHeld = false;
        checks.Check(
            "the rating floor holds",
            floorHeld,
            $"lowest rating seen {LowestRating(results)} (floor {options.MinRating})");

        checks.Check(
            "the ladder is not frozen",
            open.NeverMovedShare < 0.60,
            $"{Fmt.Pct(open.NeverMovedShare)} of coaches never changed tier at {open.FillPercent}% occupancy (cap 60%)");

        checks.Info(
            $"at {full.FillPercent}% occupancy {Fmt.Pct(full.DeniedShare)} of the tier moves the ladder decided on were " +
            "refused because the target tier had no free seat (MoveToTierAsync leaves the coach where he is). " +
            "A full pyramid can therefore stop promoting: worth deciding before launch whether promotion should " +
            "SWAP two coaches instead of looking for a vacancy.");
        checks.Info(
            $"a top-decile coach reaches the top tier in {Fmt.Pct(open.TopReachedTier1Share)} of cases within " +
            $"{opt.LadderSeasons} seasons (median {(open.MedianSeasonsToTier1 < 0 ? "never" : open.MedianSeasonsToTier1.ToString())} seasons) " +
            "- the pace of the climb is a design choice, not a bug.");
    }

    // --- the simulation ---------------------------------------------------------------------------

    private sealed class LadderRun
    {
        public int FillPercent;
        public int Coaches;
        public int Min, Max, P10, P50, P90;
        public double MeanDrift;
        public double RankCorrelation;
        public int Moves, Denied;
        public double DeniedShare => Moves + Denied > 0 ? Denied / (double)(Moves + Denied) : 0;
        public double NeverMovedShare;
        public double TopReachedTier1Share;
        public int MedianSeasonsToTier1 = -1;
    }

    private sealed class Coach
    {
        public int Id;
        public double Skill;
        public int Rating;
        public int Tier;
        public int GroupIndex;
        public int SeatIndex;
        public bool EverMovedTier;
        public int FirstSeasonInTopTier = -1;
    }

    private sealed class Group
    {
        public int Tier;
        public int[] Occupant = Array.Empty<int>(); // coach id, or -1 for an AI-held seat
        public int AiRating;

        /// <summary>The finishing order of the season just played (coach ids, -1 for AI seats) - the same
        /// table that rewarded the coaches decides who goes up and who goes down.</summary>
        public List<int>? LastOrder;
    }

    private static LadderRun Simulate(HarnessOptions opt, RankedOptions o, int fillPercent)
    {
        var rng = new Pcg32(opt.Seed + 700_000 + (ulong)fillPercent);
        IReadOnlyList<int> groupsPerTier = o.GroupsPerTier();

        // Build the pyramid: every seat exists from the start and is AI-held until a coach claims it.
        var groups = new List<Group>();
        for (int t = 0; t < groupsPerTier.Count; t++)
        {
            for (int g = 0; g < groupsPerTier[t]; g++)
            {
                var group = new Group { Tier = t + 1, AiRating = o.AiRatingForTier(t + 1), Occupant = new int[o.GroupSize] };
                for (int s = 0; s < o.GroupSize; s++) group.Occupant[s] = -1;
                groups.Add(group);
            }
        }

        // Humans only ever enter through placement, which never hands out tier 1.
        int placeable = PlaceableSeats(o);
        int coachCount = Math.Max(o.GroupSize, placeable * fillPercent / 100);

        var coaches = new List<Coach>(coachCount);
        for (int i = 0; i < coachCount; i++)
            coaches.Add(new Coach { Id = i, Skill = DrawSkill(rng, opt.LadderSkillSpread), Rating = o.StartingRating });

        Placement(coaches, groups, o, rng);

        var startRatings = new List<double>(coaches.Count);
        foreach (Coach c in coaches) startRatings.Add(c.Rating);

        int moves = 0, denied = 0;
        for (int season = 1; season <= opt.LadderSeasons; season++)
        {
            foreach (Group group in groups)
            {
                List<int> order = PlayGroupSeason(group, coaches, o, rng, rate: true);
                ApplySeasonEnd(group, order, coaches, o);
            }

            // Season reset: promotions and relegations, tier by tier (the service processes each group
            // when its own break elapses; ordering only matters for who gets a scarce free seat first).
            foreach (Group group in new List<Group>(groups))
            {
                List<int> order = LastOrder(group);
                for (int pos = 1; pos <= order.Count; pos++)
                {
                    int coachId = order[pos - 1];
                    if (coachId < 0) continue;

                    RankedTierMove move = MoveFor(group.Tier, o.TierCount(), pos, o.GroupSize, o);
                    if (move == RankedTierMove.Stay) continue;

                    int target = move == RankedTierMove.Promotion ? group.Tier - 1 : group.Tier + 1;
                    if (TryMove(coaches[coachId], group, groups, target, o))
                    {
                        moves++;
                        coaches[coachId].EverMovedTier = true;
                        coaches[coachId].Rating = EloModel.Apply(
                            coaches[coachId].Rating, EloModel.TierMoveDelta(move, o.PromotionRatingBonus), o.MinRating);
                    }
                    else
                    {
                        denied++;
                    }
                }
            }

            foreach (Coach c in coaches)
                if (c.Tier == 1 && c.FirstSeasonInTopTier < 0) c.FirstSeasonInTopTier = season;
        }

        return Summarise(opt, o, coaches, startRatings, fillPercent, moves, denied);
    }

    /// <summary>Season 0: cohorts of GroupSize newcomers play a placement season and are sorted into the
    /// placeable tiers by where they finish, exactly as RankedService.ResolvePlacement does.</summary>
    private static void Placement(List<Coach> coaches, List<Group> groups, RankedOptions o, Pcg32 rng)
    {
        for (int start = 0; start < coaches.Count; start += o.PlacementGroupSize)
        {
            int size = Math.Min(o.PlacementGroupSize, coaches.Count - start);
            var cohort = new List<Coach>();
            for (int i = 0; i < size; i++) cohort.Add(coaches[start + i]);

            List<int> order = PlayCohort(cohort, o, rng);

            for (int pos = 1; pos <= order.Count; pos++)
            {
                Coach c = coaches[order[pos - 1]];
                c.Rating = o.StartingRating + (size - pos) * o.RatingPerPlacementPosition;
                int tier = pos <= o.PlacementTopPositionsToUpperTier ? o.UpperPlacementTier() : o.LowerPlacementTier();
                // The tier his placement earned, else any free seat below the promotion-only top tier,
                // else the top tier itself. (The live ladder would open a NEW WORLD instead of pushing a
                // newcomer up; worlds never meet, so a harness that models one pyramid and fills it to the
                // swept occupancy measures the same thing with fewer moving parts.)
                if (!Seat(c, groups, tier, o) && !SeatAnywhere(c, groups, o, minTier: 2))
                    SeatAnywhere(c, groups, o, minTier: 1);
            }
        }
    }

    /// <summary>Places a coach on the first free seat of the target tier. Returns false when the tier is full
    /// - the ladder's real behaviour, which is exactly what the occupancy sweep is looking for.</summary>
    private static bool Seat(Coach c, List<Group> groups, int tier, RankedOptions o)
    {
        for (int g = 0; g < groups.Count; g++)
        {
            if (groups[g].Tier != tier) continue;
            for (int s = 0; s < o.GroupSize; s++)
            {
                if (groups[g].Occupant[s] != -1) continue;
                groups[g].Occupant[s] = c.Id;
                c.Tier = tier;
                c.GroupIndex = g;
                c.SeatIndex = s;
                return true;
            }
        }
        return false;
    }

    /// <summary>First free seat in any group at or below <paramref name="minTier"/>'s level (tiers are
    /// numbered downwards, so minTier 2 means "anywhere but the top tier").</summary>
    private static bool SeatAnywhere(Coach c, List<Group> groups, RankedOptions o, int minTier)
    {
        for (int tier = o.TierCount(); tier >= minTier; tier--)
            if (Seat(c, groups, tier, o)) return true;
        return false;
    }

    private static bool TryMove(Coach c, Group from, List<Group> groups, int targetTier, RankedOptions o)
    {
        if (targetTier < 1 || targetTier > o.TierCount() || targetTier == from.Tier) return false;

        for (int g = 0; g < groups.Count; g++)
        {
            if (groups[g].Tier != targetTier) continue;
            for (int s = 0; s < o.GroupSize; s++)
            {
                if (groups[g].Occupant[s] != -1) continue;

                from.Occupant[c.SeatIndex] = -1; // the seat he leaves plays as AI again
                groups[g].Occupant[s] = c.Id;
                c.Tier = targetTier;
                c.GroupIndex = g;
                c.SeatIndex = s;
                return true;
            }
        }
        return false;
    }

    private static RankedTierMove MoveFor(int tier, int tierCount, int position, int groupSize, RankedOptions o)
    {
        if (tier > 1 && position <= o.PromotionSlots) return RankedTierMove.Promotion;
        if (tier < tierCount && position > groupSize - o.RelegationSlots) return RankedTierMove.Relegation;
        return RankedTierMove.Stay;
    }

    // --- one group's season -----------------------------------------------------------------------

    /// <summary>Double round-robin among the group's eight seats. Human seats are rated per match with the
    /// server's Elo; an AI-held seat plays at its tier rating and is never rated. Returns the coach ids in
    /// finishing order (-1 for AI seats), which is the standings the ladder rewards.</summary>
    private static List<int> PlayGroupSeason(Group group, List<Coach> coaches, RankedOptions o, Pcg32 rng, bool rate)
    {
        int n = o.GroupSize;
        var points = new int[n];
        var gd = new int[n];

        for (int home = 0; home < n; home++)
        {
            for (int away = 0; away < n; away++)
            {
                if (home == away) continue; // every ordered pair = the double round-robin

                double sh = SeatSkill(group, home, coaches, o);
                double sa = SeatSkill(group, away, coaches, o);
                int rh = SeatRating(group, home, coaches, o);
                int ra = SeatRating(group, away, coaches, o);

                int result = DrawOutcome(sh, sa, rng); // 1 home win, 0 draw, -1 away win
                points[home] += result > 0 ? 3 : result == 0 ? 1 : 0;
                points[away] += result < 0 ? 3 : result == 0 ? 1 : 0;
                gd[home] += result; gd[away] -= result;

                if (!rate) continue;

                // Both ratings are read before either is written, so a human-vs-human match is symmetric.
                RankedMatchOutcome ho = result > 0 ? RankedMatchOutcome.Win
                    : result == 0 ? RankedMatchOutcome.Draw : RankedMatchOutcome.Loss;
                RankedMatchOutcome ao = result < 0 ? RankedMatchOutcome.Win
                    : result == 0 ? RankedMatchOutcome.Draw : RankedMatchOutcome.Loss;

                if (group.Occupant[home] >= 0)
                {
                    Coach c = coaches[group.Occupant[home]];
                    c.Rating = EloModel.Apply(c.Rating, EloModel.MatchDelta(rh, ra, ho, o.EloKFactor), o.MinRating);
                }
                if (group.Occupant[away] >= 0)
                {
                    Coach c = coaches[group.Occupant[away]];
                    c.Rating = EloModel.Apply(c.Rating, EloModel.MatchDelta(ra, rh, ao, o.EloKFactor), o.MinRating);
                }
            }
        }

        var seats = new List<int>();
        for (int i = 0; i < n; i++) seats.Add(i);
        seats.Sort((x, y) => points[y] != points[x] ? points[y].CompareTo(points[x]) : gd[y].CompareTo(gd[x]));

        var order = new List<int>();
        foreach (int seat in seats) order.Add(group.Occupant[seat]);
        group.LastOrder = order;
        return order;
    }

    /// <summary>A placement cohort: the same match model, no rating (placement seeds the rating from the
    /// finishing position instead). Returns the cohort's COACH IDS in finishing order.</summary>
    private static List<int> PlayCohort(List<Coach> cohort, RankedOptions o, Pcg32 rng)
    {
        int n = cohort.Count;
        var points = new int[n];
        var gd = new int[n];

        for (int home = 0; home < n; home++)
        {
            for (int away = 0; away < n; away++)
            {
                if (home == away) continue;
                int result = DrawOutcome(cohort[home].Skill, cohort[away].Skill, rng);
                points[home] += result > 0 ? 3 : result == 0 ? 1 : 0;
                points[away] += result < 0 ? 3 : result == 0 ? 1 : 0;
                gd[home] += result; gd[away] -= result;
            }
        }

        var order = new List<int>();
        for (int i = 0; i < n; i++) order.Add(i);
        order.Sort((x, y) => points[y] != points[x] ? points[y].CompareTo(points[x]) : gd[y].CompareTo(gd[x]));

        var byCoachId = new List<int>();
        foreach (int i in order) byCoachId.Add(cohort[i].Id);
        return byCoachId;
    }

    private static void ApplySeasonEnd(Group group, List<int> order, List<Coach> coaches, RankedOptions o)
    {
        for (int pos = 1; pos <= order.Count; pos++)
        {
            int id = order[pos - 1];
            if (id < 0) continue;
            Coach c = coaches[id];
            c.Rating = EloModel.Apply(
                c.Rating, EloModel.SeasonEndDelta(pos, o.GroupSize, o.SeasonEndPositionSwing), o.MinRating);
        }
    }

    private static List<int> LastOrder(Group group) => group.LastOrder ?? new List<int>();

    // --- the match model --------------------------------------------------------------------------

    /// <summary>1 home win / 0 draw / -1 away win, drawn from the latent skill gap. The draw share is
    /// highest for an even match and falls to zero for a total mismatch, which is how football behaves and
    /// how the engine's own scoreline distribution behaves.</summary>
    private static int DrawOutcome(double homeSkill, double awaySkill, Pcg32 rng)
    {
        double expected = 1.0 / (1.0 + Math.Pow(10.0, (awaySkill - (homeSkill + HomeAdvantage)) / 400.0));
        double draw = DrawBase * (1.0 - 2.0 * Math.Abs(expected - 0.5));
        double homeWin = expected - draw / 2.0;
        if (homeWin < 0) homeWin = 0;

        double roll = rng.NextDouble();
        if (roll < homeWin) return 1;
        if (roll < homeWin + draw) return 0;
        return -1;
    }

    private static double SeatSkill(Group group, int seat, List<Coach> coaches, RankedOptions o) =>
        group.Occupant[seat] >= 0 ? coaches[group.Occupant[seat]].Skill : group.AiRating;

    private static int SeatRating(Group group, int seat, List<Coach> coaches, RankedOptions o) =>
        group.Occupant[seat] >= 0 ? coaches[group.Occupant[seat]].Rating : group.AiRating;

    private static double DrawSkill(Pcg32 rng, int spread)
    {
        // Average of four uniforms: a bell shape without touching Math.Exp, spanning +/- spread/2.
        double u = (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() + rng.NextDouble()) / 4.0;
        return 1000.0 + (u - 0.5) * spread;
    }

    // --- reporting --------------------------------------------------------------------------------

    private static LadderRun Summarise(
        HarnessOptions opt, RankedOptions o, List<Coach> coaches, List<double> startRatings,
        int fillPercent, int moves, int denied)
    {
        var ratings = new List<double>();
        var skills = new List<double>();
        var sorted = new List<long>();
        int neverMoved = 0;

        foreach (Coach c in coaches)
        {
            ratings.Add(c.Rating);
            skills.Add(c.Skill);
            sorted.Add(c.Rating);
            if (!c.EverMovedTier) neverMoved++;
        }
        sorted.Sort();

        // Top-decile coaches by latent skill: did the ladder carry them to the top tier?
        var bySkill = new List<Coach>(coaches);
        bySkill.Sort((a, b) => b.Skill.CompareTo(a.Skill));
        int decile = Math.Max(1, bySkill.Count / 10);
        int reached = 0;
        var seasonsToTop = new List<double>();
        for (int i = 0; i < decile; i++)
        {
            if (bySkill[i].FirstSeasonInTopTier < 0) continue;
            reached++;
            seasonsToTop.Add(bySkill[i].FirstSeasonInTopTier);
        }
        seasonsToTop.Sort();

        return new LadderRun
        {
            FillPercent = fillPercent,
            Coaches = coaches.Count,
            Min = (int)sorted[0],
            Max = (int)sorted[^1],
            P10 = (int)Fmt.Percentile(sorted, 0.10),
            P50 = (int)Fmt.Percentile(sorted, 0.50),
            P90 = (int)Fmt.Percentile(sorted, 0.90),
            MeanDrift = Fmt.Mean(ratings) - Fmt.Mean(startRatings),
            RankCorrelation = Fmt.RankCorrelation(skills, ratings),
            Moves = moves,
            Denied = denied,
            NeverMovedShare = neverMoved / (double)Math.Max(1, coaches.Count),
            TopReachedTier1Share = reached / (double)decile,
            MedianSeasonsToTier1 = seasonsToTop.Count > 0 ? (int)Fmt.Percentile(seasonsToTop, 0.50) : -1,
        };
    }

    private static double LargestDrift(List<LadderRun> runs)
    {
        double worst = 0;
        foreach (LadderRun r in runs) if (Math.Abs(r.MeanDrift) > Math.Abs(worst)) worst = r.MeanDrift;
        return worst;
    }

    private static int LowestRating(List<LadderRun> runs)
    {
        int lowest = int.MaxValue;
        foreach (LadderRun r in runs) if (r.Min < lowest) lowest = r.Min;
        return lowest;
    }

    private static int TotalSeats(RankedOptions o)
    {
        int seats = 0;
        foreach (int groups in o.GroupsPerTier()) seats += groups * o.GroupSize;
        return seats;
    }

    /// <summary>Seats a newcomer can be placed into: everything below the promotion-only top tier.</summary>
    private static int PlaceableSeats(RankedOptions o)
    {
        IReadOnlyList<int> perTier = o.GroupsPerTier();
        int seats = 0;
        for (int t = 1; t < perTier.Count; t++) seats += perTier[t] * o.GroupSize;
        return seats == 0 ? perTier[0] * o.GroupSize : seats;
    }
}
