using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// The scouting / knowledge model (task 5.4, ARCHITECTURE.md §4.7). PURE and
    /// deterministic — integer math and NO live RNG. The "accuracy" of a read is a
    /// function of how much a club has scouted a player (a knowledge level in
    /// [0, MaxKnowledge]): at 0 the attributes show as wide ranges, and the ranges
    /// narrow toward the true values as knowledge accrues week by week.
    ///
    /// Two structural guarantees (the magnitudes are in <see cref="ScoutingBalance"/>):
    ///   1. The TRUE value is ALWAYS inside the reported [Min, Max] at every knowledge
    ///      level — the band half-width bounds the estimate's off-centre bias, so the
    ///      scout can be vague but never *wrong* (anti-frustration / "challenge, not
    ///      chaos"). At full knowledge the band collapses onto the truth.
    ///   2. The estimate's bias is a stable per-(worldSeed, club, player, attribute) hash,
    ///      so it's deterministic and replay-safe (no seeded stream is touched) yet two
    ///      different clubs read a player's numbers slightly differently.
    ///
    /// Nothing here is called by the match engine or SeasonProgressor → golden
    /// masters/replays are unaffected (opt-in by being called).
    /// </summary>
    public static class ScoutingModel
    {
        // Salts decorrelate the three kinds of band so they don't all bias the same way.
        private const int OverallSalt = 101;
        private const int PotentialSalt = 100;

        /// <summary>
        /// Half-width of a band at the given knowledge: linear from <paramref name="maxHalf"/>
        /// at knowledge 0 to <paramref name="minHalf"/> at full knowledge. Monotonically
        /// non-increasing in knowledge (so a band never widens as you learn more).
        /// </summary>
        public static int HalfWidth(int knowledge, int maxHalf, int minHalf, ScoutingBalance cfg)
        {
            int max = cfg.MaxKnowledge > 0 ? cfg.MaxKnowledge : 1;
            int k = knowledge < 0 ? 0 : knowledge > max ? max : knowledge;
            // minHalf + (maxHalf - minHalf) * (max - k) / max
            return minHalf + (maxHalf - minHalf) * (max - k) / max;
        }

        /// <summary>
        /// Estimates a true value at a knowledge level into a <see cref="ScoutedRange"/>.
        /// <paramref name="biasPermille"/> in [-1000, 1000] places the band centre off the
        /// truth by that fraction of the available slack (half-width − minHalf), itself
        /// scaled by <see cref="ScoutingBalance.EstimateBiasPercent"/>. Because |bias| never
        /// exceeds the half-width, the true value is always within [Min, Max]; all three
        /// are clamped to the [1, 100] skill scale (clamping only ever keeps the truth in).
        /// </summary>
        public static ScoutedRange Estimate(int trueValue, int knowledge, int maxHalf, int minHalf,
                                            int biasPermille, ScoutingBalance cfg)
        {
            int half = HalfWidth(knowledge, maxHalf, minHalf, cfg);
            int slack = half - minHalf;
            if (slack < 0) slack = 0;

            int biasMagnitude = slack * cfg.EstimateBiasPercent / 100; // ≤ slack ≤ half at ≤100%
            if (biasMagnitude > half) biasMagnitude = half; // hard guarantee: |bias| ≤ half ⇒ truth stays in band
            if (biasPermille < -1000) biasPermille = -1000;
            if (biasPermille > 1000) biasPermille = 1000;
            int bias = biasMagnitude * biasPermille / 1000; // |bias| ≤ biasMagnitude ≤ half

            int centre = trueValue + bias;
            int min = AttributeScale.ClampSkill(centre - half);
            int max = AttributeScale.ClampSkill(centre + half);
            int estimate = AttributeScale.ClampSkill(centre);
            return new ScoutedRange(min, max, estimate);
        }

        /// <summary>Knowledge after one week of an actively-watched player observed by a scout of the given level (capped at MaxKnowledge).</summary>
        public static int Accrue(int knowledge, int scoutLevel, ScoutingBalance cfg)
        {
            if (knowledge < 0) knowledge = 0;
            int gained = scoutLevel * cfg.KnowledgePerScoutLevelPerWeek;
            int k = knowledge + (gained > 0 ? gained : 0);
            return k > cfg.MaxKnowledge ? cfg.MaxKnowledge : k;
        }

        /// <summary>
        /// Builds a full scouting read of <paramref name="player"/> at the given knowledge,
        /// as seen by <paramref name="observerClubId"/> in a world seeded with
        /// <paramref name="worldSeed"/> (the bias depends on all three, so different clubs
        /// read slightly different numbers and the read is stable across reloads/replays).
        /// </summary>
        public static PlayerScoutReport Report(Player player, int knowledge, ulong worldSeed,
                                               int observerClubId, ScoutingBalance cfg)
        {
            var attributes = new ScoutedRange[PlayerAttributes.SkillCount];
            for (int i = 0; i < PlayerAttributes.SkillCount; i++)
            {
                attributes[i] = Estimate(
                    player.Attributes[i], knowledge,
                    cfg.AttributeMaxHalfWidth, cfg.AttributeMinHalfWidth,
                    BiasPermille(worldSeed, observerClubId, player.Id, i), cfg);
            }

            ScoutedRange overall = Estimate(
                PlayerRating.Overall(player), knowledge,
                cfg.AttributeMaxHalfWidth, cfg.AttributeMinHalfWidth,
                BiasPermille(worldSeed, observerClubId, player.Id, OverallSalt), cfg);

            ScoutedRange potential = Estimate(
                player.Development.Potential, knowledge,
                cfg.PotentialMaxHalfWidth, cfg.PotentialMinHalfWidth,
                BiasPermille(worldSeed, observerClubId, player.Id, PotentialSalt), cfg);

            return new PlayerScoutReport(player.Id, knowledge, overall, potential, attributes);
        }

        /// <summary>
        /// Stable signed bias in [-1000, 1000] for one (world, club, player, salt) tuple — a
        /// splitmix-style integer hash, so it's well-distributed, platform-independent and
        /// consumes no RNG stream.
        /// </summary>
        internal static int BiasPermille(ulong worldSeed, int observerClubId, int playerId, int salt)
        {
            ulong x = worldSeed + 0x9E3779B97F4A7C15UL;
            x = (x ^ ((ulong)(uint)observerClubId)) * 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27;
            x = (x ^ ((ulong)(uint)playerId)) * 0x94D049BB133111EBUL;
            x ^= x >> 31;
            x = (x ^ ((ulong)(uint)salt)) * 0xD6E8FEB86659FD93UL;
            x ^= x >> 32;
            return (int)(x % 2001UL) - 1000;
        }
    }
}
