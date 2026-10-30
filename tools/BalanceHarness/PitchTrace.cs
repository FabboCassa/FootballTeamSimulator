using System.Text;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using Sim.Core.Random;

namespace Fts.BalanceHarness;

/// <summary>
/// The tick-by-tick movement trace of free-ball spec R7 (issue #177), behind --pitch-trace. For each
/// traced match the men are picked from data: the top oscillators by the per-player reversal rate
/// (<see cref="OscillationAnalyzer"/>) and every striker. The match is played again with a
/// <see cref="MovementLog"/> attached - the hash must not move - and every reversal of an oscillator
/// is put down to what changed in its 2 s window, every tick a striker stands in an offside position
/// in possession to what sent him there. Every tick of every traced man of the first traced match
/// goes to a CSV, for the write-up's excerpts (docs/engine/oscillation-trace.md).
/// </summary>
internal sealed class PitchTrace
{
    private const int Oscillators = 3;
    private const long TurnStep2Dm = 5 * 5;          // over 0.5 m, as the oscillation metric
    private const long SteadyTarget2Dm = 15 * 15;    // a target that moved under 1.5 m in 2 s
    private const int OffsideMarginDm = StrikerOffsideAnalyzer.MarginDm;

    private readonly string _path;
    private readonly int _matches;
    private readonly int _ticksPerSecond;
    private readonly StringBuilder _csv = new();
    private readonly OscillationAnalyzer _oscillation = new();
    private readonly Dictionary<string, int> _reversalCauses = new();
    private readonly Dictionary<string, int> _offsideCauses = new();
    private int _traced, _hashMismatches, _reversals, _oscillatorMinutesTicks;

    // The control: 2 s windows in which he moved over 0.5 m in both seconds without reversing, and
    // how many of those (and of the reversals) hold a job switch.
    private int _steadyWindows, _steadyWithSwitch, _reversalsWithSwitch;
    private int _strikerPossessionTicks, _strikerOffsideTicks;

    // Offside ticks with a support, run, offer or overlap target beyond the line, and how many of
    // them had his own phase spot (the one the onside hold binds) onside.
    private int _replacedTargetBeyond, _replacedTargetBeyondSpotOnside;
    private readonly List<int> _offsideSpellTicks = new();

    public PitchTrace(string path, int matches, int ticksPerSecond)
    {
        _path = path;
        _matches = matches;
        _ticksPerSecond = ticksPerSecond;
        _csv.AppendLine(
            "match,tick,side,slot,role,tag,baseJob,job,phase,poss,owner,dead,exp,ballX,ballY,lineX,rawSpotX,spotX,spotY," +
            "steered,sprint,tX,tY,wantVx,wantVy,pushVx,pushVy,vx,vy,x,y,offside,reversal");
    }

    public bool Wants(int match) => match < _matches;

    /// <summary>Plays <paramref name="report"/>'s fixture again with the trace attached and reads it.</summary>
    public void Add(int match, MatchEngine engine, Lineup home, Lineup away, ulong seed, MatchReport report)
    {
        OscillationMetrics? oscillation = _oscillation.Measure(report);
        if (oscillation == null) return;

        var tags = new Dictionary<(int Side, int Slot), string>();
        foreach (PlayerOscillation p in oscillation.Players
                     .Where(p => p.Minutes >= OscillationMetrics.MinSpreadMinutes)
                     .OrderByDescending(p => p.PerMinute).Take(Oscillators))
            tags[(p.Home ? 0 : 1, p.Slot)] = "osc";
        for (int side = 0; side < 2; side++)
        {
            Lineup xi = side == 0 ? home : away;
            for (int slot = 0; slot < xi.Slots.Count; slot++)
                if (xi.Slots[slot].Role == PositionRole.Striker)
                    tags[(side, slot)] = tags.ContainsKey((side, slot)) ? "osc+striker" : "striker";
        }

        var log = new MovementLog();
        foreach ((int side, int slot) in tags.Keys) log.Watch(side, slot);
        engine.MovementSink = log;
        MatchReport traced = engine.Simulate(home, away, new Pcg32(seed));
        engine.MovementSink = null;
        if (MatchReportHasher.Hash(traced) != MatchReportHasher.Hash(report)) _hashMismatches++;
        _traced++;

        foreach (((int side, int slot), string tag) in tags)
        {
            List<MovementRecord> man = log.Records.Where(r => r.Side == side && r.Slot == slot).ToList();
            PositionRole role = (side == 0 ? home : away).Slots[slot].Role;
            var reversalAt = new Dictionary<int, string>();
            if (tag.StartsWith("osc", StringComparison.Ordinal)) ReadReversals(man, reversalAt);
            bool striker = tag.EndsWith("striker", StringComparison.Ordinal);
            if (striker) ReadOffside(man);
            if (_traced > 1) continue;
            foreach (MovementRecord r in man)
                Row(match, role, tag, r, striker && Offside(r), reversalAt.TryGetValue(r.Tick, out string? c) ? c : "");
        }
    }

