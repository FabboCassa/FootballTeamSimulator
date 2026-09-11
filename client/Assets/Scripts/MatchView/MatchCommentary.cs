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

                // The referee (engine phase 5). Without these the whistle, the flag and the cards
                // all fell through to "free kick", which is the RESTART and not the decision.
                case BallActionKind.Offside: return "match.action.offside";
                case BallActionKind.Foul: return "match.action.foul";
                case BallActionKind.YellowCard: return "match.action.yellowcard";
                case BallActionKind.RedCard: return "match.action.redcard";
                case BallActionKind.Penalty: return "match.action.penalty";
                case BallActionKind.HalfTime: return "match.action.halftime";

                // The strike decides (engine phase 6). A charged-down shot is neither a save nor
                // a miss and it needs saying: the defence did that, not the finishing.
                case BallActionKind.Block: return "match.action.block";

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
            // A foul's TargetSlot is the man FOULED, and he plays for the other side — so he is not
            // looked up here, where the side is the offender's. Everything else that carries a target
            // (a pass, a cross) is naming a team-mate.
            string target = nameOf != null && action.TargetSlot >= 0 && action.Kind != BallActionKind.Foul
                ? nameOf(action.Home, action.TargetSlot)
                : string.Empty;

            return tr(key, new object[] { actor, target });
        }
    }
}
