using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Fts.BalanceHarness;

/// <summary>
/// "Tactic pick/win rates" without live players: with nobody to observe, the honest proxy for what
/// coaches would PICK is what WINS - a tactic that beats the field is the tactic everyone converges on.
///
/// Three measurements:
///   1. the instruction field (81 combos on a fixed shape) played round-robin on equal squads with the
///      LIVE engine flags (condition + within-match fatigue), which is what the task-3.2 sweep never
///      covered - it used the bare engine;
///   2. the formation field (6 shapes, neutral instructions), the axis 3.2 never swept at all;
///   3. what the choice is WORTH: the field's best tactic given to a mid club over full seasons,
///      against the same club playing neutral. A tactic system nobody can abuse is only half the goal;
///      the other half is that picking well is worth something.
/// </summary>
internal static class TacticsScenario
{
    public static void Run(HarnessOptions opt, BalanceConfig cfg, CheckList checks)
    {
        Console.WriteLine();
        Console.WriteLine("=== TACTICS ===");

        // Equal squads on both sides of every duel, so only the tactic decides - but the squad ROTATES
        // across the league from duel to duel. Run 1 swept a single club and measured that club's squad
        // as much as the shapes: with its strong strikers and thin wings, two-forward shapes looked
        // better than they are (the league mean ranked the same shapes the other way round). Cycling the
        // clubs costs no extra matches and takes one squad's composition out of the answer.
        League league = new LeagueGenerator(new LeagueGenerationOptions { ClubCount = 20 }, cfg)
            .Generate(new Pcg32(opt.Seed));
        List<Club> clubs = league.Clubs;
        int famMax = cfg.Tactics.FamiliarityMax;

        // --- 1. the instruction field -------------------------------------------------------------
        var instructions = new List<TacticInstructions>();
        foreach (Mentality me in new[] { Mentality.Defensive, Mentality.Balanced, Mentality.Attacking })
            foreach (Pressing pr in new[] { Pressing.Low, Pressing.Medium, Pressing.High })
                foreach (Tempo te in new[] { Tempo.Slow, Tempo.Normal, Tempo.Fast })
                    foreach (Width wi in new[] { Width.Narrow, Width.Normal, Width.Wide })
                        instructions.Add(new TacticInstructions(me, pr, te, wi));

        var pool = new List<Tactic>(instructions.Count);
        foreach (TacticInstructions ins in instructions) pool.Add(new Tactic(Formation.F433, ins));

        FieldResult field = PlayField(pool, clubs, cfg, famMax, opt.TacticRepeats, opt.Seed + 5_000);
        Console.WriteLine(
            $"[balance-tactics] instruction field: {pool.Count} tactics, {field.GamesEach} games each " +
            $"({field.TotalMatches} matches, live condition+fatigue)");
        PrintLeaders(field, pool, "  ", 5);

        // --- 2. the formation field ---------------------------------------------------------------
        var shapes = new List<Tactic>();
        foreach (Formation f in Formations.All) shapes.Add(new Tactic(f, TacticInstructions.Neutral));

        FieldResult shapeField = PlayField(shapes, clubs, cfg, famMax, opt.FormationRepeats, opt.Seed + 9_000);
        Console.WriteLine(
            $"[balance-formations] formation field: {shapes.Count} shapes, {shapeField.GamesEach} games each " +
            $"({shapeField.TotalMatches} matches, neutral instructions, squads rotating over {clubs.Count} clubs)");
        for (int i = 0; i < shapes.Count; i++)
            Console.WriteLine(
                $"  {shapes[i].Formation,-8} win {Fmt.Pct(shapeField.WinRate(i))}  " +
                $"points share {Fmt.Pct(shapeField.PointsShare(i))}");

        // --- 2b. WHY a formation wins or loses ----------------------------------------------------
        // The engine reads three numbers off a lineup (TeamRatings): the AVERAGE rating of the defence
        // bucket, of the midfield bucket and of the attack bucket. Averages, not sums - so a shape that
        // fields FEWER bodies in a bucket fills it with the best of that pool and scores higher, at no
        // cost. If that is what separates the shapes, the fix is in the aggregation, not the geometry.
        // Now that the field rotates through every club, the league mean IS the number that decided it.
        PrintBucketRatings($"league mean ({clubs.Count} clubs, the squads that played the field)", clubs);
        PrintBucketRatings("one club alone (why a single-squad sweep misleads)", new List<Club> { clubs[9] });

        // --- 3. what the choice is worth ----------------------------------------------------------
        Tactic best = pool[field.BestIndex];
        (double bestPts, double neutralPts) = SeasonValue(opt, cfg, best, famMax);
        double delta = bestPts - neutralPts;
        Console.WriteLine(
            $"[balance-tactics-season] {opt.TacticSeasons} seasons, mid club: best tactic " +
            $"({Describe(best)}) {Fmt.N(bestPts, 1)} pts/season vs neutral {Fmt.N(neutralPts, 1)} " +
            $"(delta {(delta >= 0 ? "+" : "")}{Fmt.N(delta, 1)})");

        // --- checks -------------------------------------------------------------------------------
        checks.Check(
            "no dominant instruction set",
            field.BestWinRate < 0.56,
            $"top {Describe(pool[field.BestIndex])} wins {Fmt.Pct(field.BestWinRate)} of its games (cap 56%)");

        checks.Check(
            "no broken instruction set",
            field.WorstPointsShare > 0.35,
            $"worst {Describe(pool[field.WorstIndex])} takes {Fmt.Pct(field.WorstPointsShare)} of the points on offer (floor 35%)");

        checks.Check(
            "no dominant formation",
            shapeField.BestWinRate < 0.56,
            $"top {shapes[shapeField.BestIndex].Formation} wins {Fmt.Pct(shapeField.BestWinRate)} of its games (cap 56%)");

        checks.Check(
            "every formation is viable",
            shapeField.WorstPointsShare > 0.38,
            $"worst {shapes[shapeField.WorstIndex].Formation} takes {Fmt.Pct(shapeField.WorstPointsShare)} of the points on offer (floor 38%)");

        checks.Info(
            $"picking the field's best tactic is worth {(delta >= 0 ? "+" : "")}{Fmt.N(delta, 1)} pts/season to a mid club " +
            "- too small and the tactics screen is decoration, too large and it is the only thing that matters");
        checks.Info(
            $"spread of the instruction field: best {Fmt.Pct(field.BestWinRate)} vs worst {Fmt.Pct(field.WorstWinRate)} win rate");
        double shapeError = 0.5 / Math.Sqrt(Math.Max(1, shapeField.GamesEach));
        checks.Info(
            $"formations: {shapes[shapeField.BestPointsIndex].Formation} best at {Fmt.Pct(shapeField.BestPointsShare)} " +
            $"of the points on offer, {shapes[shapeField.WorstIndex].Formation} worst at {Fmt.Pct(shapeField.WorstPointsShare)} " +
            $"(the default F433 takes {Fmt.Pct(shapeField.PointsShare(0))}) - a spread of {Fmt.Pct(shapeField.PointsSpread)} " +
            $"against a standard error near {Fmt.Pct(shapeError)} per shape, i.e. " +
            $"{Fmt.N(shapeField.PointsSpread / Math.Max(0.0001, shapeError), 1)} standard errors end to end. " +
            "Under two, the shapes are indistinguishable and there is nothing to fix. Above three, look at the " +
            "bucket averages: TeamRatings AVERAGES each bucket, so a shape fielding fewer bodies in one fills it " +
            "with the best of that pool at no cost, and the fix would belong in the aggregation, not in the shapes.");
    }

