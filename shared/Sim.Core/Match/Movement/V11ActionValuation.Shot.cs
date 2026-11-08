using System;
using Sim.Core.Match.Movement.Models;

namespace Sim.Core.Match.Movement
{
    public sealed partial class V11ActionValuation
    {
        /// <summary>
        /// Having a go (believable-match R3, issue #182): what it is worth in goals. Its xG, less the
        /// strikes the bodies in the lane charge down (<see cref="ShotBlockPermille"/>), plus what a
        /// strike that does not go in leaves (<see cref="MissValue"/>).
        /// The side's shot appetite is a bias on it, the identity at the middle level. NoOption out
        /// of range or for a keeper.
        /// </summary>
        public int ShotValue(V11Scene s)
        {
            if (s.CarrierIsKeeper) return NoOption;
            return ShotAt(s, s.BallXDm, s.BallYDm, s.PressurePermille, s.Shooting, s.Technique);
        }

        /// <summary>
        /// The shot a man of this finishing would have from (x, y) under this pressure, against the
        /// other side as the scene has it: the value a ball to him there, or a carry there, is worth
        /// when his shot beats the threat of the spot (R3: the value at the receiver).
        /// </summary>
        private int ShotAt(V11Scene s, int x, int y, int pressure, int shooting, int technique)
        {
            int goalX = GoalX(s);
            int range = _cfg.V11ShotRangeDm;
            if (DistanceSq(x, y, goalX, Pitch.CenterY) > (long)range * range) return NoOption;

            int xg = ExpectedGoals.Permille(x, y, s.AttacksHighX, pressure, shooting, technique, _models) * 10;
            int scored = (int)((long)xg * (1000 - ShotBlockPermille(s, x, y)) / 1000);
            int value = scored + (int)((long)(10_000 - scored) * MissValue(s, x, y) / 10_000);

            value = value * Math.Max(0, s.ShotAppetitePercent) / 100;
            bool outsideBox = Math.Abs(goalX - x) > MovementGeometry.BoxDepthDm
                              || Math.Abs(y - Pitch.CenterY) > MovementGeometry.BoxHalfWidthDm;
            if (outsideBox) value = value * DistanceReachPercent(s) / 100;
            return value;
        }

        /// <summary>
        /// What a strike from (x, y) that does not go in leaves: the corner, the rebound the side
        /// keeps, less the ball lost at the other side's goal, priced as a pass's lost ball is.
        /// </summary>
        public int MissValue(V11Scene s, int x, int y)
        {
            int here = ExpectedThreat.ValuePer10k(x, y, s.AttacksHighX, _models);
            return _cfg.V11ShotMissCornerPer10k
                   + here * _cfg.V11ShotMissKeptPercent / 100
                   - LostWeight(s, GoalX(s), Pitch.CenterY) * _cfg.V11ShotMissLostPercent / 100;
        }

        /// <summary>
        /// A shot ends the possession, so it has to be worth more than the possession it ends: the
        /// threat of the spot, which already holds the shots, passes and losses that follow from
        /// there. Every other option keeps the ball, and is priced on where it ends up. Not for a free
        /// man in front of goal (<see cref="Config.MatchBalance.V11FreeShotRangeDm"/>): the threat
        /// there is mostly his own shot, and his carry and passes already price keeping the ball.
        /// </summary>
        public bool ShotBeatsPossession(V11Scene s, int shot) =>
            shot > NoOption
            && ((s.PressurePermille < _cfg.V11FreeShotPressurePermille && Within(s, _cfg.V11FreeShotRangeDm))
                || shot >= ExpectedThreat.ValuePer10k(s.BallXDm, s.BallYDm, s.AttacksHighX, _models));

        /// <summary>Whether the ball is within <paramref name="rangeDm"/> of the centre of the goal the man on it attacks.</summary>
        public bool Within(V11Scene s, int rangeDm) =>
            DistanceSq(s.BallXDm, s.BallYDm, GoalX(s), Pitch.CenterY) <= (long)rangeDm * rangeDm;

        /// <summary>A shot one touch on, after the carry: <see cref="Config.MatchBalance.V11DeferredShotPercent"/> of one now.</summary>
        private int Deferred(int shot) => shot <= NoOption ? NoOption : shot * _cfg.V11DeferredShotPercent / 100;

