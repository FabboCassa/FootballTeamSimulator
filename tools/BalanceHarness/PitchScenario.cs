using System.Diagnostics;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;

namespace Fts.BalanceHarness;

/// <summary>
/// What the match LOOKS like, in numbers (phase 0 of the match engine rework — see
/// docs/engine/MATCH_ENGINE_PLAN.md).
///
/// The other scenarios ask whether the results are fair. This one asks a different question: is the
/// thing the player watches football? "The movement makes no sense" is not something you can hold a
/// rewrite to; a back line spread over eighteen metres, a block that never widens past twenty, and
/// forty percent of the match spent inside three metres of an opponent are.
///
/// So every reading here is printed against the band real football produces, and the run says how
/// many bands it is inside. Today it is inside very few — that is the baseline the eight phases are
/// measured against, and it is meant to be read, not to be passed. The bands only become PASS/FAIL
/// checks under --pitch-strict, which is how a finished phase locks its gain in.
///
/// Three readings are checks from the start, because they are contract, not calibration: the
/// picture and the result must agree on the score, every match must produce a stream, and a ball a
/// player is holding must never be sitting on a line of the pitch (the laws call that a throw-in;
/// the engine currently clamps the carrier back inside instead, and this is that bug, counted).
/// </summary>
internal static class PitchScenario
{
    public static void Run(HarnessOptions opt, BalanceConfig cfg, CheckList checks)
    {
        Console.WriteLine();
        Console.WriteLine("=== PITCH ===");

        League league = new LeagueGenerator(new LeagueGenerationOptions { ClubCount = 20 }, cfg)
            .Generate(new Pcg32(opt.Seed));
        List<Club> clubs = league.Clubs;

        // The engine the shipped client runs (SeasonProgressor's flags), with the position stream on
        // because the stream IS the subject here. Tactics are left null so the baseline measures the
        // engine rather than a particular set of instructions.
        var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
            applyPositioning: true, generatePositions: true);

        var analyzer = new MatchAnalyzer();
        var totals = new PitchTotals();
        var clock = Stopwatch.StartNew();
        int played = 0, streamless = 0, disagreements = 0;

        for (int i = 0; i < opt.PitchMatches; i++)
        {
            Club home = clubs[(2 * i) % clubs.Count];
            Club away = clubs[(2 * i + 1 + (i / clubs.Count)) % clubs.Count];
            if (home.Id == away.Id) away = clubs[(clubs.IndexOf(away) + 1) % clubs.Count];

            Lineup homeXI = LineupSelector.BestEleven(home);
            Lineup awayXI = LineupSelector.BestEleven(away);
            MatchReport report = engine.Simulate(homeXI, awayXI, new Pcg32(opt.Seed + 31_000 + (ulong)i));

            if (opt.PitchDump != null && i == opt.PitchDumpMatch && report.Positions != null)
            {
                PitchDump.Write(opt.PitchDump, report, homeXI, awayXI, home.Name, away.Name);
                Console.WriteLine($"[balance-pitch] match {i} written to {opt.PitchDump} - open it in a browser");
            }

            MatchMetrics? metrics = analyzer.Measure(report);
            if (metrics == null)
            {
                streamless++;
                continue;
            }

            if (!metrics.GoalsAgree) disagreements++;
            totals.Add(metrics);
            played++;
        }

        clock.Stop();
        if (played == 0)
        {
            checks.Check("the pitch scenario produced streams", false, "no match returned a position stream");
            return;
        }

        double msPerMatch = clock.Elapsed.TotalMilliseconds / opt.PitchMatches;
        Console.WriteLine(
            $"[balance-pitch] {opt.PitchMatches} matches, {clubs.Count} clubs, neutral tactics, " +
            $"{Fmt.N(msPerMatch, 1)} ms/match (simulate + measure)");

        var bands = new TargetList();
        totals.Print(played, bands);

        Console.WriteLine();
        Console.WriteLine($"--- pitch: against real football ({played} matches) ---");
        bands.Print();

