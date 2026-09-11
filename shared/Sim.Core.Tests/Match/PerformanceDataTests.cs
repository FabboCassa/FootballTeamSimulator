using System;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// THE PERFORMANCE DATA (engine phase 7 — docs/engine/MATCH_ENGINE_PLAN.md §4).
    ///
    /// Phase 6 made the pitch the truth; this phase makes it LEGIBLE. Every figure on a player's
    /// line of the match report — his minutes, his passes and how many arrived, the pass that set
    /// up the strike, the duel he lost, the mark out of ten — is COUNTED OFF THE PICTURE after the
    /// match is over. Nothing here is reported by the simulator as it goes, which is the whole
    /// safety argument: a reading taken after the last roll cannot move a result, and the golden
    /// master of phase 6 is still the right number after this one.
    ///
    /// So the tests come in two families. The hand-built streams pin the COUNTING — a pass that
    /// arrives against one that does not, the goal credited to the man who was standing in the
    /// slot rather than the man who came on for him, what a strike from thirty metres is worth
    /// against one from six — with the expected answer worked out on paper. The real-engine tests
    /// pin the INVARIANTS that must hold whatever the match does: eleven men play ninety minutes
    /// each, every goal on the scoresheet belongs to somebody, and reading the match does not
    /// change it.
    /// </summary>
    [TestFixture]
    public class PerformanceDataTests
    {
        private const int Players = 11;
        private const int Keeper = 0;
        private const int Fpm = 12;

        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static readonly PerformanceBalance Perf = Cfg.Performance;

        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260907));
        }

        // ------------------------------------------------------------------ building a stream

        /// <summary>A well-formed empty match: keepers on their lines, nobody on the ball.</summary>
        private static PositionStream NewStream(int minutes = 90)
        {
            int ticks = minutes * Fpm + 1;
            var s = new PositionStream
            {
                TicksPerMinute = Fpm,
                PlayerCount = Players,
                LastTick = ticks - 1,
                BallXY = new int[ticks * 2],
                HomeXY = new int[ticks * Players * 2],
                AwayXY = new int[ticks * Players * 2],
                Owner = new int[ticks],
                HomePlayerIds = new int[Players],
                AwayPlayerIds = new int[Players],
                HomeShirts = new int[Players],
                AwayShirts = new int[Players]
            };

            for (int i = 0; i < Players; i++)
            {
                s.HomePlayerIds[i] = 100 + i;
                s.AwayPlayerIds[i] = 200 + i;
                s.HomeShirts[i] = 1 + i;
                s.AwayShirts[i] = 1 + i;
            }

            for (int t = 0; t < ticks; t++)
            {
                Put(s, true, t, Keeper, 40, Pitch.CenterY);
                Put(s, false, t, Keeper, Pitch.LengthDm - 40, Pitch.CenterY);
                for (int i = 1; i < Players; i++)
                {
                    Put(s, true, t, i, Pitch.CenterX - 100, Pitch.CenterY);
                    Put(s, false, t, i, Pitch.CenterX + 100, Pitch.CenterY);
                }

                s.BallXY[t * 2] = Pitch.CenterX;
                s.BallXY[t * 2 + 1] = Pitch.CenterY;
            }

            return s;
        }

        private static void Put(PositionStream s, bool home, int tick, int slot, int x, int y)
        {
            int[] side = home ? s.HomeXY : s.AwayXY;
            side[(tick * Players + slot) * 2] = x;
            side[(tick * Players + slot) * 2 + 1] = y;
        }

        private static void Hold(PositionStream s, int tick, bool home, int slot) =>
            s.Owner[tick] = s.OwnerCode(home, slot);

        private static void Ball(PositionStream s, int tick, int x, int y)
        {
            s.BallXY[tick * 2] = x;
            s.BallXY[tick * 2 + 1] = y;
        }

        private static void Act(PositionStream s, int tick, BallActionKind kind, bool home, int slot, int target = -1) =>
            s.Actions.Add(new BallAction(tick, kind, home, slot, target));

        private static MatchReport Report(PositionStream s, int homeGoals = 0, int awayGoals = 0) =>
            new MatchReport
            {
                HomeClubId = 1,
                AwayClubId = 2,
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                Positions = s
            };

        private static MatchStats Build(PositionStream s, int homeGoals = 0, int awayGoals = 0) =>
            MatchStatsBuilder.Build(Report(s, homeGoals, awayGoals), Perf)!;

        private static PlayerMatchStats Man(MatchStats stats, int playerId) => stats.Player(playerId)!;

        /// <summary>A real match between two generated clubs, played on the pitch.</summary>
        private static MatchReport Match(int index)
        {
            Club home = _league.Clubs[(2 * index) % _league.Clubs.Count];
            Club away = _league.Clubs[(2 * index + 1) % _league.Clubs.Count];
            return new MatchEngine(Cfg).Simulate(
                LineupSelector.BestEleven(home), LineupSelector.BestEleven(away),
                new Pcg32(70_000UL + (ulong)index));
        }

        // ------------------------------------------------------------------ there, or not there

        /// <summary>
        /// No picture, no statistics. The world's fast path resolves hundreds of fixtures a
        /// matchday and none of them is watched by anybody, so none of them is counted either.
        /// </summary>
        [Test]
        public void AMatchWithNoPicture_HasNoStatistics()
        {
            Assert.That(MatchStatsBuilder.Build(new MatchReport(), Perf), Is.Null,
                "a report with no stream cannot be read");

            MatchReport fast = new MatchEngine(Cfg, generatePositions: false).Simulate(
                LineupSelector.BestEleven(_league.Clubs[0]),
                LineupSelector.BestEleven(_league.Clubs[1]),
                new Pcg32(4242));

            Assert.That(fast.Stats, Is.Null, "and the fast path must not pay for one");
        }

        /// <summary>A played match comes back with one line per man and two tactical reports.</summary>
        [Test]
        public void APlayedMatch_ComesBackWithItsPerformanceData()
        {
            MatchReport r = Match(1);

            Assert.That(r.Stats, Is.Not.Null, "a watched match has to carry its statistics");
            Assert.That(r.Stats!.Players.Count, Is.EqualTo(22), "twenty-two men, nobody substituted");
            Assert.That(r.Stats.Home.ClubId, Is.EqualTo(r.HomeClubId));
            Assert.That(r.Stats.Away.ClubId, Is.EqualTo(r.AwayClubId));
            Assert.That(r.Stats.Players.Select(p => p.PlayerId).Distinct().Count(), Is.EqualTo(22),
                "and every one of them is a different player");
        }

        // ------------------------------------------------------------------ the minutes

        /// <summary>
        /// Eleven men for ninety minutes, both sides — 990 minutes each, unless somebody was sent
        /// off. It is the arithmetic that proves the occupancy table is right, and it is exactly
        /// what was impossible before the stream recorded its substitutions.
        /// </summary>
        [Test]
        public void ElevenMen_PlayNinetyMinutesEach()
        {
            for (int i = 0; i < 6; i++)
            {
                MatchStats stats = Match(i).Stats!;

                foreach (bool home in new[] { true, false })
                {
                    int reds = stats.Players.Where(p => p.Home == home).Sum(p => p.RedCards);
                    int minutes = stats.Players.Where(p => p.Home == home).Sum(p => p.MinutesPlayed);

                    Assert.That(minutes, Is.LessThanOrEqualTo(Players * 90),
                        $"match {i}: a side cannot play more than eleven times ninety minutes");
                    Assert.That(minutes, Is.GreaterThanOrEqualTo(Players * 90 - 90 * reds),
                        $"match {i}: and cannot play fewer, except for the men who were sent off");
                }
            }
        }

        /// <summary>
        /// A substitution splits the slot: sixty minutes to the man who came off, thirty to the
        /// man who came on. Without <see cref="SlotChange"/> the stream says the substitute played
        /// the whole match and the man he replaced never existed.
        /// </summary>
        [Test]
        public void ASubstitution_SplitsTheSlotBetweenTwoMen()
        {
            PositionStream s = NewStream();
            s.Changes.Add(new SlotChange(60 * Fpm, true, 7, onPlayerId: 999, offPlayerId: 107, onShirt: 21, offShirt: 8));
            s.HomePlayerIds[7] = 999;
            s.HomeShirts[7] = 21;

            MatchStats stats = Build(s);

            PlayerMatchStats off = Man(stats, 107);
            PlayerMatchStats on = Man(stats, 999);

            Assert.That(off.MinutesPlayed, Is.EqualTo(60), "he came off on the hour");
            Assert.That(on.MinutesPlayed, Is.EqualTo(30), "and the man who came on played the half hour");
            Assert.That(off.Shirt, Is.EqualTo(8), "the shirt of the man who went off is his own");
            Assert.That(on.Shirt, Is.EqualTo(21));
            Assert.That(stats.Players.Count, Is.EqualTo(23), "twenty-two plus the substitute");
            Assert.That(stats.Players.Where(p => p.Home).Sum(p => p.MinutesPlayed), Is.EqualTo(Players * 90),
                "and the side still played eleven times ninety minutes");
        }

        /// <summary>
        /// And what happens in a slot belongs to the man who was standing in it: the goal before
        /// the hour is the first man's, the goal after it is the substitute's.
        /// </summary>
        [Test]
        public void WhatHappensInASlot_BelongsToTheManStandingInIt()
        {
            PositionStream s = NewStream();
            s.Changes.Add(new SlotChange(60 * Fpm, true, 9, onPlayerId: 999, offPlayerId: 109, onShirt: 22, offShirt: 10));
            s.HomePlayerIds[9] = 999;
            s.HomeShirts[9] = 22;

            Act(s, 30 * Fpm, BallActionKind.Goal, true, 9);
            Act(s, 75 * Fpm, BallActionKind.Goal, true, 9);

            MatchStats stats = Build(s, homeGoals: 2);

            Assert.That(Man(stats, 109).Goals, Is.EqualTo(1), "the first goal is the first man's");
            Assert.That(Man(stats, 999).Goals, Is.EqualTo(1), "the second is the substitute's");
            Assert.That(Man(stats, 200).GoalsConceded, Is.EqualTo(2), "both went past the same keeper");
        }

        /// <summary>
        /// Every goal on the scoresheet belongs to somebody. A deflection and an own goal are
        /// credited by the engine like any other (phase 6, ScoreGoal), so the two counts agree
        /// exactly — which is the check that the whole attribution is sound.
        /// </summary>
        [Test]
        public void EveryGoal_BelongsToAMan()
        {
            int goals = 0;

            for (int i = 0; i < 10; i++)
            {
                MatchReport r = Match(i);
                MatchStats stats = r.Stats!;

                Assert.That(stats.Players.Where(p => p.Home).Sum(p => p.Goals), Is.EqualTo(r.HomeGoals),
                    $"match {i}: the home goals must add up to the home scorers");
                Assert.That(stats.Players.Where(p => !p.Home).Sum(p => p.Goals), Is.EqualTo(r.AwayGoals),
                    $"match {i}: and the away goals to the away scorers");

                goals += r.HomeGoals + r.AwayGoals;
            }

            TestContext.Out.WriteLine($"[perf-goals] {goals} goals in 10 matches, every one of them credited");
            Assert.That(goals, Is.GreaterThan(10), "sanity: enough goals sampled");
        }

        // ------------------------------------------------------------------ on the ball

        /// <summary>A pass is completed when the next man on the ball is a team-mate, and not otherwise.</summary>
        [Test]
        public void APass_IsCompleted_WhenATeamMateGetsIt()
        {
            PositionStream s = NewStream();

            Act(s, 10, BallActionKind.Pass, true, 4, 6);
            Hold(s, 13, true, 6);

            Act(s, 40, BallActionKind.Pass, true, 4, 6);
            Hold(s, 44, false, 3);

            MatchStats stats = Build(s);
            PlayerMatchStats passer = Man(stats, 104);

            Assert.That(passer.PassesAttempted, Is.EqualTo(2));
            Assert.That(passer.PassesCompleted, Is.EqualTo(1), "only the one a team-mate took");
            Assert.That(stats.Home.PassesAttempted, Is.EqualTo(2));
            Assert.That(stats.Home.PassesCompleted, Is.EqualTo(1));
            Assert.That(passer.PassAccuracyPercent, Is.EqualTo(50.0).Within(0.01));
        }

        /// <summary>
        /// The pass map is the picture of who combines with whom, kept by SLOT because it is a
        /// picture of the shape rather than of the men.
        /// </summary>
        [Test]
        public void ThePassMap_SaysWhoPlayedToWhom()
        {
            PositionStream s = NewStream();

            Act(s, 10, BallActionKind.Pass, true, 4, 6);
            Hold(s, 12, true, 6);
            Act(s, 20, BallActionKind.Pass, true, 4, 6);
            Hold(s, 22, true, 6);
            Act(s, 30, BallActionKind.Pass, true, 6, 9);
            Hold(s, 33, false, 2);

            MatchStats stats = Build(s);

            PassLink busiest = stats.Home.PassMap[0];
            Assert.That(busiest.FromSlot, Is.EqualTo(4));
            Assert.That(busiest.ToSlot, Is.EqualTo(6));
            Assert.That(busiest.Attempted, Is.EqualTo(2));
            Assert.That(busiest.Completed, Is.EqualTo(2));

            PassLink other = stats.Home.PassMap.Single(l => l.FromSlot == 6);
            Assert.That(other.ToSlot, Is.EqualTo(9));
            Assert.That(other.Completed, Is.EqualTo(0), "that one was cut out");
        }

        /// <summary>
        /// A key pass is the pass whose receiver strikes the ball; if it goes in, the same pass is
        /// an assist. A man who shoots after taking it off somebody else owes nobody anything.
        /// </summary>
        [Test]
        public void AKeyPass_SetsUpAStrike_AndAnAssist_SetsUpAGoal()
        {
            PositionStream s = NewStream();

            // 4 finds 9, who strikes it and scores. The strike falls inside the key-pass window
            // (KeyPassWindowSeconds, which is three frames at this stream's frame rate).
            Act(s, 100, BallActionKind.Pass, true, 4, 9);
            Hold(s, 101, true, 9);
            Ball(s, 102, Pitch.LengthDm - 90, Pitch.CenterY);
            Act(s, 102, BallActionKind.Shot, true, 9);
            Act(s, 104, BallActionKind.Goal, true, 9);

            // 5 finds 10, who strikes it and misses.
            Act(s, 300, BallActionKind.Pass, true, 5, 10);
            Hold(s, 301, true, 10);
            Ball(s, 302, Pitch.LengthDm - 150, Pitch.CenterY);
            Act(s, 302, BallActionKind.Shot, true, 10);
            Act(s, 304, BallActionKind.Miss, true, 10);

            MatchStats stats = Build(s, homeGoals: 1);

            Assert.That(Man(stats, 104).KeyPasses, Is.EqualTo(1));
            Assert.That(Man(stats, 104).Assists, Is.EqualTo(1), "his pass ended in the net");
            Assert.That(Man(stats, 105).KeyPasses, Is.EqualTo(1));
            Assert.That(Man(stats, 105).Assists, Is.EqualTo(0), "and his did not");
            Assert.That(Man(stats, 109).Goals, Is.EqualTo(1));
            Assert.That(Man(stats, 109).Shots, Is.EqualTo(1));
            Assert.That(Man(stats, 109).ShotsOnTarget, Is.EqualTo(1));
            Assert.That(Man(stats, 110).ShotsOnTarget, Is.EqualTo(0), "a miss is not on target");
        }

        /// <summary>
        /// What a strike is worth, read off where it was taken from: six metres beats twenty-five,
        /// and straight in front of goal beats the same distance from the byline.
        /// </summary>
        [Test]
        public void ExpectedGoals_FallWithTheDistanceAndWithTheAngle()
        {
            PositionStream s = NewStream();

            Ball(s, 100, Pitch.LengthDm - 60, Pitch.CenterY);
            Act(s, 100, BallActionKind.Shot, true, 9);
            Act(s, 102, BallActionKind.Miss, true, 9);

            Ball(s, 200, Pitch.LengthDm - 250, Pitch.CenterY);
            Act(s, 200, BallActionKind.Shot, true, 8);
            Act(s, 202, BallActionKind.Miss, true, 8);

            Ball(s, 300, Pitch.LengthDm - 60, Pitch.CenterY + 250);
            Act(s, 300, BallActionKind.Shot, true, 7);
            Act(s, 302, BallActionKind.Miss, true, 7);

            MatchStats stats = Build(s);

            int close = Man(stats, 109).XgPermille;
            int far = Man(stats, 108).XgPermille;
            int tight = Man(stats, 107).XgPermille;

            TestContext.Out.WriteLine($"[perf-xg] six metres {close}‰ · twenty-five metres {far}‰ · tight angle {tight}‰");

            Assert.That(close, Is.GreaterThan(far), "six metres is a better chance than twenty-five");
            Assert.That(close, Is.GreaterThan(tight), "and than the same six metres from the byline");
            Assert.That(close, Is.InRange(150, Perf.XgPeakPermille), "and it is still a chance, not a certainty");
            Assert.That(far, Is.GreaterThan(0));
        }

        // ------------------------------------------------------------------ off the ball

        /// <summary>A tackle is a duel: one man wins it, and the man he took it off loses one.</summary>
        [Test]
        public void ATackle_IsWonByOneManAndLostByAnother()
        {
            PositionStream s = NewStream();

            Hold(s, 50, true, 9);
            Act(s, 52, BallActionKind.Tackle, false, 4);

            MatchStats stats = Build(s);

            Assert.That(Man(stats, 204).Tackles, Is.EqualTo(1));
            Assert.That(Man(stats, 204).DuelsWon, Is.EqualTo(1));
            Assert.That(Man(stats, 109).DuelsLost, Is.EqualTo(1), "the ball was taken off him");
        }

        /// <summary>The keeper is the man who stays on his line, and the goals are his to concede.</summary>
        [Test]
        public void TheKeeper_IsTheManInGoal()
        {
            PositionStream s = NewStream();

            Act(s, 100, BallActionKind.Shot, true, 9);
            Act(s, 102, BallActionKind.Save, false, Keeper);
            Act(s, 400, BallActionKind.Shot, true, 9);
            Act(s, 402, BallActionKind.Goal, true, 9);

            MatchStats stats = Build(s, homeGoals: 1);
            PlayerMatchStats keeper = Man(stats, 200);

            Assert.That(keeper.Keeper, Is.True);
            Assert.That(Man(stats, 201).Keeper, Is.False, "and nobody else is");
            Assert.That(keeper.Saves, Is.EqualTo(1));
            Assert.That(keeper.GoalsConceded, Is.EqualTo(1));
            Assert.That(Man(stats, 109).ShotsOnTarget, Is.EqualTo(2), "a save and a goal are both on target");
        }

        /// <summary>The referee's book reaches the men it was written against.</summary>
        [Test]
        public void TheRefereesBook_ReachesTheRightMen()
        {
            PositionStream s = NewStream();

            Act(s, 100, BallActionKind.Foul, false, 5, 8);
            Act(s, 100, BallActionKind.YellowCard, false, 5);
            Act(s, 500, BallActionKind.Offside, true, 9);

            MatchStats stats = Build(s);

            Assert.That(Man(stats, 205).Fouls, Is.EqualTo(1));
            Assert.That(Man(stats, 205).YellowCards, Is.EqualTo(1));
            Assert.That(Man(stats, 108).FoulsSuffered, Is.EqualTo(1), "the man who was fouled is the other side's");
            Assert.That(Man(stats, 109).Offsides, Is.EqualTo(1));
            Assert.That(stats.Away.Fouls, Is.EqualTo(1));
            Assert.That(stats.Home.Offsides, Is.EqualTo(1));
        }

        /// <summary>A man sent off stops playing, and everything after the card happens without him.</summary>
        [Test]
        public void AManSentOff_StopsPlaying()
        {
            PositionStream s = NewStream();
            Act(s, 30 * Fpm, BallActionKind.RedCard, true, 6);

            MatchStats stats = Build(s);
            PlayerMatchStats off = Man(stats, 106);

            Assert.That(off.RedCards, Is.EqualTo(1), "the card is his");
            Assert.That(off.MinutesPlayed, Is.EqualTo(30), "and his match ends with it");
            Assert.That(stats.Players.Where(p => p.Home).Sum(p => p.MinutesPlayed), Is.EqualTo(Players * 90 - 60),
                "his side finishes the match with ten");
        }

        // ------------------------------------------------------------------ the tactical report

        /// <summary>
        /// Possession and territory are SHARES: the two sides' possession adds up to the whole of
        /// it, and the ball is always in one of the three thirds.
        /// </summary>
        [Test]
        public void PossessionAndTerritory_AreShares()
        {
            MatchStats stats = Match(3).Stats!;

            Assert.That(stats.Home.PossessionPermille + stats.Away.PossessionPermille,
                Is.InRange(998, 1000), "possession is a share of the ball somebody had");

            Assert.That(
                stats.Home.OwnThirdPermille + stats.Home.MiddleThirdPermille + stats.Home.FinalThirdPermille,
                Is.InRange(997, 1000), "and the ball is always in one of the thirds");

            Assert.That(stats.Home.OwnThirdPermille, Is.EqualTo(stats.Away.FinalThirdPermille),
                "one side's own third is the other's final third");

            Assert.That(stats.Home.DefendingWidthDm, Is.InRange(150, 600),
                "a defending block is somewhere between fifteen and sixty metres wide");
            Assert.That(stats.Home.DefendingHeightDm, Is.InRange(100, 700),
                "and it stands somewhere on its own half of the pitch");

            TestContext.Out.WriteLine(
                $"[perf-shape] defending {MatchStatsBuilder.Metres(stats.Home.DefendingWidthDm):F1} x " +
                $"{MatchStatsBuilder.Metres(stats.Home.DefendingDepthDm):F1} m, " +
                $"height {MatchStatsBuilder.Metres(stats.Home.DefendingHeightDm):F1} m · " +
                $"possession {stats.Home.PossessionPercent:F1}%");
        }

        /// <summary>Ground covered is football's ten kilometres, not a decimetre and not a hundred.</summary>
        [Test]
        public void EveryManCovers_AFootballersGround()
        {
            MatchStats stats = Match(4).Stats!;

            foreach (PlayerMatchStats player in stats.Players.Where(p => !p.Keeper))
                Assert.That(player.DistanceKm, Is.InRange(3.0, 20.0),
                    $"player {player.PlayerId} covered {player.DistanceKm:F2} km");

            double average = stats.Players.Where(p => !p.Keeper).Average(p => p.DistanceKm);
            TestContext.Out.WriteLine($"[perf-ground] {average:F2} km per outfielder");
            Assert.That(average, Is.InRange(8.0, 15.0), "an outfielder covers ten kilometres or so");
        }

        // ------------------------------------------------------------------ the mark

        /// <summary>Six out of ten is what a man gets for having been there; what he did moves him.</summary>
        [Test]
        public void TheMark_StartsAtSix_AndMovesWithWhatHeDid()
        {
            PlayerMatchStats quiet = FullMatch();
            Assert.That(MatchRatingModel.Rate(quiet, Perf), Is.EqualTo(Perf.RatingBase));

            PlayerMatchStats scorer = FullMatch();
            scorer.Goals = 1;
            Assert.That(MatchRatingModel.Rate(scorer, Perf), Is.EqualTo(Perf.RatingBase + Perf.RatingPerGoal));

            PlayerMatchStats sentOff = FullMatch();
            sentOff.RedCards = 1;
            Assert.That(MatchRatingModel.Rate(sentOff, Perf), Is.LessThan(Perf.RatingBase),
                "and a red card is not a good afternoon");

            PlayerMatchStats unused = FullMatch();
            unused.ToMinute = 0;
            Assert.That(MatchRatingModel.Rate(unused, Perf), Is.EqualTo(0), "a man who did not play has no mark");
        }

        /// <summary>
        /// THE CORRECTION OF THE FIRST MEASURED RUN. This engine produces about thirty-three
        /// defensive actions per man per match (306 tackles, 233 clearances, 192 interceptions),
        /// so a flat bonus per action handed everybody three extra points and the average mark
        /// came out at 8.5. The work off the ball is therefore paid on the DIFFERENCE from what
        /// the match asked of everybody else: a man who did his share gets nothing for it, and the
        /// mark reads the same whether the engine counts three recoveries a man or thirty.
        /// </summary>
        [Test]
        public void TheWorkOffTheBall_IsPaidOnTheDifference_NotOnTheCount()
        {
            const int Average = 33;

            PlayerMatchStats ordinary = FullMatch();
            ordinary.Tackles = 12;
            ordinary.Interceptions = 9;
            ordinary.Clearances = 12;
            Assert.That(MatchRatingModel.Rate(ordinary, Perf, Average), Is.EqualTo(Perf.RatingBase),
                "thirty-three actions in a match of thirty-threes is a day's work, not a good game");

            PlayerMatchStats busy = FullMatch();
            busy.Tackles = 24;
            busy.Interceptions = 12;
            busy.Clearances = 12;
            Assert.That(MatchRatingModel.Rate(busy, Perf, Average), Is.GreaterThan(Perf.RatingBase),
                "and doing half as much again is");

            PlayerMatchStats passenger = FullMatch();
            passenger.Tackles = 2;
            Assert.That(MatchRatingModel.Rate(passenger, Perf, Average), Is.LessThan(Perf.RatingBase),
                "while a passenger is marked as one");

            // And the swing is bounded: no amount of running about is worth more than a goal.
            PlayerMatchStats tireless = FullMatch();
            tireless.Tackles = 400;
            Assert.That(MatchRatingModel.Rate(tireless, Perf, Average),
                Is.EqualTo(Perf.RatingBase + Perf.RatingDefensiveCap));
        }

        /// <summary>A quarter of an hour cannot be a nine: the swing is scaled by the minutes.</summary>
        [Test]
        public void ACameo_CannotEarnAFullMatchsMark()
        {
            PlayerMatchStats full = FullMatch();
            full.Goals = 2;

            PlayerMatchStats cameo = FullMatch();
            cameo.FromMinute = 75;
            cameo.Goals = 2;

            Assert.That(MatchRatingModel.Rate(cameo, Perf), Is.LessThan(MatchRatingModel.Rate(full, Perf)));
            Assert.That(MatchRatingModel.Rate(cameo, Perf), Is.GreaterThan(Perf.RatingBase),
                "two goals off the bench is still a good afternoon");
        }

        /// <summary>The keeper is marked on his own match, not on his passing.</summary>
        [Test]
        public void TheKeeper_IsMarkedOnSavesAndGoalsConceded()
        {
            PlayerMatchStats worked = FullMatch();
            worked.Keeper = true;
            worked.Saves = 5;

            PlayerMatchStats beaten = FullMatch();
            beaten.Keeper = true;
            beaten.GoalsConceded = 4;

            Assert.That(MatchRatingModel.Rate(worked, Perf), Is.GreaterThan(Perf.RatingBase));
            Assert.That(MatchRatingModel.Rate(beaten, Perf), Is.LessThan(Perf.RatingBase));
        }

        /// <summary>
        /// The marks a real match produces sit where football's marks sit — around six, with the
        /// odd eight and the odd four, and nobody outside the scale.
        /// </summary>
        [Test]
        public void TheMarks_OfARealMatch_ReadLikeFootballsMarks()
        {
            double sum = 0;
            int count = 0, best = 0, worst = 100;

            for (int i = 0; i < 6; i++)
            {
                foreach (PlayerMatchStats player in Match(i).Stats!.Players)
                {
                    Assert.That(player.Rating, Is.InRange(Perf.RatingFloor, Perf.RatingCeiling));
                    sum += player.Rating;
                    count++;
                    if (player.Rating > best) best = player.Rating;
                    if (player.Rating < worst) worst = player.Rating;
                }
            }

            double average = sum / count / 10.0;
            TestContext.Out.WriteLine(
                $"[perf-marks] {count} marks, average {average:F2}, best {best / 10.0:F1}, worst {worst / 10.0:F1}");

            Assert.That(average, Is.InRange(5.0, 7.5), "the average man has an average match");
            Assert.That(best / 10.0, Is.GreaterThan(6.5), "and somebody has a good one");
        }

        /// <summary>
        /// THE SECOND CORRECTION OF THE FIRST DUMP, and the one the eye found rather than a number:
        /// on both sides of the match the harness drew, EVERY defender and midfielder came out above
        /// EVERY forward, and a man who had just scored twice was marked below his own centre-half.
        /// A forward recovers fewer balls and gives away more of them than a centre-back because
        /// that is what playing in front of the opponent's defence IS — so measuring him against the
        /// whole match's average punished him twice for doing his job. He is measured against
        /// forwards now, and the two lines have to come out level.
        /// </summary>
        [Test]
        public void TheMarks_DoNotDependOnWhereHePlayed()
        {
            double deepSum = 0, highSum = 0;
            int deepCount = 0, highCount = 0;

            for (int i = 0; i < 8; i++)
            {
                MatchStats stats = Match(i).Stats!;

                foreach (bool home in new[] { true, false })
                {
                    var outfield = stats.Players
                        .Where(p => p.Home == home && !p.Keeper && p.MinutesPlayed > 0)
                        .OrderBy(p => home ? p.AverageXDm : Pitch.LengthDm - p.AverageXDm)
                        .ToList();

                    for (int k = 0; k < outfield.Count; k++)
                    {
                        if (k < 4) { deepSum += outfield[k].Rating; deepCount++; }
                        else if (k >= outfield.Count - 3) { highSum += outfield[k].Rating; highCount++; }
                    }
                }
            }

            double back = deepSum / deepCount / 10.0;
            double front = highSum / highCount / 10.0;
            TestContext.Out.WriteLine(
                $"[perf-lines] the four deepest average {back:F2} out of ten, the three highest {front:F2}");

            Assert.That(Math.Abs(back - front), Is.LessThan(1.0),
                "a mark that depends on where a man played is not a mark, it is a position");
        }

        private static PlayerMatchStats FullMatch() =>
            new PlayerMatchStats { PlayerId = 1, FromMinute = 0, ToMinute = 90 };

        // ------------------------------------------------------------------ it cannot change the match

        /// <summary>
        /// THE SAFETY ARGUMENT, pinned. Reading the match happens after the last roll, on a report
        /// nothing will touch again — so the very same seed produces the very same match whether
        /// anybody counts it or not, and the hash a golden master is made of does not move.
        /// </summary>
        [Test]
        public void ReadingTheMatch_CannotChangeIt()
        {
            Lineup home = LineupSelector.BestEleven(_league.Clubs[2]);
            Lineup away = LineupSelector.BestEleven(_league.Clubs[5]);

            MatchReport counted = new MatchEngine(Cfg).Simulate(home, away, new Pcg32(8080));
            MatchReport uncounted = new MatchEngine(Cfg, buildStats: false).Simulate(home, away, new Pcg32(8080));

            Assert.That(uncounted.Stats, Is.Null, "the flag has to mean something");
            Assert.That(counted.Stats, Is.Not.Null);
            Assert.That(MatchReportHasher.Hash(counted), Is.EqualTo(MatchReportHasher.Hash(uncounted)),
                "counting a match must not move a single bit of it");
        }

        /// <summary>And the count itself is the same count every time, on every machine.</summary>
        [Test]
        public void TheSameMatch_IsCountedTheSameWay()
        {
            MatchStats first = Match(7).Stats!;
            MatchStats again = Match(7).Stats!;

            Assert.That(again.Players.Count, Is.EqualTo(first.Players.Count));
            for (int i = 0; i < first.Players.Count; i++)
            {
                Assert.That(again.Players[i].PlayerId, Is.EqualTo(first.Players[i].PlayerId));
                Assert.That(again.Players[i].Rating, Is.EqualTo(first.Players[i].Rating));
                Assert.That(again.Players[i].DistanceDm, Is.EqualTo(first.Players[i].DistanceDm));
                Assert.That(again.Players[i].PassesCompleted, Is.EqualTo(first.Players[i].PassesCompleted));
            }

            Assert.That(again.Home.PossessionPermille, Is.EqualTo(first.Home.PossessionPermille));
            Assert.That(again.Home.XgPermille, Is.EqualTo(first.Home.XgPermille));
        }
    }
}
