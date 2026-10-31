using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;

namespace Fts.BalanceHarness;

/// <summary>
/// The pitch scenario's free-ball turn and cross block (free-ball spec R2, issue #175): the cause of
/// every fast free-ball turn off the engine's own ball events, every turn with no cause listed as a
/// defect, and the crosses by geometry per team-match, each against its R1 band. Report only.
/// </summary>
internal sealed class PitchTurnsCrosses
{
    private readonly FreeBallTurnAnalyzer _turns = new();
    private readonly GeometricCrossAnalyzer _crosses = new();
    private readonly TurnCrossTally _tally = new();
    private readonly int _ticksPerFrame;

    public PitchTurnsCrosses(int ticksPerFrame) => _ticksPerFrame = ticksPerFrame;

    public void Add(int match, MatchReport report, BallEventLog events)
    {
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
    }
}
