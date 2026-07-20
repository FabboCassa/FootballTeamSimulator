using System.Collections.Generic;
using System.Linq;
using Fts.Application.Leagues;
using Sim.Core.Config;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;
using SimClub = Sim.Core.Domain.Club;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// Resolves a single private-league fixture with the shared Sim.Core engine (Phase 8.3/8.4): it builds
/// each side's lineup from the submitted <see cref="LineupPlan"/> (falling back to <c>BestEleven</c> when
/// none is present or it no longer materialises — "ultima formazione inviata, poi BestEleven"), applies
/// the optional tactic and pre-match plan, and runs the engine with the deterministic per-fixture seed.
/// Pure and deterministic: same clubs + inputs + seed ⇒ byte-identical <see cref="MatchReport"/>, so the
/// stored replay renders identically for every member. From 8.4 the engine resolves on the clubs' live,
/// server-authoritative condition (<paramref name="applyCondition"/> — form/morale/fitness now feed the
/// result, and within-match fatigue applies); at a fresh, neutral condition this is byte-identical to the
/// raw-attribute path, so the very first round matches 8.3. The kickoff XI is returned so the season's
/// weekly condition tick can credit the players who started.
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

    /// <summary>The resolved report plus each side's kickoff XI (player ids) for the condition tick.</summary>
    public readonly struct ResolveResult
    {
        public MatchReport Report { get; init; }
        public HashSet<int> HomeStarterIds { get; init; }
        public HashSet<int> AwayStarterIds { get; init; }
    }

    public static ResolveResult Resolve(
        SimClub home, SimClub away, SideInputs? homeInputs, SideInputs? awayInputs, ulong seed,
        BalanceConfig cfg, bool applyCondition = true)
    {
        int famMax = cfg.Tactics.FamiliarityMax;

        Lineup homeLineup = BuildLineup(home, homeInputs?.Lineup);
        Lineup awayLineup = BuildLineup(away, awayInputs?.Lineup);

        MatchTactics? tactics = BuildTactics(homeInputs?.Tactic, awayInputs?.Tactic, famMax);

        var homeRules = homeInputs?.Plan?.Resolve(home, null, famMax);
        var awayRules = awayInputs?.Plan?.Resolve(away, null, famMax);

        var engine = new MatchEngine(cfg, applyCondition: applyCondition, applyMatchFatigue: applyCondition);
        var plan = new MatchPlan(new MatchInput(homeLineup, awayLineup, tactics));
        MatchReport report = engine.Simulate(plan, homeRules, awayRules, new Pcg32(seed));

        return new ResolveResult
        {
            Report = report,
            HomeStarterIds = StarterIdsOf(homeLineup),
            AwayStarterIds = StarterIdsOf(awayLineup),
        };
    }

    /// <summary>
    /// Resolves a fixture played LIVE (Phase 8.6): folds the accumulated pause-point inputs into the
    /// authoritative <see cref="MatchPlan"/> and re-runs the whole 90' from the fixture seed. Each
    /// <see cref="LiveChange"/> updates only its own side (a substitution / reshaped XI via a new
    /// <see cref="LineupPlan"/>, and/or an instruction change via a new <see cref="TacticPlan"/>), keeping
    /// the other side as it was, then emits a <see cref="MatchInputChange"/> from that minute. Because the
    /// engine is a pure function of (plan, seed), the minutes before a change stay byte-identical and only
    /// the remainder re-rolls — that is what lets both connected clients render the same match in sync. The
    /// engine runs on the clubs' live condition with the same flags as the scheduled resolution, so a
    /// finished live report equals what the round would have produced for the same inputs. The kickoff XI
    /// (for the weekly condition credit, 8.4) is the BASE lineups, before any live sub.
    /// </summary>
    public static ResolveResult ResolveLive(
        SimClub home, SimClub away, SideInputs? homeInputs, SideInputs? awayInputs,
        IReadOnlyList<LiveChange> changes, ulong seed, BalanceConfig cfg, bool applyCondition = true)
    {
        int famMax = cfg.Tactics.FamiliarityMax;

        Lineup homeLineup = BuildLineup(home, homeInputs?.Lineup);
        Lineup awayLineup = BuildLineup(away, awayInputs?.Lineup);
        TacticPlan? homeTactic = homeInputs?.Tactic;
        TacticPlan? awayTactic = awayInputs?.Tactic;

        var plan = new MatchPlan(new MatchInput(homeLineup, awayLineup, BuildTactics(homeTactic, awayTactic, famMax)));

        foreach (LiveChange ch in changes.OrderBy(c => c.FromMinute))
        {
            if (ch.Side == LiveSide.Home)
            {
                if (ch.Lineup != null && ch.Lineup.TryMaterialize(home, out Lineup? nl) && nl != null) homeLineup = nl;
                if (ch.Tactic != null) homeTactic = ch.Tactic;
            }
            else
            {
                if (ch.Lineup != null && ch.Lineup.TryMaterialize(away, out Lineup? nl) && nl != null) awayLineup = nl;
                if (ch.Tactic != null) awayTactic = ch.Tactic;
            }

            plan = plan.WithChange(ch.FromMinute,
                new MatchInput(homeLineup, awayLineup, BuildTactics(homeTactic, awayTactic, famMax)));
        }

        var homeRules = homeInputs?.Plan?.Resolve(home, null, famMax);
        var awayRules = awayInputs?.Plan?.Resolve(away, null, famMax);

        var engine = new MatchEngine(cfg, applyCondition: applyCondition, applyMatchFatigue: applyCondition);
        MatchReport report = engine.Simulate(plan, homeRules, awayRules, new Pcg32(seed));

        return new ResolveResult
        {
            Report = report,
            HomeStarterIds = StarterIdsOf(BuildLineup(home, homeInputs?.Lineup)),
            AwayStarterIds = StarterIdsOf(BuildLineup(away, awayInputs?.Lineup)),
        };
    }

    /// <summary>The two kickoff XIs (base lineups, before any live sub) for the weekly condition credit
    /// (8.4) when the round consumes a finished live result — no engine run.</summary>
    public static (HashSet<int> Home, HashSet<int> Away) KickoffElevenIds(
        SimClub home, SimClub away, SideInputs? homeInputs, SideInputs? awayInputs) =>
        (StarterIdsOf(BuildLineup(home, homeInputs?.Lineup)), StarterIdsOf(BuildLineup(away, awayInputs?.Lineup)));

    /// <summary>The kickoff XI's player ids (credited a full match by the condition tick).</summary>
    private static HashSet<int> StarterIdsOf(Lineup lineup)
    {
        var ids = new HashSet<int>();
        foreach (LineupSlot slot in lineup.Slots)
            if (slot.Player != null) ids.Add(slot.Player.Id);
        return ids;
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
