using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// What a player's attributes are worth once the ball is at his feet (engine rework phase 4,
    /// docs/engine/MATCH_ENGINE_PLAN.md §4).
    ///
    /// Until this phase the only attribute the picture read was <see cref="PlayerAttributes.Pace"/>:
    /// a pass was aimed at a mathematically safe line and executed exactly, so the question "which
    /// players play better" had no answer the simulation could give. Everything here is the answer
    /// to that question, and it is deliberately kept OUT of the simulator: pure, static, integer,
    /// no state and no RNG of its own beyond the draws it is handed, so each piece can be measured
    /// on its own and none of it can move a seeded stream by accident.
    ///
    /// Two conventions run through the file. Odds are PERMILLE (0-1000), because a percent is too
    /// coarse to interpolate on and a float is not allowed anywhere near a deterministic engine.
    /// Skills are the raw 1-100 attributes; where several matter they are mixed by weights out of
    /// ten that live in <see cref="MatchBalance"/>, so the mix is a balance decision and the shape
    /// of the model is a code decision.
    /// </summary>
    internal static class BallSkill
    {
        /// <summary>Weighted mix of skills, each weight out of ten. Returns a 1-100 rating.</summary>
        public static int Mix(int a, int wa, int b, int wb)
        {
            int w = wa + wb;
            return w <= 0 ? 50 : (a * wa + b * wb) / w;
        }

        public static int Mix(int a, int wa, int b, int wb, int c, int wc)
        {
            int w = wa + wb + wc;
            return w <= 0 ? 50 : (a * wa + b * wb + c * wc) / w;
        }

        public static int Mix(int a, int wa, int b, int wb, int c, int wc, int d, int wd)
        {
            int w = wa + wb + wc + wd;
            return w <= 0 ? 50 : (a * wa + b * wb + c * wc + d * wd) / w;
        }

        public static int Clamp(int value, int low, int high)
            => value < low ? low : (value > high ? high : value);

        /// <summary>
        /// Linear interpolation between two permille values as <paramref name="at"/> runs from 0
        /// to <paramref name="span"/>. Clamped at both ends, integer throughout.
        /// </summary>
        public static int Lerp(int at, int span, int atZero, int atSpan)
        {
            if (span <= 0) return atSpan;
            if (at <= 0) return atZero;
            if (at >= span) return atSpan;
            return atZero + (atSpan - atZero) * at / span;
        }

        // ------------------------------------------------------------------ passing

        /// <summary>
        /// How far off line a struck ball goes, as a share (permille) of the distance it travels.
        /// A player with nothing in Passing or Technique, pressed and hitting it long, misses by
        /// the configured maximum; the best player unpressed still misses by the floor, because
        /// nobody completes every pass.
        /// </summary>
        public static int PassErrorPermille(
            int passing, int technique, int pressurePermille, bool longBall, MatchBalance cfg)
        {
            int weight = Clamp(cfg.PassSkillPassingWeight, 0, 10);
            int skill = Clamp(Mix(passing, weight, technique, 10 - weight), 1, 100);

            int error = cfg.PassErrorFloorPermille
                        + cfg.PassErrorMaxPermille * (100 - skill) / 100;

            error += error * cfg.PassErrorPressurePercent * Clamp(pressurePermille, 0, 1000) / 100_000;
            if (longBall) error += error * cfg.PassErrorLongBallPercent / 100;
            return error < 0 ? 0 : error;
        }

        /// <summary>
        /// One draw of that error, as a signed share in permille. Two uniform draws averaged, so
        /// most balls are close to their line and the wild one is rare — which is what a
        /// distribution of misplaced passes actually looks like.
        /// </summary>
        public static int Spread(IRandomSource rng, int errorPermille)
        {
            if (errorPermille <= 0) return 0;
            int a = rng.NextInt(-errorPermille, errorPermille + 1);
            int b = rng.NextInt(-errorPermille, errorPermille + 1);
            return (a + b) / 2;
        }

        /// <summary>
        /// The odds a pass into this lane arrives, before the receiver's own space is priced in.
        /// <paramref name="slackDm"/> is how far past the best-placed opponent's crossing point
        /// the ball has already rolled by the time he gets there: negative means he is there
        /// first and the ball is going through him.
        /// </summary>
        public static int LaneCompletionPermille(int slackDm, MatchBalance cfg)
        {
            if (slackDm <= 0) return cfg.CutOutCompletionPermille;
            return Lerp(slackDm, cfg.InterceptMarginDm,
                cfg.CutOutCompletionPermille, cfg.ClearLaneCompletionPermille);
        }

        /// <summary>
        /// And the odds he keeps it once it gets there. A man with an opponent standing on him
        /// can still be passed to — he shields it — but calling that a completed pass is how the
        /// engine ended up playing every ball into a marker's feet and calling it safe.
        /// </summary>
        public static int ReceptionPermille(int spaceDm, MatchBalance cfg)
            => Lerp(spaceDm, cfg.ReceiverFreeSpaceDm, cfg.ContestedReceptionFloorPermille, 1000);

        /// <summary>
        /// What a player thinks an option is worth, in decimetres of forward progress: what he
        /// gains when it comes off, less what a turnover costs where he is standing. The one
        /// currency a pass, a run and a clearance can all be quoted in — which is what turns a
        /// fixed ladder of "pass, else clear, else run" into a decision.
        ///
        /// <paramref name="visionPercent"/> is how much of the risk he actually sees: a poor
        /// reader of the game plays the ball that LOOKS best.
        /// </summary>
        public static int OptionValue(int completionPermille, int gainDm, int turnoverCostDm, int visionPercent)
        {
            int completion = Clamp(completionPermille, 0, 1000);
            int reward = gainDm * completion / 1000;
            int risk = turnoverCostDm * (1000 - completion) / 1000;
            return reward - risk * Clamp(visionPercent, 0, 100) / 100;
        }

        /// <summary>How much of the risk a player sees, from Positioning (his reading of the game).</summary>
        public static int VisionPercent(int positioning, MatchBalance cfg)
            => Lerp(Clamp(positioning, 1, 100), 100, cfg.VisionRiskFloorPercent, 100);

        // ------------------------------------------------------------------ the duel

        /// <summary>
        /// A challenge, resolved as a contest rather than a coin toss: the odds (permille, per
        /// tick of contact) that the challenger comes away with the ball or knocks it loose.
        /// Both men's ratings decide the SPLIT; the config decides the pace.
        /// </summary>
        public static int DuelWinPermille(
            int dribbling, int technique, int strength, int carrierPace,
            int defending, int positioning, int challengerPace, MatchBalance cfg)
        {
            int[] cw = cfg.DribbleDuelCarrierWeights;
            int[] dw = cfg.DribbleDuelChallengerWeights;
            int carrier = Clamp(Mix(
                dribbling, cw.Length > 0 ? cw[0] : 4,
                technique, cw.Length > 1 ? cw[1] : 2,
                strength, cw.Length > 2 ? cw[2] : 2,
                carrierPace, cw.Length > 3 ? cw[3] : 2), 1, 100);
            int challenger = Clamp(Mix(
                defending, dw.Length > 0 ? dw[0] : 5,
                positioning, dw.Length > 1 ? dw[1] : 3,
                challengerPace, dw.Length > 2 ? dw[2] : 2), 1, 100);

            // Twice the rating of the man he is up against is about four times as likely to beat
            // him: the square is what stops a 60 beating a 30 by a coin toss, and stops a 90
            // being unplayable.
            long c = (long)carrier * carrier;
            long d = (long)challenger * challenger;
            int share = (int)(d * 2000 / (c + d));            // 1000 at parity
            return cfg.DuelChancePermillePerTick * share / 1000;
        }

        // ------------------------------------------------------------------ striking it

        /// <summary>
        /// How good a chance this is, in permille — an xG-shaped reading of distance, angle,
        /// bodies in the way and the man striking it. Phase 4 spends it on WHERE the ball goes
        /// and on whether the keeper holds it; phase 6, when the causality is inverted, is where
        /// it decides the goal.
        /// </summary>
        public static int ShotQualityPermille(
            int distanceDm, int offCentreDm, int pressure, int shooting, int technique, MatchBalance cfg)
        {
            int range = Lerp(distanceDm, cfg.ShotQualityRangeDm, 1000, 60);
            int angle = Lerp(offCentreDm, cfg.ShotQualityAngleDm, 1000, 250);
            int quality = range * angle / 1000;

            int crowd = Clamp(pressure, 0, 3) * cfg.ShotQualityPressurePercent / 3;
            quality = quality * (100 - Clamp(crowd, 0, 90)) / 100;

            int finishing = Clamp(Mix(shooting, 7, technique, 3), 1, 100);
            quality = quality * (100 - cfg.ShotQualityFinishingPercent / 2
                                 + cfg.ShotQualityFinishingPercent * finishing / 100) / 100;
            return Clamp(quality, 10, 1000);
        }

        /// <summary>Does the keeper hold it, or is it a parry and a live ball in his six-yard box?</summary>
        public static int KeeperHoldPercent(int goalkeeping, int shotQualityPermille, MatchBalance cfg)
        {
            int hold = cfg.KeeperHoldBasePercent
                       + cfg.KeeperHoldSkillPercent * Clamp(goalkeeping, 1, 100) / 100;
            hold -= cfg.KeeperHoldQualityPercent * Clamp(shotQualityPermille, 0, 1000) / 1000;
            return Clamp(hold, 5, 95);
        }
    }
}
