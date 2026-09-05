using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Decisions with the ball (engine phase 4 — docs/engine/MATCH_ENGINE_PLAN.md §4).
    ///
    /// Until this phase the only attribute the picture read was Pace. A pass was aimed at a
    /// mathematically safe line and executed exactly, a run with the ball was a fixed eight-metre
    /// touch, and a challenge was one flat dice roll a tick that a winger and a centre-half won
    /// equally often. So the question this game is about — which players play better — had no
    /// answer the simulation could give, and 44% of passes went straight to an opponent.
    ///
    /// What is pinned here is the answer to that question, and it is deliberately pinned through
    /// the PICTURE rather than through the model: every reading below is taken off the position
    /// stream, the same way the balance harness takes it, so a test cannot pass by agreeing with
    /// an internal number that is itself wrong.
    ///
    ///   • two sides of the same players, one given the ball skills and one not, do not play the
    ///     same match: the better side keeps the ball, gets it into the final third far more
    ///     often, and gives it away in its own third far less;
    ///   • passing and dribbling each do this on their own, so neither is carrying the other;
    ///   • passes arrive at the rate real football's do;
    ///   • a keeper's hands are his own: a save is held or parried on Goalkeeping;
    ///   • and none of it costs the engine its determinism.
    /// </summary>
    [TestFixture]
    public class BallDecisionTests
    {
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        // ------------------------------------------------------------ the difference between players

        /// <summary>
        /// THE phase-4 assertion. The same twenty-two footballers, twice: one side's Passing,
        /// Technique and Dribbling turned up, the other's turned down, everything else — Pace,
        /// Strength, Defending, Positioning, the formation, the instructions, the seed —
        /// identical. Before this phase the two sides played an indistinguishable match.
        /// </summary>
        [Test]
        public void BetterFootballers_KeepTheBall_AndGetItForward()
        {
            SkillEffect effect = Play(seeds: 8, passing: true, dribbling: true);
            Report("[ball-skill] passing, technique and dribbling", effect);

            Assert.That(effect.GoodPossessionPercent, Is.GreaterThan(53.0),
                "the side that can play keeps the ball");
            Assert.That(effect.GoodIntoFinalThird, Is.GreaterThan(effect.BadIntoFinalThird * 1.15),
                "and gets it into the final third markedly more often");
            Assert.That(effect.GoodLostInOwnThird, Is.LessThan(effect.BadLostInOwnThird),
                "while giving it away in his own third less");
        }

        /// <summary>Passing on its own, so the duel is not doing the work.</summary>
        [Test]
        public void Passing_AloneMovesTheGame()
        {
            SkillEffect effect = Play(seeds: 8, passing: true, dribbling: false);
            Report("[ball-skill] passing and technique only", effect);

            Assert.That(effect.GoodPossessionPercent, Is.GreaterThan(51.0));
            Assert.That(effect.GoodIntoFinalThird, Is.GreaterThan(effect.BadIntoFinalThird));
            Assert.That(effect.GoodLostInOwnThird, Is.LessThan(effect.BadLostInOwnThird));
        }

        /// <summary>And dribbling on its own, so the passing model is not doing the work either.</summary>
        [Test]
        public void Dribbling_AloneMovesTheGame()
        {
            // More seeds than the other two: dribbling on its own is the narrower of the three
            // effects, and a claim this size should not rest on eight matches.
            SkillEffect effect = Play(seeds: 16, passing: false, dribbling: true);
            Report("[ball-skill] dribbling only", effect);

            Assert.That(effect.GoodPossessionPercent, Is.GreaterThan(51.0));
            Assert.That(effect.GoodIntoFinalThird, Is.GreaterThan(effect.BadIntoFinalThird));
        }

        // ------------------------------------------------------------ the picture, against real football

        /// <summary>
        /// Passes arrive as often as football's do. The old model completed 55% of them, which is
        /// not a bad passing side — it is not football. The band is the one the pitch scenario
        /// measures against, and it is the phase's headline reading.
        /// </summary>
        [Test]
        public void Passes_ArriveLikeRealFootball()
        {
            var analyzer = new MatchAnalyzer();
            double attempted = 0, completed = 0, played = 0;

            for (ulong seed = 40; seed < 52; seed++)
            {
                MatchMetrics m = analyzer.Measure(Match(_league.Clubs[9], _league.Clubs[10], seed))!;
                attempted += m.TotalPasses;
                completed += m.Home.PassesCompleted + m.Away.PassesCompleted;
                played += m.TotalPasses;
            }

            double accuracy = 100.0 * completed / Math.Max(1.0, attempted);
            TestContext.Out.WriteLine(
                $"[passing] {attempted / 12:F0} passes a match at {accuracy:F1}% accuracy");

            Assert.That(accuracy, Is.InRange(76.0, 88.0), "real football completes 76-88% of its passes");
            Assert.That(played / 12, Is.InRange(700.0, 1200.0), "and plays roughly a thousand of them");
        }

        // ------------------------------------------------------------ the keeper's hands

        /// <summary>
        /// A save used to end the move by definition: he got to it, therefore he had it. Now
        /// Goalkeeping says how often he HOLDS it, and the rest are parried back into play.
        /// </summary>
        [Test]
        public void TheKeepersHands_DependOnHim()
        {
            double sure = ParriedPercent(90);
            double shaky = ParriedPercent(20);

            TestContext.Out.WriteLine(
                $"[keeper] saves parried rather than held: {sure:F1}% by a 90 keeper, {shaky:F1}% by a 20 keeper");

            Assert.That(shaky, Is.GreaterThan(sure + 5.0), "a poor keeper spills far more of them");
            Assert.That(sure, Is.GreaterThan(0.0), "and even a good one does not hold everything");
        }

        // ------------------------------------------------------------ still deterministic

        [Test]
        public void TheSameSeed_PlaysTheSameMatch()
        {
            Club a = _league.Clubs[3], b = _league.Clubs[14];
            ulong first = MatchReportHasher.Hash(Match(a, b, 909));
            ulong again = MatchReportHasher.Hash(Match(a, b, 909));
            ulong other = MatchReportHasher.Hash(Match(a, b, 910));

            Assert.That(again, Is.EqualTo(first), "the same seed has to play the same match");
            Assert.That(other, Is.Not.EqualTo(first), "and a different one a different match");
        }

        // ------------------------------------------------------------ machinery

        private static MatchReport Match(Club home, Club away, ulong seed) =>
            new MatchEngine().Simulate(
                LineupSelector.BestEleven(home), LineupSelector.BestEleven(away), new Pcg32(seed));

        private struct SkillEffect
        {
            public double GoodPossessionPercent;
            public double GoodIntoFinalThird;
            public double BadIntoFinalThird;
            public double GoodLostInOwnThird;
            public double BadLostInOwnThird;
        }

        private static void Report(string label, SkillEffect e)
        {
            TestContext.Out.WriteLine(
                $"{label}: the good side had the ball {e.GoodPossessionPercent:F1}% of the time, " +
                $"played {e.GoodIntoFinalThird:F0} balls into the final third against {e.BadIntoFinalThird:F0}, " +
                $"and gave it away in his own third {e.GoodLostInOwnThird:F1} times a match against " +
                $"{e.BadLostInOwnThird:F1}");
        }

        private static SkillEffect Play(int seeds, bool passing, bool dribbling)
        {
            var effect = new SkillEffect();
            long goodFrames = 0, badFrames = 0;
            long goodFinal = 0, badFinal = 0, goodLost = 0, badLost = 0;

            for (int i = 0; i < seeds; i++)
            {
                Club good = Skilled(_league.Clubs[(2 * i) % _league.Clubs.Count], 88, passing, dribbling);
                Club poor = Skilled(_league.Clubs[(2 * i + 1) % _league.Clubs.Count], 24, passing, dribbling);

                MatchReport report = Match(good, poor, 500UL + (ulong)i);
                PositionStream stream = report.Positions!.Unpack();
                int frames = stream.TickCount;

                for (int t = 0; t < frames; t++)
                {
                    int code = stream.Owner[t];
                    if (code == PositionStream.NoOwner) continue;
                    stream.TryOwner(code, out bool home, out int _);
                    if (home) goodFrames++;
                    else badFrames++;
                }

                foreach (BallAction action in stream.Actions)
                {
                    if (action.Kind != BallActionKind.Pass && action.Kind != BallActionKind.LongBall
                        && action.Kind != BallActionKind.Cross) continue;
                    if (action.Tick < 0 || action.Tick >= frames) continue;

                    int[] mine = action.Home ? stream.HomeXY : stream.AwayXY;
                    int fromX = stream.PlayerX(mine, action.Tick, action.Slot);
                    int toX = action.TargetSlot >= 0
                        ? stream.PlayerX(mine, action.Tick, action.TargetSlot)
                        : fromX;

                    int landing = action.Home ? toX : Pitch.LengthDm - toX;
                    int origin = action.Home ? fromX : Pitch.LengthDm - fromX;
                    if (landing > Pitch.LengthDm * 2 / 3)
                    {
                        if (action.Home) goodFinal++; else badFinal++;
                    }

                    if (origin >= Pitch.LengthDm / 3) continue;
                    if (!Arrived(stream, action, frames))
                    {
                        if (action.Home) goodLost++; else badLost++;
                    }
                }
            }

            effect.GoodPossessionPercent = 100.0 * goodFrames / Math.Max(1L, goodFrames + badFrames);
            effect.GoodIntoFinalThird = goodFinal / (double)seeds;
            effect.BadIntoFinalThird = badFinal / (double)seeds;
            effect.GoodLostInOwnThird = goodLost / (double)seeds;
            effect.BadLostInOwnThird = badLost / (double)seeds;
            return effect;
        }

        /// <summary>Did his side touch this ball next?</summary>
        private static bool Arrived(PositionStream stream, BallAction action, int frames)
        {
            int passer = stream.OwnerCode(action.Home, action.Slot);
            for (int t = action.Tick + 1; t < frames && t < action.Tick + 200; t++)
            {
                int code = stream.Owner[t];
                if (code == PositionStream.NoOwner || code == passer) continue;
                stream.TryOwner(code, out bool home, out int _);
                return home == action.Home;
            }

            return false;
        }

        /// <summary>The same club with its ball skills set, and nothing else touched.</summary>
        private static Club Skilled(Club club, int level, bool passing, bool dribbling)
        {
            Club copy = Copy(club);
            foreach (Player player in copy.Squad.Players)
            {
                if (passing)
                {
                    player.Attributes.Passing = level;
                    player.Attributes.Technique = level;
                }

                if (dribbling) player.Attributes.Dribbling = level;
            }

            return copy;
        }

        private static double ParriedPercent(int goalkeeping)
        {
            long saves = 0, parried = 0;

            for (ulong seed = 60; seed < 84; seed++)
            {
                Club home = _league.Clubs[9];
                Club away = Keeper(_league.Clubs[10], goalkeeping);
                MatchReport report = Match(home, away, seed);
                PositionStream stream = report.Positions!.Unpack();
                int frames = stream.TickCount;

                foreach (BallAction action in stream.Actions)
                {
                    if (action.Kind != BallActionKind.Save) continue;
                    if (action.Home) continue;                       // only the away keeper was changed
                    saves++;

                    // Held: he is on the ball in the frames straight after the save. Parried: it
                    // is loose, and somebody has to go and get it.
                    bool held = false;
                    int keeper = stream.OwnerCode(false, action.Slot);
                    for (int t = action.Tick; t < frames && t <= action.Tick + 2; t++)
                        if (stream.Owner[t] == keeper) held = true;

                    if (!held) parried++;
                }
            }

            return 100.0 * parried / Math.Max(1L, saves);
        }

        private static Club Keeper(Club club, int goalkeeping)
        {
            Club copy = Copy(club);
            foreach (Player player in copy.Squad.Players)
                if (player.Role == PositionRole.Goalkeeper)
                    player.Attributes.Goalkeeping = goalkeeping;

            return copy;
        }

        private static Club Copy(Club club)
        {
            var copy = new Club
            {
                Id = club.Id,
                Name = club.Name,
                ShortName = club.ShortName,
                Strength = club.Strength
            };

            foreach (Player player in club.Squad.Players)
            {
                var clone = new Player
                {
                    Id = player.Id,
                    FirstName = player.FirstName,
                    LastName = player.LastName,
                    Age = player.Age,
                    Role = player.Role
                };

                for (int skill = 0; skill < PlayerAttributes.SkillCount; skill++)
                    clone.Attributes[skill] = player.Attributes[skill];

                clone.Condition.Form = player.Condition.Form;
                clone.Condition.Morale = player.Condition.Morale;
                clone.Condition.Fitness = player.Condition.Fitness;
                clone.Development.Potential = player.Development.Potential;
                copy.Squad.Players.Add(clone);
            }

            return copy;
        }
    }
}
