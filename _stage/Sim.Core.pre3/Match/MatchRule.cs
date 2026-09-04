using Sim.Core.Domain;
using Sim.Core.Tactics;

namespace Sim.Core.Match
{
    /// <summary>
    /// The scoreline gate of a <see cref="MatchRule"/>, evaluated from the owning
    /// side's perspective (own goals vs opponent goals). <see cref="Always"/> ignores
    /// the score; the others use <see cref="MatchRule.Margin"/> where it applies.
    /// </summary>
    public enum ScoreSituation
    {
        /// <summary>Fire regardless of the score (a pure minute trigger).</summary>
        Always = 0,
        /// <summary>Trailing by at least <see cref="MatchRule.Margin"/> goals.</summary>
        Losing = 1,
        /// <summary>Level.</summary>
        Drawing = 2,
        /// <summary>Leading by at least <see cref="MatchRule.Margin"/> goals.</summary>
        Winning = 3,
        /// <summary>Level or behind (goal difference &lt;= 0).</summary>
        NotWinning = 4,
        /// <summary>Level or ahead (goal difference &gt;= 0).</summary>
        NotLosing = 5
    }

    /// <summary>
    /// One substitution: the on-pitch player who leaves and the (already resolved)
    /// bench player who comes on, taking the outgoing player's role. The host
    /// resolves <see cref="In"/> from the squad when building the plan so the
    /// engine never needs a <see cref="Club"/> reference (mirrors how 3.4 built
    /// substitution inputs).
    /// </summary>
    public sealed class Substitution
    {
        public int OutPlayerId { get; }
        public Player In { get; }

        public Substitution(int outPlayerId, Player @in)
        {
            OutPlayerId = outPlayerId;
            In = @in;
        }
    }

    /// <summary>
    /// What a fired rule does to the side it belongs to: change tactical
    /// instructions (formation is fixed in-match, 3.4 design note (b)) and/or make
    /// a substitution. Either part is optional; an empty action is a no-op.
    /// </summary>
    public sealed class RuleAction
    {
        /// <summary>New instruction set for the side, or null to leave instructions unchanged.</summary>
        public TacticInstructions? Instructions { get; }

        /// <summary>Familiarity the side has with the resulting tactic (host-resolved, per-tactic — 3.4 note (a)).</summary>
        public int InstructionFamiliarity { get; }

        /// <summary>The substitution to make, or null for none.</summary>
        public Substitution? Sub { get; }

        public RuleAction(TacticInstructions? instructions = null, int instructionFamiliarity = 0, Substitution? sub = null)
        {
            Instructions = instructions;
            InstructionFamiliarity = instructionFamiliarity;
            Sub = sub;
        }

        public bool IsEmpty => Instructions == null && Sub == null;
    }

    /// <summary>
    /// A single conditional pre-match instruction: "from <see cref="FromMinute"/>,
    /// while the scoreline matches <see cref="When"/>, apply <see cref="Action"/>".
    /// Side-agnostic — the engine is told which side a rule belongs to by which
    /// list it is passed in (home vs away), so the same rule serves a club whether
    /// it plays home or away. Evaluated at minute boundaries and fired at most once.
    ///
    /// Pre-made plans are "just scheduled inputs" (ARCHITECTURE.md §4.3); the
    /// difference from a static <see cref="MatchInputChange"/> is that the firing
    /// minute is decided at runtime from the live score, which is exactly what an
    /// AI fallback needs when a match is skipped/unwatched (task 3.5).
    /// </summary>
    public sealed class MatchRule
    {
        /// <summary>Earliest minute the rule may fire (1..90).</summary>
        public int FromMinute { get; }

        /// <summary>Scoreline gate (own perspective).</summary>
        public ScoreSituation When { get; }

        /// <summary>Goal threshold for <see cref="ScoreSituation.Losing"/>/<see cref="ScoreSituation.Winning"/> (>= 1).</summary>
        public int Margin { get; }

        public RuleAction Action { get; }