    // --- the field round-robin -------------------------------------------------------------------

    private sealed class FieldResult
    {
        public int[] Wins = Array.Empty<int>();
        public int[] Draws = Array.Empty<int>();
        public int[] Games = Array.Empty<int>();
        public int GamesEach;
        public int TotalMatches;
        public int BestIndex;        // by win rate (the acceptance metric)
        public int WorstIndex;       // by points share
        public int BestPointsIndex;  // by points share
        public double BestWinRate;
        public double WorstWinRate;
        public double BestPointsShare;
        public double WorstPointsShare;

        /// <summary>Best minus worst points share - the honest "how far apart are they" number, both ends
        /// measured on the SAME metric.</summary>
        public double PointsSpread => BestPointsShare - WorstPointsShare;

        public double WinRate(int i) => Games[i] > 0 ? (double)Wins[i] / Games[i] : 0;
        public double PointsShare(int i) => Games[i] > 0 ? (Wins[i] + 0.5 * Draws[i]) / Games[i] : 0.5;
    }

    private static FieldResult PlayField(
        IReadOnlyList<Tactic> pool, IReadOnlyList<Club> clubs, BalanceConfig cfg, int famMax,
        int repeats, ulong seedBase)
    {
        int n = pool.Count;
        var r = new FieldResult
        {
            Wins = new int[n],
            Draws = new int[n],
            Games = new int[n],
        };

        // The live engine: condition and within-match fatigue are on in the shipped client, so the
        // field is swept the way matches are actually resolved. Equal squads means both sides carry
        // the same condition, so nothing here favours one side but the tactic.
        // The harness reads goals and events only, and runs many thousands of matches:
        // skip the movement stream (13.1). Each match gets its own RNG, so the results
        // it measures are byte-identical either way.
        var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true, generatePositions: false);

