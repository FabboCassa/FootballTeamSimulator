using System;
using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Generation
{
    /// <summary>
    /// Double round-robin schedule via the circle method, with a seeded
    /// shuffle of the club order. Same league + same seed => identical
    /// fixture list on every platform.
    /// </summary>
    public sealed class FixtureGenerator
    {
        private readonly SeasonBalance _cfg;

        public FixtureGenerator(SeasonBalance? config = null)
        {
            _cfg = config ?? new SeasonBalance();
        }

        public List<Fixture> Generate(League league, IRandomSource rng, int firstFixtureId = 1)
        {
            int n = league.Clubs.Count;
            if (n < 2 || n % 2 != 0)
                throw new InvalidOperationException($"Double round-robin needs an even club count >= 2, got {n}.");

            int[] ids = ShuffledClubIds(league, rng);
            int roundsPerHalf = n - 1;
            var fixtures = new List<Fixture>(n * (n - 1));
            int nextId = firstFixtureId;

            // First half: circle method. ids[0] is fixed, the rest rotate.
            for (int round = 1; round <= roundsPerHalf; round++)
            {
                int[] arrangement = new int[n];
                arrangement[0] = ids[0];
                for (int k = 1; k < n; k++)
                    arrangement[k] = ids[1 + (round - 1 + k - 1) % (n - 1)];

                for (int i = 0; i < n / 2; i++)
                {
                    int first = arrangement[i];
                    int second = arrangement[n - 1 - i];

                    // Alternate venues for variety (exact balance is not required:
                    // the mirrored second half guarantees one home and one away
                    // meeting per pair).
                    bool firstIsHome = (round + i) % 2 == 0;

                    fixtures.Add(new Fixture
                    {
                        Id = nextId++,
                        Round = round,
                        Day = DayOfRound(round),
                        HomeClubId = firstIsHome ? first : second,
                        AwayClubId = firstIsHome ? second : first
                    });
                }
            }

            // Second half: mirror with swapped venues.
            int firstHalfCount = fixtures.Count;
            for (int i = 0; i < firstHalfCount; i++)
            {
                Fixture f = fixtures[i];
                fixtures.Add(new Fixture
                {
                    Id = nextId++,
                    Round = f.Round + roundsPerHalf,
                    Day = DayOfRound(f.Round + roundsPerHalf),
                    HomeClubId = f.AwayClubId,
                    AwayClubId = f.HomeClubId
                });
            }

            return fixtures;
        }

        private int DayOfRound(int round) => _cfg.FirstMatchDay + (round - 1) * _cfg.DaysBetweenRounds;

        private static int[] ShuffledClubIds(League league, IRandomSource rng)
        {
            int[] ids = new int[league.Clubs.Count];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = league.Clubs[i].Id;

            // Fisher-Yates.
            for (int i = ids.Length - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }

            return ids;
        }
    }
}