        /// <summary>The receiver's shot after a ball to him: <see cref="Config.MatchBalance.V11ReceiverShotPercent"/> of one now.</summary>
        private int ReceiverShot(int shot) => shot <= NoOption ? NoOption : shot * _cfg.V11ReceiverShotPercent / 100;

        /// <summary>
        /// The odds a strike from (x, y) is charged down, in permille, by the simulator's own block
        /// model (<see cref="ShotBlock"/>): each outfield defender past the strike point and short
        /// of the goal line has his go, at his distance from the ball's line. The strike is aimed
        /// either side of the keeper, as the simulator aims it, from the placement of a strike of no
        /// quality (<see cref="Config.MatchBalance.ShotPlacementCentrePermille"/>) to just inside
        /// the post; the four aims are equally likely.
        /// </summary>
        public int ShotBlockPermille(V11Scene s, int x, int y)
        {
            int goalX = GoalX(s);
            int reach = MovementGeometry.GoalHalfWidthDm - _cfg.ShotAimInsidePostDm;
            int centre = reach * _cfg.ShotPlacementCentrePermille / 1000;
            return (BlockedPermille(s, x, y, goalX, Pitch.CenterY - reach)
                    + BlockedPermille(s, x, y, goalX, Pitch.CenterY - centre)
                    + BlockedPermille(s, x, y, goalX, Pitch.CenterY + centre)
                    + BlockedPermille(s, x, y, goalX, Pitch.CenterY + reach)) / 4;
        }

        private int BlockedPermille(V11Scene s, int x, int y, int aimX, int aimY)
        {
            long dx = aimX - x, dy = aimY - y;
            long length2 = dx * dx + dy * dy;
            if (length2 == 0) return 0;
            int length = MovementGeometry.Distance(x, y, aimX, aimY);
            int reachU = U.Units(_cfg.ControlRadiusDm);

            int through = 1000;
            for (int i = 0; i < s.FoeCount; i++)
            {
                if (i == s.FoeKeeper) continue;
                PitchActor foe = s.Foes[i];
                long along = (foe.XDm - x) * dx + (foe.YDm - y) * dy;
                if (along <= 0 || along >= length2) continue;

                long cross = (foe.XDm - x) * dy - (foe.YDm - y) * dx;
                int lane = (int)(Math.Abs(cross) / length);
                int fromStrike = MovementGeometry.Distance(foe.XDm, foe.YDm, x, y);
                int p = ShotBlock.Permille(U.Units(lane), reachU, U.Units(fromStrike), _cfg);
                through = through * (1000 - p) / 1000;
            }

            return 1000 - through;
        }

        /// <summary>
        /// The pressure a man at (x, y) would be under once the ball is there, as the simulator reads
        /// it (the nearest opponent inside the pressure radius), with every opponent first closing
        /// <paramref name="closingDm"/> plus what his top speed covers in <paramref name="closingMs"/>:
        /// whoever can get to him while the ball travels, or while he runs with it, is on him when he shoots.
        /// </summary>
        private int PressureAt(V11Scene s, int x, int y, int closingDm, int closingMs)
        {
            int radius = Math.Max(1, _cfg.PressureRadiusDm);
            int worst = 0;
            for (int i = 0; i < s.FoeCount; i++)
            {
                PitchActor foe = s.Foes[i];
                int distance = MovementGeometry.Distance(x, y, foe.XDm, foe.YDm)
                               - closingDm - foe.TopSpeedDmPerSecond * closingMs / 1000;
                if (distance >= radius) continue;
                int p = distance <= 0 ? 1000 : 1000 - 1000 * distance / radius;
                if (p > worst) worst = p;
            }

            return worst;
        }

        /// <summary>
        /// A keen side has a go from distance more readily: the appetite above 100, at
        /// <see cref="Config.MatchBalance.V11ShotAppetiteRangePercent"/>, on a shot from outside the box.
        /// Never below 100 — a patient side's shot is already priced down by its appetite.
        /// </summary>
        private int DistanceReachPercent(V11Scene s)
        {
            int over = s.ShotAppetitePercent > 100 ? s.ShotAppetitePercent - 100 : 0;
            return 100 + over * _cfg.V11ShotAppetiteRangePercent / 100;
        }
    }
}
