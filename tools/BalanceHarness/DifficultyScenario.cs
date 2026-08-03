using Sim.Core.Config;
using Sim.Core.Difficulty;
using Sim.Core.Domain;

namespace Fts.BalanceHarness;

/// <summary>
/// The difficulty curve. Task 5.7 validated the three levels on the BARE engine (no condition, no market)
/// and got a clean, monotone separation; run 1 of this harness ran them inside the LIVE loop and the lever
/// came out non-monotone. Rather than guess which live ingredient did it, this scenario runs the same
/// three levels in five configurations and lets the columns say it:
///
///   A control (5.7)        no market, no condition   - validates the difficulty settings themselves
///   B market only          market on,  condition off - is it the transfer market?
///   C condition only       market off, condition on  - is it condition/fatigue?
///   D full live            both on, the club trades  - what an engaged player meets
///   E full live, passive   both on, club sits it out - what run 1 measured
///
/// The suspicion worth naming: <c>LineupSelector.CompetentEleven</c> degrades an AI lineup by slipping a
/// rank down per missed roll, which with live condition is indistinguishable from ROTATING the squad - and
/// rotation is worth real points (harness 4.2). If that is what happens, a lower difficulty is
/// accidentally handing the AI fresher legs, and the two effects fight each other. Column C decides it.
///
/// The user's own behaviour is identical everywhere: best XI, neutral tactics, no in-match intervention.
/// </summary>
internal static class DifficultyScenario
{
    private const int UserClubIndex = 9; // a mid-strength club, as in the 5.7 harness

    private sealed class Variant
    {
        public string Name = string.Empty;
        public bool Market;
        public bool Condition;
        public bool ClubTrades;
        public double[] Points = new double[3];
        public double[] Positions = new double[3];
        public int[] Titles = new int[3];
        public int[] Top4 = new int[3];

        public double Gap => Points[0] - Points[2];
        public bool Monotone => Points[0] > Points[1] && Points[1] > Points[2];
    }

