using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.WorldGen
{
    /// <summary>
    /// R22 (real-match-and-playing-styles.md): AI clubs pick a formation and a style that suit their
    /// squad and their manager, and a generated league shows a varied, tier-coherent mix.
    /// </summary>
    [TestFixture]
    public class AiStyleChoiceTests
    {
        private const ulong Seed = 20261022UL;
        private const int MaxStyleSharePercent = 30;
        private const int MinStylesInUse = 6;

        /// <summary>Bottom tier's mean style directness over the top tier's, in hundredths of a trait point.</summary>
        private const int MinDirectnessGap = 75;

        private static readonly BalanceConfig Config = new BalanceConfig();
        private static readonly ManagerProfile NeutralProfile = new ManagerProfile();

        private World _world = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _world = Generate(Seed);
        }

        private static World Generate(ulong seed)
        {
            var scope = new WorldScope { Size = DatabaseSize.Medium };
            scope.Playable.Add(new PlayableNation { Code = "ITA", PlayableTiers = 2 });
            scope.Playable.Add(new PlayableNation { Code = "ENG", PlayableTiers = 2 });
            return new WorldGenerator(new WorldGenerationOptions { Scope = scope }, Config).Generate(seed);
        }

        private static bool IsDirect(PlayingStyle style) =>
            PlayingStyles.Get(style).Instructions.PassingDirectness == PassingDirectness.Direct;

        // ------------------------------------------------------------ world distribution

        [TestCase(Seed)]
        [TestCase(1UL)]
        [TestCase(777UL)]
        public void EveryLeague_UsesAtLeastSixStyles_AndNoStyleAboveThirtyPercent(ulong seed)
        {
            List<League> leagues = Generate(seed).AllLeagues().Where(l => l.Clubs.Count >= 10).ToList();
            Assert.That(leagues.Count, Is.GreaterThan(10), "the test world must have many full leagues");

            foreach (League league in leagues)
            {
                var counts = league.Clubs.GroupBy(c => c.Style).ToDictionary(g => g.Key, g => g.Count());
                TestContext.Out.WriteLine($"{league.Name} (tier {league.Division}, {league.Clubs.Count} clubs): " +
                    string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));

                Assert.That(counts.Count, Is.GreaterThanOrEqualTo(MinStylesInUse), $"{league.Name}: styles in use");
                foreach (var kv in counts)
                    Assert.That(kv.Value * 100, Is.LessThanOrEqualTo(MaxStyleSharePercent * league.Clubs.Count),
                        $"{league.Name}: {kv.Key} used by {kv.Value}/{league.Clubs.Count}");
            }
        }

        [TestCase(Seed)]
        [TestCase(1UL)]
        [TestCase(777UL)]
        public void LowerTiers_LeanTowardDirectStyles(ulong seed)
        {
            World world = Generate(seed);
            // Nations with a full pyramid loaded (the playable ones), so every tier compares like with like.
            var pyramids = new HashSet<string>(world.AllLeagues().Where(l => l.Division >= 3).Select(l => l.NationCode));
            var byTier = world.AllLeagues()
                .Where(l => pyramids.Contains(l.NationCode))
                .GroupBy(l => l.Division)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    List<Club> clubs = g.SelectMany(l => l.Clubs).ToList();
                    int directLeaning = clubs.Count(c => Directness(c.Style) > 0);
                    int meanDirectness = clubs.Sum(c => Directness(c.Style)) * 100 / clubs.Count;
                    TestContext.Out.WriteLine($"tier {g.Key}: {directLeaning}/{clubs.Count} direct-leaning, mean directness {meanDirectness / 100.0:0.00}; " +
                        string.Join(", ", clubs.GroupBy(c => c.Style).OrderByDescending(s => s.Count()).Select(s => $"{s.Key} {s.Count()}")) +
                        " | formations: " +
                        string.Join(", ", clubs.GroupBy(c => c.Formation).OrderByDescending(s => s.Count()).Select(s => $"{Formations.Name(s.Key)} {s.Count()}")));
                    return (Tier: g.Key, Mean: meanDirectness, Share: directLeaning * 1000 / clubs.Count);
                })
                .ToList();

            Assert.That(byTier.Count, Is.GreaterThanOrEqualTo(3), "need at least three tiers to compare");
            for (int i = 1; i < byTier.Count; i++)
                Assert.That(byTier[i].Mean, Is.GreaterThanOrEqualTo(byTier[i - 1].Mean),
                    $"tier {byTier[i].Tier} must be at least as direct as tier {byTier[i - 1].Tier}");
            Assert.That(byTier[byTier.Count - 1].Mean, Is.GreaterThanOrEqualTo(byTier[0].Mean + MinDirectnessGap),
                "the bottom tier must be clearly more direct than the top tier");
            Assert.That(byTier[byTier.Count - 1].Share, Is.GreaterThan(byTier[0].Share),
                "more bottom-tier than top-tier clubs play a direct-leaning style");
        }

        [Test]
        public void EveryClub_LinesUpInOneOfItsStylesSuggestedFormations()
        {
            foreach (Club club in _world.AllLeagues().SelectMany(l => l.Clubs))
                Assert.That(PlayingStyles.Get(club.Style).SuggestedFormations, Does.Contain(club.Formation),
                    $"{club.Name}: {club.Formation} with {club.Style}");
        }

        [Test]
        public void SameSeed_GivesSameProfilesStylesAndFormations()
        {
            World again = Generate(Seed);
            List<Club> a = _world.AllLeagues().SelectMany(l => l.Clubs).ToList();
            List<Club> b = again.AllLeagues().SelectMany(l => l.Clubs).ToList();

            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].Style, Is.EqualTo(a[i].Style), a[i].Name);
                Assert.That(b[i].Formation, Is.EqualTo(a[i].Formation), a[i].Name);
                Assert.That(b[i].Coach.Profile.Attack, Is.EqualTo(a[i].Coach.Profile.Attack), a[i].Name);
                Assert.That(b[i].Coach.Profile.Possession, Is.EqualTo(a[i].Coach.Profile.Possession), a[i].Name);
                Assert.That(b[i].Coach.Profile.Pressing, Is.EqualTo(a[i].Coach.Profile.Pressing), a[i].Name);
            }
        }

        [Test]
        public void ManagerProfiles_AreVaried()
        {
            List<Coach> coaches = _world.AllLeagues().SelectMany(l => l.Clubs).Select(c => c.Coach).ToList();
            Assert.That(coaches.Select(c => (c.Profile.Attack, c.Profile.Possession, c.Profile.Pressing)).Distinct().Count(),
                Is.GreaterThan(20));
            Assert.That(coaches.All(c => c.Profile.Attack >= -2 && c.Profile.Attack <= 2
                                         && c.Profile.Possession >= -2 && c.Profile.Possession <= 2
                                         && c.Profile.Pressing >= -2 && c.Profile.Pressing <= 2), Is.True);
        }

        // ------------------------------------------------------------ squad and manager drive the choice

        private static SquadStrengths Squad(int passing, int forwards, int stamina, int defending, int overall) =>
            new SquadStrengths(passing, forwards, stamina, defending, overall, overall);

        /// <summary>How direct a style's possession game is: passing + build-up + tempo, in [-3, 3].</summary>
        private static int Directness(PlayingStyle style)
        {
            TacticInstructions t = PlayingStyles.Get(style).Instructions;
            return (int)t.PassingDirectness + (int)t.BuildUp + (int)t.Tempo - 3;
        }

        private static PlayingStyle Best(SquadStrengths squad, SquadStrengths mean, ManagerProfile profile) =>
            PlayingStyles.All.Select(d => d.Style)
                .OrderByDescending(s => AiStyleChooser.Score(s, squad, mean, profile))
                .ThenBy(s => (int)s)
                .First();

        [Test]
        public void StrongPassers_WeakForwards_PickAShortPassingStyle()
        {
            SquadStrengths mean = Squad(65, 65, 65, 65, 65);
            PlayingStyle best = Best(Squad(80, 55, 65, 65, 66), mean, NeutralProfile);
            Assert.That(PlayingStyles.Get(best).Instructions.PassingDirectness, Is.EqualTo(PassingDirectness.Short), best.ToString());
        }

        [Test]
        public void FastStrongForwards_WeakPassers_PickADirectStyle()
        {
            SquadStrengths mean = Squad(65, 65, 65, 65, 65);
            PlayingStyle best = Best(Squad(50, 80, 65, 65, 64), mean, NeutralProfile);
            Assert.That(IsDirect(best), Is.True, best.ToString());
        }

        [Test]
        public void TikiTaka_IsNeverPicked_WithWeakPassers_EvenByAPossessionManager()
        {
            var possessionManager = new ManagerProfile { Attack = 2, Possession = 2, Pressing = 2 };
            SquadStrengths mean = Squad(48, 48, 48, 48, 48);
            SquadStrengths weakPassers = Squad(48, 48, 48, 48, 48);

            Assert.That(AiStyleChooser.Score(PlayingStyle.TikiTaka, weakPassers, mean, possessionManager),
                Is.EqualTo(int.MinValue));
            Assert.That(Best(weakPassers, mean, possessionManager), Is.Not.EqualTo(PlayingStyle.TikiTaka));
        }

        [Test]
        public void ManagerProfile_ShiftsTheChoice_ForTheSameSquad()
        {
            SquadStrengths average = Squad(76, 76, 76, 76, 76);
            var possession = new ManagerProfile { Attack = 1, Possession = 2, Pressing = 0 };
            var direct = new ManagerProfile { Attack = -1, Possession = -2, Pressing = -1 };

            PlayingStyle a = Best(average, average, possession);
            PlayingStyle b = Best(average, average, direct);

            Assert.That(PlayingStyles.Get(a).Instructions.PassingDirectness, Is.EqualTo(PassingDirectness.Short), a.ToString());
            Assert.That(Directness(b), Is.GreaterThan(0), b.ToString());
        }

        [Test]
        public void SquadStrengths_ReadPassersFromMidfieldAndPhysiqueFromForwards()
        {
            League league = new LeagueGenerator(new LeagueGenerationOptions { ClubCount = 2 }, Config).Generate(new Pcg32(Seed));
            Club club = league.Clubs[0];
            foreach (Player p in club.Squad.Players)
            {
                bool mid = p.Role == PositionRole.DefensiveMidfielder || p.Role == PositionRole.CentralMidfielder
                           || p.Role == PositionRole.AttackingMidfielder;
                bool fwd = p.Role == PositionRole.Winger || p.Role == PositionRole.Striker;
                if (mid) { p.Attributes.Passing = 90; p.Attributes.Technique = 90; }
                if (fwd) { p.Attributes.Pace = 30; p.Attributes.Strength = 30; }
            }

            SquadStrengths s = SquadStrengths.Of(club);

            Assert.That(s.Passing, Is.EqualTo(90));
            Assert.That(s.Forwards, Is.EqualTo(30));
        }

        [TestCase(Seed)]
        [TestCase(1UL)]
        [TestCase(777UL)]
        public void BestFormation_IsTheSuggestedShapeTheSquadFillsBest(ulong seed)
        {
            League league = new LeagueGenerator(new LeagueGenerationOptions { ClubCount = 2 }, Config).Generate(new Pcg32(seed));
            Club club = league.Clubs[0];
            // Man-oriented press suggests 3-4-3 and 3-5-2: a squad with no wingers fills the 3-5-2.
            club.Squad.Players.RemoveAll(p => p.Role == PositionRole.Winger);
            Assert.That(AiStyleChooser.BestFormation(club, PlayingStyle.ManOrientedPress), Is.EqualTo(Formation.F352));

            // Catenaccio suggests 5-3-2 first and 3-5-2: with no full-backs and only three
            // centre-backs, the 5-3-2 needs two players out of position and the 3-5-2 fits better.
            Club other = league.Clubs[1];
            other.Squad.Players.RemoveAll(p => p.Role == PositionRole.FullBack);
            List<Player> centreBacks = other.Squad.Players.Where(p => p.Role == PositionRole.CentreBack).ToList();
            foreach (Player extra in centreBacks.Skip(3)) other.Squad.Players.Remove(extra);
            Assert.That(AiStyleChooser.BestFormation(other, PlayingStyle.Catenaccio), Is.EqualTo(Formation.F352));
        }
    }
}
