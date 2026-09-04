using System;

namespace Sim.Core.Domain
{
    /// <summary>
    /// Computes a player's overall rating as a role-weighted average of skills.
    /// Integer arithmetic only - fully deterministic on every platform.
    /// </summary>
    public static class PlayerRating
    {
        // Skill order: Pace, Strength, Stamina, Technique, Passing,
        //              Dribbling, Shooting, Defending, Positioning, Goalkeeping.
        // Each row sums to 100.
        private static readonly int[,] Weights =
        {
            //                     Pac Str Sta Tec Pas Dri Sho Def Pos  Gk
            /* Goalkeeper        */ {  5, 10,  5,  5,  5,  0,  0,  5, 15, 50 },
            /* CentreBack        */ { 10, 20, 10,  5,  5,  0,  0, 30, 20,  0 },
            /* FullBack          */ { 20, 10, 15,  5, 10,  5,  0, 20, 15,  0 },
            /* DefensiveMidfield */ { 10, 15, 15, 10, 15,  5,  0, 20, 10,  0 },
            /* CentralMidfield   */ { 10, 10, 15, 15, 25, 10,  5,  5,  5,  0 },
            /* AttackingMidfield */ { 10,  5, 10, 20, 20, 15, 10,  0, 10,  0 },
            /* Winger            */ { 25,  5, 10, 15, 10, 20, 10,  0,  5,  0 },
            /* Striker           */ { 15, 10, 10, 10,  5, 10, 25,  0, 15,  0 }
        };

        /// <summary>Overall rating in [1, 100] for the player's own role.</summary>
        public static int Overall(Player player) => OverallFor(player, player.Role);

        /// <summary>Overall rating the player would have if deployed in the given role.</summary>
        public static int OverallFor(Player player, PositionRole role)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            PlayerAttributes a = player.Attributes;
            int r = (int)role;

            int sum =
                a.Pace        * Weights[r, 0] +
                a.Strength    * Weights[r, 1] +
                a.Stamina     * Weights[r, 2] +
                a.Technique   * Weights[r, 3] +
                a.Passing     * Weights[r, 4] +
                a.Dribbling   * Weights[r, 5] +
                a.Shooting    * Weights[r, 6] +
                a.Defending   * Weights[r, 7] +
                a.Positioning * Weights[r, 8] +
                a.Goalkeeping * Weights[r, 9];

            return AttributeScale.ClampSkill(sum / 100);
        }

        /// <summary>Generation bias for a skill: positive for skills the role relies on. Index uses the same skill order as the weight table.</summary>
        internal static int WeightOf(PositionRole role, int skillIndex) => Weights[(int)role, skillIndex];
    }
}