        // --- contract, not calibration -------------------------------------------------------
        checks.Check(
            "every match produced a position stream",
            streamless == 0,
            $"{streamless} of {opt.PitchMatches} matches came back without one");

        checks.Check(
            "the picture and the result agree on the score",
            disagreements == 0,
            $"{disagreements} of {played} matches put a different number of goals in the stream than in the report");

        checks.Check(
            "a held ball is never sitting on a line of the pitch",
            totals.BallOnLineWhileHeldTicks == 0,
            $"{Fmt.N(totals.BallOnLineWhileHeldTicks / (double)played, 1)} ticks per match with a player holding " +
            "the ball on a boundary - the laws call that a throw-in or a goal kick");

        checks.Info(
            $"simulation cost {Fmt.N(msPerMatch, 1)} ms per match at {cfg.Match.TicksPerMinute} ticks/minute; " +
            "phase 1 raises the tick rate, so this is the number that must stay affordable");

        if (opt.PitchStrict)
        {
            bands.Register(checks);
            return;
        }

        checks.Info(
            $"{bands.Met}/{bands.Count} readings are inside the band real football produces - " +
            "run with --pitch-strict once a phase claims to have closed them");
    }
}

/// <summary>
/// One reading, the band real football keeps it in, and whether we are inside it. Printed rather
/// than asserted by default: phase 0 measures, it does not fix.
/// </summary>
internal sealed class TargetList
{
    private readonly List<(string Name, double Value, double Lo, double Hi, string Unit)> _rows = new();

    public void Add(string name, double value, double lo, double hi, string unit = "")
        => _rows.Add((name, value, lo, hi, unit));

    public int Count => _rows.Count;

    public int Met
    {
        get
        {
            int n = 0;
            foreach (var r in _rows) if (r.Value >= r.Lo && r.Value <= r.Hi) n++;
            return n;
        }
    }

    public void Print()
    {
        foreach (var r in _rows)
        {
            bool ok = r.Value >= r.Lo && r.Value <= r.Hi;
            string arrow = ok ? "  ok " : (r.Value < r.Lo ? " LOW " : "HIGH ");
            Console.WriteLine(
                $"  [{arrow}] {r.Name,-38} {Fmt.N(r.Value, 1),8}{r.Unit,-4}  " +
                $"want {Fmt.N(r.Lo, 1)}-{Fmt.N(r.Hi, 1)}{r.Unit}");
        }

        Console.WriteLine($"  {Met}/{Count} inside the band");
    }

    /// <summary>Turns every band into a real check. Used by --pitch-strict.</summary>
    public void Register(CheckList checks)
    {
        foreach (var r in _rows)
            checks.Check(
                r.Name,
                r.Value >= r.Lo && r.Value <= r.Hi,
                $"{Fmt.N(r.Value, 1)}{r.Unit} against {Fmt.N(r.Lo, 1)}-{Fmt.N(r.Hi, 1)}{r.Unit}");
    }
}

/// <summary>Sums of every reading over the matches played, and the printout of their averages.</summary>
internal sealed class PitchTotals
{
    private double _goals, _shots, _onTarget, _passes, _completed, _longBalls, _crosses;
    private double _dribbles, _clearances, _tackles, _interceptions;
    private double _throwIns, _corners, _goalKicks, _offsides, _fouls;
    private double _homePossession, _loose, _homeThird, _middleThird, _awayThird, _ticks;
    private double _kmPerPlayer, _kmMax, _kmMin;

    private double _defWidth, _defDepth, _defBackLine, _defGap, _defMate, _defMarked;
    private double _attWidth, _attDepth, _attBackLine, _attGap, _attMate, _attMarked;
    private int _shapeSamples;

    public long BallOnLineWhileHeldTicks { get; private set; }

