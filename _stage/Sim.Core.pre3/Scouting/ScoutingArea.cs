using System.Collections.Generic;
using System.Globalization;
using Sim.Core.Domain;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// What a scouting assignment covers (task 11.2). The order is deliberate: the value is the
    /// assignment's BREADTH, from the single named player up to a whole continent, and every
    /// balance table in <see cref="Config.ScoutingBalance"/> that varies with breadth is indexed
    /// by it. Wider means more players found and a lower ceiling on how well any of them is read.
    /// </summary>
    public enum ScoutingAreaKind
    {
        /// <summary>One named player, put under observation by hand (the pre-11.2 behaviour of task 5.4b).</summary>
        Player = 0,

        /// <summary>One club: the scout sits in its stand week after week and learns its squad fast.</summary>
        Club = 1,

        /// <summary>A whole nation's pyramid, every division the world loaded for it.</summary>
        Nation = 2,

        /// <summary>A whole continent: the broadest, vaguest brief there is.</summary>
        Continent = 3
    }

    /// <summary>
    /// WHERE a scout is sent (task 11.2). Plain data with one meaningful field per
    /// <see cref="ScoutingAreaKind"/>; the others are ignored, which keeps the type flat and
    /// trivially serializable by the host (no polymorphic JSON).
    ///
    /// <see cref="Key"/> is the stable string identity of an area — it is what the per-area
    /// knowledge meter (<see cref="AreaKnowledgeStore"/>) and the report book are keyed by, and
    /// what the save stores. It must stay stable across versions.
    /// </summary>
    public sealed class ScoutingArea
    {
        public ScoutingAreaKind Kind { get; set; } = ScoutingAreaKind.Player;

        /// <summary>Target player when <see cref="Kind"/> is <see cref="ScoutingAreaKind.Player"/>.</summary>
        public int PlayerId { get; set; }

        /// <summary>Target club when <see cref="Kind"/> is <see cref="ScoutingAreaKind.Club"/>.</summary>
        public int ClubId { get; set; }

        /// <summary>Target nation code (e.g. "ITA") when <see cref="Kind"/> is <see cref="ScoutingAreaKind.Nation"/>.</summary>
        public string NationCode { get; set; } = string.Empty;

        /// <summary>Target continent when <see cref="Kind"/> is <see cref="ScoutingAreaKind.Continent"/>.</summary>
        public Continent Continent { get; set; }

        public static ScoutingArea ForPlayer(int playerId)
            => new ScoutingArea { Kind = ScoutingAreaKind.Player, PlayerId = playerId };

        public static ScoutingArea ForClub(int clubId)
            => new ScoutingArea { Kind = ScoutingAreaKind.Club, ClubId = clubId };

        public static ScoutingArea ForNation(string nationCode)
            => new ScoutingArea { Kind = ScoutingAreaKind.Nation, NationCode = nationCode ?? string.Empty };

        public static ScoutingArea ForContinent(Continent continent)
            => new ScoutingArea { Kind = ScoutingAreaKind.Continent, Continent = continent };

        /// <summary>How wide the brief is, 0 (one player) to 3 (a continent). Indexes the balance tables.</summary>
        public int Breadth => (int)Kind;

        /// <summary>
        /// Stable identity of this area: "P:12", "C:401", "N:ITA", "K:1". Used as the save key of the
        /// area knowledge meter and to tie a report back to the assignment that produced it, so the
        /// format is part of the save contract — extend it, never renumber it.
        /// </summary>
        public string Key
        {
            get
            {
                switch (Kind)
                {
                    case ScoutingAreaKind.Club:
                        return "C:" + ClubId.ToString(CultureInfo.InvariantCulture);
                    case ScoutingAreaKind.Nation:
                        return "N:" + NationCode;
                    case ScoutingAreaKind.Continent:
                        return "K:" + ((int)Continent).ToString(CultureInfo.InvariantCulture);
                    default:
                        return "P:" + PlayerId.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        /// <summary>Rebuilds an area from its <see cref="Key"/>; null when the key is malformed.</summary>
        public static ScoutingArea? Parse(string? key)
        {
            if (string.IsNullOrEmpty(key) || key!.Length < 3 || key[1] != ':')
                return null;

            string body = key.Substring(2);
            switch (key[0])
            {
                case 'P':
                    return int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerId)
                        ? ForPlayer(playerId) : null;
                case 'C':
                    return int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out int clubId)
                        ? ForClub(clubId) : null;
                case 'N':
                    return ForNation(body);
                case 'K':
                    return int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out int continent)
                        ? ForContinent((Continent)continent) : null;
                default:
                    return null;
            }
        }

        public ScoutingArea Clone() => new ScoutingArea
        {
            Kind = Kind,
            PlayerId = PlayerId,
            ClubId = ClubId,
            NationCode = NationCode,
            Continent = Continent
        };

        public bool SameAs(ScoutingArea? other) => other != null && other.Key == Key;

        /// <summary>
        /// The clubs this area covers, in world registry order (nations, then tiers top-first, then
        /// the league's own club order) — so a scan is deterministic and order-independent of any
        /// dictionary. A <see cref="ScoutingAreaKind.Player"/> area covers no clubs: a named target
        /// is observed directly, it is not a place to go looking.
        /// </summary>
        public IEnumerable<Club> Clubs(World world)
        {
            if (world == null)
                yield break;

            switch (Kind)
            {
                case ScoutingAreaKind.Club:
                {
                    Club? club = world.FindClub(ClubId);
                    if (club != null)
                        yield return club;
                    break;
                }

                case ScoutingAreaKind.Nation:
                {
                    Nation? nation = world.FindNation(NationCode);
                    if (nation != null)
                    {
                        foreach (League league in nation.Leagues)
                        {
                            foreach (Club club in league.Clubs)
                                yield return club;
                        }
                    }

                    break;
                }

                case ScoutingAreaKind.Continent:
                {
                    foreach (Nation nation in world.Nations)
                    {
                        if (nation.Continent != Continent)
                            continue;

                        foreach (League league in nation.Leagues)
                        {
                            foreach (Club club in league.Clubs)
                                yield return club;
                        }
                    }

                    break;
                }
            }
        }
    }
}
