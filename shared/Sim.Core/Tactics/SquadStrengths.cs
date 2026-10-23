using System.Collections.Generic;
using Sim.Core.Domain;
using Sim.Core.Match;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// What a squad is good at (R22), on the attribute scale.
    /// <see cref="AiStyleChooser"/> matches styles to these.
    /// </summary>
    public readonly struct SquadStrengths
    {
        /// <summary>Passing and technique of the midfielders.</summary>
        public int Passing { get; }

        /// <summary>Pace and strength of the wingers and strikers.</summary>
        public int Forwards { get; }

        /// <summary>Stamina of the outfield players.</summary>
        public int Stamina { get; }

        /// <summary>Defending and positioning of the back line.</summary>
        public int Defending { get; }

        /// <summary>Pace and dribbling of the wingers and full-backs.</summary>
        public int Wide { get; }

        /// <summary>Average overall of the ten best outfield players.</summary>
        public int Overall { get; }

        public SquadStrengths(int passing, int forwards, int stamina, int defending, int wide, int overall)
        {
            Passing = passing;
            Forwards = forwards;
            Stamina = stamina;
            Defending = defending;
            Wide = wide;
            Overall = overall;
        }

        /// <summary>
        /// Reads each unit's first-choice players by their natural role: the best
        /// <see cref="UnitSize"/> by overall. A small (data-only) squad with an empty unit falls
        /// back to the squad's overall for it.
        /// </summary>
        public static SquadStrengths Of(Club club)
        {
            var mids = new List<Player>();
            var fwds = new List<Player>();
            var defs = new List<Player>();
            var wide = new List<Player>();
            var outfield = new List<Player>();
            foreach (Player p in club.Squad.Players)
            {
                if (p.Role == PositionRole.Winger || p.Role == PositionRole.FullBack) wide.Add(p);
                switch (p.Role)
                {
                    case PositionRole.Goalkeeper: continue;
                    case PositionRole.CentreBack:
                    case PositionRole.FullBack: defs.Add(p); break;
                    case PositionRole.Winger:
                    case PositionRole.Striker: fwds.Add(p); break;
                    default: mids.Add(p); break;
                }
                outfield.Add(p);
            }

            int overall = Best(outfield, Lineup.Size - 1, p => PlayerRating.Overall(p) * 2, club.Strength);
            return new SquadStrengths(
                Best(mids, UnitSize, p => p.Attributes.Passing + p.Attributes.Technique, overall),
                Best(fwds, UnitSize, p => p.Attributes.Pace + p.Attributes.Strength, overall),
                Best(outfield, Lineup.Size - 1, p => p.Attributes.Stamina * 2, overall),
                Best(defs, UnitSize + 1, p => p.Attributes.Defending + p.Attributes.Positioning, overall),
                Best(wide, UnitSize + 1, p => p.Attributes.Pace + p.Attributes.Dribbling, overall),
                overall);
        }

        /// <summary>First-choice players read per unit (four at the back).</summary>
        public const int UnitSize = 3;

        /// <summary>Half the mean of <paramref name="pair"/> over the <paramref name="take"/> best players by overall.</summary>
        private static int Best(List<Player> players, int take, System.Func<Player, int> pair, int fallback)
        {
            if (players.Count == 0) return fallback;
            players.Sort((a, b) =>
            {
                int byOverall = PlayerRating.Overall(b).CompareTo(PlayerRating.Overall(a));
                return byOverall != 0 ? byOverall : a.Id.CompareTo(b.Id);
            });
            int n = players.Count < take ? players.Count : take;
            int sum = 0;
            for (int i = 0; i < n; i++) sum += pair(players[i]);
            return sum / (2 * n);
        }

        /// <summary>The per-unit mean of a league's squads.</summary>
        public static SquadStrengths Mean(IReadOnlyList<SquadStrengths> squads)
        {
            if (squads.Count == 0) return default;
            int pass = 0, fwd = 0, stam = 0, def = 0, wide = 0, ov = 0;
            foreach (SquadStrengths s in squads)
            {
                pass += s.Passing; fwd += s.Forwards; stam += s.Stamina; def += s.Defending; wide += s.Wide; ov += s.Overall;
            }
            int n = squads.Count;
            return new SquadStrengths(pass / n, fwd / n, stam / n, def / n, wide / n, ov / n);
        }
    }
}
