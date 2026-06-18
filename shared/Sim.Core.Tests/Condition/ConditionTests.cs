using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Condition;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Condition
{
    /// <summary>
    /// Task 4.1 acceptance: the condition model (form/morale/fitness) feeds match
    /// performance with capped maluses, form is mean-reverting (no player stuck in
    /// bad form &gt; 6 matches over 3 seasons), the performance floor is ≥ 70% of
    /// ability, and rotation outperforms a fixed XI fatigue-wise. Plus the identity
    /// guarantees that keep the opt-in safe for existing golden masters.
    /// </summary>
    [TestFixture]
    public class ConditionTests
    {
        private static League _league = null!;
        private static Club _club = null!;
        private static Club _opp = null!;
        private static readonly BalanceConfig _cfg = new BalanceConfig();
        private static ConditionBalance C => _cfg.Condition;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260618));
            _club = _league.Clubs[9];
            _opp = _league.Clubs[10];
        }

        // ------------------------------------------------------------ performance multiplier

        [Test]
        public void NeutralCondition_MultiplierIsExactlyOne()
        {
            var neutral = new PlayerCondition { Form = C.FormNeutral, Morale = C.MoraleNeutral, Fitness = 100 };
            Assert.That(ConditionModel.PerformanceMultiplier(neutral, C), Is.EqualTo(1.0),
                "Neutral condition must be the exact identity multiplier (keeps the engine opt-in byte-identical)");
        }

        [Test]
        public void PerformanceFloor_IsNeverBelow70Percent()
        {
            double floor = C.PerformanceFloorPercent / 100.0;
            double cap = C.PerformanceCapPercent / 100.0;

            // Rock-bottom condition lands exactly on the floor.
            var worst = new PlayerCondition { Form = 0, Morale = 0, Fitness = 0 };
            Assert.That(ConditionModel.PerformanceMultiplier(worst, C), Is.EqualTo(floor).Within(1e-9));

            // Sweep the whole condition cube: nothing escapes [floor, cap].
            var rng = new Pcg32(4242);
            for (int i = 0; i < 20000; i++)
            {
                var c = new PlayerCondition
                {
                    Form = rng.NextInt(0, 101),
                    Morale = rng.NextInt(0, 101),
                    Fitness = rng.NextInt(0, 101)
                };
                double m = ConditionModel.PerformanceMultiplier(c, C);
                Assert.That(m, Is.InRange(floor, cap),
                    $"multiplier {m} out of [{floor},{cap}] for F{c.Form}/M{c.Morale}/Fit{c.Fitness}");
            }
        }

        [Test]
        public void FitnessIsTheDominantPenalty_AndPeakFormIsAMildBonus()
        {
            var tired = new PlayerCondition { Form = C.FormNeutral, Morale = C.MoraleNeutral, Fitness = 0 };
            var hot = new PlayerCondition { Form = 100, Morale = 100, Fitness = 100 };

            Assert.That(ConditionModel.PerformanceMultiplier(tired, C), Is.LessThan(1.0));
            Assert.That(ConditionModel.PerformanceMultiplier(hot, C), Is.GreaterThan(1.0),
                "Full fitness + peak form/morale should give a modest bonus, not a malus");
        }

        // ------------------------------------------------------------ engine identity (opt-in safety)

        [Test]
        public void Engine_ConditionFlagOff_IgnoresConditionEntirely()
        {
            // Wreck the squad's condition, then prove the default (flag-off) engine
            // produces the same report as it always has — condition is opt-in.
            Lineup l = LineupSelector.BestEleven(_club);
            SetCondition(_club, form: 3, morale: 4, fitness: 5);

            string wrecked = JsonSerializer.Serialize(new MatchEngine(_cfg).Simulate(l, l, new Pcg32(77)));

            SetCondition(_club, form: 90, morale: 95, fitness: 100);
            string healthy = JsonSerializer.Serialize(new MatchEngine(_cfg).Simulate(l, l, new Pcg32(77)));

            Assert.That(healthy, Is.EqualTo(wrecked),
                "With applyCondition off the engine must be byte-identical regardless of condition");
        }

        [Test]
        public void Engine_NeutralSquad_WithConditionOn_MatchesBaseEngine()
        {
            SetCondition(_club, C.FormNeutral, C.MoraleNeutral, 100);
            Lineup l = LineupSelector.BestEleven(_club);

            string baseEngine = JsonSerializer.Serialize(new MatchEngine(_cfg).Simulate(l, l, new Pcg32(2024)));
            string withCond = JsonSerializer.Serialize(new MatchEngine(_cfg, applyCondition: true).Simulate(l, l, new Pcg32(2024)));

            Assert.That(withCond, Is.EqualTo(baseEngine),
                "A fully-neutral squad must play identically whether or not condition is applied");
        }

        [Test]
        public void Engine_WithConditionOn_TiredSquadScoresLess()
        {
            // Same fixture, same seed: a knackered side creates fewer/worse chances.
            SetCondition(_club, C.FormNeutral, C.MoraleNeutral, 100);
            SetCondition(_opp, C.FormNeutral, C.MoraleNeutral, 100);
            var engine = new MatchEngine(_cfg, applyCondition: true);

            int freshGoalsFor = 0, tiredGoalsFor = 0;
            for (ulong i = 0; i < 400; i++)
            {
                SetCondition(_club, C.FormNeutral, C.MoraleNeutral, 100);
                freshGoalsFor += engine.Simulate(
                    LineupSelector.BestEleven(_club), LineupSelector.BestEleven(_opp), new Pcg32(8000 + i)).HomeGoals;

                SetCondition(_club, C.FormNeutral, C.MoraleNeutral, 20); // exhausted
                tiredGoalsFor += engine.Simulate(
                    LineupSelector.BestEleven(_club), LineupSelector.BestEleven(_opp), new Pcg32(8000 + i)).HomeGoals;
            }

            TestContext.Out.WriteLine($"[fitness->result] fresh scored {freshGoalsFor}, exhausted scored {tiredGoalsFor} (over 400 matches)");
            Assert.That(tiredGoalsFor, Is.LessThan(freshGoalsFor),
                "An exhausted side must score fewer goals than the same side fresh");

            SetCondition(_club, C.FormNeutral, C.MoraleNeutral, 100); // leave the world tidy for other tests
        }

        // ------------------------------------------------------------ fitness dynamics

        [Test]
        public void Fitness_DrainsWithMinutes_AndRecoversWithRest()
        {
            var rng = new Pcg32(1);
            var c = new PlayerCondition { Fitness = 100 };

            ConditionModel.ApplyMatchResult(c, minutesPlayed: 90, TeamResult.Draw, rng, C);
            Assert.That(c.Fitness, Is.EqualTo(100 - C.FitnessDrainPer90Minutes), "90' drains the full per-90 amount");

            var half = new PlayerCondition { Fitness = 100 };
            ConditionModel.ApplyMatchResult(half, minutesPlayed: 45, TeamResult.Draw, rng, C);
            Assert.That(half.Fitness, Is.EqualTo(100 - C.FitnessDrainPer90Minutes * 45 / 90), "Partial minutes drain pro-rata");

            var unused = new PlayerCondition { Fitness = 80 };
            ConditionModel.ApplyMatchResult(unused, minutesPlayed: 0, TeamResult.Win, rng, C);
            Assert.That(unused.Fitness, Is.EqualTo(80), "An unused player loses no fitness");

            ConditionModel.ApplyRest(c, days: 3, C);
            Assert.That(c.Fitness, Is.EqualTo(100 - C.FitnessDrainPer90Minutes + 3 * C.FitnessRecoveryPerDay));

            ConditionModel.ApplyRest(c, days: 100, C);
            Assert.That(c.Fitness, Is.EqualTo(100), "Fitness recovery caps at 100");
        }

        // ------------------------------------------------------------ morale dynamics

        [Test]
        public void Morale_RespondsToPlayingTimeAndResults_AndDecaysToNeutral()
        {
            var rng = new Pcg32(2);

            var starter = new PlayerCondition { Form = 50, Morale = 50, Fitness = 100 };
            ConditionModel.ApplyMatchResult(starter, 90, TeamResult.Win, rng, C);
            Assert.That(starter.Morale, Is.EqualTo(50 + C.MoralePlayBonus + C.MoraleWinBonus), "Playing and winning lifts morale");

            var benched = new PlayerCondition { Form = 50, Morale = 50, Fitness = 100 };
            ConditionModel.ApplyMatchResult(benched, 0, TeamResult.Loss, rng, C);
            Assert.That(benched.Morale, Is.EqualTo(50 - C.MoraleBenchPenalty - C.MoraleLossPenalty), "Being dropped in a defeat hurts morale");

            var happy = new PlayerCondition { Morale = 70 };
            ConditionModel.ApplyRest(happy, days: 5, C);
            Assert.That(happy.Morale, Is.EqualTo(70 - 5 * C.MoraleDecayPerDay), "High morale decays toward neutral with time");

            var sad = new PlayerCondition { Morale = 30 };
            ConditionModel.ApplyRest(sad, days: 5, C);
            Assert.That(sad.Morale, Is.EqualTo(30 + 5 * C.MoraleDecayPerDay), "Low morale recovers toward neutral with time");

            var nearNeutral = new PlayerCondition { Morale = C.MoraleNeutral + 1 };
            ConditionModel.ApplyRest(nearNeutral, days: 10, C); // plenty of decay available, but only 1 point of gap
            Assert.That(nearNeutral.Morale, Is.EqualTo(C.MoraleNeutral), "Decay never overshoots neutral");
        }

        // ------------------------------------------------------------ form: mean reversion over 3 seasons

        [Test]
        public void Form_MeanReverts_NoPlayerStuckInBadFormOver3Seasons()
        {
            const int badForm = 35;         // "out of form" line
            const int matchesPerSeason = 38;
            const int matches = matchesPerSeason * 3;
            var rng = new Pcg32(909090);

            // A spread of starting forms, including the extremes, across many players.
            int worstRunOverall = 0;
            int playersBelowAtEnd = 0;
            int sampleSize = 300;

            for (int p = 0; p < sampleSize; p++)
            {
                // Spread starting forms across a wide, deliberately off-neutral range
                // (generated players actually start at neutral, so this is stress-testing).
                var c = new PlayerCondition { Form = 12 + p * 76 / (sampleSize - 1), Morale = 50, Fitness = 100 };
                int currentRun = 0, worstRun = 0;

                for (int m = 0; m < matches; m++)
                {
                    // Random result each match; form must still self-correct.
                    TeamResult result = (TeamResult)rng.NextInt(0, 3);
                    ConditionModel.ApplyMatchResult(c, 90, result, rng, C);

                    if (c.Form < badForm)
                    {
                        currentRun++;
                        if (currentRun > worstRun) worstRun = currentRun;
                    }
                    else currentRun = 0;

                    Assert.That(c.Form, Is.InRange(0, 100), "Form must stay clamped in [0,100]");
                }

                if (c.Form < badForm) playersBelowAtEnd++;
                if (worstRun > worstRunOverall) worstRunOverall = worstRun;
            }

            TestContext.Out.WriteLine(
                $"[form] {sampleSize} players x {matches} matches | longest consecutive run below {badForm}: {worstRunOverall} matches | players below at end: {playersBelowAtEnd}");
            Assert.That(worstRunOverall, Is.LessThanOrEqualTo(6),
                "Mean reversion must stop any player being stuck in bad form for more than 6 matches");
        }

        // ------------------------------------------------------------ rotation beats a fixed XI (fatigue)

        [Test]
        public void Rotation_OutperformsFixedXI_OverACongestedBlock()
        {
            // Identical club, identical opponent, identical schedule (alternating
            // venue). The only difference is whether we field the same XI every match
            // (fitness craters) or rotate to keep players fresh. Form/morale are held
            // neutral so the comparison isolates fatigue, per the ✅ wording.
            //
            // Aggregated over several independent blocks with distinct seeds so the
            // sign of the effect is robust, not a single-block coin-flip; the run is
            // genuinely congested (a match every other day) so fatigue actually bites.
            const int matchdays = 16;
            const int gapDays = 1;          // truly congested: every other day
            const int blocks = 6;

            int fixedPoints = 0, rotatedPoints = 0;
            double fixedEndFitness = 0, rotatedEndFitness = 0;
            for (int b = 0; b < blocks; b++)
            {
                ulong baseSeed = 70000UL + (ulong)b * 1000UL;
                RunFatigueBlock(false, baseSeed, matchdays, gapDays, out int fp, out double ff);
                RunFatigueBlock(true, baseSeed, matchdays, gapDays, out int rp, out double rf);
                fixedPoints += fp; rotatedPoints += rp;
                fixedEndFitness += ff; rotatedEndFitness += rf;
            }
            fixedEndFitness /= blocks; rotatedEndFitness /= blocks;

            TestContext.Out.WriteLine(
                $"[rotation] over {blocks} blocks x {matchdays} congested matches — fixed XI: {fixedPoints} pts (regulars end ~{fixedEndFitness:F0} fitness) | rotated: {rotatedPoints} pts (squad ends ~{rotatedEndFitness:F0} fitness)");

            Assert.That(fixedEndFitness, Is.LessThan(60),
                "Sanity: a fixed XI on a congested run must actually accumulate heavy fatigue");
            Assert.That(rotatedEndFitness, Is.GreaterThan(fixedEndFitness),
                "Rotation must keep the squad fresher than a fixed XI");
            Assert.That(rotatedPoints, Is.GreaterThan(fixedPoints),
                "A fresher rotated squad must outperform the fatigued fixed XI over a congested calendar");

            SetCondition(_club, C.FormNeutral, C.MoraleNeutral, 100);
            SetCondition(_opp, C.FormNeutral, C.MoraleNeutral, 100);
        }

        /// <summary>
        /// Plays a congested block for <see cref="_club"/> vs a constant fresh
        /// <see cref="_opp"/>. Managed players' form/morale are pinned neutral each
        /// match (so only fitness carries over); starters drain fitness, everyone
        /// rests between matches. Returns the club's points and the end-of-block
        /// average fitness of its first-choice eleven.
        /// </summary>
        private void RunFatigueBlock(bool rotate, ulong baseSeed, int matchdays, int gapDays, out int points, out double regularsEndFitness)
        {
            SetCondition(_club, C.FormNeutral, C.MoraleNeutral, 100);
            var engine = new MatchEngine(_cfg, applyCondition: true);
            points = 0;

            for (int md = 0; md < matchdays; md++)
            {
                // Pin form/morale neutral; opponent fully fresh and neutral.
                foreach (Player pl in _club.Squad.Players) { pl.Condition.Form = C.FormNeutral; pl.Condition.Morale = C.MoraleNeutral; }
                SetCondition(_opp, C.FormNeutral, C.MoraleNeutral, 100);

                Lineup mine = rotate ? FreshestEleven(_club) : LineupSelector.BestEleven(_club);
                Lineup theirs = LineupSelector.BestEleven(_opp);

                bool atHome = md % 2 == 0;
                var rng = new Pcg32(baseSeed + (ulong)md);
                MatchReport r = atHome
                    ? engine.Simulate(mine, theirs, rng)
                    : engine.Simulate(theirs, mine, rng);

                int myGoals = atHome ? r.HomeGoals : r.AwayGoals;
                int oppGoals = atHome ? r.AwayGoals : r.HomeGoals;
                if (myGoals > oppGoals) points += _cfg.Season.PointsForWin;
                else if (myGoals == oppGoals) points += _cfg.Season.PointsForDraw;

                // Starters drain; everyone recovers over the gap to the next match.
                foreach (LineupSlot slot in mine.Slots)
                    slot.Player.Condition.Fitness -= C.FitnessDrainPer90Minutes;
                foreach (Player pl in _club.Squad.Players)
                    ConditionModel.ApplyRest(pl.Condition, gapDays, C);
            }

            // Fatigue readout: average fitness of the would-be first XI.
            Lineup regulars = LineupSelector.BestEleven(_club);
            double sum = 0;
            foreach (LineupSlot slot in regulars.Slots) sum += slot.Player.Condition.Fitness;
            regularsEndFitness = sum / regulars.Slots.Count;
        }

        // ------------------------------------------------------------ helpers

        /// <summary>Best XI weighted by fitness (4-3-3), so tired players rotate out — a simple manager's rotation.</summary>
        private static Lineup FreshestEleven(Club club)
        {
            var lineup = new Lineup { ClubId = club.Id };
            var used = new HashSet<int>();

            foreach (PositionRole role in LineupSelector.DefaultFormation)
            {
                Player? best = null;
                int bestScore = int.MinValue;
                foreach (Player candidate in club.Squad.Players)
                {
                    if (used.Contains(candidate.Id)) continue;
                    // Effective ability for this match = role rating scaled by fitness.
                    int score = PlayerRating.OverallFor(candidate, role) * candidate.Condition.Fitness;
                    if (score > bestScore || (score == bestScore && best != null && candidate.Id < best.Id))
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }

                if (best != null)
                {
                    used.Add(best.Id);
                    lineup.Slots.Add(new LineupSlot { Role = role, Player = best });
                }
            }

            lineup.Validate();
            return lineup;
        }

        private static void SetCondition(Club club, int form, int morale, int fitness)
        {
            foreach (Player p in club.Squad.Players)
            {
                p.Condition.Form = form;
                p.Condition.Morale = morale;
                p.Condition.Fitness = fitness;
            }
        }
    }
}
