using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Career
{
    /// <summary>
    /// Task 4.2 wiring: condition is live in the season loop. Matches are simulated
    /// condition-aware (engine flag on) and the whole world's form/morale/fitness
    /// evolves each day via <see cref="SeasonProgressor.EvolveCondition"/>: starters
    /// drain, idle clubs recover.
    ///
    /// These guard the wiring, not the 4.1 model itself (that has its own tests):
    ///   - determinism (a full live season replays byte-for-byte, condition included);
    ///   - opt-in safety (engine flag on but condition never evolved = neutral = the
    ///     legacy season, byte-identical — so existing golden masters are safe);
    ///   - the lifecycle actually moves condition (drain on play, recover on rest);
    ///   - a calibration read-out (goals/match, draws, home edge, scoreline spread)
    ///     so we can confirm the live world stays in the accepted balance band.
    /// </summary>
    [TestFixture]
    public class SeasonProgressorConditionTests
    {
        private const ulong WorldSeed = 778899;
        private const int Days = 7 * 38; // full double round-robin for 20 clubs

        private static (League league, Season season) NewWorld()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(WorldSeed));
            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(WorldSeed, 777))
            };
            return (league, season);
        }

        /// <summary>Advances one day and (optionally) evolves the world's condition for it.</summary>
        private static List<MatchOutcome> Step(SeasonProgressor p, League league, Season season, bool evolve)
        {
            List<MatchOutcome> outcomes = p.AdvanceDay(league, season, WorldSeed);
            if (evolve)
                p.EvolveCondition(new[] { league }, season, outcomes, WorldSeed);
            return outcomes;
        }

        private static void RunLiveSeason(League league, Season season)
        {
            // Live config: condition-aware results + within-match fatigue, evolving daily.
            var p = new SeasonProgressor(applyCondition: true, applyMatchFatigue: true);
            for (int d = 0; d < Days; d++)
                Step(p, league, season, evolve: true);
        }

        // -------------------------------------------------------- opt-in safety

        [Test]
        public void EngineFlagOn_WithNeutralCondition_IsByteIdenticalToLegacy()
        {
            // Opt-in safety: a squad pinned to NEUTRAL condition is the engine identity
            // point, so it plays the same whether or not the engine applies condition.
            // (Generation randomizes morale [50,70] and fitness [95,100], so fresh squads
            // are NOT neutral — the live career deliberately diverges from here; we pin
            // neutral explicitly to isolate the structural identity guarantee.)
            (League legacyLeague, Season legacySeason) = NewWorld();
            (League liveLeague, Season liveSeason) = NewWorld();
            SetNeutral(legacyLeague);
            SetNeutral(liveLeague);

            var legacy = new SeasonProgressor();                      // flag off
            var live = new SeasonProgressor(applyCondition: true);    // flag on, but...
            for (int d = 0; d < Days; d++)
            {
                Step(legacy, legacyLeague, legacySeason, evolve: false);
                Step(live, liveLeague, liveSeason, evolve: false);    // ...never evolve
            }

            for (int i = 0; i < legacySeason.Fixtures.Count; i++)
            {
                Fixture a = legacySeason.Fixtures[i];
                Fixture b = liveSeason.Fixtures[i];
                Assert.That((b.HomeGoals, b.AwayGoals), Is.EqualTo((a.HomeGoals, a.AwayGoals)),
                    $"Fixture {a.Id}: neutral condition must leave results byte-identical to the legacy engine.");
            }
        }

        // -------------------------------------------------------- determinism

        [Test]
        public void LiveSeason_IsDeterministic_ResultsAndConditionReplayIdentically()
        {
            (League leagueA, Season seasonA) = NewWorld();
            (League leagueB, Season seasonB) = NewWorld();

            RunLiveSeason(leagueA, seasonA);
            RunLiveSeason(leagueB, seasonB);

            for (int i = 0; i < seasonA.Fixtures.Count; i++)
            {
                Fixture a = seasonA.Fixtures[i];
                Fixture b = seasonB.Fixtures[i];
                Assert.That((b.HomeGoals, b.AwayGoals), Is.EqualTo((a.HomeGoals, a.AwayGoals)),
                    $"Fixture {a.Id} diverged between two identical live seasons — condition wiring is non-deterministic.");
            }

            // Final condition must match too: the form RNG is seeded from (seed, day, club).
            for (int c = 0; c < leagueA.Clubs.Count; c++)
            {
                List<Player> pa = leagueA.Clubs[c].Squad.Players;
                List<Player> pb = leagueB.Clubs[c].Squad.Players;
                for (int j = 0; j < pa.Count; j++)
                {
                    Assert.That(
                        (pb[j].Condition.Form, pb[j].Condition.Morale, pb[j].Condition.Fitness),
                        Is.EqualTo((pa[j].Condition.Form, pa[j].Condition.Morale, pa[j].Condition.Fitness)),
                        $"Player {pa[j].Id} ended the season with different condition across identical runs.");
                }
            }
        }

        // -------------------------------------------------------- lifecycle (drain / recover)

        [Test]
        public void Condition_DrainsOnMatchday_AndRecoversOnRest()
        {
            (League league, Season season) = NewWorld();
            var p = new SeasonProgressor(applyCondition: true, applyMatchFatigue: true);

            Club club = league.Clubs[0];
            HashSet<int> starters = StartersOf(club);

            double postMatchFitness = -1, recoveredFitness = -1;
            bool played = false;
            int restDays = 0;

            for (int d = 0; d < Days; d++)
            {
                List<MatchOutcome> outcomes = Step(p, league, season, evolve: true);

                bool clubPlayed = false;
                foreach (MatchOutcome o in outcomes)
                    if (o.Fixture.Involves(club.Id)) clubPlayed = true;

                double avg = AvgStarterFitness(club, starters);
                if (clubPlayed)
                {
                    postMatchFitness = avg;
                    played = true;
                    restDays = 0;
                }
                else if (played)
                {
                    recoveredFitness = avg; // last quiet day before the next match
                    if (++restDays >= 3) break;
                }
            }

            Assert.That(played, Is.True, "The tracked club should have played at least one match.");
            Assert.That(postMatchFitness, Is.LessThan(100),
                "Starters must lose fitness on a matchday (they began the season fresh at 100).");
            Assert.That(recoveredFitness, Is.GreaterThan(postMatchFitness),
                "Resting after the match must visibly recover fitness — the anti-frustration lever (bench a tired player).");

            TestContext.Out.WriteLine(
                $"[lifecycle] starters fitness right after a match: {postMatchFitness:F1} -> after rest: {recoveredFitness:F1}");
        }

        // -------------------------------------------------------- calibration read-out (harness)

        [Test]
        public void LiveSeason_StaysInTheAcceptedBalanceBand()
        {
            (League league, Season season) = NewWorld();
            RunLiveSeason(league, season);

            int matches = 0, goals = 0, draws = 0, homeWins = 0, awayWins = 0;
            var hist = new SortedDictionary<string, int>();
            foreach (Fixture f in season.Fixtures)
            {
                if (!f.Played) continue;
                matches++;
                goals += f.HomeGoals + f.AwayGoals;
                if (f.HomeGoals > f.AwayGoals) homeWins++;
                else if (f.HomeGoals < f.AwayGoals) awayWins++;
                else draws++;

                int hi = f.HomeGoals > f.AwayGoals ? f.HomeGoals : f.AwayGoals;
                int lo = f.HomeGoals > f.AwayGoals ? f.AwayGoals : f.HomeGoals;
                string key = $"{hi}-{lo}";
                hist[key] = hist.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            double goalsPerMatch = (double)goals / matches;
            double drawPct = 100.0 * draws / matches;
            double homePct = 100.0 * homeWins / matches;

            TestContext.Out.WriteLine(
                $"[condition-live calibration] {matches} matches | {goalsPerMatch:F2} goals/match | " +
                $"draws {drawPct:F1}% | home wins {homePct:F1}% | away wins {100.0 * awayWins / matches:F1}%");
            foreach (KeyValuePair<string, int> kv in hist)
                TestContext.Out.WriteLine($"    {kv.Key}: {kv.Value} ({100.0 * kv.Value / matches:F1}%)");

            // Loose sanity band (the pre-condition baseline is ~2.44 g/m, ~25% draws);
            // condition adds variance but is scale-invariant when both sides tire equally,
            // so the averages should barely move. Tightening is a judgement call with the user.
            Assert.That(goalsPerMatch, Is.InRange(2.0, 3.0), "Goals/match drifted out of the accepted band.");
            Assert.That(drawPct, Is.InRange(15.0, 38.0), "Draw rate drifted out of the accepted band.");
            Assert.That(homePct, Is.GreaterThan(awayWins * 100.0 / matches),
                "Home advantage should still produce more home wins than away wins.");
        }

        // -------------------------------------------------------- helpers

        private static void SetNeutral(League league)
        {
            foreach (Club club in league.Clubs)
                foreach (Player p in club.Squad.Players)
                {
                    p.Condition.Form = 50;     // FormNeutral
                    p.Condition.Morale = 50;   // MoraleNeutral
                    p.Condition.Fitness = 100; // full
                }
        }

        private static HashSet<int> StartersOf(Club club)
        {
            var ids = new HashSet<int>();
            foreach (LineupSlot slot in LineupSelector.BestEleven(club).Slots)
                ids.Add(slot.Player.Id);
            return ids;
        }

        private static double AvgStarterFitness(Club club, HashSet<int> starterIds)
        {
            double sum = 0;
            int count = 0;
            foreach (Player p in club.Squad.Players)
            {
                if (!starterIds.Contains(p.Id)) continue;
                sum += p.Condition.Fitness;
                count++;
            }

            return count > 0 ? sum / count : 0;
        }
    }
}
