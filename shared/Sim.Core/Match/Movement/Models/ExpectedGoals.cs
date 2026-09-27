using Sim.Core.Config;

namespace Sim.Core.Match.Movement.Models
{
    /// <summary>
    /// Expected goals: the share of shots like this one that go in, in permille. Distance sets
    /// the base from a table in <see cref="ActionModelBalance"/>; the angle, the pressure on the
    /// shooter and his finishing scale it. The same shape as
    /// <see cref="BallSkill.ShotQualityPermille"/>, but on a real xG scale so it can be compared
    /// with xT gains. Pure, static, integer, allocation-free.
    /// </summary>
    public static class ExpectedGoals
    {
        /// <param name="xDm">Shooter X, pitch decimetres.</param>
        /// <param name="yDm">Shooter Y, pitch decimetres.</param>
        /// <param name="attacksHighX">True when the attacked goal is at X = <see cref="Pitch.LengthDm"/>.</param>
        /// <param name="pressurePermille">How pressed he is, 0 (free) to 1000 (closed down).</param>
        /// <param name="shooting">Shooting attribute, 1-100.</param>
        /// <param name="technique">Technique attribute, 1-100.</param>
        public static int Permille(
            int xDm, int yDm, bool attacksHighX, int pressurePermille, int shooting, int technique,
            ActionModelBalance cfg)
        {
            int x = Pitch.ClampX(xDm);
            int dx = attacksHighX ? Pitch.LengthDm - x : x;
            int dy = Pitch.ClampY(yDm) - Pitch.CenterY;
            int distance = MovementGeometry.Distance(dx, dy, 0, 0);

            int xg = ByDistance(distance, cfg);
            xg = xg * AngleFactorPermille(dx, distance, cfg) / 1000;

            int cut = BallSkill.Clamp(cfg.XgPressureCutPercent, 0, 100);
            xg = xg * (100_000 - cut * BallSkill.Clamp(pressurePermille, 0, 1000)) / 100_000;

            int weight = BallSkill.Clamp(cfg.XgShootingWeight, 0, 10);
            int finishing = BallSkill.Clamp(BallSkill.Mix(shooting, weight, technique, 10 - weight), 1, 100);
            int spread = BallSkill.Clamp(cfg.XgSkillSpreadPercent, 0, 200);
            xg = xg * (100 - spread / 2 + spread * finishing / 100) / 100;

            return BallSkill.Clamp(xg, 1, 999);
        }

        private static int ByDistance(int distanceDm, ActionModelBalance cfg)
        {
            int[] table = cfg.XgByDistancePermille;
            if (table == null || table.Length == 0) return 0;

            int step = cfg.XgDistanceStepDm < 1 ? 1 : cfg.XgDistanceStepDm;
            int index = distanceDm / step;
            if (index >= table.Length - 1) return table[table.Length - 1];
            return BallSkill.Lerp(distanceDm - index * step, step, table[index], table[index + 1]);
        }

        /// <summary>
        /// Cosine squared of the bearing off the goal's axis: straight on keeps it all, 45 degrees
        /// half, and a shot from along the byline only the floor.
        /// </summary>
        private static int AngleFactorPermille(int dx, int distanceDm, ActionModelBalance cfg)
        {
            if (distanceDm <= 0) return 1000;
            int cos = dx * 1000 / distanceDm;
            int factor = cos * cos / 1000;
            int floor = BallSkill.Clamp(cfg.XgAngleFloorPermille, 0, 1000);
            return factor < floor ? floor : factor;
        }
    }
}
