using System.Linq;
using NUnit.Framework;
using Sim.Core.Match.Analysis;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>The bookkeeping behind the R8 tactic tournament: schedule, points shares, worst matchups.</summary>
    [TestFixture]
    public class TournamentTableTests
    {
        [Test]
        public void Pairings_AreEveryUnorderedPairOnce_InOrder()
        {
            var pairs = TournamentTable.Pairings(4);

            Assert.That(pairs, Is.EqualTo(new[] { (0, 1), (0, 2), (0, 3), (1, 2), (1, 3), (2, 3) }));
            Assert.That(TournamentTable.Pairings(6).Count, Is.EqualTo(15));
            Assert.That(TournamentTable.Pairings(1), Is.Empty);
        }

        [Test]
        public void PointsShare_IsPointsOverThreePerGame()
        {
            var t = new TournamentTable(3);
            t.Add(0, 1, 2, 0);   // 0 wins
            t.Add(1, 0, 1, 1);   // draw
            t.Add(0, 2, 0, 1);   // 2 wins

            Assert.That(t.Points(0), Is.EqualTo(4));
            Assert.That(t.Games(0), Is.EqualTo(3));
            Assert.That(t.PointsShare(0), Is.EqualTo(4.0 / 9).Within(1e-12));
            Assert.That(t.PointsShare(1), Is.EqualTo(1.0 / 6).Within(1e-12));
            Assert.That(t.PointsShare(2), Is.EqualTo(1.0).Within(1e-12));
            Assert.That(t.ShareAgainst(0, 1), Is.EqualTo(4.0 / 6).Within(1e-12));
            Assert.That(t.ShareAgainst(1, 0), Is.EqualTo(1.0 / 6).Within(1e-12));
            Assert.That(t.ShareAgainst(0, 2), Is.EqualTo(0.0));
        }

        [Test]
        public void AnUnplayedPreset_HasNoShare_AndIsNotAnyonesWorstOpponent()
        {
            var t = new TournamentTable(3);
            t.Add(0, 1, 0, 3);

            Assert.That(t.PointsShare(2), Is.EqualTo(0.0));
            Assert.That(t.WorstOpponent(0), Is.EqualTo(1));
            Assert.That(t.WorstOpponent(2), Is.EqualTo(-1));
        }

        [Test]
        public void WorstOpponent_IsWhereThePresetTakesLeast_TiesToTheLowerIndex()
        {
            var t = new TournamentTable(4);
            t.Add(0, 1, 1, 0);
            t.Add(0, 2, 0, 0);
            t.Add(0, 3, 0, 0);

            Assert.That(t.WorstOpponent(0), Is.EqualTo(2));
            Assert.That(t.WorstOpponent(1), Is.EqualTo(0));
        }

        [Test]
        public void WorstMatchup_IsTheLowestShareOfAnyPresetAgainstAnyOpponent()
        {
            var t = new TournamentTable(3);
            t.Add(0, 1, 1, 1);
            t.Add(0, 2, 2, 1);
            t.Add(0, 2, 0, 0);   // 0 takes 4/6 of 2, so 2 takes 1/6 of 0
            t.Add(1, 2, 0, 0);

            TournamentMatchup worst = t.WorstMatchup();

            Assert.That(worst.Preset, Is.EqualTo(2));
            Assert.That(worst.Opponent, Is.EqualTo(0));
            Assert.That(worst.Share, Is.EqualTo(1.0 / 6).Within(1e-12));
        }

        [Test]
        public void Format_NamesEveryPreset_ItsShare_AndTheWorstMatchup()
        {
            var t = new TournamentTable(2);
            t.Add(0, 1, 3, 0);
            t.Add(1, 0, 1, 1);

            string text = t.Format("V10", new[] { "alpha", "beta" });

            Assert.That(text, Does.Contain("V10"));
            Assert.That(text, Does.Contain("alpha"));
            Assert.That(text, Does.Contain("66.7%"));   // alpha: 4 of 6
            Assert.That(text, Does.Contain("16.7%"));   // beta: 1 of 6
            Assert.That(text, Does.Contain("worst matchup: beta takes 16.7% vs alpha"));
        }

        [Test]
        public void Presets_CoverEveryFormation_WithDistinctNamesAndTactics()
        {
            var presets = TacticPresets.All;

            Assert.That(presets.Select(p => p.Tactic.Formation), Is.EquivalentTo(Formations.All));
            Assert.That(presets.Select(p => p.Name).Distinct().Count(), Is.EqualTo(presets.Count));
            Assert.That(presets.Select(p => p.Tactic).Distinct().Count(), Is.EqualTo(presets.Count));
        }
    }
}