        // One eleven per (squad, shape), picked once. Two instances per pair - the same eleven either
        // way, but never the same object on both sides of a match.
        var lineupsA = new Dictionary<(int ClubId, Formation Shape), Lineup>();
        var lineupsB = new Dictionary<(int ClubId, Formation Shape), Lineup>();
        foreach (Club c in clubs)
        {
            foreach (Tactic t in pool)
            {
                var key = (c.Id, t.Formation);
                if (lineupsA.ContainsKey(key)) continue;
                lineupsA[key] = LineupSelector.BestEleven(c, t.Formation);
                lineupsB[key] = LineupSelector.BestEleven(c, t.Formation);
            }
        }

        for (int a = 0; a < n; a++)
        {
            for (int b = 0; b < n; b++)
            {
                if (a == b) continue;
                for (int rep = 0; rep < repeats; rep++)
                {
                    // A alternates home and away so the home advantage cancels out over the field.
                    bool aHome = ((a + b + rep) & 1) == 0;
                    ulong seed = seedBase + (ulong)((a * n + b) * repeats + rep);

                    // Both sides field the SAME squad (so only the tactic differs), but which squad
                    // rotates duel by duel, so no single composition can colour the answer.
                    Club club = clubs[((a * n + b) * repeats + rep) % clubs.Count];

                    var ca = new TacticContext(pool[a], famMax);
                    var cb = new TacticContext(pool[b], famMax);
                    Lineup la = lineupsA[(club.Id, pool[a].Formation)];
                    Lineup lb = lineupsB[(club.Id, pool[b].Formation)];

                    int aGoals, bGoals;
                    if (aHome)
                    {
                        MatchReport rep1 = engine.Simulate(la, lb, new Pcg32(seed), new MatchTactics(ca, cb));
                        aGoals = rep1.HomeGoals; bGoals = rep1.AwayGoals;
                    }
                    else
                    {
                        MatchReport rep2 = engine.Simulate(lb, la, new Pcg32(seed), new MatchTactics(cb, ca));
                        aGoals = rep2.AwayGoals; bGoals = rep2.HomeGoals;
                    }

                    r.Games[a]++;
                    r.TotalMatches++;
                    if (aGoals > bGoals) r.Wins[a]++;
                    else if (aGoals == bGoals) r.Draws[a]++;
                }
            }
        }

        r.GamesEach = n > 0 ? r.Games[0] : 0;
        r.BestWinRate = 0; r.WorstWinRate = 1; r.WorstPointsShare = 1; r.BestPointsShare = 0;
        for (int i = 0; i < n; i++)
        {
            double win = r.WinRate(i);
            double pts = r.PointsShare(i);
            if (win > r.BestWinRate) { r.BestWinRate = win; r.BestIndex = i; }
            if (win < r.WorstWinRate) r.WorstWinRate = win;
            if (pts < r.WorstPointsShare) { r.WorstPointsShare = pts; r.WorstIndex = i; }
            if (pts > r.BestPointsShare) { r.BestPointsShare = pts; r.BestPointsIndex = i; }
        }

