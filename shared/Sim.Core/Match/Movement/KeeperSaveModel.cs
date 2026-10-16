using Sim.Core.Config;
using Sim.Core.Random;

namespace Sim.Core.Match.Movement
{
    /// <summary>Where a strike was hit from, for <see cref="KeeperSaveModel"/>.</summary>
    public enum ShotZone
    {
        SixYardBox,
        Box,
        Outside,
        Penalty
    }

    /// <summary>
    /// The save model of the real-match spec, R7: the odds the keeper keeps out a strike on target,
    /// decided once when it is struck. Pure and integer, in permille and decimetres.
    ///
    /// The zone sets the base (six-yard box, penalty area, outside it, the spot), and on top of it
    /// every metre from the goal centre adds, a tighter angle adds, each opponent pressing the
    /// striker adds (a hurried strike is a weaker one), the keeper's Goalkeeping adds or takes away
    /// around 50, and every metre the keeper stands off the strike's line takes away (a strike at
    /// him is the easy one). Whether he then actually gets a hand to it is the pitch's business:
    /// the movement brain sends him at the line of the shot, and one he cannot reach goes in.
    /// </summary>
    public static class KeeperSaveModel
    {
        /// <summary>The six-yard box: 5.5 m deep, 9.16 m either side of the goal centre.</summary>
        public const int SixYardDepthDm = 55;
        public const int SixYardHalfWidthDm = 92;

        private const int PressingMenCap = 3;

        /// <summary>The zone of a strike <paramref name="depthDm"/> off the goal line and <paramref name="offCentreDm"/> off its centre.</summary>
        public static ShotZone ZoneOf(int depthDm, int offCentreDm, bool penalty)
        {
            if (penalty) return ShotZone.Penalty;
            if (depthDm <= SixYardDepthDm && offCentreDm <= SixYardHalfWidthDm) return ShotZone.SixYardBox;
            if (depthDm <= MovementGeometry.BoxDepthDm && offCentreDm <= MovementGeometry.BoxHalfWidthDm) return ShotZone.Box;
            return ShotZone.Outside;
        }

        /// <summary>
        /// The odds, in permille, that the keeper saves a strike on target hit from
        /// <paramref name="depthDm"/> off his goal line and <paramref name="offCentreDm"/> off its
        /// centre, with <paramref name="pressingMen"/> opponents on the striker, by a keeper of
        /// <paramref name="goalkeeping"/> (1-100) standing <paramref name="keeperOffLineDm"/> off the
        /// strike's line. A penalty reads only the spot and the keeper.
        /// </summary>
        public static int SavePermille(
            MatchBalance cfg, int depthDm, int offCentreDm, int pressingMen, int goalkeeping,
            int keeperOffLineDm, bool penalty = false)
        {
            int skill = cfg.SaveSkillPermille * (MovementGeometry.Clamp(goalkeeping, 1, 100) - 50) / 50;
            int depth = depthDm < 0 ? -depthDm : depthDm;
            int offCentre = offCentreDm < 0 ? -offCentreDm : offCentreDm;

            ShotZone zone = ZoneOf(depth, offCentre, penalty);
            if (zone == ShotZone.Penalty)
                return MovementGeometry.Clamp(cfg.SavePenaltyPermille + skill, cfg.SaveMinPermille, cfg.SaveMaxPermille);

            int distance = MovementGeometry.Distance(0, 0, depth, offCentre);
            int angle = distance <= 0 ? 0 : offCentre * 1000 / distance;
            int pressing = MovementGeometry.Clamp(pressingMen, 0, PressingMenCap);
            int offLine = keeperOffLineDm < 0 ? 0 : keeperOffLineDm;

            int save = ZoneBase(cfg, zone)
                       + cfg.SavePerMetrePermille * distance / 10
                       + cfg.SaveAnglePermille * angle / 1000
                       + cfg.SavePressurePermille * pressing
                       + skill
                       - cfg.SaveOffLinePermillePerMetre * offLine / 10;
            return MovementGeometry.Clamp(save, cfg.SaveMinPermille, cfg.SaveMaxPermille);
        }

        /// <summary>One draw: is it saved, at <paramref name="permille"/>?</summary>
        public static bool Saves(IRandomSource rng, int permille) => rng.NextInt(0, 1000) < permille;

        private static int ZoneBase(MatchBalance cfg, ShotZone zone) => zone switch
        {
            ShotZone.SixYardBox => cfg.SaveSixYardBoxPermille,
            ShotZone.Box => cfg.SaveBoxPermille,
            _ => cfg.SaveOutsidePermille
        };
    }
}