    public void Print(CheckList checks)
    {
        File.WriteAllText(_path, _csv.ToString());
        Console.WriteLine();
        Console.WriteLine($"--- pitch: movement trace ({_traced} matches, written to {_path}) ---");
        double minutes = _oscillatorMinutesTicks / (60.0 * _ticksPerSecond);
        Console.WriteLine(
            $"  top {Oscillators} oscillators a match: {_reversals} reversals in {Fmt.N(minutes, 1)} open-play minutes " +
            $"({Fmt.N(minutes <= 0 ? 0 : _reversals / minutes, 2)}/min), put down to what changed in the 2 s:");
        PrintShares(_reversalCauses, _reversals);
        Console.WriteLine(
            $"  a job switch is in {Fmt.N(Share(_reversalsWithSwitch, _reversals), 1)}% of the reversing windows and in " +
            $"{Fmt.N(Share(_steadyWithSwitch, _steadyWindows), 1)}% of the {_steadyWindows} moving windows that do not reverse");
        Console.WriteLine(
            $"  strikers: {_strikerOffsideTicks} of {_strikerPossessionTicks} open-play possession ticks in an offside position " +
            $"({Fmt.N(_strikerPossessionTicks == 0 ? 0 : 100.0 * _strikerOffsideTicks / _strikerPossessionTicks, 1)}%), " +
            $"spells median {Fmt.N(Median(_offsideSpellTicks) / (double)_ticksPerSecond, 1)} s; what sent him there:");
        PrintShares(_offsideCauses, _strikerOffsideTicks);
        Console.WriteLine(
            $"  of the {_replacedTargetBeyond} offside ticks sent beyond the line by a support, run, offer or overlap target, " +
            $"his own (onside-held) phase spot was onside in {Fmt.N(Share(_replacedTargetBeyondSpotOnside, _replacedTargetBeyond), 1)}%");

        checks.Check(
            "the movement trace does not move the match",
            _hashMismatches == 0,
            $"{_hashMismatches} of {_traced} traced matches hashed differently with the trace attached");
    }

    // ------------------------------------------------------------------ oscillation

