using System.Diagnostics;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Fts.BalanceHarness;

/// <summary>
/// DO THE INSTRUCTIONS COUNT? (engine phase 8 — see docs/engine/MATCH_ENGINE_PLAN.md §4.)
///
/// The `pitch` scenario asks whether the match is football. This one asks a different question,
/// and it is the only question phase 8 has: when the coach asks for something, does the picture
/// DO it? The plan states the test in one line — *"tattiche contrapposte devono produrre
/// differenze misurabili … se non le producono, la tattica non conta e la fase non è finita"* —
/// and names three of the readings by hand: a high press wins the ball further up the pitch, a
/// wide side crosses more, a defensive side holds a lower line.
///
/// HOW IT ASKS. One axis at a time, three settings of it, the SAME fixtures each time: the home
/// side is given the instruction, the away side is left neutral, and every reading below is the
/// home side's. Nothing else moves — same clubs, same seeds, same everything — so a difference
/// between the three columns is the instruction and cannot be anything else.
///
/// WHAT IT PROVES, and what it does not. Each axis has ONE reading it is asked to move
/// monotonically, chosen because it is the thing the instruction MEANS rather than a thing it
/// happens to correlate with (how high the block stood; where the ball was won; how quickly it
/// was moved; how much of the play went down the touchline). Those four are checks under
/// --instructions-strict. Everything else is printed to be read: a lever that moves goals is
/// interesting, but goals are the result model's business and a tactic that only reads as a
/// scoreline is exactly the thing phase 6 abolished.
/// </summary>
internal static class InstructionsScenario
{
    public static void Run(HarnessOptions opt, BalanceConfig cfg, CheckList checks)
    {
        Console.WriteLine();
        Console.WriteLine("=== INSTRUCTIONS ===");

        League league = new LeagueGenerator(new LeagueGenerationOptions { ClubCount = 20 }, cfg)
            .Generate(new Pcg32(opt.Seed));
        List<Club> clubs = league.Clubs;

        var clock = Stopwatch.StartNew();
        Console.WriteLine(
            $"[balance-instructions] {opt.InstructionMatches} matches per setting, " +
            "the home side carries the instruction and the away side is neutral");

        Axis mentality = Measure(opt, cfg, clubs, "Mentality", new[] { "defensive", "balanced", "attacking" },
            i => new TacticInstructions((Mentality)i, Pressing.Medium, Tempo.Normal, Width.Normal));

        Axis pressing = Measure(opt, cfg, clubs, "Pressing", new[] { "low", "medium", "high" },
            i => new TacticInstructions(Mentality.Balanced, (Pressing)i, Tempo.Normal, Width.Normal));

        Axis tempo = Measure(opt, cfg, clubs, "Tempo", new[] { "slow", "normal", "fast" },
            i => new TacticInstructions(Mentality.Balanced, Pressing.Medium, (Tempo)i, Width.Normal));

        Axis width = Measure(opt, cfg, clubs, "Width", new[] { "narrow", "normal", "wide" },
            i => new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Normal, (Width)i));

        clock.Stop();

        mentality.Print();
        pressing.Print();
        tempo.Print();
        width.Print();

        Console.WriteLine();
        Console.WriteLine($"  {Fmt.N(clock.Elapsed.TotalMilliseconds / (12.0 * opt.InstructionMatches), 1)} ms per match");

        // --- the four claims ------------------------------------------------------------------
        //
        // Each is "the instruction moves ITS OWN reading, in the right direction, monotonically".
        // A margin is demanded on the extremes so a pair of columns separated by nothing but
        // noise cannot pass: the middle setting only has to sit between them.

        Claim(checks, opt, "a defensive side holds a lower line than an attacking one",
            mentality, a => a.BlockHeightM, "m", ascending: true, minSpan: 3.0);

        Claim(checks, opt, "a high press wins the ball further up the pitch",
            pressing, a => a.RecoveryHeightM, "m", ascending: true, minSpan: 2.0);

        Claim(checks, opt, "a fast side moves the ball on quicker",
            tempo, a => a.PassesPerMatch, "", ascending: true, minSpan: 30.0);

        Claim(checks, opt, "a wide side plays more of it down the touchline",
            width, a => a.CrossesPerMatch, "", ascending: true, minSpan: 1.0);

