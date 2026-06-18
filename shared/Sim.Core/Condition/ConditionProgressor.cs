using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Condition
{
    /// <summary>
    /// Evolves a whole world's player condition one day at a time (task 4.2 wiring
    /// of the 4.1 model). For the day just played:
    ///   - a club that played gets <see cref="ConditionModel.ApplyMatchResult"/> for
    ///     every squad player (starters credited 90', everyone else 0'), so starters
    ///     drain fitness while the bench only takes the morale/form step;
    ///   - every other club rests one day (<see cref="ConditionModel.ApplyRest"/>):
    ///     fitness recovers, morale drifts toward neutral.
    ///
    /// Deterministic and order-independent: the form walk's RNG is derived per club
    /// from (worldSeed, day, clubId), so the result never depends on the order clubs
    /// or fixtures are visited in (mirrors SeasonProgressor's per-fixture seeding).
    /// The host owns the call schedule; nothing here is called unless a host opts in,
    /// so AdvanceDay stays byte-identical for callers that don't evolve condition.
    /// </summary>
    public sealed class ConditionProgressor
    {
        /// <summary>Golden-ratio odd constant, decorrelates per-club condition seeds.</summary>
        private const ulong ClubSeedMix = 0x9E3779B97F4A7C15UL;

        /// <summary>A start is credited a full match; partial minutes (subs) aren't modelled in v1.</summary>
        private const int FullMatchMinutes = 90;

        /// <summary>Each Evolve call advances exactly one calendar day.</summary>
        private const int OneRestDay = 1;

        private readonly ConditionBalance _cfg;

        public ConditionProgressor(ConditionBalance cfg)
        {
            _cfg = cfg;
        }

        /// <summary>What a club did on a matchday: who started, and the team's result.</summary>
        public readonly struct Participation
        {
            public readonly HashSet<int> StarterIds;
            public readonly TeamResult Result;

            public Participation(HashSet<int> starterIds, TeamResult result)
            {
                StarterIds = starterIds;
                Result = result;
            }
        }

        /// <summary>
        /// Evolves every club in the world for <paramref name="day"/>: clubs present in
        /// <paramref name="played"/> get the match step, the rest get one rest day.
        /// </summary>
        public void Evolve(
            IReadOnlyList<League> leagues,
            IReadOnlyDictionary<int, Participation> played,
            ulong worldSeed,
            int day)
        {
            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    if (played.TryGetValue(club.Id, out Participation p))
                        ApplyMatch(club, p, new Pcg32(worldSeed ^ ((ulong)club.Id * ClubSeedMix), (ulong)day));
                    else
                        Rest(club);
                }
            }
        }

        private void ApplyMatch(Club club, Participation participation, IRandomSource rng)
        {
            foreach (Player player in club.Squad.Players)
            {
                int minutes = participation.StarterIds.Contains(player.Id) ? FullMatchMinutes : 0;
                ConditionModel.ApplyMatchResult(
                    player.Condition, minutes, participation.Result, rng, _cfg, player.Attributes.Stamina);
            }
        }

        private void Rest(Club club)
        {
            foreach (Player player in club.Squad.Players)
                ConditionModel.ApplyRest(player.Condition, OneRestDay, _cfg);
        }
    }
}