    /// <summary>
    /// The oscillation metric's reversal (two 1 s steps over 0.5 m at more than 120°), read off the
    /// trace's own positions, and put down to the first of: a job change; his side winning or losing
    /// the ball; the target itself reversing with the job unchanged (and what it was following);
    /// the target standing still while he turned (his legs); a target moving without reversing.
    /// </summary>
    private void ReadReversals(List<MovementRecord> man, Dictionary<int, string> reversalAt)
    {
        int s = _ticksPerSecond;
        for (int i = 0; i < man.Count; i++)
            if (Open(man[i]) && man[i].Tick % s == 0) _oscillatorMinutesTicks += s;

        for (int i = s; i + s < man.Count; i++)
        {
            if (man[i].Tick % s != 0 || man[i + s].Tick - man[i - s].Tick != 2 * s) continue;
            bool open = true;
            for (int j = i - s; j <= i + s && open; j++) open = Open(man[j]);
            if (!open) continue;

            MovementRecord a = man[i - s], m = man[i], b = man[i + s];
            bool switches = JobSwitches(man, i - s, i + s);
            if (!Reverses(m.XDm - a.XDm, m.YDm - a.YDm, b.XDm - m.XDm, b.YDm - m.YDm))
            {
                if (Sq(m.XDm - a.XDm, m.YDm - a.YDm) <= TurnStep2Dm || Sq(b.XDm - m.XDm, b.YDm - m.YDm) <= TurnStep2Dm) continue;
                _steadyWindows++;
                if (switches) _steadyWithSwitch++;
                continue;
            }

            string cause = ReversalCause(man, i - s, i + s);
            _reversals++;
            if (switches) _reversalsWithSwitch++;
            Count(_reversalCauses, cause);
            reversalAt[m.Tick] = cause;
        }
    }

    private static bool JobSwitches(List<MovementRecord> man, int from, int to)
    {
        for (int j = from + 1; j <= to; j++)
            if (man[j].Job != man[j - 1].Job) return true;
        return false;
    }

    private static string ReversalCause(List<MovementRecord> man, int from, int to)
    {
        MovementRecord a = man[from], m = man[(from + to) / 2], b = man[to];
        for (int j = from + 1; j <= to; j++)
            if (man[j].Job != man[j - 1].Job) return $"job switch {man[j - 1].Job}->{man[j].Job}";
        for (int j = from + 1; j <= to; j++)
            if (man[j].InPossession != man[j - 1].InPossession) return $"possession flip, {m.Job}";

        int tax = m.TargetXDm - a.TargetXDm, tay = m.TargetYDm - a.TargetYDm;
        int tbx = b.TargetXDm - m.TargetXDm, tby = b.TargetYDm - m.TargetYDm;
        if (Reverses(tax, tay, tbx, tby))
        {
            bool ball = Reverses(m.BallXDm - a.BallXDm, m.BallYDm - a.BallYDm, b.BallXDm - m.BallXDm, b.BallYDm - m.BallYDm);
            return $"target reversed, {m.Job}" + (ball ? ", with the ball" : "");
        }

        long net2 = Sq(b.TargetXDm - a.TargetXDm, b.TargetYDm - a.TargetYDm);
        long moved2 = Sq(tax, tay) + Sq(tbx, tby);
        if (net2 <= SteadyTarget2Dm && moved2 <= 4 * SteadyTarget2Dm) return $"target steady (legs), {m.Job}";
        return $"target moving, {m.Job}";
    }

    // ------------------------------------------------------------------ striker offside

    private void ReadOffside(List<MovementRecord> man)
    {
        int spell = 0;
        foreach (MovementRecord r in man)
        {
            if (!Open(r) || !r.InPossession)
            {
                EndSpell(ref spell);
                continue;
            }

            _strikerPossessionTicks++;
            if (!Offside(r))
            {
                EndSpell(ref spell);
                continue;
            }

            _strikerOffsideTicks++;
            spell++;
            Count(_offsideCauses, OffsideCause(r));
            if (ReplacesSpot(r.Job) && Beyond(r, r.TargetXDm))
            {
                _replacedTargetBeyond++;
                if (!Beyond(r, r.SpotXDm)) _replacedTargetBeyondSpotOnside++;
            }
        }

        EndSpell(ref spell);
    }

    private void EndSpell(ref int spell)
    {
        if (spell > 0) _offsideSpellTicks.Add(spell);
        spell = 0;
    }

    /// <summary>
    /// Whether the target he was given is itself beyond the line (the job sends him offside), or
    /// onside with him still beyond it - and then whether he is walking back (under 2 m/s toward his
    /// own goal) or running.
    /// </summary>
    private static string OffsideCause(in MovementRecord r)
    {
        if (Beyond(r, r.TargetXDm)) return $"target beyond the line, {r.Job}";
        int back = -Direction(r.Side) * r.VelXCmPerS;
        string pace = back <= 0 ? "not coming back" : back < 200 ? "walking back" : "running back";
        return $"target onside, {pace}, {r.Job}";
    }

