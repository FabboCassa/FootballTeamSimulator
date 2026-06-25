using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;
using Sim.Core.Scouting;

namespace Sim.Core.Tests.Scouting
{
    /// <summary>
    /// Task 5.4 acceptance: the scouting / knowledge layer. THE ✅ — an unscouted player
    /// shows WIDE attribute ranges, and after N weeks of scouting the ranges NARROW around
    /// the true values. Plus the supporting guarantees: the narrowing is monotonic in
    /// knowledge, a better scout narrows faster, the hidden potential is estimated as a band
    /// that narrows too, the TRUE value is always inside the reported band at every knowledge
    /// level (anti-frustration), the estimate converges exactly onto the truth at full
    /// knowledge, the model is deterministic (no RNG), accrual caps at full, and the
    /// whole-world progressor scouts AI clubs too — deterministically and order-independently.
    ///
    /// Nothing here is called by the engine, so the golden master / existing 162 tests are
    /// unaffected.
    /// </summary>
    [TestFixture]
    public class ScoutingTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static ScoutingBalance S => Cfg.Scouting;
        private const ulong WorldSeed = 12345UL;
        private const int ClubId = 7;

        // A player whose every attribute equals `skill` ⇒ Overall == skill (each role row sums to 100).
        private static Player Make(int id, int skill, int potential)
        {
            var p = new Player { Id = id, Role = PositionRole.FullBack, Age = 24 };
            for (int i = 0; i < PlayerAttributes.SkillCount; i++) p.Attributes[i] = skill;
            p.Development.Potential = potential;
            return p;
        }

        private static ScoutedRange Attr0(Player p, int knowledge)
            => ScoutingModel.Report(p, knowledge, WorldSeed, ClubId, S).Attributes[0];

        // ------------------------------------------------------- THE ✅: wide → narrow

        [Test]
        public void Unscouted_ShowsWideRanges()
        {
            Player p = Make(1, skill: 55, potential: 70); // mid-range ⇒ no clamping at the edges
            ScoutedRange r = Attr0(p, knowledge: 0);

            Assert.That(r.Width, Is.GreaterThanOrEqualTo(2 * S.AttributeMaxHalfWidth - 2),
                "An unscouted attribute should read as a wide band (~±max half-width)");
            Assert.That(r.Min, Is.LessThanOrEqualTo(55).And.GreaterThanOrEqualTo(1));
            Assert.That(r.Max, Is.GreaterThanOrEqualTo(55).And.LessThanOrEqualTo(100));
        }

        [Test]
        public void Scouted_RangeNarrows_AfterWeeks()
        {
            Player p = Make(1, skill: 55, potential: 70);

            int wide = Attr0(p, knowledge: 0).Width;

            // Scout with a level-3 department, week by week, until full knowledge.
            int knowledge = 0, weeks = 0;
            while (knowledge < S.MaxKnowledge)
            {
                knowledge = ScoutingModel.Accrue(knowledge, scoutLevel: 3, S);
                weeks++;
            }
            int tight = Attr0(p, knowledge).Width;

            TestContext.Out.WriteLine(
                $"[scouting-narrow] attr width: unscouted={wide} → fully-scouted={tight} in {weeks} weeks (lvl3)");

            Assert.That(tight, Is.LessThan(wide), "Scouting must narrow the range");
            Assert.That(tight, Is.LessThanOrEqualTo(2 * S.AttributeMinHalfWidth),
                "Fully scouted, the band is at its tightest");
            Assert.That(Attr0(p, knowledge).Min, Is.LessThanOrEqualTo(55));
            Assert.That(Attr0(p, knowledge).Max, Is.GreaterThanOrEqualTo(55));
        }

        [Test]
        public void Narrowing_IsMonotonic_InKnowledge()
        {
            Player p = Make(1, skill: 55, potential: 70);
            int previous = int.MaxValue;
            for (int k = 0; k <= S.MaxKnowledge; k += 5)
            {
                int width = Attr0(p, k).Width;
                Assert.That(width, Is.LessThanOrEqualTo(previous),
                    $"Width must not grow as knowledge rises (at k={k})");
                previous = width;
            }
        }

