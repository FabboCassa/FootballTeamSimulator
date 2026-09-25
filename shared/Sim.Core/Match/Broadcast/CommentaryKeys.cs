using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// The string-table keys the commentary is written in. Kept beside the builder so a test can
    /// check every one of them against the en and it tables.
    /// </summary>
    public static class CommentaryKeys
    {
        /// <summary>{0} minute, {1} sentence.</summary>
        public const string Line = "match.commentary.line";
        /// <summary>{0} first minute, {1} last minute, {2} sentence.</summary>
        public const string Span = "match.commentary.span";
        /// <summary>{0} the steps so far, {1} the next step.</summary>
        public const string Join = "match.commentary.join";
        /// <summary>{0} the steps, {1} how the chain ended.</summary>
        public const string Result = "match.commentary.outcome";

        public const string Counter = "match.commentary.counter";

        // A step has a named form and an "on" form for when its actor is the man the ball just went to.
        public const string Pass = "match.commentary.step.pass";
        public const string PassOn = "match.commentary.step.pass.on";
        public const string LongBall = "match.commentary.step.longball";
        public const string LongBallOn = "match.commentary.step.longball.on";
        public const string Cross = "match.commentary.step.cross";
        public const string CrossOn = "match.commentary.step.cross.on";
        public const string Dribble = "match.commentary.step.dribble";
        public const string DribbleOn = "match.commentary.step.dribble.on";
        public const string Shot = "match.commentary.step.shot";
        public const string ShotOn = "match.commentary.step.shot.on";
        public const string Header = "match.commentary.step.header";
        public const string HeaderOn = "match.commentary.step.header.on";
        public const string Recovery = "match.commentary.step.recovery";
        public const string Interception = "match.commentary.step.interception";
        public const string FreeKick = "match.commentary.step.freekick";
        public const string Corner = "match.commentary.step.corner";
        public const string Penalty = "match.commentary.step.penalty";
        public const string Foul = "match.commentary.step.foul";
        public const string Booked = "match.commentary.step.yellowcard";
        public const string SentOff = "match.commentary.step.redcard";

        public const string Goal = "match.commentary.outcome.goal";
        public const string Saved = "match.commentary.outcome.save";
        public const string Blocked = "match.commentary.outcome.block";
        public const string Missed = "match.commentary.outcome.miss";
        public const string FreeKickGiven = "match.commentary.outcome.freekick";
        public const string PenaltyGiven = "match.commentary.outcome.penalty";

        public const string CutNobody = "match.commentary.cut.none";
        public const string CutDefensive = "match.commentary.cut.defensive";
        public const string CutMiddle = "match.commentary.cut.middle";
        public const string CutAttacking = "match.commentary.cut.attacking";
        public const string StopHalfTime = "match.commentary.cut.stop.halftime";
        public const string StopFoul = "match.commentary.cut.stop.foul";
        public const string StopOffside = "match.commentary.cut.stop.offside";
        public const string StopCorner = "match.commentary.cut.stop.corner";
        public const string StopFreeKick = "match.commentary.cut.stop.freekick";
        public const string StopGoalKick = "match.commentary.cut.stop.goalkick";
        public const string StopThrowIn = "match.commentary.cut.stop.throwin";
        public const string StopKickoff = "match.commentary.cut.stop.kickoff";

        public const string ShoutPressHigh = "match.commentary.shout.press_high";
        public const string ShoutKeepBall = "match.commentary.shout.keep_ball";
        public const string ShoutAllForward = "match.commentary.shout.all_forward";
        public const string ShoutEncourage = "match.commentary.shout.encourage";
        public const string ShoutConcentrate = "match.commentary.shout.concentrate";

        public static IReadOnlyList<string> All { get; } = new[]
        {
            Line, Span, Join, Result, Counter,
            Pass, PassOn, LongBall, LongBallOn, Cross, CrossOn, Dribble, DribbleOn,
            Shot, ShotOn, Header, HeaderOn, Recovery, Interception, FreeKick, Corner, Penalty,
            Foul, Booked, SentOff,
            Goal, Saved, Blocked, Missed, FreeKickGiven, PenaltyGiven,
            CutNobody, CutDefensive, CutMiddle, CutAttacking,
            StopHalfTime, StopFoul, StopOffside, StopCorner, StopFreeKick, StopGoalKick, StopThrowIn, StopKickoff,
            ShoutPressHigh, ShoutKeepBall, ShoutAllForward, ShoutEncourage, ShoutConcentrate
        };
    }
}
