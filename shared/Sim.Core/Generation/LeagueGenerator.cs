using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Generation
{
    /// <summary>
    /// Generates a complete league: clubs with a strength hierarchy, full squads, coaches.
    /// Same options + same config + same seed => byte-identical league on every platform.
    /// </summary>
    public sealed class LeagueGenerator
    {
        private readonly LeagueGenerationOptions _options;
        private readonly GenerationBalance _cfg;

        public LeagueGenerator(LeagueGenerationOptions? options = null, BalanceConfig? config = null)
        {
            _options = options ?? new LeagueGenerationOptions();
            _cfg = (config ?? new BalanceConfig()).Generation;
        }

        public League Generate(IRandomSource rng)
        {
            var league = new League
            {
                Id = _options.LeagueId,
                Name = _options.LeagueName,
                Division = _options.Division
            };

            var playerGenerator = new PlayerGenerator(_cfg);
            string[] clubNames = BuildUniqueClubNames(rng);
            int nextPlayerId = _options.FirstPlayerId;

            for (int c = 0; c < _options.ClubCount; c++)
            {
                int jitter = _cfg.ClubStrengthJitter;
                int baseline = ClubBaseline(c) + rng.NextInt(-jitter, jitter + 1);

                var club = new Club
                {
                    Id = c + 1,
                    Name = clubNames[c],
                    ShortName = MakeShortName(clubNames[c]),
                    Coach = new Coach
                    {
                        Id = c + 1,
                        Name = $"{NameDatabase.FirstNames[rng.NextInt(0, NameDatabase.FirstNames.Length)]} " +
                               $"{NameDatabase.LastNames[rng.NextInt(0, NameDatabase.LastNames.Length)]}",
                        IsHuman = false,
                        Reputation = AttributeScale.ClampCondition(baseline)
                    }
                };

                var usedNames = new HashSet<string>();
                foreach (var (role, count) in SquadTemplate.Default)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int target = AttributeScale.ClampSkill(
                            baseline + rng.NextInt(-_cfg.PlayerTargetNoise, _cfg.PlayerTargetNoise + 1));
                        Player player = playerGenerator.Generate(nextPlayerId++, role, target, rng);

                        // Avoid duplicate full names inside the same squad (rare, but ugly).
                        int retries = 0;
                        while (!usedNames.Add(player.FullName) && retries++ < 20)
                        {
                            player.FirstName = NameDatabase.FirstNames[rng.NextInt(0, NameDatabase.FirstNames.Length)];
                            player.LastName = NameDatabase.LastNames[rng.NextInt(0, NameDatabase.LastNames.Length)];
                        }

                        club.Squad.Players.Add(player);
                    }
                }

                league.Clubs.Add(club);
            }

            return league;
        }

        /// <summary>Linear strength spread from top club to bottom club.</summary>
        private int ClubBaseline(int clubIndex)
        {
            if (_options.ClubCount <= 1) return _cfg.TopClubStrength;

            int top = _cfg.TopClubStrength;
            int bottom = _cfg.BottomClubStrength;
            return top - (top - bottom) * clubIndex / (_options.ClubCount - 1);
        }

        private string[] BuildUniqueClubNames(IRandomSource rng)
        {
            // Deterministic Fisher-Yates over the town pool, then take the first N.
            string[] towns = (string[])NameDatabase.ClubTowns.Clone();
            for (int i = towns.Length - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (towns[i], towns[j]) = (towns[j], towns[i]);
            }

            var names = new string[_options.ClubCount];
            for (int i = 0; i < _options.ClubCount; i++)
            {
                string prefix = NameDatabase.ClubPrefixes[rng.NextInt(0, NameDatabase.ClubPrefixes.Length)];
                string town = towns[i % towns.Length];
                names[i] = $"{prefix} {town}";
            }

            return names;
        }

        private static string MakeShortName(string clubName)
        {
            int space = clubName.IndexOf(' ');
            string town = space >= 0 ? clubName.Substring(space + 1) : clubName;
            return (town.Length >= 3 ? town.Substring(0, 3) : town).ToUpperInvariant();
        }
    }
}