        [Test]
        public void HigherScoutLevel_NarrowsFaster()
        {
            Player p = Make(1, skill: 55, potential: 70);

            // After the same 2 weeks, a level-5 dept knows more than a level-1 dept ⇒ tighter.
            int weak = ScoutingModel.Accrue(ScoutingModel.Accrue(0, 1, S), 1, S);
            int strong = ScoutingModel.Accrue(ScoutingModel.Accrue(0, 5, S), 5, S);

            Assert.That(strong, Is.GreaterThan(weak), "A better scout accrues knowledge faster");
            Assert.That(Attr0(p, strong).Width, Is.LessThan(Attr0(p, weak).Width),
                "Faster knowledge ⇒ a tighter band sooner");
        }

        // ------------------------------------------------------- potential estimation

        [Test]
        public void Potential_BandNarrows_AndContainsTruth()
        {
            Player p = Make(1, skill: 55, potential: 82);

            ScoutedRange wide = ScoutingModel.Report(p, 0, WorldSeed, ClubId, S).Potential;
            ScoutedRange tight = ScoutingModel.Report(p, S.MaxKnowledge, WorldSeed, ClubId, S).Potential;

            TestContext.Out.WriteLine(
                $"[scouting-potential] potential 82: unscouted [{wide.Min},{wide.Max}] est {wide.Estimate} " +
                $"→ scouted [{tight.Min},{tight.Max}] est {tight.Estimate}");

            Assert.That(tight.Width, Is.LessThan(wide.Width), "Potential band must narrow with scouting");
            Assert.That(wide.Min, Is.LessThanOrEqualTo(82).And.LessThanOrEqualTo(wide.Max));
            Assert.That(wide.Max, Is.GreaterThanOrEqualTo(82));
            Assert.That(tight.Min, Is.LessThanOrEqualTo(82));
            Assert.That(tight.Max, Is.GreaterThanOrEqualTo(82));
        }

        // ------------------------------------------------------- anti-frustration guarantee

        [Test]
        public void TrueValue_AlwaysInsideBand_AcrossEverything()
        {
            // Sweep true value × potential × knowledge × several players/clubs (varies the bias).
            for (int trueVal = 1; trueVal <= 100; trueVal++)
            {
                for (int playerId = 1; playerId <= 6; playerId++)
                {
                    Player p = Make(playerId, trueVal, potential: trueVal);
                    for (int k = 0; k <= S.MaxKnowledge; k += 4)
                    {
                        for (int club = 0; club < 4; club++)
                        {
                            PlayerScoutReport rep = ScoutingModel.Report(p, k, WorldSeed, club, S);
                            foreach (ScoutedRange a in rep.Attributes)
                            {
                                Assert.That(a.Min, Is.LessThanOrEqualTo(trueVal));
                                Assert.That(a.Max, Is.GreaterThanOrEqualTo(trueVal));
                            }
                            Assert.That(rep.Potential.Min, Is.LessThanOrEqualTo(trueVal));
                            Assert.That(rep.Potential.Max, Is.GreaterThanOrEqualTo(trueVal));
                            Assert.That(rep.Overall.Min, Is.LessThanOrEqualTo(trueVal));
                            Assert.That(rep.Overall.Max, Is.GreaterThanOrEqualTo(trueVal));
                        }
                    }
                }
            }
        }

        [Test]
        public void Estimate_ConvergesExactlyToTruth_AtFullKnowledge()
        {
            for (int trueVal = 1; trueVal <= 100; trueVal++)
            {
                Player p = Make(1, trueVal, potential: trueVal);
                PlayerScoutReport rep = ScoutingModel.Report(p, S.MaxKnowledge, WorldSeed, ClubId, S);
                foreach (ScoutedRange a in rep.Attributes)
                    Assert.That(a.Estimate, Is.EqualTo(trueVal), "At full knowledge the estimate is the truth");
                Assert.That(rep.Potential.Estimate, Is.EqualTo(trueVal));
                Assert.That(rep.Overall.Estimate, Is.EqualTo(trueVal));
            }
        }

        // ------------------------------------------------------- purity & accrual

        [Test]
        public void Report_IsDeterministic_NoRng()
        {
            Player p = Make(3, 64, 88);
            string a = Serialize(ScoutingModel.Report(p, 40, WorldSeed, ClubId, S));
            string b = Serialize(ScoutingModel.Report(p, 40, WorldSeed, ClubId, S));
            Assert.That(b, Is.EqualTo(a));

            // Different clubs read slightly different numbers (bias depends on the club): across
            // several clubs, more than one distinct read appears (robust, not a single-pair flip).
            var reads = new HashSet<string>();
            for (int club = 0; club < 8; club++)
                reads.Add(Serialize(ScoutingModel.Report(p, 40, WorldSeed, club, S)));
            Assert.That(reads.Count, Is.GreaterThan(1), "Different clubs should read the player differently");
        }

