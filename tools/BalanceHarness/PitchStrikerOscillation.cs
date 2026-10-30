using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;

namespace Fts.BalanceHarness;

/// <summary>
/// The pitch scenario's striker-line and oscillation block (free-ball spec R2, issue #176): the
/// striker's offside-position share of his team's possession, offsides called, and reversals per
/// player-minute, each against its R1 band. Report only. Kept in its own block so the other R2
/// readings land beside it without touching it.
/// </summary>
internal sealed class PitchStrikerOscillation
{
    private readonly StrikerOffsideAnalyzer _striker = new();
    private readonly OscillationAnalyzer _oscillation = new();
    private readonly StrikerOscillationTally _tally = new();

    public void Add(MatchReport report, Lineup home, Lineup away)
    {
        StrikerOffsideMetrics? s = _striker.Measure(report, Roles(home), Roles(away));
        OscillationMetrics? o = _oscillation.Measure(report);
        if (s == null || o == null) return;
        _tally.Add(s);
        _tally.Add(o);
    }

    public void Print()
    {
        Console.WriteLine();
        Console.Write(_tally.Format("pitch"));
    }

    private static List<PositionRole> Roles(Lineup xi) => xi.Slots.Select(slot => slot.Role).ToList();
}