        if (!opt.InstructionsStrict)
            checks.Info(
                "the four instruction claims are printed above but not asserted - " +
                "run with --instructions-strict to hold them");
    }

    /// <summary>
    /// Registers one axis claim. Under --instructions-strict it is a PASS/FAIL check; otherwise
    /// it is printed for the eye, the same discipline the pitch bands follow.
    /// </summary>
    private static void Claim(
        CheckList checks, HarnessOptions opt, string name, Axis axis,
        Func<Setting, double> read, string unit, bool ascending, double minSpan)
    {
        double lo = read(axis.Settings[0]);
        double mid = read(axis.Settings[1]);
        double hi = read(axis.Settings[2]);

        double span = ascending ? hi - lo : lo - hi;
        bool ordered = ascending
            ? mid >= lo && hi >= mid
            : mid <= lo && hi <= mid;
        bool ok = ordered && span >= minSpan;

        string detail =
            $"{axis.Name} {axis.Labels[0]} {Fmt.N(lo, 1)}{unit} - " +
            $"{axis.Labels[1]} {Fmt.N(mid, 1)}{unit} - " +
            $"{axis.Labels[2]} {Fmt.N(hi, 1)}{unit} " +
            $"(span {Fmt.N(span, 1)}{unit}, wanted {Fmt.N(minSpan, 1)}{unit} and in order)";

        if (opt.InstructionsStrict)
        {
            checks.Check(name, ok, detail);
            return;
        }

        string mark = ok ? " ok " : "WEAK";
        checks.Info($"[{mark}] {name} - {detail}");
    }

    private static Axis Measure(
        HarnessOptions opt, BalanceConfig cfg, List<Club> clubs,
        string axisName, string[] labels, Func<int, TacticInstructions> instructionsFor)
    {
        var axis = new Axis(axisName, labels);

        for (int setting = 0; setting < 3; setting++)
        {
            var totals = new Setting();
            TacticContext home = Context(instructionsFor(setting), cfg);
            TacticContext away = Context(TacticInstructions.Neutral, cfg);
            var tactics = new MatchTactics(home, away);

            // The stream IS the subject, so it is on; condition and fatigue are on because that is
            // what the shipped client runs. Every setting plays the SAME fixtures from the SAME
            // seeds, so nothing but the instruction differs between the three columns.
            var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                applyPositioning: true, generatePositions: true);
            var analyzer = new MatchAnalyzer();

            for (int i = 0; i < opt.InstructionMatches; i++)
            {
                Club h = clubs[(2 * i) % clubs.Count];
                Club a = clubs[(2 * i + 1 + (i / clubs.Count)) % clubs.Count];
                if (h.Id == a.Id) a = clubs[(clubs.IndexOf(a) + 1) % clubs.Count];

                MatchReport report = engine.Simulate(
                    LineupSelector.BestEleven(h), LineupSelector.BestEleven(a),
                    new Pcg32(opt.Seed + 77_000 + (ulong)i), tactics);

                MatchMetrics? metrics = analyzer.Measure(report);
                if (metrics == null || report.Positions == null) continue;

                totals.Add(metrics, report, cfg);
            }

            axis.Settings[setting] = totals;
        }

        return axis;
    }

    private static TacticContext Context(TacticInstructions instructions, BalanceConfig cfg) =>
        new TacticContext(new Tactic(Formation.F433, instructions), cfg.Tactics.FamiliarityMax);

    /// <summary>One axis: its name, its three setting labels and the readings each produced.</summary>
    private sealed class Axis
    {
        public Axis(string name, string[] labels)
        {
            Name = name;
            Labels = labels;
        }

        public string Name { get; }
        public string[] Labels { get; }
        public Setting[] Settings { get; } = { new(), new(), new() };

        public void Print()
        {
            Console.WriteLine();
            Console.WriteLine("  " + Name);
            Console.WriteLine(
                "    " + Pad("setting", -12) + Pad("block", 8) + Pad("recovery", 10) +
                Pad("passes", 8) + Pad("forward", 9) + Pad("crosses", 9) + Pad("width", 8) +
                Pad("shots", 7) + Pad("in box", 8) + Pad("edge", 7) + Pad("long", 7) +
                Pad("goals", 7));

            for (int i = 0; i < 3; i++)
            {
                Setting s = Settings[i];
                Console.WriteLine(
                    "    " + Pad(Labels[i], -12) +
                    Pad(Fmt.N(s.BlockHeightM, 1), 8) +
                    Pad(Fmt.N(s.RecoveryHeightM, 1), 10) +
                    Pad(Fmt.N(s.PassesPerMatch, 0), 8) +
                    Pad(Fmt.N(s.ForwardPassPercent, 0) + "%", 9) +
                    Pad(Fmt.N(s.CrossesPerMatch, 1), 9) +
                    Pad(Fmt.N(s.AttackingWidthM, 1), 8) +
                    Pad(Fmt.N(s.ShotsPerMatch, 1), 7) +
                    Pad(Fmt.N(s.ShotsInBox, 1), 8) +
                    Pad(Fmt.N(s.ShotsEdge, 1), 7) +
                    Pad(Fmt.N(s.ShotsLong, 1), 7) +
                    Pad(Fmt.N(s.GoalsPerMatch, 2), 7));
            }
        }

        /// <summary>Right-aligned in <paramref name="width"/> columns; a negative width left-aligns.</summary>
        private static string Pad(string text, int width) =>
            width < 0 ? text.PadRight(-width) : text.PadLeft(width);
    }

    /// <summary>
    /// What one setting of one axis produced, averaged over the matches. Every reading is the
    /// HOME side's, and every distance is measured from the home side's OWN goal so that "higher"
    /// always means "further up the pitch" without a mirrored copy of the rule.
    /// </summary>
    private sealed class Setting
    {
        private double _blockHeight, _recoveryHeight, _passes, _forward, _crosses, _width;
        private double _shots, _inBox, _edge, _long, _goals;
        private int _matches, _heightSamples, _recoverySamples;

        public double BlockHeightM => Avg(_blockHeight, _heightSamples) / 10.0;
        public double RecoveryHeightM => Avg(_recoveryHeight, _recoverySamples) / 10.0;
        public double PassesPerMatch => Avg(_passes, _matches);
        public double ForwardPassPercent => _passes <= 0 ? 0 : 100.0 * _forward / _passes;
        public double CrossesPerMatch => Avg(_crosses, _matches);
        public double AttackingWidthM => Avg(_width, _matches);
        public double ShotsPerMatch => Avg(_shots, _matches);
        public double ShotsInBox => Avg(_inBox, _matches);
        public double ShotsEdge => Avg(_edge, _matches);
        public double ShotsLong => Avg(_long, _matches);
        public double GoalsPerMatch => Avg(_goals, _matches);

        public void Add(MatchMetrics m, MatchReport report, BalanceConfig cfg)
        {
            _matches++;

            SideMetrics h = m.Home;
            _passes += h.PassesAttempted;
            _crosses += h.Crosses;
            _width += h.Attacking.WidthM;
            _shots += h.Shots;
            _inBox += h.ShotsInBox;
            _edge += h.ShotsEdge;
            _long += h.ShotsLong;
            _goals += report.HomeGoals;

            // HOW HIGH IT STOOD while the other side had the ball — the number the plan says
            // Mentality has to move, and phase 7 already reads it off the finished match.
            if (report.Stats != null)
            {
                _blockHeight += report.Stats.Home.DefendingHeightDm;
                _heightSamples++;
            }

            ReadStream(report.Positions!, cfg);
        }

        /// <summary>
        /// The two readings the finished report does not already carry: WHERE the home side won
        /// the ball back, and how much of its passing went forward. Both are read off the picture
        /// after the match, drawing nothing and touching nothing — the contract every measuring
        /// instrument in this rework has kept since phase 0.
        /// </summary>
        private void ReadStream(PositionStream stream, BalanceConfig cfg)
        {
            foreach (BallAction a in stream.Actions)
            {
                if (!a.Home) continue;

                switch (a.Kind)
                {
                    case BallActionKind.Recovery:
                    case BallActionKind.Interception:
                        {
                            PitchPoint ball = stream.BallAt(a.Tick);
                            _recoveryHeight += ball.X;   // home attacks toward the far goal
                            _recoverySamples++;
                            break;
                        }

                    case BallActionKind.Pass:
                    case BallActionKind.LongBall:
                    case BallActionKind.Cross:
                        {
                            if (a.Slot < 0 || a.TargetSlot < 0) break;
                            PitchPoint from = stream.HomeAt(a.Tick, a.Slot);
                            PitchPoint to = stream.HomeAt(a.Tick, a.TargetSlot);
                            if (to.X - from.X > cfg.Match.BackPassMinDm) _forward++;
                            break;
                        }
                }
            }
        }

        private static double Avg(double total, int n) => n <= 0 ? 0 : total / n;
    }
}
