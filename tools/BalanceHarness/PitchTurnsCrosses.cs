using Sim.Core.Config;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;

namespace Fts.BalanceHarness;

/// <summary>
/// The pitch scenario's free-ball turn and cross block (free-ball spec R2, issue #175): the cause of
/// every fast free-ball turn off the engine's own ball events, every turn with no cause listed as a
/// defect, and the crosses by geometry per team-match, each against its R1 band. Report only, but
/// for the touches made from beyond reach (R3, issue #178): every one of them is a defect, checked.
/// </summary>
internal sealed class PitchTurnsCrosses
{
    private readonly FreeBallTurnAnalyzer _turns = new();
    private readonly GeometricCrossAnalyzer _crosses = new();
    private readonly TurnCrossTally _tally = new();
    private readonly int _ticksPerFrame;
    private readonly MatchBalance _cfg;
    private int _touches, _beyondReach;

    public PitchTurnsCrosses(MatchBalance cfg)
    {
        _cfg = cfg;
        _ticksPerFrame = cfg.StreamTicksPerFrame;
    }

    public void Add(int match, MatchReport report, BallEventLog events)
    {
        foreach (BallEvent e in events.Records)
            if (e.Kind == BallEventKind.Touch || e.Kind == BallEventKind.Deflection) _touches++;
        _beyondReach += TouchReachAnalyzer.BeyondReach(events.Records, _cfg);

        FreeBallTurnMetrics? t = _turns.Measure(report, events.Records, _ticksPerFrame);
        GeometricCrossMetrics? c = _crosses.Measure(report, events.Records, _ticksPerFrame);
        if (t == null || c == null) return;
        _tally.Add(match, t);
        _tally.Add(c);
    }

    public void Print(CheckList checks)
    {
        Console.WriteLine();
        Console.Write(_tally.Format("pitch"));
        checks.Info($"{_tally.Defects.Count} fast free-ball turns with no cause over {_tally.Matches} matches (defects, listed above)");
        double perMatch = _tally.Matches <= 0 ? 0 : _beyondReach / (double)_tally.Matches;
        Console.WriteLine($"  touches from beyond reach/match {Fmt.N(perMatch, 2)} (of {Fmt.N(_tally.Matches <= 0 ? 0 : _touches / (double)_tally.Matches, 1)} touches; feet {_cfg.ControlRadiusDm} dm, head {_cfg.TouchHeightDm} dm, keeper +{_cfg.KeeperSaveReachDm} dm, +{_cfg.KeeperArmHeightDm} dm up)");
        checks.Check(
            "a man touches the ball only within his reach",
            _beyondReach == 0,
            $"{_beyondReach} touches over {_tally.Matches} matches made from beyond reach (free-ball spec R3)");
    }
}