    public void Add(MatchMetrics m)
    {
        SideMetrics h = m.Home, a = m.Away;

        _goals += m.TotalGoals;
        _shots += m.TotalShots;
        _onTarget += h.ShotsOnTarget + a.ShotsOnTarget;
        _passes += m.TotalPasses;
        _completed += h.PassesCompleted + a.PassesCompleted;
        _longBalls += h.LongBalls + a.LongBalls;
        _crosses += h.Crosses + a.Crosses;
        _dribbles += h.Dribbles + a.Dribbles;
        _clearances += h.Clearances + a.Clearances;
        _tackles += h.TacklesWon + a.TacklesWon;
        _interceptions += h.Interceptions + a.Interceptions;
        _throwIns += m.TotalThrowIns;
        _corners += m.TotalCorners;
        _goalKicks += h.GoalKicks + a.GoalKicks;
        _offsides += m.TotalOffsides;
        _fouls += m.TotalFouls;

        _homePossession += m.HomePossessionPercent;
        _loose += m.LoosePercent;
        _ticks += m.Ticks;
        double thirds = Math.Max(1, m.HomeThirdTicks + m.MiddleThirdTicks + m.AwayThirdTicks);
        _homeThird += 100.0 * m.HomeThirdTicks / thirds;
        _middleThird += 100.0 * m.MiddleThirdTicks / thirds;
        _awayThird += 100.0 * m.AwayThirdTicks / thirds;

        int n = Math.Max(1, m.PlayerCount);
        _kmPerPlayer += (h.DistancePerPlayerKm(n) + a.DistancePerPlayerKm(n)) / 2;
        _kmMax += (h.MaxPlayerDistanceKm + a.MaxPlayerDistanceKm) / 2;
        _kmMin += (h.MinPlayerDistanceKm + a.MinPlayerDistanceKm) / 2;

        BallOnLineWhileHeldTicks += m.BallOnLineWhileHeldTicks;

        AddShape(h.Defending, a.Defending, ref _defWidth, ref _defDepth, ref _defBackLine, ref _defGap, ref _defMate, ref _defMarked);
        AddShape(h.Attacking, a.Attacking, ref _attWidth, ref _attDepth, ref _attBackLine, ref _attGap, ref _attMate, ref _attMarked);
        _shapeSamples++;
    }

    private static void AddShape(
        ShapeMetrics h, ShapeMetrics a,
        ref double width, ref double depth, ref double backLine, ref double gap, ref double mate, ref double marked)
    {
        width += (h.WidthM + a.WidthM) / 2;
        depth += (h.DepthM + a.DepthM) / 2;
        backLine += (h.BackLineSpreadM + a.BackLineSpreadM) / 2;
        gap += (h.LargestLineGapM + a.LargestLineGapM) / 2;
        mate += (h.NearestTeammateM + a.NearestTeammateM) / 2;
        marked += (h.WithinThreeMetresOfOpponentPercent + a.WithinThreeMetresOfOpponentPercent) / 2;
    }

