using System;
using Sim.Core.Match;

namespace Fts.MatchView
{
    /// <summary>
    /// Turns a <see cref="BallAction"/> into a line of commentary (task 13.1). Lives in
    /// FTS.MatchView because it needs the Sim.Core type; the views stay Sim.Core-free and
    /// simply take the finished string.
    ///
    /// Naming is the caller's job (<c>nameOf</c>): a career screen can look a player up
    /// in the squad, while an online replay arrives as a bare MatchReport and has only
    /// the shirt numbers the stream carries — which is exactly why it carries them.
    /// </summary>
    public static class MatchCommentary
    {
        /// <summary>Actions worth reading at 2x/4x, when a line per touch would be a blur.</summary>
        public static bool IsMajor(BallActionKind kind)
        {
            switch (kind)
            {
                case BallActionKind.Pass:
                case BallActionKind.Dribble:
                case BallActionKind.ThrowIn:
                case BallActionKind.GoalKick:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// The localization key for an action. Every kind has one, in en and it alike.
        /// </summary>
        public static string KeyOf(BallActionKind kind)
        {
            switch (kind)
            {
                case BallActionKind.Kickoff: return "match.action.kickoff";
                case BallActionKind.Pass: return "match.action.pass";
                case BallActionKind.LongBall: return "match.action.longball";
                case BallActionKind.Cross: return "match.action.cross";
                case BallActionKind.Dribble: return "match.action.dribble";
                case BallActionKind.Tackle: return "match.action.tackle";
                case BallActionKind.Interception: return "match.action.interception";
                case BallActionKind.Clearance: return "match.action.clearance";
                case BallActionKind.Shot: return "match.action.shot";
                case BallActionKind.Save: return "match.action.save";
                case BallActionKind.Goal: return "match.action.goal";
                case BallActionKind.Miss: return "match.action.miss";
                case BallActionKind.Corner: return "match.action.corner";
                case BallActionKind.ThrowIn: return "match.action.throwin";
                case BallActionKind.GoalKick: return "match.action.goalkick";
                default: return "match.action.freekick";
            }
        }

        /// <summary>
        /// One line of commentary. <paramref name="tr"/> is the localization lookup and
        /// <paramref name="nameOf"/> resolves (side, lineup slot) to something printable.
        /// </summary>
        public static string Describe(
            BallAction action, Func<string, object[], string> tr, Func<bool, int, string> nameOf)
        {
            if (tr == null)
                return string.Empty;

            string key = KeyOf(action.Kind);
            string actor = nameOf != null && action.Slot >= 0 ? nameOf(action.Home, action.Slot) : string.Empty;
            string target = nameOf != null && action.TargetSlot >= 0
                ? nameOf(action.Home, action.TargetSlot)
                : string.Empty;

            return tr(key, new object[] { actor, target });
        }
    }
}