    /// <summary>His side's offside-position definition of §10.3: beyond the line by the margin (the line already includes the ball and halfway).</summary>
    private static bool Offside(in MovementRecord r) => Beyond(r, r.XDm);

    private static bool Beyond(in MovementRecord r, int xDm) =>
        Direction(r.Side) * (xDm - r.OffsideLineXDm) > OffsideMarginDm;

    // ------------------------------------------------------------------ helpers

    /// <summary>The V11 targets that replace his phase spot after it has been read (and held onside).</summary>
    private static bool ReplacesSpot(MovementJob job) =>
        job == MovementJob.Support || job == MovementJob.Run || job == MovementJob.Offer || job == MovementJob.Overlap;

    private static bool Open(in MovementRecord r) =>
        !r.BallDead && r.Job != MovementJob.SetPiece && r.Job != MovementJob.Restart && r.Job != MovementJob.RestartOffer;

    private static int Direction(int side) => side == 0 ? 1 : -1;

    private static long Sq(long x, long y) => x * x + y * y;

    private static double Share(int part, int whole) => whole == 0 ? 0 : 100.0 * part / whole;

    private static bool Reverses(long ax, long ay, long bx, long by)
    {
        long a2 = Sq(ax, ay), b2 = Sq(bx, by);
        if (a2 <= TurnStep2Dm || b2 <= TurnStep2Dm) return false;
        long dot = ax * bx + ay * by;
        return dot < 0 && 4 * dot * dot > a2 * b2;
    }

    private static void Count(Dictionary<string, int> into, string key) =>
        into[key] = into.TryGetValue(key, out int n) ? n + 1 : 1;

    private static void PrintShares(Dictionary<string, int> causes, int total)
    {
        foreach ((string cause, int n) in causes.OrderByDescending(kv => kv.Value).Take(14))
            Console.WriteLine($"    {Fmt.N(total == 0 ? 0 : 100.0 * n / total, 1),5}%  {n,6}  {cause}");
    }

    private static double Median(List<int> values)
    {
        if (values.Count == 0) return 0;
        List<int> sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    private void Row(int match, PositionRole role, string tag, in MovementRecord r, bool offside, string reversal)
    {
        _csv.Append(match).Append(',').Append(r.Tick).Append(',').Append(r.Side).Append(',').Append(r.Slot).Append(',')
            .Append(role).Append(',').Append(tag).Append(',').Append(r.BaseJob).Append(',').Append(r.Job).Append(',')
            .Append(r.Phase).Append(',').Append(r.InPossession ? 1 : 0).Append(',').Append(r.BallOwnerSide).Append(',')
            .Append(r.BallDead ? 1 : 0).Append(',').Append(r.ExpansionPermille).Append(',')
            .Append(r.BallXDm).Append(',').Append(r.BallYDm).Append(',').Append(r.OffsideLineXDm).Append(',')
            .Append(r.RawSpotXDm).Append(',').Append(r.SpotXDm).Append(',').Append(r.SpotYDm).Append(',')
            .Append(r.Steered ? 1 : 0).Append(',').Append(r.Sprint ? 1 : 0).Append(',')
            .Append(r.TargetXDm).Append(',').Append(r.TargetYDm).Append(',')
            .Append(r.WantXCmPerS).Append(',').Append(r.WantYCmPerS).Append(',')
            .Append(r.PushedXCmPerS).Append(',').Append(r.PushedYCmPerS).Append(',')
            .Append(r.VelXCmPerS).Append(',').Append(r.VelYCmPerS).Append(',')
            .Append(r.XDm).Append(',').Append(r.YDm).Append(',').Append(offside ? 1 : 0).Append(',')
            .Append(reversal.Replace(',', ';'))
            .AppendLine();
    }
}
