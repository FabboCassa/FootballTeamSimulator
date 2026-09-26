using System;
using Sim.Core.Config;
using Sim.Core.Match.Movement.Models;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>What the man on the ball does, as the V11 brain decides it.</summary>
    public enum V11ActionKind
    {
        /// <summary>Keeps it at his feet for now (the tempo's hold), with no open goal in front of him.</summary>
        Hold = 0,
        Carry,
        Pass,
        Cross,
        Clear,
        Shot,

        /// <summary>A 1v1 with the keeper on top of him: he takes it round him.</summary>
        RoundKeeper,

        /// <summary>An open goal from the edge of range: one touch in, then the shot.</summary>
        Drive
    }

    /// <summary>One option, and what it is worth in ten-thousandths of a goal.</summary>
    public struct V11Choice
    {
        public V11ActionKind Kind;

        /// <summary>Index into <see cref="V11Scene.Mates"/> for a pass or a cross, -1 otherwise.</summary>
        public int Mate;

        /// <summary>Where the ball is played (pass, cross) or taken (carry, round the keeper), in dm.</summary>
        public int XDm;
        public int YDm;

        /// <summary>The odds it comes off, in permille.</summary>
        public int SafetyPermille;

        public int ValuePer10k;
    }

    /// <summary>
    /// The V11 brain's choice on the ball (watchable-match spec R3-R5): utility-AI selection
    /// (Buckland) over one currency, ten-thousandths of a goal. Moving the ball — a pass, a cross,
    /// a carry, a clearance — is worth the expected threat (Karun Singh's xT) where it ends up if
    /// it comes off, less the threat the other side gets from there if it does not; the odds it
    /// comes off are pitch control's (Spearman) for a pass and a cross, the duel's for a carry. A
    /// shot is worth its xG. The instructions weigh the terms: Tempo the gain, and its directness
    /// the long forward ball; Mentality the risk; Mentality times Tempo the shot; Width the cross.
    ///
    /// R4 sits above the prices: a carrier inside <see cref="MatchBalance.V11OpenGoalRangeDm"/> of
    /// goal with no outfield defender in the ball-to-posts triangle shoots, whatever else is on:
    /// at once from close in, after one touch in from the edge of range, and with the keeper on
    /// top of him he takes it round him instead.
    ///
    /// Pure, integer, draw-free and allocation-free, like <see cref="V11Positioning"/>.
    /// </summary>
    public sealed class V11ActionValuation
    {
        /// <summary>An option that is not open to him; never chosen.</summary>
        public const int NoOption = int.MinValue / 4;

        private readonly MatchBalance _cfg;
        private readonly ActionModelBalance _models;

        public V11ActionValuation(MatchBalance cfg)
        {
            _cfg = cfg;
            _models = cfg.ActionModels;
        }

        /// <summary>
        /// The choice. While <paramref name="holding"/> (the tempo's hold is still running) he acts
        /// only on an open goal; otherwise he takes the dearest option — ties go to the shot, then
        /// the pass, then the clearance, and a man with nothing else carries it.
        /// </summary>
        public V11Choice Choose(V11Scene s, bool holding)
        {
            if (!s.CarrierIsKeeper && IsOpenGoal(s))
            {
                if (KeeperOnTopOfHim(s, out int rx, out int ry))
                    return new V11Choice { Kind = V11ActionKind.RoundKeeper, Mate = -1, XDm = rx, YDm = ry };

                if (DriveIn(s, out int dx, out int dy))
                    return new V11Choice { Kind = V11ActionKind.Drive, Mate = -1, XDm = dx, YDm = dy };

                return new V11Choice
                {
                    Kind = V11ActionKind.Shot, Mate = -1, XDm = GoalX(s), YDm = Pitch.CenterY,
                    ValuePer10k = ShotValue(s)
                };
            }

            if (holding) return new V11Choice { Kind = V11ActionKind.Hold, Mate = -1 };

            int carry = CarryValue(s, out int cx, out int cy);
            var best = new V11Choice
            {
                Kind = V11ActionKind.Carry, Mate = -1, XDm = cx, YDm = cy,
                SafetyPermille = s.CarryKeepPermille, ValuePer10k = carry
            };

            int clear = ClearValue(s);
            if (clear > best.ValuePer10k)
            {
                best = new V11Choice
                {
                    Kind = V11ActionKind.Clear, Mate = -1, XDm = s.ClearXDm, YDm = s.ClearYDm,
                    SafetyPermille = _cfg.ClearanceRetentionPermille, ValuePer10k = clear
                };
            }

            int pass = PassValue(s, out V11Choice bestPass);
            if (pass > NoOption && pass >= best.ValuePer10k) best = bestPass;

            int shot = ShotValue(s);
            if (shot > NoOption && shot >= best.ValuePer10k)
                best = new V11Choice { Kind = V11ActionKind.Shot, Mate = -1, XDm = GoalX(s), YDm = Pitch.CenterY, ValuePer10k = shot };

            return best;
        }

        // ------------------------------------------------------------------ R4

        /// <summary>
        /// Inside the open-goal range of the goal centre, and no outfield defender inside (or on the
        /// edge of) the triangle from the ball to the posts. The keeper does not close it.
        /// </summary>
        public bool IsOpenGoal(V11Scene s)
        {
            int goalX = GoalX(s);
            int range = _cfg.V11OpenGoalRangeDm;
            if (DistanceSq(s.BallXDm, s.BallYDm, goalX, Pitch.CenterY) > (long)range * range) return false;

            for (int i = 0; i < s.FoeCount; i++)
            {
                if (i == s.FoeKeeper) continue;
                if (InShootingTriangle(s, goalX, s.Foes[i].XDm, s.Foes[i].YDm)) return false;
            }

            return true;
        }

        /// <summary>
        /// An open goal seen from beyond <see cref="MatchBalance.V11OpenGoalShootNowDm"/>: the first
        /// time he sees it he takes one touch in, <see cref="MatchBalance.V11OpenGoalDriveDm"/> at the
        /// goal centre, and shoots on the next decision, still inside R4's 1.5 s, from closer in.
        /// </summary>
        private bool DriveIn(V11Scene s, out int x, out int y)
        {
            x = 0;
            y = 0;
            int goalX = GoalX(s);
            int near = _cfg.V11OpenGoalShootNowDm;
            if (s.OpenGoalForMs > 0) return false;
            if (DistanceSq(s.BallXDm, s.BallYDm, goalX, Pitch.CenterY) <= (long)near * near) return false;

            // Not into a keeper coming out to meet him: the touch would be his.
            if (s.FoeKeeper >= 0)
            {
                PitchActor keeper = s.Foes[s.FoeKeeper];
                int clear = _cfg.V11OpenGoalDriveDm + _cfg.V11RoundKeeperDm;
                if (InShootingTriangle(s, goalX, keeper.XDm, keeper.YDm)
                    && DistanceSq(s.BallXDm, s.BallYDm, keeper.XDm, keeper.YDm) < (long)clear * clear)
                    return false;
            }

            int span = MovementGeometry.Distance(s.BallXDm, s.BallYDm, goalX, Pitch.CenterY);
            int touch = Math.Min(_cfg.V11OpenGoalDriveDm, span);
            x = s.BallXDm + (int)((long)(goalX - s.BallXDm) * touch / span);
            y = s.BallYDm + (int)((long)(Pitch.CenterY - s.BallYDm) * touch / span);
            return true;
        }

        /// <summary>
        /// A 1v1 with the keeper standing on him: in the triangle, within
        /// <see cref="MatchBalance.V11RoundKeeperDm"/>. The way round is the side he is not already
        /// on (the middle, when he is square), to a point past him.
        /// </summary>
        private bool KeeperOnTopOfHim(V11Scene s, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (s.FoeKeeper < 0) return false;

            PitchActor keeper = s.Foes[s.FoeKeeper];
            int goalX = GoalX(s);
            int reach = _cfg.V11RoundKeeperDm;
            if (DistanceSq(s.BallXDm, s.BallYDm, keeper.XDm, keeper.YDm) > (long)reach * reach) return false;
            if (!InShootingTriangle(s, goalX, keeper.XDm, keeper.YDm)) return false;

            int dir = s.AttacksHighX ? 1 : -1;
            int side = keeper.YDm > s.BallYDm ? -1 : 1;
            if (keeper.YDm == s.BallYDm) side = s.BallYDm > Pitch.CenterY ? -1 : 1;
            x = Pitch.ClampX(keeper.XDm + dir * reach / 2);
            y = Pitch.ClampY(keeper.YDm + side * _cfg.V11RoundKeeperSideDm);
            return true;
        }

        private static bool InShootingTriangle(V11Scene s, int goalX, int px, int py)
        {
            int half = MovementGeometry.GoalHalfWidthDm;
            long d1 = Cross(px, py, s.BallXDm, s.BallYDm, goalX, Pitch.CenterY - half);
            long d2 = Cross(px, py, goalX, Pitch.CenterY - half, goalX, Pitch.CenterY + half);
            long d3 = Cross(px, py, goalX, Pitch.CenterY + half, s.BallXDm, s.BallYDm);
            bool negative = d1 < 0 || d2 < 0 || d3 < 0;
            bool positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive);
        }

        private static long Cross(long px, long py, long ax, long ay, long bx, long by) =>
            (bx - ax) * (py - ay) - (by - ay) * (px - ax);

        // ------------------------------------------------------------------ the options

        /// <summary>Having a go: the xG of it, weighed by his side's shot appetite. NoOption out of range or for a keeper.</summary>
        public int ShotValue(V11Scene s)
        {
            if (s.CarrierIsKeeper) return NoOption;
            int goalX = GoalX(s);
            int range = _cfg.V11ShotRangeDm;
            if (DistanceSq(s.BallXDm, s.BallYDm, goalX, Pitch.CenterY) > (long)range * range) return NoOption;

            int xg = ExpectedGoals.Permille(
                s.BallXDm, s.BallYDm, s.AttacksHighX, s.PressurePermille, s.Shooting, s.Technique, _models);
            return xg * 10 * Math.Max(0, s.ShotAppetitePercent) / 100;
        }

        /// <summary>
        /// Running with it, one touch on: straight on, drifting in off the touchline, and at the
        /// goal mouth once he is in the last stretch — the carry the simulator will execute. Lost,
        /// it is lost where he stands.
        /// </summary>
        public int CarryValue(V11Scene s, out int x, out int y)
        {
            int dir = s.AttacksHighX ? 1 : -1;
            int goalX = GoalX(s);
            int room = dir * (goalX - s.BallXDm);
            int aimX, aimY;
            if (room <= _cfg.CarryCutInsideDm)
            {
                aimX = goalX - dir * _cfg.CarryGoalStandOffDm;
                aimY = Pitch.CenterY;
            }
            else
            {
                aimX = s.BallXDm + dir * Pitch.LengthDm;
                aimY = s.BallYDm + (Pitch.CenterY - s.BallYDm) / 8;
            }

            int span = MovementGeometry.Distance(s.BallXDm, s.BallYDm, aimX, aimY);
            int touch = Math.Max(0, s.CarryTouchDm);
            if (span <= touch || span == 0)
            {
                x = Pitch.ClampX(aimX);
                y = Pitch.ClampY(aimY);
            }
            else
            {
                x = Pitch.ClampX(s.BallXDm + (int)((long)(aimX - s.BallXDm) * touch / span));
                y = Pitch.ClampY(s.BallYDm + (int)((long)(aimY - s.BallYDm) * touch / span));
            }

            // A keeper runs with it only when there is nothing else to do with it.
            if (s.CarrierIsKeeper) return NoOption;
            return MoveValue(s, x, y, s.CarryKeepPermille, s.BallXDm, s.BallYDm, GainPercent(s, false, false));
        }

        /// <summary>Hoofing it upfield: kept now and then, and lost forty metres from his own goal.</summary>
        public int ClearValue(V11Scene s)
        {
            if (s.CarrierIsKeeper || !s.CanClear) return NoOption;
            int keep = _cfg.ClearanceRetentionPermille;
            return MoveValue(s, s.ClearXDm, s.ClearYDm, keep, s.ClearXDm, s.ClearYDm, GainPercent(s, false, false));
        }

        /// <summary>
        /// The best ball to a team-mate: to his feet, or into his path toward goal. From the wide
        /// channel of the final stretch a ball to a man in the box is a cross. The odds are the
        /// lane's and the receiver's under pitch control, times the passer's execution.
        /// </summary>
        public int PassValue(V11Scene s, out V11Choice best)
        {
            best = new V11Choice { Kind = V11ActionKind.Pass, Mate = -1, ValuePer10k = NoOption };
            int dir = s.AttacksHighX ? 1 : -1;
            ReadOnlySpan<PitchActor> foes = new ReadOnlySpan<PitchActor>(s.Foes, 0, s.FoeCount);
            bool wide = IsWideChannel(s.BallYDm) && DistanceSq(s.BallXDm, s.BallYDm, GoalX(s), Pitch.CenterY)
                        < (long)(Pitch.LengthDm / 3) * (Pitch.LengthDm / 3);

            for (int m = 0; m < s.MateCount; m++)
            {
                PitchActor mate = s.Mates[m];
                if (dir * mate.XDm > dir * s.OffsideLineXDm) continue;

                for (int option = 0; option < 2; option++)
                {
                    int tx = Pitch.ClampX(mate.XDm + (option == 1 ? dir * _cfg.V11PassLeadDm : 0));
                    int ty = mate.YDm;
                    if (option == 1 && s.MateIsKeeper[m]) continue;

                    int distance = MovementGeometry.Distance(s.BallXDm, s.BallYDm, tx, ty);
                    if (distance < _cfg.MinPassDm || distance > s.MaxPassDm) continue;

                    bool cross = wide && !s.MateIsKeeper[m] && InAttackedBox(s, tx, ty);
                    bool longForward = distance > _cfg.LongBallFromDm && dir * (tx - s.BallXDm) > 0;

                    int lane = PitchControl.LaneSafetyPermille(
                        s.BallXDm, s.BallYDm, tx, ty, _cfg.V11PassBallSpeedDmPerSecond, foes, _models);
                    int receiver = PitchControl.ReceiverSafetyPermille(mate, foes, tx, ty, _models);
                    int safety = lane * receiver / 1000 * Execution(s, distance) / 1000;

                    int value = MoveValue(s, tx, ty, safety, tx, ty, GainPercent(s, longForward, cross));
                    if (value > best.ValuePer10k)
                    {
                        best = new V11Choice
                        {
                            Kind = cross ? V11ActionKind.Cross : V11ActionKind.Pass,
                            Mate = m, XDm = tx, YDm = ty, SafetyPermille = safety, ValuePer10k = value
                        };
                    }
                }
            }

            return best.Mate >= 0 ? best.ValuePer10k : NoOption;
        }

        // ------------------------------------------------------------------ the currency

        /// <summary>
        /// What moving the ball to (x, y) is worth if it comes off at <paramref name="safety"/>:
        /// the threat there (its gain over here weighed by the instructions), less the threat the
        /// other side has from where the ball would be lost, weighed by the side's risk and by
        /// how much of it the man sees.
        /// </summary>
        private int MoveValue(V11Scene s, int x, int y, int safety, int lostX, int lostY, int gainPercent)
        {
            int here = ExpectedThreat.ValuePer10k(s.BallXDm, s.BallYDm, s.AttacksHighX, _models);
            int gain = ExpectedThreat.ValuePer10k(x, y, s.AttacksHighX, _models) - here;
            if (gain > 0) gain = gain * gainPercent / 100;

            int theirs = ExpectedThreat.ValuePer10k(lostX, lostY, !s.AttacksHighX, _models);
            int risk = MovementTactics.Percent(_cfg.V11MentalityRiskPercent, (int)s.Instructions.Mentality);
            int lost = theirs * risk / 100 * BallSkill.Clamp(s.VisionPercent, 0, 100) / 100;

            int p = BallSkill.Clamp(safety, 0, 1000);
            return ((here + gain) * p - lost * (1000 - p)) / 1000;
        }

        private int GainPercent(V11Scene s, bool longForward, bool cross)
        {
            TacticInstructions i = s.Instructions;
            int percent = MovementTactics.Percent(_cfg.V11TempoGainPercent, (int)i.Tempo);
            if (longForward) percent = percent * MovementTactics.Percent(_cfg.V11DirectnessPercent, (int)i.Tempo) / 100;
            if (cross) percent = percent * MovementTactics.Percent(_cfg.V11WidthCrossPercent, (int)i.Width) / 100;
            return percent;
        }

        /// <summary>The odds his own foot puts it where he means to, in permille: the pass error the simulator will draw.</summary>
        private int Execution(V11Scene s, int distanceDm)
        {
            bool longBall = distanceDm > _cfg.LongBallFromDm;
            int error = BallSkill.PassErrorPermille(s.Passing, s.Technique, s.PressurePermille, longBall, _cfg);
            int miss = (int)((long)distanceDm * error / 1000 * 375 / 1000);
            int tolerance = _cfg.ControlRadiusDm + 20;
            if (miss <= tolerance) return 1000;
            return BallSkill.Lerp(miss - tolerance, tolerance * 3, 1000, 250);
        }

        private static int GoalX(V11Scene s) => MovementGeometry.AttackedGoalX(s.AttacksHighX);

        private static bool IsWideChannel(int yDm) => yDm < Pitch.WidthDm / 4 || yDm > Pitch.WidthDm * 3 / 4;

        private static bool InAttackedBox(V11Scene s, int x, int y)
        {
            int depth = s.AttacksHighX ? Pitch.LengthDm - x : x;
            int across = y - Pitch.CenterY;
            return depth <= MovementGeometry.BoxDepthDm
                   && across <= MovementGeometry.BoxHalfWidthDm && -across <= MovementGeometry.BoxHalfWidthDm;
        }

        private static long DistanceSq(int ax, int ay, int bx, int by)
        {
            long dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy;
        }
    }
}
