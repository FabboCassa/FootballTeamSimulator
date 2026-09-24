using System;
using Sim.Core.Config;

namespace Sim.Core.Match.Movement.Models
{
    /// <summary>A player as pitch control sees him: where he is, where he is going, how fast he can go.</summary>
    public readonly struct PitchActor
    {
        public readonly int XDm;
        public readonly int YDm;
        public readonly int VxDmPerSecond;
        public readonly int VyDmPerSecond;
        public readonly int TopSpeedDmPerSecond;

        public PitchActor(int xDm, int yDm, int vxDmPerSecond, int vyDmPerSecond, int topSpeedDmPerSecond)
        {
            XDm = xDm;
            YDm = yDm;
            VxDmPerSecond = vxDmPerSecond;
            VyDmPerSecond = vyDmPerSecond;
            TopSpeedDmPerSecond = topSpeedDmPerSecond;
        }
    }

    /// <summary>
    /// Simplified Spearman pitch control. A player's time to intercept a point is his reaction
    /// time — spent carrying on at his current velocity — plus the straight run from there at top
    /// speed. Whoever gets there first controls it; the odds swing linearly over
    /// <see cref="ActionModelBalance.PitchControlSpanMs"/> instead of Spearman's logistic, so the
    /// whole model stays integer. Times are milliseconds, odds permille. Pure, static,
    /// allocation-free: the players are read through a span over the caller's own array.
    /// </summary>
    public static class PitchControl
    {
        /// <summary>Milliseconds before this player can play the ball at the target.</summary>
        public static int TimeToReachMs(in PitchActor player, int targetXDm, int targetYDm, ActionModelBalance cfg)
        {
            int reaction = cfg.PitchControlReactionMs < 0 ? 0 : cfg.PitchControlReactionMs;
            int startX = player.XDm + player.VxDmPerSecond * reaction / 1000;
            int startY = player.YDm + player.VyDmPerSecond * reaction / 1000;

            int run = MovementGeometry.Distance(startX, startY, targetXDm, targetYDm) - cfg.PitchControlReachDm;
            if (run <= 0) return reaction;

            int speed = player.TopSpeedDmPerSecond < 1 ? 1 : player.TopSpeedDmPerSecond;
            return reaction + run * 1000 / speed;
        }

        /// <summary>The quickest of these players to the target; <see cref="int.MaxValue"/> when there are none.</summary>
        public static int FastestMs(ReadOnlySpan<PitchActor> players, int targetXDm, int targetYDm, ActionModelBalance cfg)
        {
            int best = int.MaxValue;
            for (int i = 0; i < players.Length; i++)
            {
                int t = TimeToReachMs(players[i], targetXDm, targetYDm, cfg);
                if (t < best) best = t;
            }

            return best;
        }

        /// <summary>
        /// The odds the ball gets through the lane untouched: at each sampled point between the
        /// passer and the receiver, the ball's arrival against the quickest defender's, and the
        /// worst point decides. The receiver's own spot is <see cref="ReceiverSafetyPermille"/>'s
        /// question. The ball is taken at a constant speed, as in Spearman's model.
        /// </summary>
        public static int LaneSafetyPermille(
            int fromXDm, int fromYDm, int toXDm, int toYDm, int ballSpeedDmPerSecond,
            ReadOnlySpan<PitchActor> defenders, ActionModelBalance cfg)
        {
            if (defenders.Length == 0) return 1000;

            int samples = cfg.PitchControlLaneSamples < 1 ? 1 : cfg.PitchControlLaneSamples;
            int speed = ballSpeedDmPerSecond < 1 ? 1 : ballSpeedDmPerSecond;
            int length = MovementGeometry.Distance(fromXDm, fromYDm, toXDm, toYDm);
            int worstMargin = int.MaxValue;

            for (int i = 1; i <= samples; i++)
            {
                int x = fromXDm + (toXDm - fromXDm) * i / (samples + 1);
                int y = fromYDm + (toYDm - fromYDm) * i / (samples + 1);
                int ballMs = length * i / (samples + 1) * 1000 / speed;

                int defenderMs = FastestMs(defenders, x, y, cfg);
                int margin = defenderMs - ballMs;
                if (margin < worstMargin) worstMargin = margin;
            }

            return ControlPermille(worstMargin, cfg);
        }

        /// <summary>
        /// The odds the receiver, not a defender, controls the ball at the target: his time
        /// there against the quickest defender's. Even when they arrive together.
        /// </summary>
        public static int ReceiverSafetyPermille(
            in PitchActor receiver, ReadOnlySpan<PitchActor> defenders, int targetXDm, int targetYDm,
            ActionModelBalance cfg)
        {
            if (defenders.Length == 0) return 1000;

            int receiverMs = TimeToReachMs(receiver, targetXDm, targetYDm, cfg);
            int defenderMs = FastestMs(defenders, targetXDm, targetYDm, cfg);
            return ControlPermille(defenderMs - receiverMs, cfg);
        }

        /// <summary>Odds of control from how much sooner (positive) the side on the ball gets there.</summary>
        private static int ControlPermille(int leadMs, ActionModelBalance cfg)
        {
            int span = cfg.PitchControlSpanMs < 2 ? 2 : cfg.PitchControlSpanMs;
            int half = span / 2;
            if (leadMs >= half) return 1000;
            if (leadMs <= -half) return 0;
            return BallSkill.Lerp(leadMs + half, 2 * half, 0, 1000);
        }
    }
}
