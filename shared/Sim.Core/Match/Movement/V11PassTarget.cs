using Sim.Core.Config;
using Sim.Core.Match.Movement.Models;

namespace Sim.Core.Match.Movement
{
    /// <summary>Where a ball to a team-mate is aimed (free-ball spec R4).</summary>
    public enum V11PassShape
    {
        /// <summary>To where he stands.</summary>
        Feet = 0,

        /// <summary>Ahead of him: where his run takes him while it travels, or into his path toward goal when he stands.</summary>
        Lead,

        /// <summary>Behind the defensive line, for an onside man on its shoulder to run onto.</summary>
        Through
    }

    /// <summary>
    /// The aim of a ball to a team-mate (free-ball spec R4, issue #180), for the V11 brain to price
    /// (<see cref="V11ActionValuation"/>): to his feet, ahead of him (a lead pass), or behind the
    /// line (a through ball). The ball is free once struck (R3): he gets it only by running onto
    /// it, and whoever is first to it has it, so the odds are pitch control's at the aim, and an
    /// aim is offered whatever they are. The flight is read at the ball speed pitch control prices
    /// a pass at. Pure, integer and allocation-free.
    /// </summary>
    public static class V11PassTarget
    {
        /// <summary>How many shapes a ball to one team-mate can take.</summary>
        public const int Shapes = 3;

        /// <summary>
        /// Where a ball of this shape to <paramref name="mate"/> is aimed, in dm. False when the
        /// shape is not on: nothing but a ball to feet for his own keeper, and a through ball only
        /// for a man within <see cref="MatchBalance.ThroughBallRunInDm"/> short of the offside line
        /// with the ball at least <see cref="MatchBalance.MinPassDm"/> behind it.
        /// </summary>
        public static bool Aim(V11Scene s, in PitchActor mate, bool mateIsKeeper, V11PassShape shape, MatchBalance cfg,
            out int xDm, out int yDm)
        {
            xDm = mate.XDm;
            yDm = mate.YDm;
            if (shape == V11PassShape.Feet) return true;
            if (mateIsKeeper) return false;

            int dir = s.AttacksHighX ? 1 : -1;
            if (shape == V11PassShape.Lead) return Lead(s, mate, dir, cfg, out xDm, out yDm);
            return Through(s, mate, dir, cfg, out xDm, out yDm);
        }

        private static bool Lead(V11Scene s, in PitchActor mate, int dir, MatchBalance cfg, out int x, out int y)
        {
            int speed = MovementGeometry.Distance(0, 0, mate.VxDmPerSecond, mate.VyDmPerSecond);
            if (speed < cfg.LeadPassMinSpeedDmPerSecond)
            {
                x = Pitch.ClampX(mate.XDm + dir * cfg.V11PassLeadDm);
                y = mate.YDm;
                return true;
            }

            // Where his run has him when it gets there, read twice: a ball to a man running away
            // from it travels further than to where he stands now.
            RunOn(mate, FlightMs(s, mate.XDm, mate.YDm, cfg), cfg, out x, out y);
            RunOn(mate, FlightMs(s, x, y, cfg), cfg, out x, out y);
            return true;
        }

        private static bool Through(V11Scene s, in PitchActor mate, int dir, MatchBalance cfg, out int x, out int y)
        {
            x = mate.XDm;
            y = mate.YDm;
            int line = s.OffsideLineXDm;
            int shortOfLine = dir * (line - mate.XDm);
            if (shortOfLine < 0 || shortOfLine > cfg.ThroughBallRunInDm) return false;
            if (dir * (line - s.BallXDm) < cfg.MinPassDm) return false;

            int deepest = MovementGeometry.AttackedGoalX(s.AttacksHighX) - dir * cfg.ThroughBallGoalLineGapDm;
            int tx = line + dir * cfg.ThroughBallDepthDm;
            if (dir * tx > dir * deepest) tx = deepest;
            if (dir * (tx - line) <= 0) return false;

            // In behind, on the line of his run across the pitch.
            int dy = mate.VyDmPerSecond * FlightMs(s, tx, mate.YDm, cfg) / 1000;
            int cap = cfg.PassRunLeadMaxDm;
            if (dy > cap) dy = cap;
            if (dy < -cap) dy = -cap;
            x = tx;
            y = Pitch.ClampY(mate.YDm + dy);
            return true;
        }

        /// <summary>Where his run takes him in <paramref name="flightMs"/>, no further than PassRunLeadMaxDm.</summary>
        private static void RunOn(in PitchActor mate, int flightMs, MatchBalance cfg, out int x, out int y)
        {
            int dx = mate.VxDmPerSecond * flightMs / 1000, dy = mate.VyDmPerSecond * flightMs / 1000;
            int length = MovementGeometry.Distance(0, 0, dx, dy), cap = cfg.PassRunLeadMaxDm;
            if (length > cap)
            {
                dx = dx * cap / length;
                dy = dy * cap / length;
            }

            x = Pitch.ClampX(mate.XDm + dx);
            y = Pitch.ClampY(mate.YDm + dy);
        }

        private static int FlightMs(V11Scene s, int x, int y, MatchBalance cfg)
        {
            int speed = cfg.V11PassBallSpeedDmPerSecond < 1 ? 1 : cfg.V11PassBallSpeedDmPerSecond;
            return MovementGeometry.Distance(s.BallXDm, s.BallYDm, x, y) * 1000 / speed;
        }
    }
}
