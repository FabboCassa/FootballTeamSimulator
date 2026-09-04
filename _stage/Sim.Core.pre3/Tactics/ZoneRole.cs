using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// Resolves the role a player is effectively playing from WHERE he stands on the
    /// pitch (task 6.10 "role by zone"). A goalkeeper always stays a goalkeeper;
    /// otherwise the line height (X) picks the band CentreBack → DM → CM → AM → Striker,
    /// and standing WIDE (far from the centre in Y) promotes the deep band to FullBack
    /// and the higher bands to Winger. So dragging a fullback up the flank turns him
    /// into a winger, and the resolved role then flows through the existing engine
    /// (role bucket in <see cref="TeamRatings"/> + out-of-position rating in
    /// <see cref="PlayerRating.OverallFor"/>).
    ///
    /// Pure/deterministic (integer comparisons only). The band boundaries are chosen so
    /// that every formation preset anchor maps back to its own role — a clean preset is
    /// a fixed point of this map, which is why "reset on formation change" restores the
    /// exact preset roles.
    /// </summary>
    public static class ZoneRole
    {
        public static PositionRole Resolve(PositionRole baseRole, SlotPosition pos, PositioningBalance cfg)
        {
            if (baseRole == PositionRole.Goalkeeper) return PositionRole.Goalkeeper;

            int x = pos.XPermille;
            int fromCentre = pos.YPermille - 500;
            if (fromCentre < 0) fromCentre = -fromCentre;
            bool wide = fromCentre > cfg.WideZoneThresholdPermille;

            if (wide)
            {
                if (x < cfg.DeepBandMaxXPermille) return PositionRole.FullBack;
                if (x >= cfg.WideForwardMinXPermille) return PositionRole.Winger;
                return PositionRole.CentralMidfielder; // a wide midfielder sits in the mid bucket
            }

            if (x < cfg.DeepBandMaxXPermille) return PositionRole.CentreBack;
            if (x < cfg.DmBandMaxXPermille) return PositionRole.DefensiveMidfielder;
            if (x < cfg.CmBandMaxXPermille) return PositionRole.CentralMidfielder;
            if (x < cfg.AmBandMaxXPermille) return PositionRole.AttackingMidfielder;
            return PositionRole.Striker;
        }
    }
}
