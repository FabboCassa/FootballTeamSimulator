using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Career
{
    /// <summary>
    /// The fast model of the watchable-match spec (R17): the background resolver carries the V11
    /// calibration, and it reads the four instruction axes through
    /// percent tables whose middle entry is the identity. The 1,000-match comparison against the
    /// full engine is <see cref="FastModelHarnessTests"/>.
    /// </summary>
    [TestFixture]
    public class QuickResultResolverTests
    {
        private const ulong Seed = 20260927UL;
        private const int Strength = 66;

        private static (double Home, double Away) Expected(BalanceConfig cfg, TacticInstructions home, TacticInstructions away)
        {
            QuickResultResolver.ExpectedGoals(Strength, Strength, cfg, home, away, out double h, out double a);
            return (h, a);
        }

        private static readonly TacticInstructions Neutral = TacticInstructions.Neutral;

        [Test]
        public void ExpectedGoals_AreTheV11CalibratedFormula()
        {
            var cfg = new BalanceConfig();
            WorldBalance w = cfg.World;
            double home = 70 * (100.0 + cfg.Match.HomeAdvantagePercent) / 100.0 + w.QuickV11HomeAdvantageStrength;
            double difference = home - 62;

            QuickResultResolver.ExpectedGoals(70, 62, cfg, Neutral, Neutral, out double h, out double a);

            Assert.That(h, Is.EqualTo(w.QuickV11BaseGoals * (1.0 + difference * w.QuickV11StrengthFactor)));
            Assert.That(a, Is.EqualTo(w.QuickV11BaseGoals * (1.0 - difference * w.QuickV11StrengthFactor)));
        }

        [Test]
        public void AnOlderDocument_WithTheEngineV10Calibration_LoadsAndIsIgnored()
        {
            // Balance revisions stored before engine v11 still carry the v10 fit under the old names.
            BalanceConfig older = JsonSerializer.Deserialize<BalanceConfig>(
                "{\"World\":{\"QuickBaseGoals\":1.29,\"QuickStrengthFactor\":0.022," +
                "\"QuickHomeAdvantageStrength\":2,\"QuickGoalTrials\":14}}")!;
            BalanceConfig current = new BalanceConfig();

            Assert.That(Expected(older, Neutral, Neutral), Is.EqualTo(Expected(current, Neutral, Neutral)));
            for (int id = 1; id <= 50; id++)
            {
                var a = new Fixture { Id = id };
                var b = new Fixture { Id = id };
                QuickResultResolver.Resolve(a, 70, 62, Seed, older);
                QuickResultResolver.Resolve(b, 70, 62, Seed, current);
                Assert.That((a.HomeGoals, a.AwayGoals), Is.EqualTo((b.HomeGoals, b.AwayGoals)), $"fixture {id}");
            }
        }

        [Test]
        public void NeutralInstructions_AreTheIdentity()
        {
            var cfg = new BalanceConfig();
            for (int id = 1; id <= 200; id++)
            {
                var none = new Fixture { Id = id };
                var neutral = new Fixture { Id = id };
                QuickResultResolver.Resolve(none, 70, 62, Seed, cfg);
                QuickResultResolver.Resolve(neutral, 70, 62, Seed, cfg, Neutral, Neutral);
                Assert.That((neutral.HomeGoals, neutral.AwayGoals), Is.EqualTo((none.HomeGoals, none.AwayGoals)), $"fixture {id}");
            }
        }

        [Test]
        public void EveryTable_HasTheIdentityInTheMiddle()
        {
            foreach (int[] table in Tables(new BalanceConfig().World))
            {
                Assert.That(table.Length, Is.EqualTo(3));
                Assert.That(table[1], Is.EqualTo(0));
            }
        }

        [Test]
        public void AGoalsForEntry_MovesTheSidesOwnExpectedGoals_AndOnlyThose()
        {
            var cfg = new BalanceConfig();
            cfg.World.QuickMentalityGoalsForPercent = new[] { -20, 0, 20 };
            cfg.World.QuickMentalityGoalsAgainstPercent = new[] { 0, 0, 0 };
            var attacking = new TacticInstructions(Mentality.Attacking, Pressing.Medium, Tempo.Normal, Width.Normal);
            (double home, double away) = Expected(cfg, Neutral, Neutral);

            (double h, double a) = Expected(cfg, attacking, Neutral);
            Assert.That(h, Is.EqualTo(home * 1.2).Within(1e-9));
            Assert.That(a, Is.EqualTo(away));

            (h, a) = Expected(cfg, Neutral, attacking);
            Assert.That(h, Is.EqualTo(home));
            Assert.That(a, Is.EqualTo(away * 1.2).Within(1e-9), "the away side reads its table the same way");
        }

        [Test]
        public void AGoalsAgainstEntry_MovesTheOpponentsExpectedGoals()
        {
            var cfg = new BalanceConfig();
            cfg.World.QuickPressingGoalsForPercent = new[] { 0, 0, 0 };
            cfg.World.QuickPressingGoalsAgainstPercent = new[] { -10, 0, 10 };
            var low = new TacticInstructions(Mentality.Balanced, Pressing.Low, Tempo.Normal, Width.Normal);
            (double home, double away) = Expected(cfg, Neutral, Neutral);

            (double h, double a) = Expected(cfg, low, Neutral);
            Assert.That(h, Is.EqualTo(home));
            Assert.That(a, Is.EqualTo(away * 0.9).Within(1e-9), "a low block concedes less here");
        }

        [Test]
        public void AxesAdd_InPercentPoints()
        {
            var cfg = new BalanceConfig();
            WorldBalance w = cfg.World;
            w.QuickTempoGoalsForPercent = new[] { 0, 0, 6 };
            w.QuickWidthGoalsForPercent = new[] { 0, 0, 4 };
            w.QuickTempoGoalsAgainstPercent = new[] { 0, 0, 0 };
            w.QuickWidthGoalsAgainstPercent = new[] { 0, 0, 0 };
            var fastWide = new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Fast, Width.Wide);
            (double home, _) = Expected(cfg, Neutral, Neutral);

            Assert.That(Expected(cfg, fastWide, Neutral).Home, Is.EqualTo(home * 1.10).Within(1e-9));
        }

        /// <summary>The sign of each shipped extreme: the V11 engine's measured shift against a neutral side (FastModelHarnessTests).</summary>
        [TestCase(Mentality.Defensive, Pressing.Medium, Tempo.Normal, Width.Normal, -1, -1)]
        [TestCase(Mentality.Attacking, Pressing.Medium, Tempo.Normal, Width.Normal, +1, +1)]
        [TestCase(Mentality.Balanced, Pressing.High, Tempo.Normal, Width.Normal, -1, +1)]
        [TestCase(Mentality.Balanced, Pressing.Low, Tempo.Normal, Width.Normal, -1, +1)]
        [TestCase(Mentality.Balanced, Pressing.Medium, Tempo.Fast, Width.Normal, +1, -1)]
        [TestCase(Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Narrow, +1, -1)]
        [TestCase(Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Wide, -1, +1)]
        public void ShippedTables_ShiftGoalsTheWayTheV11EngineDoes(Mentality m, Pressing p, Tempo t, Width w, int goalsFor, int goalsAgainst)
        {
            var cfg = new BalanceConfig();
            var set = new TacticInstructions(m, p, t, w);
            (double home, double away) = Expected(cfg, Neutral, Neutral);

            (double h, double a) = Expected(cfg, set, Neutral);
            Assert.That(System.Math.Sign(h - home), Is.EqualTo(goalsFor), "goals for");
            Assert.That(System.Math.Sign(a - away), Is.EqualTo(goalsAgainst), "goals against");
        }

        [Test]
        public void Instructions_MoveTheDrawnScores()
        {
            var cfg = new BalanceConfig();
            cfg.World.QuickMentalityGoalsForPercent = new[] { -30, 0, 30 };
            var attacking = new TacticInstructions(Mentality.Attacking, Pressing.Medium, Tempo.Normal, Width.Normal);
            int neutralGoals = 0, attackingGoals = 0;

            for (int id = 1; id <= 2000; id++)
            {
                var n = new Fixture { Id = id };
                var t = new Fixture { Id = id };
                QuickResultResolver.Resolve(n, Strength, Strength, Seed, cfg, Neutral, Neutral);
                QuickResultResolver.Resolve(t, Strength, Strength, Seed, cfg, attacking, Neutral);
                neutralGoals += n.HomeGoals;
                attackingGoals += t.HomeGoals;
            }

            Assert.That(attackingGoals, Is.GreaterThan(neutralGoals * 1.15), "a 30% lift must show in 2,000 draws");
        }

        [Test]
        public void BackgroundProgressor_PassesEachClubsInstructions()
        {
            World plain = Generate(), neutral = Generate(), instructed = Generate();
            Club club = instructed.BackgroundSeason.Fixtures
                .Select(f => instructed.FindClub(f.HomeClubId)).First(c => c != null)!;
            var attacking = new TacticInstructions(Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Wide);
            var cfg = new BalanceConfig();
            cfg.World.QuickMentalityGoalsForPercent = new[] { -40, 0, 40 };
            var progressor = new BackgroundLeagueProgressor(cfg);

            progressor.AdvanceTo(plain, int.MaxValue, Seed);
            progressor.AdvanceTo(neutral, int.MaxValue, Seed, new Dictionary<int, TacticInstructions> { [club.Id] = Neutral });
            progressor.AdvanceTo(instructed, int.MaxValue, Seed, new Dictionary<int, TacticInstructions> { [club.Id] = attacking });

            int ownBefore = 0, ownAfter = 0;
            for (int i = 0; i < plain.BackgroundSeason.Fixtures.Count; i++)
            {
                Fixture p = plain.BackgroundSeason.Fixtures[i], n = neutral.BackgroundSeason.Fixtures[i], t = instructed.BackgroundSeason.Fixtures[i];
                Assert.That((n.HomeGoals, n.AwayGoals), Is.EqualTo((p.HomeGoals, p.AwayGoals)), "a neutral instruction is no instruction");

                bool involved = p.HomeClubId == club.Id || p.AwayClubId == club.Id;
                if (!involved)
                {
                    Assert.That((t.HomeGoals, t.AwayGoals), Is.EqualTo((p.HomeGoals, p.AwayGoals)), "other clubs' fixtures are untouched");
                    continue;
                }

                ownBefore += p.HomeClubId == club.Id ? p.HomeGoals : p.AwayGoals;
                ownAfter += t.HomeClubId == club.Id ? t.HomeGoals : t.AwayGoals;
            }

            Assert.That(ownAfter, Is.GreaterThan(ownBefore), "the instructed club scores more over a season");
        }

        private static World Generate()
        {
            var scope = new WorldScope { Size = DatabaseSize.Medium };
            scope.Playable.Add(new PlayableNation { Code = "ITA", PlayableTiers = 1 });
            return new WorldGenerator(new WorldGenerationOptions { Scope = scope }, new BalanceConfig()).Generate(Seed);
        }

        private static IEnumerable<int[]> Tables(WorldBalance w) => new[]
        {
            w.QuickMentalityGoalsForPercent, w.QuickMentalityGoalsAgainstPercent,
            w.QuickPressingGoalsForPercent, w.QuickPressingGoalsAgainstPercent,
            w.QuickTempoGoalsForPercent, w.QuickTempoGoalsAgainstPercent,
            w.QuickWidthGoalsForPercent, w.QuickWidthGoalsAgainstPercent
        };
    }
}