        return r;
    }

    private static void PrintLeaders(FieldResult r, IReadOnlyList<Tactic> pool, string indent, int count)
    {
        var order = new List<int>();
        for (int i = 0; i < pool.Count; i++) order.Add(i);
        order.Sort((x, y) => r.WinRate(y).CompareTo(r.WinRate(x)));

        for (int i = 0; i < count && i < order.Count; i++)
        {
            int idx = order[i];
            Console.WriteLine($"{indent}#{i + 1} {Describe(pool[idx]),-38} win {Fmt.Pct(r.WinRate(idx))}  points share {Fmt.Pct(r.PointsShare(idx))}");
        }
        for (int i = Math.Max(0, order.Count - 2); i < order.Count; i++)
        {
            int idx = order[i];
            Console.WriteLine($"{indent}#{i + 1} {Describe(pool[idx]),-38} win {Fmt.Pct(r.WinRate(idx))}  points share {Fmt.Pct(r.PointsShare(idx))}");
        }
    }

    // --- what a good tactic is worth over a season ------------------------------------------------

    private static (double Best, double Neutral) SeasonValue(
        HarnessOptions opt, BalanceConfig cfg, Tactic best, int famMax)
    {
        var lab = new WorldLab(cfg);
        double bestTotal = 0, neutralTotal = 0;

        for (int s = 0; s < opt.TacticSeasons; s++)
        {
            ulong seed = opt.Seed + 20_000 + (ulong)s;

            LabWorld w1 = lab.NewWorld(seed, divisions: 1);
            int clubId = w1.Leagues[0].Clubs[9].Id;
            var withBest = new Dictionary<int, TacticContext> { [clubId] = new TacticContext(best, famMax) };
            bestTotal += lab.RunSeason(w1, clubId, tactics: withBest).UserPoints;

            LabWorld w2 = lab.NewWorld(seed, divisions: 1);
            neutralTotal += lab.RunSeason(w2, clubId).UserPoints;
        }

        int n = Math.Max(1, opt.TacticSeasons);
        return (bestTotal / n, neutralTotal / n);
    }

    /// <summary>
    /// The three numbers the engine actually reads off each shape's best XI, per club set. Printed next to
    /// the bodies in each bucket, because that is the suspected mechanism: TeamRatings AVERAGES a bucket,
    /// so fewer bodies means the best of a pool and no cost for leaving a line thin.
    /// </summary>
    private static void PrintBucketRatings(string label, IReadOnlyList<Club> clubs)
    {
        Console.WriteLine($"[balance-formation-ratings] {label}:");
        foreach (Formation f in Formations.All)
        {
            double att = 0, mid = 0, def = 0;
            foreach (Club c in clubs)
            {
                TeamRatings r = TeamRatings.From(LineupSelector.BestEleven(c, f));
                att += r.Attack; mid += r.Midfield; def += r.Defense;
            }

            int n = Math.Max(1, clubs.Count);
            CountBodies(f, out int defBodies, out int midBodies, out int attBodies);
            Console.WriteLine(
                $"  {f,-8} att {Fmt.N(att / n, 1)}  mid {Fmt.N(mid / n, 1)}  def {Fmt.N(def / n, 1)}   " +
                $"bodies {defBodies}-{midBodies}-{attBodies}");
        }
    }

    /// <summary>How many outfield bodies a shape puts in each of the engine's three buckets.</summary>
    private static void CountBodies(Formation formation, out int defence, out int midfield, out int attack)
    {
        defence = midfield = attack = 0;
        foreach (PositionRole role in Formations.Roles(formation))
        {
            switch (role)
            {
                case PositionRole.Goalkeeper: break;
                case PositionRole.CentreBack:
                case PositionRole.FullBack: defence++; break;
                case PositionRole.DefensiveMidfielder:
                case PositionRole.CentralMidfielder:
                case PositionRole.AttackingMidfielder: midfield++; break;
                default: attack++; break;
            }
        }
    }

    private static string Describe(Tactic t) =>
        $"{t.Formation}/{t.Instructions.Mentality}/{t.Instructions.Pressing}/{t.Instructions.Tempo}/{t.Instructions.Width}";
}