        [Test]
        public void Accrue_CapsAtMaxKnowledge()
        {
            int k = 0;
            for (int i = 0; i < 100; i++) k = ScoutingModel.Accrue(k, scoutLevel: 5, S);
            Assert.That(k, Is.EqualTo(S.MaxKnowledge), "Knowledge saturates exactly at the cap, never beyond");
        }

        // ------------------------------------------------------- whole-world progressor

        [Test]
        public void Progressor_NarrowsAssigned_NotUnassigned()
        {
            League league = SmallLeague();
            var knowledge = new KnowledgeStore();
            var book = new ScoutingAssignmentBook();
            var progressor = new ScoutingProgressor(S);

            Club me = league.Clubs[0];
            Club rival = league.Clubs[1];
            int watchedId = rival.Squad.Players[0].Id;
            int ignoredId = rival.Squad.Players[1].Id;
            book.Assign(me.Id, watchedId);

            for (int week = 0; week < 8; week++)
                progressor.EvolveWeek(new[] { league }, knowledge, book);

            Assert.That(knowledge.Get(me.Id, watchedId), Is.GreaterThan(0), "The watched player gets scouted");
            Assert.That(knowledge.Get(me.Id, ignoredId), Is.EqualTo(0),
                "A player I don't watch stays unscouted (explicit assignments ⇒ no policy fallback)");

            Player watched = rival.Squad.Players[0];
            int wide = ScoutingModel.Report(watched, 0, WorldSeed, me.Id, S).Attributes[0].Width;
            int tight = ScoutingModel.Report(watched, knowledge.Get(me.Id, watchedId), WorldSeed, me.Id, S).Attributes[0].Width;
            Assert.That(tight, Is.LessThan(wide), "Eight weeks of watching narrows his ranges");
        }

        [Test]
        public void WholeWorld_Scouts_AiClubsToo_AndIsDeterministic()
        {
            League a = SmallLeague();
            League b = SmallLeague(); // identical seed ⇒ identical world
            b.Clubs.Reverse();        // same clubs, opposite iteration order ⇒ proves order-independence

            var kA = new KnowledgeStore();
            var kB = new KnowledgeStore();
            var prog = new ScoutingProgressor(S);

            // No explicit assignments ⇒ every club uses the default policy (AI scouts too).
            for (int week = 0; week < 6; week++)
            {
                prog.EvolveWeek(new[] { a }, kA, new ScoutingAssignmentBook());
                prog.EvolveWeek(new[] { b }, kB, new ScoutingAssignmentBook());
            }

            var mapA = kA.Export().ToDictionary(e => (e.ClubId, e.PlayerId), e => e.Knowledge);
            var mapB = kB.Export().ToDictionary(e => (e.ClubId, e.PlayerId), e => e.Knowledge);

            Assert.That(mapA.Count, Is.GreaterThan(0), "AI clubs accrue knowledge with no explicit assignments");
            Assert.That(mapB, Is.EquivalentTo(mapA), "Scouting is deterministic and order-independent");

            // At least one AI club (not club 0) has learned something.
            bool aiLearned = mapA.Any(kv => kv.Key.Item1 != a.Clubs[0].Id && kv.Value > 0);
            Assert.That(aiLearned, Is.True, "The whole world scouts, not just one club");

            TestContext.Out.WriteLine($"[scouting-world] {mapA.Count} (club,player) knowledge entries after 6 weeks");
        }

        // ------------------------------------------------------- helpers

        private static League SmallLeague()
        {
            var options = new LeagueGenerationOptions { ClubCount = 6 };
            return new LeagueGenerator(options).Generate(new Pcg32(777));
        }

        private static string Serialize(PlayerScoutReport r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(r.PlayerId).Append('|').Append(r.Knowledge)
              .Append('|').Append(r.Overall.Min).Append(',').Append(r.Overall.Max).Append(',').Append(r.Overall.Estimate)
              .Append('|').Append(r.Potential.Min).Append(',').Append(r.Potential.Max).Append(',').Append(r.Potential.Estimate);
            foreach (ScoutedRange a in r.Attributes)
                sb.Append('|').Append(a.Min).Append(',').Append(a.Max).Append(',').Append(a.Estimate);
            return sb.ToString();
        }
    }
}
