using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private static readonly int DefaultFamiliarityMax = new TacticsBalance().FamiliarityMax;

        /// <summary>The minute each man (side * n + slot) came on: 0 for the eleven who started.</summary>
        private int[] _onSince = System.Array.Empty<int>();

        /// <summary>
        /// Each side's skills as its familiarity with its tactic leaves them, in permille. V11
        /// only (watchable-match spec R9); on V10 it stays 1000, which BindSkills spends as an
        /// exact identity.
        /// </summary>
        private readonly int[] _familiarityPermille = { 1000, 1000 };

        private bool IsV11 => _cfg.Brain == MatchBrainVersion.V11;

        /// <summary>
        /// V11 (R9): a side that does not know its tactic plays below itself. At zero familiarity
        /// every skill loses <see cref="MatchBalance.V11UnfamiliarPenaltyPermille"/>, and the loss
        /// shrinks in a straight line to nothing at full familiarity.
        /// </summary>
        private void ReadFamiliarity(MatchTactics? tactics)
        {
            for (int side = 0; side < SideCount; side++)
            {
                _familiarityPermille[side] = 1000;
                if (!IsV11 || tactics == null) continue;

                int max = _feed != null ? _feed.FamiliarityMax : DefaultFamiliarityMax;
                if (max <= 0) continue;
                int familiarity = BallSkill.Clamp((side == 0 ? tactics.Home : tactics.Away).Familiarity, 0, max);
                _familiarityPermille[side] = 1000 - _cfg.V11UnfamiliarPenaltyPermille * (max - familiarity) / max;
            }
        }

        /// <summary>
        /// V11 (R9): a man out of his natural role plays below himself, by
        /// <see cref="MatchBalance.V11OffRolePermillePerStep"/> for each step between his role and
        /// the slot's along the pitch, up to <see cref="MatchBalance.V11OffRoleMaxPermille"/>; a
        /// keeper out of goal, or an outfielder in it, loses the most. 1000 on V10.
        /// </summary>
        private int RoleFitPermille(LineupSlot slot)
        {
            if (!IsV11) return 1000;
            PositionRole natural = slot.Player.Role;
            if (natural == slot.Role) return 1000;
            if (natural == PositionRole.Goalkeeper || slot.Role == PositionRole.Goalkeeper)
                return 1000 - _cfg.V11OffRoleMaxPermille;

            int steps = (int)natural - (int)slot.Role;
            if (steps < 0) steps = -steps;
            int loss = steps * _cfg.V11OffRolePermillePerStep;
            return 1000 - (loss > _cfg.V11OffRoleMaxPermille ? _cfg.V11OffRoleMaxPermille : loss);
        }

        /// <summary>
        /// V11 (R10): how tired a man is, in permille before his Stamina scales it, from the
        /// minutes he has been ON the pitch — a substitute comes on fresh. It grows with the
        /// square of them, so the legs go in the last half hour rather than evenly from the
        /// first minute. The break gives some of it back only to a man who played the first half.
        /// </summary>
        private int V11TirednessPermille(int k, int minute)
        {
            int on = _onSince[k];
            int played = minute - on;
            if (played < 0) played = 0;
            int permille = _cfg.V11MatchFatigueAt90Permille * played * played / (90 * 90);
            if (minute > 45 && on < 45) permille -= _cfg.V11HalfTimeRecoveryPermille;
            return permille < 0 ? 0 : permille;
        }
    }
}