    public void Print(int played, TargetList bands)
    {
        double n = played;

        Console.WriteLine();
        Console.WriteLine("  what happened (per match, both sides together)");
        Console.WriteLine($"    goals {Fmt.N(_goals / n, 2)}   shots {Fmt.N(_shots / n, 1)} " +
                          $"({Fmt.N(_onTarget / n, 1)} on target)   " +
                          $"passes {Fmt.N(_passes / n, 0)} at {Fmt.N(100.0 * _completed / Math.Max(1, _passes), 1)}% accuracy");
        Console.WriteLine($"    long balls {Fmt.N(_longBalls / n, 1)}   crosses {Fmt.N(_crosses / n, 1)}   " +
                          $"dribbles {Fmt.N(_dribbles / n, 1)}   clearances {Fmt.N(_clearances / n, 1)}");
        Console.WriteLine($"    tackles won {Fmt.N(_tackles / n, 1)}   interceptions {Fmt.N(_interceptions / n, 1)}");
        Console.WriteLine($"    throw-ins {Fmt.N(_throwIns / n, 1)}   corners {Fmt.N(_corners / n, 1)}   " +
                          $"goal kicks {Fmt.N(_goalKicks / n, 1)}   offsides {Fmt.N(_offsides / n, 1)}   " +
                          $"fouls {Fmt.N(_fouls / n, 1)}");
        Console.WriteLine($"    possession home {Fmt.N(_homePossession / n, 1)}%   " +
                          $"nobody on the ball {Fmt.N(_loose / n, 1)}% of frames   " +
                          $"({Fmt.N(_ticks / n, 0)} frames per match)");
        Console.WriteLine($"    ball by third (home->away) {Fmt.N(_homeThird / n, 1)}% / " +
                          $"{Fmt.N(_middleThird / n, 1)}% / {Fmt.N(_awayThird / n, 1)}%");
        Console.WriteLine($"    ground covered {Fmt.N(_kmPerPlayer / n, 2)} km per player " +
                          $"(busiest {Fmt.N(_kmMax / n, 2)}, laziest {Fmt.N(_kmMin / n, 2)})");

        double s = Math.Max(1, _shapeSamples);
        Console.WriteLine();
        Console.WriteLine("  the shape (ten outfielders, averaged over both sides)");
        Console.WriteLine($"    defending  width {Fmt.N(_defWidth / s, 1)} m   depth {Fmt.N(_defDepth / s, 1)} m   " +
                          $"back line spread {Fmt.N(_defBackLine / s, 1)} m   biggest hole {Fmt.N(_defGap / s, 1)} m");
        Console.WriteLine($"               nearest team-mate {Fmt.N(_defMate / s, 1)} m   " +
                          $"an opponent within 3 m {Fmt.N(_defMarked / s, 1)}% of the time");
        Console.WriteLine($"    attacking  width {Fmt.N(_attWidth / s, 1)} m   depth {Fmt.N(_attDepth / s, 1)} m   " +
                          $"back line spread {Fmt.N(_attBackLine / s, 1)} m   biggest hole {Fmt.N(_attGap / s, 1)} m");
        Console.WriteLine($"               nearest team-mate {Fmt.N(_attMate / s, 1)} m   " +
                          $"an opponent within 3 m {Fmt.N(_attMarked / s, 1)}% of the time");

        // Bands from real football. Sources for the event bands are top-division season averages;
        // the shape bands are the range tracking data puts a professional block in.
        bands.Add("goals per match", _goals / n, 2.4, 3.0);
        bands.Add("shots per match", _shots / n, 20, 30);
        bands.Add("shots on target", _onTarget / n, 6, 11);
        bands.Add("passes per match", _passes / n, 850, 1150);
        bands.Add("pass accuracy", 100.0 * _completed / Math.Max(1, _passes), 76, 88, "%");
        bands.Add("throw-ins per match", _throwIns / n, 30, 50);
        bands.Add("corners per match", _corners / n, 8, 13);
        bands.Add("offsides per match", _offsides / n, 1.5, 5);
        bands.Add("fouls per match", _fouls / n, 18, 28);
        bands.Add("km per player", _kmPerPlayer / n, 9.0, 12.0, " km");
        bands.Add("frames with nobody on the ball", _loose / n, 15, 45, "%");
        bands.Add("defending: block width", _defWidth / s, 28, 42, " m");
        bands.Add("defending: block depth", _defDepth / s, 22, 38, " m");
        bands.Add("defending: back line spread", _defBackLine / s, 0, 6, " m");
        bands.Add("defending: biggest hole", _defGap / s, 0, 15, " m");
        bands.Add("defending: opponent within 3 m", _defMarked / s, 5, 25, "%");
        bands.Add("attacking: block width", _attWidth / s, 40, 60, " m");
        bands.Add("attacking: block depth", _attDepth / s, 30, 50, " m");
        bands.Add("attacking: opponent within 3 m", _attMarked / s, 5, 25, "%");
    }
}
