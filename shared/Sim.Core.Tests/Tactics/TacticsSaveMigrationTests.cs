using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Tactics
{
    /// <summary>
    /// Issue #96 (spec R23): the v18 -> v19 save migration of the stored v11 tactics. The fixtures
    /// are the tactic-bearing part of v18 career saves (the world graph omitted), written as the
    /// client stored them: a <see cref="TacticPlan"/> with the formation and the four v11 axes, the
    /// familiarity map keyed by <see cref="TacticPlan.Key"/>, and the prematch plan.
    /// </summary>
    [TestFixture]
    public class TacticsSaveMigrationTests
    {
        private const int FamMax = 20;

        private static readonly string[] Fixtures =
        {
            "v18-attacking-4231.json", "v18-defensive-532.json", "v18-no-tactic.json"
        };

        private sealed class StoredTactics
        {
            public int SaveVersion;
            public TacticPlan? UserTactic;
            public Dictionary<string, int> TacticFamiliarity = new Dictionary<string, int>();
            public PrematchPlan? UserPlan;
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FootballTeamSimulator.sln")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new DirectoryNotFoundException("Repo root containing FootballTeamSimulator.sln not found");
        }

        private static StoredTactics Load(string fixture)
        {
            string path = Path.Combine(FindRepoRoot(), "shared", "Sim.Core.Tests", "Tactics", "Fixtures", fixture);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = doc.RootElement;
            return new StoredTactics
            {
                SaveVersion = root.GetProperty("SaveVersion").GetInt32(),
                UserTactic = root.GetProperty("UserTactic").Deserialize<TacticPlan>(),
                TacticFamiliarity = root.GetProperty("TacticFamiliarity").Deserialize<Dictionary<string, int>>()!,
                UserPlan = root.GetProperty("UserPlan").Deserialize<PrematchPlan>()
            };
        }

        private static int Familiarity(Dictionary<string, int> map, TacticPlan plan) =>
            map.TryGetValue(plan.Key(), out int v) ? v : 0;

        [TestCaseSource(nameof(Fixtures))]
        public void Fixture_IsAV18Save(string fixture)
        {
            Assert.That(Load(fixture).SaveVersion, Is.EqualTo(TacticsSaveMigration.FromSaveVersion));
            Assert.That(TacticsSaveMigration.ToSaveVersion, Is.EqualTo(TacticsSaveMigration.FromSaveVersion + 1));
        }

        [TestCaseSource(nameof(Fixtures))]
        public void Tactic_MigratesToCatalogueFormationAndBalancedStyleWithOverrides_Losslessly(string fixture)
        {
            StoredTactics stored = Load(fixture);
            TacticPlan? migrated = TacticsSaveMigration.MigrateTactic(stored.UserTactic);

            if (stored.UserTactic == null)
            {
                Assert.That(migrated, Is.Null, "no stored tactic stays no tactic (the neutral, untouched path)");
                return;
            }

            TacticPlan v11 = stored.UserTactic;
            var expected = new Tactic(v11.Formation,
                TacticInstructions.FromV11(v11.Mentality, v11.Pressing, v11.Tempo, v11.Width));

            Assert.That(migrated, Is.Not.Null);
            Assert.That(Formations.Catalogue, Does.Contain(migrated!.Formation));
            Assert.That(migrated.Formation, Is.EqualTo(v11.Formation));
            Assert.That(migrated.Style, Is.EqualTo(PlayingStyle.Balanced));

            StylePlan style = migrated.ToStylePlan();
            Assert.That(style.Style, Is.EqualTo(PlayingStyle.Balanced));
            int changedV11Axes = (v11.Mentality != Mentality.Balanced ? 1 : 0) + (v11.Pressing != Pressing.Medium ? 1 : 0)
                                 + (v11.Tempo != Tempo.Normal ? 1 : 0) + (v11.Width != Width.Normal ? 1 : 0);
            Assert.That(style.Overrides.Count(), Is.EqualTo(changedV11Axes),
                "the overrides are exactly the v11 axes that differ from Balanced");
            Assert.That(style.ToTactic(migrated.Formation), Is.EqualTo(expected), "formation + style + overrides replay the v11 tactic");
            Assert.That(migrated.ToTactic(), Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(Fixtures))]
        public void MigratedTactic_RoundTripsThroughJson(string fixture)
        {
            TacticPlan? migrated = TacticsSaveMigration.MigrateTactic(Load(fixture).UserTactic);
            if (migrated == null) return;

            TacticPlan back = JsonSerializer.Deserialize<TacticPlan>(JsonSerializer.Serialize(migrated))!;
            Assert.That(back.Style, Is.EqualTo(migrated.Style));
            Assert.That(back.ToTactic(), Is.EqualTo(migrated.ToTactic()));
            Assert.That(back.Key(), Is.EqualTo(migrated.Key()));
        }

        [TestCaseSource(nameof(Fixtures))]
        public void Familiarity_SurvivesMigration_ForEveryStoredTactic(string fixture)
        {
            StoredTactics stored = Load(fixture);
            Dictionary<string, int> migrated = TacticsSaveMigration.MigrateFamiliarity(stored.TacticFamiliarity);

            Assert.That(migrated.Count, Is.EqualTo(stored.TacticFamiliarity.Count));
            foreach (KeyValuePair<string, int> entry in stored.TacticFamiliarity)
            {
                TacticPlan v11 = ParseV11Key(entry.Key);
                TacticPlan plan = TacticsSaveMigration.MigrateTactic(v11)!;
                Assert.That(Familiarity(migrated, plan), Is.EqualTo(entry.Value), $"familiarity of {entry.Key}");
            }

            if (stored.UserTactic != null)
            {
                TacticPlan user = TacticsSaveMigration.MigrateTactic(stored.UserTactic)!;
                Assert.That(Familiarity(migrated, user), Is.EqualTo(stored.TacticFamiliarity[stored.UserTactic.Key()]),
                    "the user's own tactic keeps its familiarity");
            }
        }

        [TestCaseSource(nameof(Fixtures))]
        public void PrematchPlanAndShouts_SurviveMigration(string fixture)
        {
            StoredTactics stored = Load(fixture);
            PrematchPlan? migrated = TacticsSaveMigration.MigratePlan(stored.UserPlan);

            if (stored.UserPlan == null)
            {
                Assert.That(migrated, Is.Null);
                return;
            }

            Assert.That(migrated, Is.Not.SameAs(stored.UserPlan));
            Assert.That(migrated!.Rules.Select(Describe), Is.EqualTo(stored.UserPlan.Rules.Select(Describe)));
            Assert.That(migrated.Rules.All(r => r.IsValid()), Is.True);
            Assert.That(migrated.Rules.Count(r => r.Shout != TouchlineShout.None),
                Is.EqualTo(stored.UserPlan.Rules.Count(r => r.Shout != TouchlineShout.None)).And.GreaterThan(0));

            // The match sees the same rules: same instructions, familiarity (looked up the host's way,
            // by the in-match formation) and shouts, before and after the migration.
            Club club = new LeagueGenerator().Generate(new Pcg32(20260618)).Clubs[0];
            Formation before = stored.UserTactic?.Formation ?? Formation.F433;
            Formation after = TacticsSaveMigration.MigrateTactic(stored.UserTactic)?.Formation ?? Formation.F433;
            Dictionary<string, int> fam = TacticsSaveMigration.MigrateFamiliarity(stored.TacticFamiliarity);

            List<MatchRule> old = stored.UserPlan.Resolve(club,
                i => Familiarity(stored.TacticFamiliarity, TacticPlan.FromTactic(new Tactic(before, i))), FamMax);
            List<MatchRule> now = migrated.Resolve(club,
                i => Familiarity(fam, TacticPlan.FromTactic(new Tactic(after, i))), FamMax);

            Assert.That(now.Select(Describe), Is.EqualTo(old.Select(Describe)));
            Assert.That(now.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Tactic_WithAFormationOutsideTheCatalogue_FallsBackTo433_AndItsFamiliarityFollows()
        {
            var stray = new TacticPlan { Formation = (Formation)99, Mentality = Mentality.Attacking };
            TacticPlan migrated = TacticsSaveMigration.MigrateTactic(stray)!;
            Assert.That(migrated.Formation, Is.EqualTo(Formation.F433));
            Assert.That(migrated.Mentality, Is.EqualTo(Mentality.Attacking));

            Dictionary<string, int> fam = TacticsSaveMigration.MigrateFamiliarity(
                new Dictionary<string, int> { ["99:2111"] = 5, ["0:2111"] = 11 });
            Assert.That(fam, Is.EqualTo(new Dictionary<string, int> { ["0:2111"] = 11 }),
                "two keys merging onto one tactic keep the higher familiarity");
        }

        [Test]
        public void Familiarity_KeyThatIsNotAV11Key_IsKeptVerbatim()
        {
            var stored = new Dictionary<string, int> { ["custom"] = 4 };
            Assert.That(TacticsSaveMigration.MigrateFamiliarity(stored), Is.EqualTo(stored));
        }

        [Test]
        public void Key_TellsApartPlansThatDifferOnlyInAnExpandedAxis_AndKeepsTheV11KeyOtherwise()
        {
            var v11 = new Tactic(Formation.F442,
                TacticInstructions.FromV11(Mentality.Attacking, Pressing.Low, Tempo.Fast, Width.Normal));
            TacticPlan plain = TacticPlan.FromTactic(v11);
            TacticPlan tweaked = TacticPlan.FromTactic(v11);
            tweaked.DefensiveLine = DefensiveLine.VeryHigh;

            Assert.That(plain.Key(), Is.EqualTo($"{(int)Formation.F442}:2021"));
            Assert.That(tweaked.Key(), Is.Not.EqualTo(plain.Key()));
            Assert.That(tweaked.Key(), Does.StartWith(plain.Key() + ":"));

            TacticPlan other = TacticPlan.FromTactic(v11);
            other.MarkingScheme = MarkingScheme.ManOriented;
            Assert.That(other.Key(), Is.Not.EqualTo(tweaked.Key()));
        }

        [Test]
        public void Key_IgnoresTheStyleLabel()
        {
            TacticPlan a = TacticPlan.FromStyle(Formation.F433, StylePlan.Of(PlayingStyle.Gegenpress));
            TacticPlan b = TacticPlan.FromTactic(a.ToTactic());
            Assert.That(b.Style, Is.EqualTo(PlayingStyle.Balanced));
            Assert.That(a.Key(), Is.EqualTo(b.Key()), "familiarity follows what is played, not its name");
        }

        [Test]
        public void FromStyle_ThenToStylePlan_KeepsStyleAndOverrides()
        {
            StylePlan tweaked = StylePlan.FromInstructions(PlayingStyle.Gegenpress, new TacticInstructions(
                Mentality.Attacking, PassingDirectness.Direct, Tempo.Fast, Width.Wide,
                BuildUp.Mixed, FinalThirdApproach.Mixed, ShotSelection.Normal, DribbleFrequency.Normal,
                OnBallLost.CounterPress, OnBallWon.Counter,
                LineOfEngagement.High, DefensiveLine.VeryHigh, Pressing.High,
                MarkingScheme.Zonal, PressDirection.ForceInside));

            TacticPlan plan = TacticPlan.FromStyle(Formation.F4231, tweaked);
            StylePlan back = plan.ToStylePlan();

            Assert.That(plan.Formation, Is.EqualTo(Formation.F4231));
            Assert.That(back.Style, Is.EqualTo(PlayingStyle.Gegenpress));
            Assert.That(back.Overrides.Count(), Is.EqualTo(1));
            Assert.That(back.Overrides.Width, Is.EqualTo(Width.Wide));
            Assert.That(plan.ToTactic(), Is.EqualTo(tweaked.ToTactic(Formation.F4231)));
        }

        [Test]
        public void Copy_KeepsEveryAxisAndTheStyle()
        {
            TacticPlan plan = TacticPlan.FromStyle(Formation.F3412, StylePlan.Of(PlayingStyle.ManOrientedPress));
            TacticPlan copy = plan.Copy();

            Assert.That(copy, Is.Not.SameAs(plan));
            Assert.That(copy.Style, Is.EqualTo(PlayingStyle.ManOrientedPress));
            Assert.That(copy.ToTactic(), Is.EqualTo(plan.ToTactic()));
            copy.Width = Width.Narrow;
            Assert.That(plan.Width, Is.EqualTo(Width.Wide), "the copy is independent");
        }

        private static TacticPlan ParseV11Key(string key)
        {
            string[] parts = key.Split(':');
            string axes = parts[1];
            return new TacticPlan
            {
                Formation = (Formation)int.Parse(parts[0]),
                Mentality = (Mentality)(axes[0] - '0'),
                Pressing = (Pressing)(axes[1] - '0'),
                Tempo = (Tempo)(axes[2] - '0'),
                Width = (Width)(axes[3] - '0')
            };
        }

        private static string Describe(PrematchRule r) =>
            $"{r.FromMinute}|{r.When}|{r.Margin}|{r.ChangeInstructions}|{r.Mentality}{r.Pressing}{r.Tempo}{r.Width}|{r.SubOutPlayerId}>{r.SubInPlayerId}|{r.Shout}";

        private static string Describe(MatchRule r) =>
            $"{r.FromMinute}|{r.When}|{r.Margin}|{(r.Action.Instructions.HasValue ? r.Action.Instructions.Value.GetHashCode().ToString() : "-")}" +
            $"|{r.Action.InstructionFamiliarity}|{r.Action.Sub?.OutPlayerId}|{r.Action.Shout}";
    }
}
