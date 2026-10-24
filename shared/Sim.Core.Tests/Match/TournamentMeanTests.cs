using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Match.Analysis;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The R14 tournament gate (real-match spec): the R8 table read as the mean over several seed
    /// sets, so one coin-flip seed set can neither pass nor fail it. Every expected value is worked
    /// out on paper from 3 points a win, 1 a draw.
    /// </summary>
    [TestFixture]
    public class TournamentMeanTests
    {
        private const double MaxPointsShare = 0.55;
        private const double MaxWorstMatchupShare = 0.45;

        [Test]
        public void Shares_AreTheMeanOfEachSeedSetsShares()
        {
            var a = new TournamentTable(2);
            a.Add(0, 1, 3, 0);   // 0: 4 of 6
            a.Add(0, 1, 1, 1);
            var b = new TournamentTable(2);
            b.Add(1, 0, 2, 0);   // 0: 1 of 6
            b.Add(0, 1, 0, 0);
            var c = new TournamentTable(2);
            c.Add(0, 1, 0, 0);   // 0: 2 of 6
            c.Add(0, 1, 2, 2);

            var mean = new TournamentMean(new[] { a, b, c });

            Assert.That(mean.SeedSets, Is.EqualTo(3));
            Assert.That(mean.Presets, Is.EqualTo(2));
            Assert.That(mean.PointsShare(0), Is.EqualTo((4.0 / 6 + 1.0 / 6 + 2.0 / 6) / 3).Within(1e-12));
            Assert.That(mean.PointsShare(1), Is.EqualTo((1.0 / 6 + 4.0 / 6 + 2.0 / 6) / 3).Within(1e-12));
            Assert.That(mean.ShareAgainst(0, 1), Is.EqualTo(7.0 / 18).Within(1e-12));
        }

        [Test]
        public void TheMean_IsOfShares_NotOfPooledPoints()
        {
            var a = new TournamentTable(2);
            a.Add(0, 1, 1, 0);   // 0: 3 of 3 = 100%
            var b = new TournamentTable(2);
            b.Add(0, 1, 0, 1);   // 0: 0 of 9 = 0%
            b.Add(0, 1, 0, 1);
            b.Add(0, 1, 0, 1);

            var mean = new TournamentMean(new[] { a, b });

            // Pooled it would read 3 of 12 = 25%; each seed set weighs the same.
            Assert.That(mean.PointsShare(0), Is.EqualTo(0.5).Within(1e-12));
        }

        [Test]
        public void WorstOpponent_IsReadOnTheMean_NotOnOneSeedSet()
        {
            var a = new TournamentTable(3);
            a.Add(0, 1, 0, 1);   // seed set a alone: 0 takes nothing from 1
            a.Add(0, 2, 1, 1);
            var b = new TournamentTable(3);
            b.Add(0, 1, 1, 0);
            b.Add(0, 2, 1, 1);
            var c = new TournamentTable(3);
            c.Add(0, 1, 1, 0);
            c.Add(0, 2, 1, 1);

            var mean = new TournamentMean(new[] { a, b, c });

            Assert.That(a.WorstOpponent(0), Is.EqualTo(1));
            Assert.That(mean.ShareAgainst(0, 1), Is.EqualTo(2.0 / 3).Within(1e-12));
            Assert.That(mean.ShareAgainst(0, 2), Is.EqualTo(1.0 / 3).Within(1e-12));
            Assert.That(mean.WorstOpponent(0), Is.EqualTo(2));
        }

        [Test]
        public void WorstMatchup_IsTheLowestMeanShareOfAnyPresetAgainstAnyOpponent()
        {
            var a = new TournamentTable(3);
            a.Add(0, 1, 1, 1);
            a.Add(0, 2, 2, 0);
            a.Add(1, 2, 0, 0);
            var b = new TournamentTable(3);
            b.Add(0, 1, 1, 1);
            b.Add(0, 2, 0, 0);
            b.Add(1, 2, 0, 0);

            TournamentMatchup worst = new TournamentMean(new[] { a, b }).WorstMatchup();

            // 2 takes 0 of 3 from 0 in a and 1 of 3 in b: a mean of 1/6.
            Assert.That(worst.Preset, Is.EqualTo(2));
            Assert.That(worst.Opponent, Is.EqualTo(0));
            Assert.That(worst.Share, Is.EqualTo(1.0 / 6).Within(1e-12));
        }

        [Test]
        public void Gate_PassesOnTheMean_WhenOneSeedSetAloneWouldFailIt()
        {
            var a = new TournamentTable(3);   // 0 wins everything: 100% of its points
            a.Add(0, 1, 1, 0);
            a.Add(0, 2, 1, 0);
            a.Add(1, 2, 1, 0);
            var b = new TournamentTable(3);   // the mirror: 0 takes nothing
            b.Add(0, 1, 0, 1);
            b.Add(0, 2, 0, 1);
            b.Add(1, 2, 0, 1);
            var c = new TournamentTable(3);   // all drawn
            c.Add(0, 1, 0, 0);
            c.Add(0, 2, 0, 0);
            c.Add(1, 2, 0, 0);

            var mean = new TournamentMean(new[] { a, b, c });

            Assert.That(a.PointsShare(0), Is.GreaterThan(MaxPointsShare), "seed set a alone fails");
            for (int p = 0; p < 3; p++)
            {
                Assert.That(mean.PointsShare(p), Is.EqualTo(4.0 / 9).Within(1e-12), $"preset {p}");
                Assert.That(mean.ShareAgainst(p, mean.WorstOpponent(p)), Is.EqualTo(4.0 / 9).Within(1e-12), $"preset {p}");
            }

            Assert.That(mean.MeetsGate(MaxPointsShare, MaxWorstMatchupShare), Is.True);
        }

        [Test]
        public void Gate_Fails_WhenAPresetTakesTooMuchOnTheMean()
        {
            var a = new TournamentTable(2);
            a.Add(0, 1, 1, 0);
            var b = new TournamentTable(2);
            b.Add(0, 1, 1, 1);

            var mean = new TournamentMean(new[] { a, b });

            // 0: (100% + 33.3%) / 2 = 66.7% > 55%.
            Assert.That(mean.PointsShare(0), Is.EqualTo(2.0 / 3).Within(1e-12));
            Assert.That(mean.MeetsGate(MaxPointsShare, MaxWorstMatchupShare), Is.False);
        }

        [Test]
        public void Gate_Fails_WhenAPresetTakesAtLeastTheWorstShareFromEveryOpponent()
        {
            var a = new TournamentTable(2);
            a.Add(0, 1, 1, 0);
            a.Add(0, 1, 0, 1);
            var b = new TournamentTable(2);
            b.Add(0, 1, 2, 0);
            b.Add(0, 1, 0, 2);

            var mean = new TournamentMean(new[] { a, b });

            // One win each in every seed set: 50% each, under 55%, but no one is under 45% vs anyone.
            Assert.That(mean.PointsShare(0), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(mean.ShareAgainst(0, 1), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(mean.MeetsGate(MaxPointsShare, MaxWorstMatchupShare), Is.False);
        }

        [Test]
        public void Breaches_NameThePresetAboveTheMaxPointsShare_WithItsMeanShare()
        {
            var a = new TournamentTable(2);
            a.Add(0, 1, 1, 0);
            var b = new TournamentTable(2);
            b.Add(0, 1, 1, 1);

            IReadOnlyList<TournamentBreach> breaches = new TournamentMean(new[] { a, b })
                .Breaches(MaxPointsShare, MaxWorstMatchupShare);

            // 0 takes (100% + 33.3%) / 2 = 66.7%, also from its only opponent; 1 takes 16.7%, under 45% vs 0.
            Assert.That(breaches, Has.Count.EqualTo(2));
            Assert.That(breaches[0].Preset, Is.EqualTo(0));
            Assert.That(breaches[0].Check, Is.EqualTo(TournamentCheck.PointsShare));
            Assert.That(breaches[0].Value, Is.EqualTo(2.0 / 3).Within(1e-12));
            Assert.That(breaches[0].Limit, Is.EqualTo(MaxPointsShare));
            Assert.That(breaches[0].Opponent, Is.EqualTo(-1));
            Assert.That(breaches[1].Preset, Is.EqualTo(0));
            Assert.That(breaches[1].Check, Is.EqualTo(TournamentCheck.WorstMatchup));
            Assert.That(breaches[1].Opponent, Is.EqualTo(1));
        }

        [Test]
        public void Breaches_NameThePresetThatTakesAtLeastTheWorstShareFromEveryone_WithItsWorstOpponent()
        {
            var t = new TournamentTable(3);
            t.Add(0, 1, 1, 1);   // 0 vs 1: a draw and a win, 4 of 6
            t.Add(0, 1, 1, 0);
            t.Add(0, 2, 1, 1);   // 0 vs 2: a draw and a loss, 1 of 6
            t.Add(0, 2, 0, 1);
            t.Add(1, 2, 1, 0);   // 1 vs 2: a win each, 3 of 6 both ways
            t.Add(1, 2, 0, 1);

            IReadOnlyList<TournamentBreach> breaches = new TournamentMean(new[] { t }).Breaches(1.0, 0.45);

            // 0's worst is 16.7% vs 2 and 1's is 16.7% vs 0, but 2 takes 66.7% vs 0 and 50% vs 1.
            Assert.That(breaches, Has.Count.EqualTo(1));
            Assert.That(breaches[0].Preset, Is.EqualTo(2));
            Assert.That(breaches[0].Check, Is.EqualTo(TournamentCheck.WorstMatchup));
            Assert.That(breaches[0].Opponent, Is.EqualTo(1));
            Assert.That(breaches[0].Value, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(breaches[0].Limit, Is.EqualTo(0.45));
        }

        [Test]
        public void Breaches_AreEmpty_ExactlyWhenTheGateIsMet()
        {
            var a = new TournamentTable(3);
            a.Add(0, 1, 1, 0);
            a.Add(1, 2, 1, 0);
            a.Add(2, 0, 1, 0);   // rock-paper-scissors: 50% each, 0% vs one opponent

            var mean = new TournamentMean(new[] { a });

            Assert.That(mean.Breaches(MaxPointsShare, MaxWorstMatchupShare), Is.Empty);
            Assert.That(mean.MeetsGate(MaxPointsShare, MaxWorstMatchupShare), Is.True);
            Assert.That(mean.Breaches(0.49, MaxWorstMatchupShare), Has.Count.EqualTo(3));
            Assert.That(mean.MeetsGate(0.49, MaxWorstMatchupShare), Is.False);
        }

        [Test]
        public void PointsBreaches_GateOnlyTheGivenPresets_AtTheirMeanShare()
        {
            // A sweep: challengers 1 and 2 each play only the reference 0.
            var a = new TournamentTable(3);
            a.Add(1, 0, 1, 0);   // 1: 100%
            a.Add(2, 0, 1, 1);   // 2: 33.3%
            var b = new TournamentTable(3);
            b.Add(1, 0, 0, 0);   // 1: 33.3% -> mean 66.7%
            b.Add(2, 0, 2, 1);   // 2: 100%  -> mean 66.7%, but only 1 is asked for first
            var mean = new TournamentMean(new[] { a, b });

            IReadOnlyList<TournamentBreach> breaches = mean.PointsBreaches(0.60, new[] { 1 });

            Assert.That(breaches, Has.Count.EqualTo(1));
            Assert.That(breaches[0].Preset, Is.EqualTo(1));
            Assert.That(breaches[0].Check, Is.EqualTo(TournamentCheck.PointsShare));
            Assert.That(breaches[0].Value, Is.EqualTo(2.0 / 3).Within(1e-12));
            Assert.That(mean.PointsBreaches(0.60, new[] { 1, 2 }).Select(x => x.Preset), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(mean.PointsBreaches(0.67, new[] { 1, 2 }), Is.Empty);
        }

        [Test]
        public void PointsBreaches_LeaveOutAPresetThatNeverPlayed()
        {
            var a = new TournamentTable(3);
            a.Add(1, 0, 1, 0);

            Assert.That(new TournamentMean(new[] { a }).PointsBreaches(0.60, new[] { 2 }), Is.Empty);
        }

        [Test]
        public void APairingASeedSetDidNotPlay_IsLeftOutOfItsMean()
        {
            var a = new TournamentTable(3);
            a.Add(0, 1, 1, 0);
            a.Add(0, 2, 1, 0);
            var b = new TournamentTable(3);
            b.Add(0, 1, 0, 0);   // 0 never meets 2 in b

            var mean = new TournamentMean(new[] { a, b });

            Assert.That(mean.ShareAgainst(0, 2), Is.EqualTo(1.0).Within(1e-12));
            Assert.That(mean.ShareAgainst(0, 1), Is.EqualTo(2.0 / 3).Within(1e-12));
            Assert.That(mean.ShareAgainst(1, 2), Is.EqualTo(0.0));
            Assert.That(mean.WorstOpponent(1), Is.EqualTo(0));
        }

        [Test]
        public void SeedSets_MustBeGivenAndMustAgreeOnThePresets()
        {
            Assert.Throws<ArgumentNullException>(() => new TournamentMean(null!));
            Assert.Throws<ArgumentException>(() => new TournamentMean(Array.Empty<TournamentTable>()));
            Assert.Throws<ArgumentException>(() => new TournamentMean(new[] { new TournamentTable(2), new TournamentTable(3) }));
        }

        [Test]
        public void Format_NamesEachPreset_ItsMeanAndPerSeedSetShares_AndTheGate()
        {
            var a = new TournamentTable(2);
            a.Add(0, 1, 3, 0);   // alpha 100%, beta 0%
            var b = new TournamentTable(2);
            b.Add(0, 1, 1, 1);   // 33.3% each

            string text = new TournamentMean(new[] { a, b }).Format("V11", new[] { "alpha", "beta" },
                MaxPointsShare, MaxWorstMatchupShare);

            Assert.That(text, Does.Contain("V11"));
            Assert.That(text, Does.Contain("2 seed sets"));
            Assert.That(text, Does.Contain("alpha"));
            Assert.That(text, Does.Contain("66.7%"));            // alpha's mean
            Assert.That(text, Does.Contain("100.0% / 33.3%"));   // alpha per seed set
            Assert.That(text, Does.Contain("worst matchup: beta takes 16.7% vs alpha"));
            Assert.That(text, Does.Contain("gate: FAIL"));
        }
    }
}
