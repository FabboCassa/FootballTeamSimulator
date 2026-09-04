using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    [TestFixture]
    public class MatchEngineTests
    {
        private static League _league = null!;
        private static Club _top = null!;
        private static Club _bottom = null!;
        private static Club _midA = null!;
        private static Club _midB = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
            _top = _league.Clubs[0];      // generator orders clubs strongest-first
            _bottom = _league.Clubs[19];
            _midA = _league.Clubs[9];
            _midB = _league.Clubs[10];
        }

        /// <summary>
        /// One match. The position stream is OFF by default (engine phase 3): this fixture asks
        /// what the SCORE model does, and its three calibration harnesses sweep thousands of
        /// matches to ask it. Since phase 1 a match with the picture costs half a second against
        /// the result model's one millisecond, so leaving it on here was ninety-three percent of
        /// the time the whole match namespace took — a picture nothing in this file looks at.
        /// `SkippingTheStream_LeavesTheResultUntouched` is the test that says this is safe, and
        /// the golden master below keeps the stream on so that claim is still pinned somewhere.
        /// </summary>
        private static MatchReport Play(Club home, Club away, ulong seed, BalanceConfig? cfg = null,
            bool positions = false) =>
            new MatchEngine(cfg, generatePositions: positions).Simulate(
                LineupSelector.BestEleven(home), LineupSelector.BestEleven(away), new Pcg32(seed));

        // ------------------------------------------------------------------ determinism

        [Test]
        public void GoldenMaster_SameSeed_IdenticalReport()
        {
            string a = JsonSerializer.Serialize(Play(_midA, _midB, 42, positions: true));
            string b = JsonSerializer.Serialize(Play(_midA, _midB, 42, positions: true));

            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void BestEleven_IsValid_AndDeterministic()
        {
            Lineup l1 = LineupSelector.BestEleven(_top);
            Lineup l2 = LineupSelector.BestEleven(_top);

            Assert.That(l1.Slots.Count, Is.EqualTo(11));
            Assert.That(l1.Slots.Count(s => s.Role == PositionRole.Goalkeeper), Is.EqualTo(1));
            Assert.That(l1.Slots.Select(s => s.Player.Id), Is.EqualTo(l2.Slots.Select(s => s.Player.Id)));
        }

        // ------------------------------------------------------------------ balance harness

        [Test]
        public void Harness_StrongBeatsWeak_70to80Percent()
        {
            int strongWins = 0, draws = 0;

            for (ulong i = 0; i < 1000; i++)
            {
                // Alternate venue so home advantage cancels out.
                MatchReport r = i % 2 == 0 ? Play(_top, _bottom, i) : Play(_bottom, _top, i);
                int strongGoals = i % 2 == 0 ? r.HomeGoals : r.AwayGoals;
                int weakGoals = i % 2 == 0 ? r.AwayGoals : r.HomeGoals;

                if (strongGoals > weakGoals) strongWins++;
                else if (strongGoals == weakGoals) draws++;
            }

            TestContext.Out.WriteLine($"Strong wins: {strongWins / 10.0}% | draws: {draws / 10.0}%");
            Assert.That(strongWins, Is.InRange(650, 880),
                "Top club should win ~70-80% against bottom club");
        }

        [Test]
        public void Harness_EqualTeams_RealisticScores()
        {
            var scoreCounts = new Dictionary<string, int>();
            int totalGoals = 0, draws = 0, homeWins = 0, maxTeamGoals = 0;

            for (ulong i = 0; i < 1000; i++)
            {
                MatchReport r = Play(_midA, _midB, 5000 + i);
                totalGoals += r.HomeGoals + r.AwayGoals;
                if (r.HomeGoals == r.AwayGoals) draws++;
                if (r.HomeGoals > r.AwayGoals) homeWins++;
                maxTeamGoals = System.Math.Max(maxTeamGoals, System.Math.Max(r.HomeGoals, r.AwayGoals));

                string key = $"{r.HomeGoals}-{r.AwayGoals}";
                scoreCounts[key] = scoreCounts.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            // Print the harness so a human can judge "does this look like football?"
            int matchesWith3Plus = scoreCounts.Where(kv =>
                    kv.Key.Split('-').Select(int.Parse).Max() >= 3)
                .Sum(kv => kv.Value);

            TestContext.Out.WriteLine($"Avg goals/match: {totalGoals / 1000.0:F2} | draws: {draws / 10.0}% | home wins: {homeWins / 10.0}% | max single-team goals: {maxTeamGoals}");
            TestContext.Out.WriteLine($"Matches where a team scored 3+: {matchesWith3Plus / 10.0}%");
            foreach (var kv in scoreCounts.OrderByDescending(kv => kv.Value).Take(12))
                TestContext.Out.WriteLine($"  {kv.Key}: {kv.Value / 10.0}%");

            Assert.That(totalGoals / 1000.0, Is.InRange(2.0, 3.4), "Average goals per match");
            Assert.That(draws, Is.InRange(150, 380), "Draw share");
            Assert.That(maxTeamGoals, Is.LessThan(10), "No absurd blowouts between league teams");
        }

        [Test]
        public void Harness_HomeAdvantage_IsRealAndConfigurable()
        {
            int homeWinsDefault = 0, homeWinsNone = 0;
            var noAdvantage = new BalanceConfig();
            noAdvantage.Match.HomeAdvantagePercent = 0;

            for (ulong i = 0; i < 1000; i++)
            {
                MatchReport d = Play(_midA, _midB, 9000 + i);
                MatchReport n = Play(_midA, _midB, 9000 + i, noAdvantage);
                if (d.HomeGoals > d.AwayGoals) homeWinsDefault++;
                if (n.HomeGoals > n.AwayGoals) homeWinsNone++;
            }

            TestContext.Out.WriteLine($"Home wins with advantage: {homeWinsDefault / 10.0}% | without: {homeWinsNone / 10.0}%");
            Assert.That(homeWinsDefault, Is.GreaterThan(homeWinsNone),
                "Config home advantage must increase home wins (acceptance criterion 1.3/1.4)");
        }

        [Test]
        public void Goals_AreScoredMostlyByAttackers()
        {
            var attackingRoles = new HashSet<PositionRole>
                { PositionRole.Striker, PositionRole.Winger, PositionRole.AttackingMidfielder };

            var playersById = _league.Clubs.SelectMany(c => c.Squad.Players).ToDictionary(p => p.Id);
            int attackerGoals = 0, totalGoals = 0;

            for (ulong i = 0; i < 300; i++)
            {
                foreach (MatchEvent e in Play(_midA, _midB, 7000 + i).Events)
                {
                    if (e.Type != MatchEventType.Goal) continue;
                    totalGoals++;
                    if (attackingRoles.Contains(playersById[e.PlayerId].Role)) attackerGoals++;
                }
            }

            Assert.That(totalGoals, Is.GreaterThan(100), "Sanity: enough goals sampled");
            Assert.That(attackerGoals, Is.GreaterThan(totalGoals * 6 / 10),
                "At least 60% of goals from ST/W/AM");
        }

        [Test]
        public void Events_AreConsistentWithScore()
        {
            for (ulong i = 0; i < 50; i++)
            {
                MatchReport r = Play(_top, _bottom, 300 + i);

                int homeGoalEvents = r.Events.Count(e => e.Type == MatchEventType.Goal && e.ClubId == r.HomeClubId);
                int awayGoalEvents = r.Events.Count(e => e.Type == MatchEventType.Goal && e.ClubId == r.AwayClubId);

                Assert.That(homeGoalEvents, Is.EqualTo(r.HomeGoals));
                Assert.That(awayGoalEvents, Is.EqualTo(r.AwayGoals));
                Assert.That(r.Events.All(e => e.Minute >= 1 && e.Minute <= 90), Is.True);
                Assert.That(r.Events.Select(e => e.Minute), Is.Ordered);
            }
        }

        [Test]
        public void InvalidLineup_IsRejected()
        {
            Lineup tooFew = LineupSelector.BestEleven(_midA);
            tooFew.Slots.RemoveAt(10);

            Assert.Throws<System.InvalidOperationException>(
                () => new MatchEngine().Simulate(tooFew, LineupSelector.BestEleven(_midB), new Pcg32(1)));
        }
    }
}
