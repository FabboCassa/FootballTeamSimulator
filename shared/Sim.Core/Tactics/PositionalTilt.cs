using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// The light continuous "tilt" of a team's shape (task 6.10). On top of the discrete
    /// role change (<see cref="ZoneRole"/>), how far the outfield players sit from the
    /// canonical anchors of the roles they are playing nudges attack/midfield/defence:
    ///   • a higher average line (players pushed forward of their role anchor) → more
    ///     attack, a little less defensive solidity;
    ///   • a wider average spread than the role anchors → a little more attack (chance
    ///     creation) at the cost of central control (midfield) — the same trade-off the
    ///     Width instruction makes.
    ///
    /// Measured PER SLOT against that slot's resolved-role anchor and averaged, so a
    /// clean formation preset (every player on his anchor) produces ZERO tilt — the
    /// identity. Slots with no custom position contribute nothing, so a lineup with no
    /// positioning at all is also the identity (this, plus the engine's opt-in flag,
    /// keeps golden masters/replays byte-identical).
    ///
    /// Pure/deterministic: integer math, no RNG, every component clamped to a small cap
    /// so free positioning is a nudge, never a game-breaking lever (no "park everyone in
    /// attack" exploit — proven in the harness).
    /// </summary>
    public static class PositionalTilt
    {
        public static TacticModifiers.Multipliers Compute(Lineup lineup, MatchBalance match, PositioningBalance cfg)
        {
            int count = lineup.Slots.Count;
            var roles = new PositionRole[count];
            for (int i = 0; i < count; i++) roles[i] = lineup.Slots[i].Role;

            long sumHeight = 0, sumSpread = 0;
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                LineupSlot slot = lineup.Slots[i];
                if (slot.Role == PositionRole.Goalkeeper) continue;
                if (slot.Position == null) continue; // no custom position → contributes 0 (identity)

                SlotPosition p = slot.Position.Value;
                int anchorX = FormationGeometry.AnchorX(slot.Role, match);
                int anchorY = FormationGeometry.AnchorY(roles, i, match);

                sumHeight += p.XPermille - anchorX;                 // + = pushed forward
                sumSpread += Abs(p.YPermille - 500) - Abs(anchorY - 500); // + = wider than anchor
                n++;
            }

            if (n == 0) return TacticModifiers.Multipliers.Identity;

            int dHeight = (int)(sumHeight / n); // average permille delta
            int dSpread = (int)(sumSpread / n);

            // percent = (permille delta / 100) * coefficient-per-100-permille
            int attack = (dHeight * cfg.HeightAttackPercentPer100 / 100)
                       + (dSpread * cfg.SpreadAttackPercentPer100 / 100);
            int defense = -(dHeight * cfg.HeightDefensePercentPer100 / 100);
            int midfield = -(dSpread * cfg.SpreadMidfieldPercentPer100 / 100);

            attack = ClampTilt(attack, cfg);
            defense = ClampTilt(defense, cfg);
            midfield = ClampTilt(midfield, cfg);

            return new TacticModifiers.Multipliers(
                (100 + attack) / 100.0,
                (100 + midfield) / 100.0,
                (100 + defense) / 100.0);
        }

        private static int Abs(int v) => v < 0 ? -v : v;

        private static int ClampTilt(int v, PositioningBalance cfg)
        {
            int m = cfg.MaxTiltPercent;
            return v < -m ? -m : (v > m ? m : v);
        }
    }
}
