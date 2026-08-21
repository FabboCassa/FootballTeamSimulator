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
    /// TASK 11.2 adds two orthogonal dials on top, both of which leave guarantee (1) intact
    /// because they only ever change WHICH knowledge level a band is built at:
    ///   • <see cref="ScoutQuality"/> — the individual scout. A good judge of ability reads a
    ///     player as if he had watched him longer; judging potential is a separate dial.
    ///   • <see cref="KnowledgeCap"/> — the breadth of the brief. A scout covering a continent
    ///     cannot push per-player knowledge past a low ceiling however long he stays, and that
    ///     ceiling is lifted by the club's accumulated knowledge OF THAT AREA.
    /// Both are neutral by default (<see cref="ScoutQuality.Neutral"/>, a Player-kind brief), so
    /// every pre-11.2 call site reads exactly the numbers it read before.
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
            => Report(player, knowledge, worldSeed, observerClubId, cfg, ScoutQuality.Neutral);

        /// <summary>
        /// The same read, but through a particular scout (task 11.2): his judging attributes shift
        /// the knowledge the ability bands and the potential band are built at, independently.
        /// Passing <see cref="ScoutQuality.Neutral"/> is byte-identical to the overload above.
        /// </summary>
        public static PlayerScoutReport Report(Player player, int knowledge, ulong worldSeed,
                                               int observerClubId, ScoutingBalance cfg, ScoutQuality quality)
        {
            int abilityK = AbilityKnowledge(knowledge, quality, cfg);
            int potentialK = PotentialKnowledge(knowledge, quality, cfg);

            var attributes = new ScoutedRange[PlayerAttributes.SkillCount];
            for (int i = 0; i < PlayerAttributes.SkillCount; i++)
            {
                attributes[i] = Estimate(
                    player.Attributes[i], abilityK,
                    cfg.AttributeMaxHalfWidth, cfg.AttributeMinHalfWidth,
                    BiasPermille(worldSeed, observerClubId, player.Id, i), cfg);
            }

            ScoutedRange overall = Estimate(
                PlayerRating.Overall(player), abilityK,
                cfg.AttributeMaxHalfWidth, cfg.AttributeMinHalfWidth,
                BiasPermille(worldSeed, observerClubId, player.Id, OverallSalt), cfg);

            ScoutedRange potential = Estimate(
                player.Development.Potential, potentialK,
                cfg.PotentialMaxHalfWidth, cfg.PotentialMinHalfWidth,
                BiasPermille(worldSeed, observerClubId, player.Id, PotentialSalt), cfg);

            // The report carries the RAW knowledge (what the club has actually accumulated), not the
            // scout-adjusted one: the UI's "62% scouted" bar must mean weeks of work, not the current
            // scout's eyesight — otherwise reassigning a scout would appear to erase progress.
            return new PlayerScoutReport(player.Id, knowledge, overall, potential, attributes);
        }

        /// <summary>
        /// Just the overall band, without allocating the ten attribute ranges (task 11.2). This is
        /// what a wide discovery scan uses: a continental brief walks tens of thousands of players
        /// every week and only needs the two numbers the filters test.
        /// </summary>
        public static ScoutedRange OverallOf(Player player, int knowledge, ulong worldSeed,
                                             int observerClubId, ScoutingBalance cfg, ScoutQuality quality)
            => Estimate(
                PlayerRating.Overall(player), AbilityKnowledge(knowledge, quality, cfg),
                cfg.AttributeMaxHalfWidth, cfg.AttributeMinHalfWidth,
                BiasPermille(worldSeed, observerClubId, player.Id, OverallSalt), cfg);

        /// <summary>Just the potential band; see <see cref="OverallOf"/>.</summary>
        public static ScoutedRange PotentialOf(Player player, int knowledge, ulong worldSeed,
                                               int observerClubId, ScoutingBalance cfg, ScoutQuality quality)
            => Estimate(
                player.Development.Potential, PotentialKnowledge(knowledge, quality, cfg),
                cfg.PotentialMaxHalfWidth, cfg.PotentialMinHalfWidth,
                BiasPermille(worldSeed, observerClubId, player.Id, PotentialSalt), cfg);

        /// <summary>The knowledge a scout of this quality effectively reads CURRENT ability at.</summary>
        public static int AbilityKnowledge(int knowledge, ScoutQuality quality, ScoutingBalance cfg)
            => ClampKnowledge(Scaled(knowledge, quality.AbilityPercent), cfg);

        /// <summary>The knowledge a scout of this quality effectively reads POTENTIAL at.</summary>
        public static int PotentialKnowledge(int knowledge, ScoutQuality quality, ScoutingBalance cfg)
            => ClampKnowledge(Scaled(knowledge, quality.PotentialPercent), cfg);

        // ------------------------------------------------------------------ task 11.2: area briefs

        /// <summary>
        /// The ceiling an assignment of this breadth can push per-player knowledge to, lifted by how
        /// well the club already knows the area. This is the mechanical heart of "the bigger the
        /// area, the LOWER the precision": a continental scout saturates at a rough read no matter
        /// how many weeks he spends, while a scout parked on one club approaches full knowledge —
        /// and seasons of accumulated area knowledge raise the continental ceiling toward the
        /// club-level one, which is the reward for keeping a scout in one region.
        /// </summary>
        public static int KnowledgeCap(ScoutingAreaKind kind, int areaKnowledge, ScoutingBalance cfg)
        {
            int basePercent = Table(cfg.AreaKnowledgeCapPercent, (int)kind, 100);
            if (basePercent > 100) basePercent = 100;
            if (basePercent < 0) basePercent = 0;

            int maxArea = cfg.MaxAreaKnowledge > 0 ? cfg.MaxAreaKnowledge : 1;
            int a = areaKnowledge < 0 ? 0 : areaKnowledge > maxArea ? maxArea : areaKnowledge;

            // Full area knowledge closes AreaKnowledgeLiftPercent of the gap up to 100%.
            int lift = (100 - basePercent) * a * cfg.AreaKnowledgeLiftPercent / (maxArea * 100);
            int percent = basePercent + lift;
            if (percent > 100) percent = 100;

            return cfg.MaxKnowledge * percent / 100;
        }

        /// <summary>
        /// Knowledge of one player after another week of an AREA brief. Slower the wider the brief
        /// (<see cref="ScoutingBalance.AreaGainPercent"/>), faster the more adaptable the scout, and
        /// hard-stopped at <paramref name="cap"/>. Never REDUCES knowledge: a continental scout
        /// glancing at a player a club scout already knows well leaves that knowledge alone.
        /// </summary>
        public static int AccrueInArea(int knowledge, int scoutLevel, ScoutingAreaKind kind,
                                       int cap, ScoutQuality quality, ScoutingBalance cfg)
        {
            if (knowledge < 0) knowledge = 0;
            if (cap > cfg.MaxKnowledge) cap = cfg.MaxKnowledge;
            if (knowledge >= cap)
                return knowledge;

            int full = scoutLevel > 0 ? scoutLevel * cfg.KnowledgePerScoutLevelPerWeek : 0;
            if (full <= 0)
                return knowledge;

            int gained = full * Table(cfg.AreaGainPercent, (int)kind, 100) / 100;
            gained = gained * quality.SpeedPercent / 100;
            // A scout who is working at all learns SOMETHING each week: integer division must not
            // silently freeze a low-level scout on a wide brief at zero progress forever.
            if (gained <= 0) gained = 1;

            int k = knowledge + gained;
            return k > cap ? cap : k;
        }

        /// <summary>How many new names a brief of this breadth surfaces in one week (wider ⇒ more, and vaguer).</summary>
        public static int CandidatesPerWeek(ScoutingAreaKind kind, ScoutingBalance cfg)
        {
            int n = Table(cfg.AreaCandidatesPerWeek, (int)kind, 0);
            return n < 0 ? 0 : n;
        }

        // ------------------------------------------------------------------ helpers

        private static int Scaled(int knowledge, int percent)
        {
            if (knowledge < 0) knowledge = 0;
            return knowledge * percent / 100;
        }

        private static int ClampKnowledge(int knowledge, ScoutingBalance cfg)
        {
            int max = cfg.MaxKnowledge > 0 ? cfg.MaxKnowledge : 1;
            return knowledge < 0 ? 0 : knowledge > max ? max : knowledge;
        }

        /// <summary>Safe read of a per-breadth balance table (a short or absent table falls back).</summary>
        private static int Table(int[]? table, int index, int fallback)
            => table != null && index >= 0 && index < table.Length ? table[index] : fallback;

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
