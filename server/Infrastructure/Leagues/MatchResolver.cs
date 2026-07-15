using Sim.Core.Config;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;
using SimClub = Sim.Core.Domain.Club;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// Resolves a single private-league fixture with the shared Sim.Core engine (Phase 8.3): it builds each
/// side's lineup from the submitted <see cref="LineupPlan"/> (falling back to <c>BestEleven</c> when
/// none is present or it no longer materialises — "ultima formazione inviata, poi BestEleven"), applies
/// the optional tactic and pre-match plan, and runs the engine with the deterministic per-fixture seed.
/// Pure and deterministic: same clubs + inputs + seed ⇒ byte-identical <see cref="MatchReport"/>, so the
/// stored replay renders identically for every member. The engine runs with default flags (no condition /
/// positioning): the server does not track live condition yet (that becomes server-authoritative in 8.4),
/// so matches resolve from raw attributes.
/// </summary>
public static class MatchResolver
{
    /// <summary>Deserialized inputs for one club, any of which may be null (missing submission).</summary>
    public sealed class SideInputs
    {
        public LineupPlan? Lineup { get; init; }
        public TacticPlan? Tactic { get; init; }
        public PrematchPlan? Plan { get; init; }
    }

    public static MatchReport Resolve(
        SimClub home, SimClub away, SideInputs? homeInputs, SideInputs? awayInputs, ulong seed, BalanceConfig cfg)
    {
        int famMax = cfg.Tactics.FamiliarityMax;

        Lineup homeLineup = BuildLineup(home, homeInputs?.Lineup);
        Lineup awayLineup = BuildLineup(away, awayInputs?.Lineup);

        MatchTactics? tactics = BuildTactics(homeInputs?.Tactic, awayInputs?.Tactic, famMax);

        var homeRules = homeInputs?.Plan?.Resolve(home, null, famMax);
        var awayRules = awayInputs?.Plan?.Resolve(away, null, famMax);

        var engine = new MatchEngine(cfg);
        var plan = new MatchPlan(new MatchInput(homeLineup, awayLineup, tactics));
        return engine.Simulate(plan, homeRules, awayRules, new Pcg32(seed));
    }

    /// <summary>The submitted lineup if it materialises against the current squad, else the best XI (AI fallback).</summary>
    private static Lineup BuildLineup(SimClub club, LineupPlan? plan)
    {
        if (plan != null && plan.TryMaterialize(club, out Lineup? lineup) && lineup != null)
            return lineup;
        return LineupSelector.BestEleven(club);
    }

    /// <summary>Null when neither side submitted a tactic (byte-identical engine path); otherwise each
    /// side gets its tactic at full familiarity (the server does not track per-club familiarity — 8.4),
    /// and a side with no tactic gets the neutral (identity) context.</summary>
    private static MatchTactics? BuildTactics(TacticPlan? home, TacticPlan? away, int famMax)
    {
        if (home == null && away == null) return null;
        TacticContext h = home != null ? new TacticContext(home.ToTactic(), famMax) : TacticContext.Neutral(famMax);
        TacticContext a = away != null ? new TacticContext(away.ToTactic(), famMax) : TacticContext.Neutral(famMax);
        return new MatchTactics(h, a);
    }
}
