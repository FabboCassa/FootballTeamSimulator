using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// Reads the ball's story into sentences: every strike's outcome (goal, save, block, miss) told
    /// with the touches that led to it, and every foul with its card and the restart it gave.
    /// </summary>
    internal static class ChainReader
    {
        // A ball won in the own half and shot from within this long is told as a counter-attack.
        private const int CounterShotWindowSeconds = 15;

        public static void Read(PositionStream s, List<CommentaryLine> lines)
        {
            List<BallAction> actions = s.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction a = actions[i];
                switch (a.Kind)
                {
                    case BallActionKind.Goal:
                    case BallActionKind.Miss:
                        lines.Add(ShotLine(s, i, a.Home));
                        break;
                    case BallActionKind.Save:
                    case BallActionKind.Block:
                        lines.Add(ShotLine(s, i, !a.Home));
                        break;
                    case BallActionKind.Foul:
                        lines.Add(FoulLine(s, i));
                        break;
                }
            }
        }

        /// <summary>The strike that ended at <paramref name="end"/>, told from the start of its move.</summary>
        private static CommentaryLine ShotLine(PositionStream s, int end, bool attacking)
        {
            List<BallAction> actions = s.Actions;
            BallAction outcome = actions[end];
            var steps = new List<CommentaryClause>();

            int shot = StrikeBefore(actions, end, attacking);
            bool counter = false;
            if (shot >= 0)
            {
                int start = MoveStart(actions, shot, attacking);
                counter = IsCounter(s, actions, start, shot);
                if (counter) steps.Add(new CommentaryClause(CommentaryKeys.Counter, attacking));
                AddBuildUp(actions, start, shot, steps);
            }

            bool goal = outcome.Kind == BallActionKind.Goal;
            CommentaryIcon icon = goal ? CommentaryIcon.Goal
                : counter ? CommentaryIcon.Counter
                : outcome.Kind == BallActionKind.Save ? CommentaryIcon.Save
                : outcome.Kind == BallActionKind.Block ? CommentaryIcon.Block
                : CommentaryIcon.Miss;
            return Line(s, outcome.Tick, icon, goal, steps, OutcomeClause(outcome));
        }

        private static CommentaryClause OutcomeClause(BallAction a)
        {
            switch (a.Kind)
            {
                case BallActionKind.Goal: return new CommentaryClause(CommentaryKeys.Goal, a.Home, a.Slot);
                case BallActionKind.Save: return new CommentaryClause(CommentaryKeys.Saved, a.Home, a.Slot);
                case BallActionKind.Block: return new CommentaryClause(CommentaryKeys.Blocked, a.Home, a.Slot);
                default: return new CommentaryClause(CommentaryKeys.Missed, a.Home, a.Slot);
            }
        }

        /// <summary>
        /// The strike this outcome ends, or -1 when another outcome comes first: a shot blocked and
        /// then turned in has had its strike told already.
        /// </summary>
        private static int StrikeBefore(List<BallAction> actions, int end, bool attacking)
        {
            for (int j = end - 1; j >= 0; j--)
            {
                BallActionKind kind = actions[j].Kind;
                if (kind == BallActionKind.Shot) return actions[j].Home == attacking ? j : -1;
                if (IsOutcome(kind)) return -1;
            }

            return -1;
        }

        private static bool IsOutcome(BallActionKind kind) =>
            kind == BallActionKind.Goal || kind == BallActionKind.Save
            || kind == BallActionKind.Miss || kind == BallActionKind.Block;

        /// <summary>
        /// The first action of the move that ends in the strike at <paramref name="shot"/>: walking back,
        /// the move stops at an opponent's touch, at the restart or the ball won that began it.
        /// </summary>
        private static int MoveStart(List<BallAction> actions, int shot, bool attacking)
        {
            int start = shot;
            for (int j = shot - 1; j >= 0; j--)
            {
                BallAction a = actions[j];
                if (a.Home != attacking) break;

                switch (a.Kind)
                {
                    case BallActionKind.Pass:
                    case BallActionKind.LongBall:
                    case BallActionKind.Cross:
                    case BallActionKind.Dribble:
                        start = j;
                        continue;
                    case BallActionKind.Recovery:
                    case BallActionKind.Interception:
                    case BallActionKind.FreeKick:
                    case BallActionKind.Corner:
                    case BallActionKind.Penalty:
                        return j;
                }

                break;
            }

            return start;
        }

        private static bool IsCounter(PositionStream s, List<BallAction> actions, int start, int shot)
        {
            BallAction won = actions[start];
            if (won.Kind != BallActionKind.Recovery && won.Kind != BallActionKind.Interception) return false;
            if (won.Tick * 2 + 1 >= s.BallXY.Length) return false;

            int window = CounterShotWindowSeconds * s.TicksPerMinute / 60;
            return actions[shot].Tick - won.Tick <= window
                && FrameClassifier.Progress(s.BallXY[won.Tick * 2], won.Home) < Pitch.LengthDm / 2;
        }

        /// <summary>
        /// The last <see cref="CommentaryBuilder.MaxBuildUpSteps"/> touches and the strike. A touch by
        /// the man the ball just went to reads without his name ("cross"), as a commentator would.
        /// </summary>
        private static void AddBuildUp(List<BallAction> actions, int start, int shot, List<CommentaryClause> steps)
        {
            int from = System.Math.Max(start, shot - CommentaryBuilder.MaxBuildUpSteps);
            int holder = -1;
            for (int j = from; j < shot; j++)
            {
                BallAction a = actions[j];
                steps.Add(Step(a, a.Slot == holder));
                holder = IsDelivery(a.Kind) && a.TargetSlot >= 0 ? a.TargetSlot : a.Slot;
            }

            BallAction strike = actions[shot];
            bool header = shot > start && actions[shot - 1].Kind == BallActionKind.Cross;
            bool on = strike.Slot == holder;
            string key = header ? (on ? CommentaryKeys.HeaderOn : CommentaryKeys.Header)
                : on ? CommentaryKeys.ShotOn : CommentaryKeys.Shot;
            steps.Add(new CommentaryClause(key, strike.Home, strike.Slot));
        }

        private static bool IsDelivery(BallActionKind kind) =>
            kind == BallActionKind.Pass || kind == BallActionKind.LongBall || kind == BallActionKind.Cross;

        private static CommentaryClause Step(BallAction a, bool on)
        {
            string key;
            switch (a.Kind)
            {
                case BallActionKind.Pass: key = on ? CommentaryKeys.PassOn : CommentaryKeys.Pass; break;
                case BallActionKind.LongBall: key = on ? CommentaryKeys.LongBallOn : CommentaryKeys.LongBall; break;
                case BallActionKind.Cross: key = on ? CommentaryKeys.CrossOn : CommentaryKeys.Cross; break;
                case BallActionKind.Dribble: key = on ? CommentaryKeys.DribbleOn : CommentaryKeys.Dribble; break;
                case BallActionKind.Recovery: key = CommentaryKeys.Recovery; break;
                case BallActionKind.Interception: key = CommentaryKeys.Interception; break;
                case BallActionKind.FreeKick: key = CommentaryKeys.FreeKick; break;
                case BallActionKind.Corner: key = CommentaryKeys.Corner; break;
                default: key = CommentaryKeys.Penalty; break;
            }

            return new CommentaryClause(key, a.Home, a.Slot, a.TargetSlot);
        }

        /// <summary>The foul, the card it earned and the restart it gave, all recorded on its frame.</summary>
        private static CommentaryLine FoulLine(PositionStream s, int at)
        {
            List<BallAction> actions = s.Actions;
            BallAction foul = actions[at];
            var steps = new List<CommentaryClause>
            {
                new CommentaryClause(CommentaryKeys.Foul, foul.Home, foul.Slot, !foul.Home, foul.TargetSlot)
            };

            CommentaryIcon icon = CommentaryIcon.Foul;
            CommentaryClause? restart = null;
            for (int j = at + 1; j < actions.Count && actions[j].Tick == foul.Tick; j++)
            {
                BallAction a = actions[j];
                if (a.Home == foul.Home && a.Slot == foul.Slot && a.Kind == BallActionKind.YellowCard)
                {
                    steps.Add(new CommentaryClause(CommentaryKeys.Booked, a.Home, a.Slot));
                    icon = CommentaryIcon.YellowCard;
                }
                else if (a.Home == foul.Home && a.Slot == foul.Slot && a.Kind == BallActionKind.RedCard)
                {
                    steps.Add(new CommentaryClause(CommentaryKeys.SentOff, a.Home, a.Slot));
                    icon = CommentaryIcon.RedCard;
                }
                else if (a.Home != foul.Home && a.Kind == BallActionKind.FreeKick)
                {
                    restart = new CommentaryClause(CommentaryKeys.FreeKickGiven, a.Home, a.Slot);
                }
                else if (a.Home != foul.Home && a.Kind == BallActionKind.Penalty)
                {
                    restart = new CommentaryClause(CommentaryKeys.PenaltyGiven, a.Home, a.Slot);
                    if (icon == CommentaryIcon.Foul) icon = CommentaryIcon.Penalty;
                }
            }

            return Line(s, foul.Tick, icon, icon != CommentaryIcon.Foul, steps, restart);
        }

        private static CommentaryLine Line(PositionStream s, int frame, CommentaryIcon icon, bool highlight,
            List<CommentaryClause> steps, CommentaryClause? outcome)
        {
            int minute = CommentaryBuilder.MinuteOf(frame, s.TicksPerMinute);
            return new CommentaryLine(frame, minute, minute, icon, highlight, steps, outcome);
        }
    }
}