        public MatchRule(int fromMinute, ScoreSituation when, RuleAction action, int margin = 1)
        {
            FromMinute = fromMinute < 1 ? 1 : fromMinute;
            When = when;
            Action = action;
            Margin = margin < 1 ? 1 : margin;
        }

        /// <summary>
        /// Whether the scoreline gate is satisfied given the side's own and the
        /// opponent's goals so far. The minute gate (<see cref="FromMinute"/>) is
        /// checked by the engine separately. Pure: consumes no randomness.
        /// </summary>
        public bool ConditionMet(int ownGoals, int opponentGoals)
        {
            int gd = ownGoals - opponentGoals;
            switch (When)
            {
                case ScoreSituation.Always: return true;
                case ScoreSituation.Losing: return gd <= -Margin;
                case ScoreSituation.Drawing: return gd == 0;
                case ScoreSituation.Winning: return gd >= Margin;
                case ScoreSituation.NotWinning: return gd <= 0;
                case ScoreSituation.NotLosing: return gd >= 0;
                default: return false;
            }
        }
    }

    /// <summary>
    /// Applies a fired <see cref="MatchRule"/> to the current match input,
    /// producing the new input the engine runs from that minute on. Pure and
    /// deterministic (no randomness): the same (input, rule) always yields the
    /// same result, preserving replayability.
    /// </summary>
    public static class MatchRuleApplier
    {
        /// <summary>
        /// Returns a new <see cref="MatchInput"/> with the rule's action applied to
        /// the given side. A substitution rebuilds that side's lineup (incoming
        /// player keeps the outgoing slot's role); an instruction change rebuilds
        /// that side's tactic context (formation and the other side untouched).
        /// </summary>
        public static MatchInput Apply(MatchInput active, MatchRule rule, bool isHome, int familiarityMax)
        {
            Lineup home = active.Home;
            Lineup away = active.Away;
            MatchTactics? tactics = active.Tactics;
            RuleAction action = rule.Action;

            Substitution? sub = action.Sub;
            if (sub != null)
            {
                Lineup target = isHome ? home : away;
                Lineup subbed = ApplySub(target, sub);
                if (isHome) home = subbed; else away = subbed;
            }

            TacticInstructions? instructions = action.Instructions;
            if (instructions.HasValue)
            {
                tactics = WithInstructions(
                    tactics, isHome, instructions.Value, action.InstructionFamiliarity, familiarityMax);
            }

            return new MatchInput(home, away, tactics);
        }

        /// <summary>Replaces the slot held by <see cref="Substitution.OutPlayerId"/> with the incoming player.</summary>
        private static Lineup ApplySub(Lineup target, Substitution sub)
        {
            var result = new Lineup { ClubId = target.ClubId };
            foreach (LineupSlot slot in target.Slots)
            {
                if (slot.Player.Id == sub.OutPlayerId)
                    result.Slots.Add(new LineupSlot { Role = slot.Role, Player = sub.In });
                else
                    result.Slots.Add(new LineupSlot { Role = slot.Role, Player = slot.Player });
            }

            return result;
        }

        /// <summary>Rebuilds the firing side's tactic context with new instructions, keeping its formation.</summary>
        private static MatchTactics WithInstructions(
            MatchTactics? tactics, bool isHome, TacticInstructions instructions, int familiarity, int familiarityMax)
        {
            Formation formation = SideContext(tactics, isHome, familiarityMax).Tactic.Formation;
            var changed = new TacticContext(new Tactic(formation, instructions), familiarity);
            TacticContext other = SideContext(tactics, !isHome, familiarityMax);

            return isHome ? new MatchTactics(changed, other) : new MatchTactics(other, changed);
        }

        /// <summary>The given side's current context, or a neutral full-familiarity one when no tactics are set.</summary>
        private static TacticContext SideContext(MatchTactics? tactics, bool isHome, int familiarityMax)
        {
            if (tactics == null)
                return TacticContext.Neutral(familiarityMax);

            return isHome ? tactics.Home : tactics.Away;
        }
    }
}
