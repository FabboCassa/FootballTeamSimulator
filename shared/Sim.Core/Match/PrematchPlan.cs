using System;
using System.Collections.Generic;
using Sim.Core.Domain;
using Sim.Core.Tactics;

namespace Sim.Core.Match
{
    /// <summary>
    /// A serializable conditional instruction (player ids and enums, not
    /// references): "from <see cref="FromMinute"/>, while the scoreline matches
    /// <see cref="When"/>, optionally switch to these instructions, make this
    /// substitution and/or call this shout". Resolved against a club's current squad when the match runs,
    /// mirroring how <see cref="LineupPlan"/>/<see cref="TacticPlan"/> persist a
    /// selection. The host (client/server) owns persistence; Sim.Core stays I/O-free.
    ///
    /// Triggers are minute + scoreline for now; fitness-based triggers
    /// ("tired player -> sub") hook in once the condition model lands (task 4.1).
    /// </summary>
    public sealed class PrematchRule
    {
        public int FromMinute { get; set; } = 1;
        public ScoreSituation When { get; set; } = ScoreSituation.Always;

        /// <summary>Goal margin for Losing/Winning gates (ignored by the others).</summary>
        public int Margin { get; set; } = 1;

        // --- Instruction change (optional) ---
        /// <summary>When true, the rule switches to the instruction set below.</summary>
        public bool ChangeInstructions { get; set; }
        public Mentality Mentality { get; set; } = Mentality.Balanced;
        public Pressing Pressing { get; set; } = Pressing.Medium;
        public Tempo Tempo { get; set; } = Tempo.Normal;
        public Width Width { get; set; } = Width.Normal;

        // --- Substitution (optional) ---
        /// <summary>On-pitch player to take off; 0 = no substitution.</summary>
        public int SubOutPlayerId { get; set; }

        /// <summary>Bench player to bring on; 0 = no substitution.</summary>
        public int SubInPlayerId { get; set; }

        // --- Touchline shout (optional) ---
        /// <summary>The shout to call when the rule fires; None (also what an older plan reads as) = no shout.</summary>
        public TouchlineShout Shout { get; set; } = TouchlineShout.None;

        /// <summary>A rule whose only action is to call <paramref name="shout"/> (the rule editor's shout action).</summary>
        public static PrematchRule ForShout(int fromMinute, ScoreSituation when, TouchlineShout shout) =>
            new PrematchRule { FromMinute = fromMinute, When = when, Shout = shout };

        /// <summary>
        /// Whether the engine can act on the rule as written: a minute inside the match, known
        /// enums, a positive margin, and an action — a tactic change, a complete substitution
        /// and/or a shout. A method rather than a property so it never lands in a save.
        /// </summary>
        public bool IsValid()
        {
            if (FromMinute < 1 || FromMinute > 90 || Margin < 1) return false;
            if (!Enum.IsDefined(typeof(ScoreSituation), When) || !Enum.IsDefined(typeof(TouchlineShout), Shout)) return false;
            if ((SubOutPlayerId == 0) != (SubInPlayerId == 0)) return false;
            return ChangeInstructions || SubOutPlayerId != 0 || Shout != TouchlineShout.None;
        }

        public PrematchRule Copy() => (PrematchRule)MemberwiseClone();
    }

    /// <summary>
    /// A club's pre-match plan: an ordered list of conditional rules. Resolved into
    /// engine <see cref="MatchRule"/>s for a match (task 3.5). The plan is
    /// side-agnostic — the same plan serves the club home or away, since the engine
    /// is told the side by which list it receives.
    /// </summary>
    public sealed class PrematchPlan
    {
        public List<PrematchRule> Rules { get; set; } = new List<PrematchRule>();

        /// <summary>How many rules the plan editor lets a coach keep.</summary>
        public const int MaxRules = 6;

        public static PrematchPlan Empty() => new PrematchPlan();

        /// <summary>Whether <see cref="WithRule"/> would take <paramref name="rule"/>.</summary>
        public bool CanAdd(PrematchRule? rule) => rule != null && rule.IsValid() && Rules.Count < MaxRules;

        /// <summary>A new plan with a copy of <paramref name="rule"/> appended; this plan is unchanged.</summary>
        public PrematchPlan WithRule(PrematchRule rule)
        {
            if (!CanAdd(rule))
                throw new ArgumentException("The rule is not valid or the plan is full.", nameof(rule));

            PrematchPlan next = Copy();
            next.Rules.Add(rule.Copy());
            return next;
        }

        /// <summary>A new plan without the rule at <paramref name="index"/> (out of range removes nothing).</summary>
        public PrematchPlan WithoutRule(int index)
        {
            PrematchPlan next = Copy();
            if (index >= 0 && index < next.Rules.Count)
                next.Rules.RemoveAt(index);
            return next;
        }

        /// <summary>A deep copy: the rules are copied too, so editing it never touches this plan.</summary>
        public PrematchPlan Copy()
        {
            var copy = new PrematchPlan();
            foreach (PrematchRule rule in Rules)
                copy.Rules.Add(rule.Copy());
            return copy;
        }

        /// <summary>
        /// Resolves the plan against <paramref name="club"/>'s squad.
        /// <paramref name="familiarityFor"/> supplies the club's familiarity with a
        /// resulting instruction set (the host's per-tactic familiarity, keyed by
        /// the in-match formation); <paramref name="familiarityMax"/> is the scale's
        /// maximum. Rules that resolve to nothing (empty action, or a substitution
        /// whose incoming player is not in the squad) are dropped — the silent
        /// fallback used elsewhere (ARCHITECTURE.md §6.4).
        /// </summary>
        public List<MatchRule> Resolve(Club club, Func<TacticInstructions, int>? familiarityFor, int familiarityMax)
        {
            var resolved = new List<MatchRule>();
            foreach (PrematchRule rule in Rules)
            {
                TacticInstructions? instructions = null;
                int familiarity = familiarityMax;
                if (rule.ChangeInstructions)
                {
                    var instr = new TacticInstructions(rule.Mentality, rule.Pressing, rule.Tempo, rule.Width);
                    instructions = instr;
                    familiarity = familiarityFor != null ? familiarityFor(instr) : familiarityMax;
                }

                Substitution? sub = null;
                if (rule.SubOutPlayerId != 0 && rule.SubInPlayerId != 0)
                {
                    Player? incoming = FindPlayer(club, rule.SubInPlayerId);
                    if (incoming != null)
                        sub = new Substitution(rule.SubOutPlayerId, incoming);
                }

                var action = new RuleAction(instructions, familiarity, sub, rule.Shout);
                if (action.IsEmpty)
                    continue;

                resolved.Add(new MatchRule(rule.FromMinute, rule.When, action, rule.Margin));
            }

            return resolved;
        }

        private static Player? FindPlayer(Club club, int playerId)
        {
            foreach (Player player in club.Squad.Players)
            {
                if (player.Id == playerId)
                    return player;
            }

            return null;
        }
    }
}
