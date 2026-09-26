using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>The icon a commentary line is drawn with; the panel maps it to a style.</summary>
    public enum CommentaryIcon
    {
        Goal = 0,
        Save = 1,
        Block = 2,
        Miss = 3,
        Foul = 4,
        YellowCard = 5,
        RedCard = 6,
        Penalty = 7,
        Counter = 8,
        Cut = 9,
        Shout = 10,
        Substitution = 11
    }

    /// <summary>
    /// One localised phrase of a sentence. Its string takes {0} = the actor's name, {1} = the target's
    /// name and {2} = the actor's club; a slot of -1 means nobody is named.
    /// </summary>
    public readonly struct CommentaryClause
    {
        public string Key { get; }
        public bool Home { get; }
        public int Actor { get; }
        public bool TargetHome { get; }
        public int Target { get; }

        public CommentaryClause(string key, bool home, int actor = -1, int target = -1)
            : this(key, home, actor, home, target)
        {
        }

        public CommentaryClause(string key, bool home, int actor, bool targetHome, int target)
        {
            Key = key;
            Home = home;
            Actor = actor;
            TargetHome = targetHome;
            Target = target;
        }
    }

    /// <summary>
    /// One line of the commentary panel (spec R15): shown from <see cref="Frame"/>, stamped with the
    /// minute (a span for a cut summary), an icon, and the steps of the chain followed by how it ended.
    /// </summary>
    public sealed class CommentaryLine
    {
        public CommentaryLine(int frame, int minute, int endMinute, CommentaryIcon icon, bool highlight,
            IReadOnlyList<CommentaryClause> steps, CommentaryClause? outcome, SlotChange? change = null)
        {
            Frame = frame;
            Minute = minute;
            EndMinute = endMinute;
            Icon = icon;
            Highlight = highlight;
            Steps = steps;
            Outcome = outcome;
            Change = change;
        }

        public int Frame { get; }
        public int Minute { get; }

        /// <summary>The last minute a cut summary covers; equal to <see cref="Minute"/> for everything else.</summary>
        public int EndMinute { get; }

        public CommentaryIcon Icon { get; }

        /// <summary>Goals, cards, penalties and substitutions stand out in the panel.</summary>
        public bool Highlight { get; }

        public IReadOnlyList<CommentaryClause> Steps { get; }
        public CommentaryClause? Outcome { get; }

        /// <summary>
        /// The substitution this line tells, or null. It names players by id, not by slot: the slot
        /// changes hands at this very frame, so a slot lookup cannot name both men.
        /// </summary>
        public SlotChange? Change { get; }
    }
}