    public static void Run(HarnessOptions opt, BalanceConfig cfg, CheckList checks)
    {
        Console.WriteLine();
        Console.WriteLine("=== DIFFICULTY ===");

        var levels = new[] { DifficultyLevel.Easy, DifficultyLevel.Normal, DifficultyLevel.Hard };
        var variants = new[]
        {
            new Variant { Name = "A control (5.7)",     Market = false, Condition = false, ClubTrades = false },
            new Variant { Name = "B market only",       Market = true,  Condition = false, ClubTrades = true  },
            new Variant { Name = "C condition only",    Market = false, Condition = true,  ClubTrades = false },
            new Variant { Name = "D full live",         Market = true,  Condition = true,  ClubTrades = true  },
            new Variant { Name = "E full live, passive", Market = true, Condition = true,  ClubTrades = false },
        };

        foreach (Variant v in variants)
        {
            for (int l = 0; l < levels.Length; l++)
            {
                int totalPoints = 0, totalPosition = 0;
                for (int s = 0; s < opt.DifficultySeasons; s++)
                {
                    // Every level and every variant replays the SAME worlds: the columns differ only in
                    // what is switched on, and the rows only in the AI.
                    ulong seed = opt.Seed + 300_000 + (ulong)s;
                    var lab = new WorldLab(cfg, liveCondition: v.Condition, market: v.Market);
                    LabWorld world = lab.NewWorld(seed, divisions: 1);
                    Club user = world.Leagues[0].Clubs[UserClubIndex];

                    DifficultySettings settings = DifficultyModel.Resolve(levels[l], cfg);
                    DifficultyContext ctx = DifficultyModel.MatchContext(user.Id, settings);

                    SeasonResult r = lab.RunSeason(world, user.Id, ctx, settings, watchedClubTrades: v.ClubTrades);
                    totalPoints += r.UserPoints;
                    totalPosition += r.UserPosition;
                    if (r.UserPosition == 1) v.Titles[l]++;
                    if (r.UserPosition is >= 1 and <= 4) v.Top4[l]++;
                }

                int n = Math.Max(1, opt.DifficultySeasons);
                v.Points[l] = totalPoints / (double)n;
                v.Positions[l] = totalPosition / (double)n;
            }

            Console.WriteLine(
                $"[balance-difficulty] {v.Name,-20} Easy {Fmt.N(v.Points[0], 1),5} | Normal {Fmt.N(v.Points[1], 1),5} | " +
                $"Hard {Fmt.N(v.Points[2], 1),5} pts/season   gap {Fmt.N(v.Gap, 1),5}  " +
                $"{(v.Monotone ? "monotone" : "NOT monotone")}");
            Console.WriteLine(
                $"[balance-difficulty] {v.Name,-20} avg finish {Fmt.N(v.Positions[0], 1)}/{Fmt.N(v.Positions[1], 1)}/" +
                $"{Fmt.N(v.Positions[2], 1)}, titles {v.Titles[0]}/{v.Titles[1]}/{v.Titles[2]}, " +
                $"top-4 {v.Top4[0]}/{v.Top4[1]}/{v.Top4[2]} (of {opt.DifficultySeasons} seasons each)");
        }

        Variant control = variants[0];
        Variant marketOnly = variants[1];
        Variant conditionOnly = variants[2];
        Variant live = variants[3];
        Variant passive = variants[4];

        Console.WriteLine(
            $"[balance-difficulty] where the lever breaks: control {Fmt.N(control.Gap, 1)} -> " +
            $"market-only {Fmt.N(marketOnly.Gap, 1)} -> condition-only {Fmt.N(conditionOnly.Gap, 1)} -> " +
            $"full live {Fmt.N(live.Gap, 1)} (Easy-Hard points; a gap that collapses names the culprit)");

        // --- checks -------------------------------------------------------------------------------
        // The control validates the SETTINGS: if this fails, DifficultyBalance itself is wrong.
        checks.Check(
            "the difficulty settings separate the levels (bare engine, 5.7 control)",
            control.Monotone && control.Gap >= 4.0,
            $"Easy {Fmt.N(control.Points[0], 1)} > Normal {Fmt.N(control.Points[1], 1)} > Hard {Fmt.N(control.Points[2], 1)}, " +
            $"gap {Fmt.N(control.Gap, 1)} (floor 4)");

        // The live loop is what a player actually meets: if the lever inverts here, the levels are a lie
        // however well the settings behave in a laboratory.
        checks.Check(
            "the difficulty lever survives the live loop",
            live.Monotone && live.Gap >= 4.0,
            $"Easy {Fmt.N(live.Points[0], 1)} / Normal {Fmt.N(live.Points[1], 1)} / Hard {Fmt.N(live.Points[2], 1)}, " +
            $"gap {Fmt.N(live.Gap, 1)} (floor 4)");

        // "Winnable" cannot mean "a club with no human at the wheel wins anyway". What Hard must not do is
        // CRUSH a mid squad: the 10th-strongest club of twenty should still finish somewhere near its own
        // rank, not be shoved into the relegation zone by difficulty alone.
        const double MidTableFloor = 14.0;
        checks.Check(
            "Hard does not crush a mid-table club",
            live.Positions[2] <= MidTableFloor,
            $"the 10th-strongest club averages {Fmt.N(live.Positions[2], 1)} on Hard in the live loop " +
            $"(floor {Fmt.N(MidTableFloor, 0)} of 20; it finished top-4 {live.Top4[2]}/{opt.DifficultySeasons} times)");

        checks.Info(
            $"isolation: control {Fmt.N(control.Gap, 1)} | market-only {Fmt.N(marketOnly.Gap, 1)} | " +
            $"condition-only {Fmt.N(conditionOnly.Gap, 1)} | full live {Fmt.N(live.Gap, 1)} | live+passive club " +
            $"{Fmt.N(passive.Gap, 1)}. A gap that survives the control but dies in condition-only points at " +
            "CompetentEleven: degrading an AI lineup rotates it, and rotation buys freshness (4.2). A gap " +
            "that dies in market-only points at the AI kitties instead.");
        checks.Info(
            $"passive vs trading watched club under the full live loop: gap {Fmt.N(passive.Gap, 1)} vs " +
            $"{Fmt.N(live.Gap, 1)} - the difference is how much of run 1's inversion was the harness leaving " +
            "the watched club out of the market rather than the game.");
        checks.Info(
            $"average finish for a mid (10th-strongest) club under the full live loop: Easy {Fmt.N(live.Positions[0], 1)}, " +
            $"Normal {Fmt.N(live.Positions[1], 1)}, Hard {Fmt.N(live.Positions[2], 1)} - Normal is the level a new " +
            "player meets, so its number is the one that sets first impressions.");
    }
}
