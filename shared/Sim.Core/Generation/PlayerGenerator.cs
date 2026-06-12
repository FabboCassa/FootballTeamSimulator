using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Generation
{
    /// <summary>
    /// Generates a single player around a target overall rating.
    /// Deterministic: all randomness flows through the injected IRandomSource;
    /// all tunables come from GenerationBalance.
    /// </summary>
    public sealed class PlayerGenerator
    {
        private readonly GenerationBalance _cfg;

        public PlayerGenerator(GenerationBalance cfg)
        {
            _cfg = cfg;
        }

        public Player Generate(int id, PositionRole role, int targetOverall, IRandomSource rng)
        {
            int age = SampleAge(rng);

            var player = new Player
            {
                Id = id,
                FirstName = NameDatabase.FirstNames[rng.NextInt(0, NameDatabase.FirstNames.Length)],
                LastName = NameDatabase.LastNames[rng.NextInt(0, NameDatabase.LastNames.Length)],
                Age = age,
                Role = role
            };

            GenerateAttributes(player, role, targetOverall, rng);

            int overall = PlayerRating.Overall(player);

            // Younger players keep headroom to grow; potential is never below current ability.
            int headroom = (_cfg.PeakAge - age) * _cfg.HeadroomPerYear
                           + rng.NextInt(_cfg.HeadroomNoiseMin, _cfg.HeadroomNoiseMax + 1);
            player.Development.Potential = AttributeScale.ClampSkill(
                overall + (headroom > 0 ? headroom : 0));

            player.Condition.Form = 50;
            player.Condition.Morale = rng.NextInt(_cfg.MoraleMin, _cfg.MoraleMax + 1);
            player.Condition.Fitness = rng.NextInt(_cfg.FitnessMin, _cfg.FitnessMax + 1);

            player.Contract.WeeklyWage = (long)overall * overall * _cfg.WageFactor;
            player.Contract.SeasonsRemaining = rng.NextInt(_cfg.ContractSeasonsMin, _cfg.ContractSeasonsMax + 1);

            return player;
        }

        private int SampleAge(IRandomSource rng)
        {
            int span = _cfg.MaxAge - _cfg.MinAge + 1;
            int[] weights = _cfg.AgeWeights;

            int totalWeight = 0;
            for (int i = 0; i < span && i < weights.Length; i++) totalWeight += weights[i];
            if (totalWeight <= 0) return _cfg.MinAge;

            int roll = rng.NextInt(0, totalWeight);
            for (int i = 0; i < span && i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll < 0) return _cfg.MinAge + i;
            }

            return _cfg.MinAge;
        }

        private void GenerateAttributes(Player player, PositionRole role, int targetOverall, IRandomSource rng)
        {
            PlayerAttributes a = player.Attributes;

            // skill = target + (roleWeight - 10) + noise. Skills the role relies on land higher.
            a.Pace        = Skill(role, 0, targetOverall, rng);
            a.Strength    = Skill(role, 1, targetOverall, rng);
            a.Stamina     = Skill(role, 2, targetOverall, rng);
            a.Technique   = Skill(role, 3, targetOverall, rng);
            a.Passing     = Skill(role, 4, targetOverall, rng);
            a.Dribbling   = Skill(role, 5, targetOverall, rng);
            a.Shooting    = Skill(role, 6, targetOverall, rng);
            a.Defending   = Skill(role, 7, targetOverall, rng);
            a.Positioning = Skill(role, 8, targetOverall, rng);

            // Goalkeeping is special: outfielders are simply bad at it.
            a.Goalkeeping = role == PositionRole.Goalkeeper
                ? Skill(role, 9, targetOverall, rng)
                : rng.NextInt(_cfg.OutfieldGoalkeepingMin, _cfg.OutfieldGoalkeepingMax + 1);
        }

        private int Skill(PositionRole role, int skillIndex, int target, IRandomSource rng)
        {
            int bias = PlayerRating.WeightOf(role, skillIndex) - 10;
            int noise = rng.NextInt(-_cfg.SkillNoise, _cfg.SkillNoise + 1);
            return AttributeScale.ClampSkill(target + bias + noise);
        }
    }
}
