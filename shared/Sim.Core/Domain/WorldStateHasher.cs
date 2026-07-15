using System.Collections.Generic;

namespace Sim.Core.Domain
{
    /// <summary>
    /// Order-independent FNV-1a (64-bit) hash of a whole world's mutable player state — every
    /// player's 10 attributes plus his condition (form / morale / fitness) — used by the online
    /// season to prove the client and the server agree after a week of play (Phase 8.4 ✅). Like
    /// <see cref="Match.MatchReportHasher"/> it mixes only 32-bit integer fields, so the hash is
    /// bit-identical across .NET, Unity Mono and Unity IL2CPP: the server evolves the authoritative
    /// state with the deterministic <see cref="Condition.ConditionProgressor"/> /
    /// <see cref="Development.DevelopmentProgressor"/> (composed by <see cref="Career.OnlineSeasonTick"/>),
    /// and a client re-running the same Sim.Core progressors derives the identical hash.
    ///
    /// Order-independent by construction: players are folded in a canonical order (by club id, then
    /// player id), so the hash never depends on the order clubs/players are visited in. Development is
    /// long-term (potential drives it) but only the attributes it mutates are hashed; potential itself
    /// is immutable per world, so it is not part of the evolving state.
    /// </summary>
    public static class WorldStateHasher
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        /// <summary>Hashes every player across all clubs of every league in the world.</summary>
        public static ulong Hash(IReadOnlyList<League> leagues)
        {
            var clubs = new List<Club>();
            foreach (League league in leagues)
                foreach (Club club in league.Clubs)
                    clubs.Add(club);
            return Hash(clubs);
        }

        /// <summary>Hashes every player across the given clubs, in a canonical (club id, player id) order.</summary>
        public static ulong Hash(IReadOnlyList<Club> clubs)
        {
            // Canonical club order.
            var ordered = new List<Club>(clubs);
            ordered.Sort((a, b) => a.Id.CompareTo(b.Id));

            ulong h = OffsetBasis;
            h = Mix(h, ordered.Count);

            foreach (Club club in ordered)
            {
                h = Mix(h, club.Id);

                // Canonical player order within the club.
                var players = new List<Player>(club.Squad.Players);
                players.Sort((a, b) => a.Id.CompareTo(b.Id));
                h = Mix(h, players.Count);

                foreach (Player p in players)
                {
                    h = Mix(h, p.Id);
                    PlayerAttributes a = p.Attributes;
                    for (int s = 0; s < PlayerAttributes.SkillCount; s++)
                        h = Mix(h, a[s]);
                    h = Mix(h, p.Condition.Form);
                    h = Mix(h, p.Condition.Morale);
                    h = Mix(h, p.Condition.Fitness);
                }
            }

            return h;
        }

        /// <summary>Uppercase 0x-prefixed hex, matching the determinism-check convention.</summary>
        public static string ToHex(ulong hash) => "0x" + hash.ToString("X16");

        /// <summary>Folds one 32-bit value into the hash, byte by byte (FNV-1a).</summary>
        private static ulong Mix(ulong h, int value)
        {
            unchecked
            {
                uint v = (uint)value;
                h = (h ^ (v & 0xFF)) * Prime;
                h = (h ^ ((v >> 8) & 0xFF)) * Prime;
                h = (h ^ ((v >> 16) & 0xFF)) * Prime;
                h = (h ^ ((v >> 24) & 0xFF)) * Prime;
                return h;
            }
        }
    }
}
