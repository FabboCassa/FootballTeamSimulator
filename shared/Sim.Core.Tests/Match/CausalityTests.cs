using System;
using System.Collections.Generic;
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
    /// THE CAUSALITY, INVERTED (engine phase 6 — docs/engine/MATCH_ENGINE_PLAN.md §13).
    ///
    /// Every phase before this one made the picture better while the SCORE still came from
    /// somewhere else: a minute-by-minute statistical model decided who scored and when, and a
    /// director then worked the ball toward the elected shooter, switching on super-powers for
    /// forty-five ticks at a time so it would arrive. The consequence, and the reason the whole
    /// rework exists, is that no amount of watching could tell you whether your tactics worked —
    /// the pitch produced nothing.
    ///
    /// Now it produces everything. A man on the ball weighs the SHOT against the pass, the run and
    /// the clearance in the one currency they are all quoted in; he strikes it as well as his
    /// Shooting and Technique let him; a defender may charge it down; the keeper's dive may reach
    /// it and his Goalkeeping may not be equal to it; and a goal is the ball crossing the line
    /// between the posts. The report is written BY that match rather than illustrated by it.
    ///
    /// What the tests below pin is not the calibration — that is the pitch harness's job, and it
    /// holds 20/20 readings inside football's bands — but the CAUSALITY: that no goal exists that
    /// the ball did not score, that the attributes which are supposed to decide one do, and that
    /// the fast path for the world nobody watches still exists and still skips all of it.
    /// </summary>
    [TestFixture]
    public class CausalityTests
    {
        private static League _league = null!;
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        // ------------------------------------------------------------ no goal without a ball

        /// <summary>
        /// The heart of it. Every goal in the report has a Goal in the stream and vice versa, and
        /// every one of those is the ball ACROSS THE LINE AND BETWEEN THE POSTS. Before this phase
        /// the second half of that sentence was untestable, because a goal was awarded by a
        /// timeline and the ball was then snapped to the net to illustrate it.
        /// </summary>
        [Test]
        public void EveryGoal_IsTheBallCrossingTheLine()
        {
            int goals = 0;

            for (int i = 0; i < 20; i++)
            {
                MatchReport r = Match(i);
                PositionStream stream = r.Positions!;

                int streamGoals = stream.Actions.Count(a => a.Kind == BallActionKind.Goal);
                Assert.That(streamGoals, Is.EqualTo(r.HomeGoals + r.AwayGoals),
                    $"match {i}: the report and the picture must be the same match");

                foreach (BallAction a in stream.Actions.Where(x => x.Kind == BallActionKind.Goal))
                {
                    PitchPoint ball = stream.BallAt(a.Tick);
                    int goalX = a.Home ? Pitch.LengthDm : 0;

                    Assert.That(Math.Abs(ball.X - goalX), Is.LessThanOrEqualTo(6),
                        "a goal is the ball ON the line it crossed");
                    Assert.That(Math.Abs(ball.Y - Pitch.CenterY), Is.LessThanOrEqualTo(40),
                        "and between the posts");
                    goals++;
                }
            }

            TestContext.Out.WriteLine($"[causality] {goals} goals in 20 matches, every one of them a ball over a line");
            Assert.That(goals, Is.GreaterThan(30), "sanity: enough goals sampled");
        }

        /// <summary>
        /// And the scoresheet agrees with the picture the other way round: the events say exactly
        /// what the actions say, in the minute they happened in, credited to a man on the pitch.
        /// </summary>
        [Test]
        public void TheEvents_AreWrittenByTheMatch()
        {
            for (int i = 0; i < 12; i++)
            {
                MatchReport r = Match(i);
                var ids = new HashSet<int>(r.Positions!.HomePlayerIds.Concat(r.Positions!.AwayPlayerIds));

                Assert.That(r.Events.Count(e => e.Type == MatchEventType.Goal && e.ClubId == r.HomeClubId),
                    Is.EqualTo(r.HomeGoals), "home goal events must equal home goals");
                Assert.That(r.Events.Count(e => e.Type == MatchEventType.Goal && e.ClubId == r.AwayClubId),
                    Is.EqualTo(r.AwayGoals), "away goal events must equal away goals");
                Assert.That(r.Events.All(e => e.Minute >= 1 && e.Minute <= 90), Is.True,
                    "every event happens inside the ninety minutes");
                Assert.That(r.Events.Select(e => e.Minute), Is.Ordered,
                    "and they are written in the order they happened");
                Assert.That(r.Events.All(e => ids.Contains(e.PlayerId)), Is.True,
                    "every event is credited to somebody who was on the pitch");
            }
        }

        // ------------------------------------------------------------ the shot as a decision

        /// <summary>
        /// A shot is now a DECISION, and this is what says it is a footballer's. Nobody strikes it
        /// from beyond the range a footballer strikes it from, the great majority of strikes come
        /// from inside the box, and the keeper is not a spectator: most of what is on target he
        /// keeps out.
        /// </summary>
        [Test]
        public void TheShot_IsTakenWhereAFootballerTakesOne()
        {
            var analyzer = new MatchAnalyzer();
            int shots = 0, inBox = 0, onTarget = 0, saves = 0;

            for (int i = 0; i < 20; i++)
            {
                MatchReport r = Match(i);
                MatchMetrics m = analyzer.Measure(r)!;
                shots += m.Home.Shots + m.Away.Shots;
                inBox += m.Home.ShotsInBox + m.Away.ShotsInBox;
                onTarget += m.Home.ShotsOnTarget + m.Away.ShotsOnTarget;
                saves += m.Home.Saves + m.Away.Saves;

                // Nobody has a go from his own half. The exact range cannot be read back off the
                // stream — a frame is five ticks and a struck ball covers sixteen metres in that
                // time, so by the frame the action is filed under it has already gone — but which
                // half it is in survives the rounding, because a strike travels toward the goal.
                foreach (BallAction a in r.Positions!.Actions.Where(x => x.Kind == BallActionKind.Shot))
                {
                    PitchPoint ball = r.Positions!.BallAt(a.Tick);
                    Assert.That(a.Home ? ball.X > Pitch.CenterX : ball.X < Pitch.CenterX, Is.True,
                        "a strike is taken in the half a side is attacking, or it is not a strike");
                }
            }

            TestContext.Out.WriteLine(
                $"[causality-shots] {shots / 20.0:F1} a match, {100 * inBox / Math.Max(1, shots)}% from inside the box, " +
                $"{100 * onTarget / Math.Max(1, shots)}% on target, keeper saved {100 * saves / Math.Max(1, onTarget)}% of those");

            Assert.That(shots / 20.0, Is.InRange(15.0, 35.0), "a normal number of shots a match");
            Assert.That(100 * inBox / shots, Is.GreaterThan(50), "most strikes come from inside the box");
            Assert.That(100 * onTarget / shots, Is.InRange(20, 60), "and a footballer's share of them is on target");
            Assert.That(100 * saves / onTarget, Is.GreaterThan(50), "the keeper keeps most of those out");
        }

        /// <summary>
        /// A charged-down strike is its own thing: neither a save nor a shot on target. It was
        /// worth giving the block an action of its own, because it is a quarter of the strikes in
        /// a real match and the difference between "he shot straight at him" and "somebody threw
        /// himself in front of it" is most of what a viewer reads off a defence.
        /// </summary>
        [Test]
        public void ABlock_IsNeitherASaveNorAMiss()
        {
            int blocks = 0;

            for (int i = 0; i < 12; i++)
            {
                List<BallAction> actions = Match(i).Positions!.Actions;
                for (int k = 0; k < actions.Count; k++)
                {
                    if (actions[k].Kind != BallActionKind.Block) continue;
                    blocks++;

                    Assert.That(k > 0, Is.True, "a block cannot be the first thing that happens");
                    Assert.That(
                        actions.Take(k).Last(a => a.Kind == BallActionKind.Shot
                                                  || a.Kind == BallActionKind.Save
                                                  || a.Kind == BallActionKind.Goal
                                                  || a.Kind == BallActionKind.Miss).Kind,
                        Is.EqualTo(BallActionKind.Shot),
                        "a block only ever charges down a strike that was still live");
                }
            }

            TestContext.Out.WriteLine($"[causality-blocks] {blocks / 12.0:F1} strikes charged down a match");
            Assert.That(blocks, Is.GreaterThan(10), "sanity: defenders do get in the way");
        }

        // ------------------------------------------------------------ the attributes decide

        /// <summary>
        /// FINISHING. The same eleven, twice, with nothing changed but Shooting and Technique:
        /// the better finishers must score more. Before this phase this test could not have been
        /// written at all — the scorer came off a weighted role table and the strike was a
        /// picture of a decision made elsewhere.
        /// </summary>
        [Test]
        public void BetterFinishers_ScoreMore()
        {
            int sharp = GoalsOver(30, shooting: 95);
            int blunt = GoalsOver(30, shooting: 25);

            TestContext.Out.WriteLine($"[causality-finishing] shooting 95 scored {sharp}, shooting 25 scored {blunt} (30 matches each)");
            Assert.That(sharp, Is.GreaterThan(blunt), "the better finishers have to score more");
        }

        /// <summary>
        /// GOALKEEPING, the same way: one XI's keeper is a 95, the other's a 25, and nothing else
        /// moves. The save is a dive that reaches it and a contest he has to win, so both halves
        /// of him are in this number.
        /// </summary>
        [Test]
        public void BetterKeepers_ConcedeLess()
        {
            int strong = ConcededOver(30, 95);
            int weak = ConcededOver(30, 25);

            TestContext.Out.WriteLine($"[causality-keeper] goalkeeping 95 conceded {strong}, goalkeeping 25 conceded {weak} (30 matches each)");
            Assert.That(strong, Is.LessThan(weak), "the better keeper has to concede fewer");
        }

        /// <summary>
        /// HOME ADVANTAGE reaches the pitch. The result model has always had it as a multiplier on
        /// three team ratings; with the causality inverted the watched match needs its own, or the
        /// one match the player actually plays is the only one in his league without it.
        /// </summary>
        [Test]
        public void HomeAdvantage_IsPlayedOut()
        {
            int homeWins = 0, awayWins = 0;

            for (int i = 0; i < 60; i++)
            {
                // The SAME club at both ends, so nothing but the end of the pitch differs.
                Club club = _league.Clubs[i % _league.Clubs.Count];
                Lineup eleven = LineupSelector.BestEleven(club);
                MatchReport r = new MatchEngine(Cfg, applyCondition: true, applyMatchFatigue: true)
                    .Simulate(eleven, LineupSelector.BestEleven(club), new Pcg32(77_000UL + (ulong)i));

                if (r.HomeGoals > r.AwayGoals) homeWins++;
                else if (r.AwayGoals > r.HomeGoals) awayWins++;
            }

            TestContext.Out.WriteLine($"[causality-home] a club against itself, 60 times: {homeWins} home wins, {awayWins} away wins");
            Assert.That(homeWins, Is.GreaterThan(awayWins), "the same side, at home, must do better than away");
        }

        /// <summary>
        /// CONDITION AND TIREDNESS reach it too — and, just as importantly, they are the identity
        /// when they are switched off. A neutral squad with the flags on has to play exactly the
        /// match it plays with them off, or every golden master in the project moves the day
        /// somebody turns a flag on.
        /// </summary>
        [Test]
        public void Condition_ReachesThePitch_AndIsTheIdentityWhenNeutral()
        {
            Club home = _league.Clubs[3], away = _league.Clubs[8];
            Lineup h = LineupSelector.BestEleven(home), a = LineupSelector.BestEleven(away);

            // NEUTRAL, said out loud. A generated squad carries whatever form and fitness the
            // world gave it, so "the flag on is the identity" is a claim about a neutral squad and
            // has to be set up as one — otherwise the test is really asking whether these
            // particular twenty-two men happen to be average today.
            foreach (LineupSlot slot in h.Slots.Concat(a.Slots))
            {
                slot.Player.Condition.Form = Cfg.Condition.FormNeutral;
                slot.Player.Condition.Morale = Cfg.Condition.MoraleNeutral;
                slot.Player.Condition.Fitness = 100;
            }

            ulong off = MatchReportHasher.Hash(
                new MatchEngine(Cfg).Simulate(h, a, new Pcg32(4242)));
            ulong on = MatchReportHasher.Hash(
                new MatchEngine(Cfg, applyCondition: true).Simulate(h, a, new Pcg32(4242)));

            Assert.That(on, Is.EqualTo(off),
                "a squad at neutral condition must play the identical match with the flag on");

            // And a squad that is out of form does not.
            foreach (LineupSlot slot in h.Slots) slot.Player.Condition.Form = 10;
            ulong tired = MatchReportHasher.Hash(
                new MatchEngine(Cfg, applyCondition: true).Simulate(h, a, new Pcg32(4242)));
            foreach (LineupSlot slot in h.Slots) slot.Player.Condition.Form = Cfg.Condition.FormNeutral;

            Assert.That(tired, Is.Not.EqualTo(off), "and a squad out of form must not play the same match");
        }

        // ------------------------------------------------------------ the world still has its fast path

        /// <summary>
        /// The other half of the phase, and the reason every league table and every balance check
        /// in the project reads what it read before: the world nobody watches still goes through
        /// the minute model, at a millisecond a match, and never builds a stream.
        /// </summary>
        [Test]
        public void TheWorld_StillTakesTheFastPath()
        {
            var quick = new MatchEngine(generatePositions: false);
            int goals = 0;

            for (ulong seed = 900; seed < 950; seed++)
            {
                MatchReport r = quick.Simulate(
                    LineupSelector.BestEleven(_league.Clubs[2]),
                    LineupSelector.BestEleven(_league.Clubs[9]),
                    new Pcg32(seed));

                Assert.That(r.Positions, Is.Null, "the world's path must not build a picture");
                Assert.That(r.Events.Count(e => e.Type == MatchEventType.Goal),
                    Is.EqualTo(r.HomeGoals + r.AwayGoals), "and its events must still match its score");
                goals += r.HomeGoals + r.AwayGoals;
            }

            TestContext.Out.WriteLine($"[causality-fastpath] 50 background matches, {goals / 50.0:F2} goals a match");
            Assert.That(goals / 50.0, Is.InRange(1.8, 3.6), "the fast model still produces football scores");
        }

        /// <summary>A substitution reaches the pitch: the man who comes on is in the picture.</summary>
        [Test]
        public void ASubstitute_ComesOn()
        {
            Club home = _league.Clubs[1], away = _league.Clubs[6];
            Lineup eleven = LineupSelector.BestEleven(home);
            Lineup subbed = WithOneSubstitution(home, eleven);
            int newMan = subbed.Slots.Select(s => s.Player.Id).Except(eleven.Slots.Select(s => s.Player.Id)).Single();

            MatchPlan plan = new MatchPlan(new MatchInput(eleven, LineupSelector.BestEleven(away)))
                .WithChange(60, new MatchInput(subbed, LineupSelector.BestEleven(away)));

            MatchReport r = new MatchEngine().Simulate(plan, new Pcg32(555));

            Assert.That(r.Positions!.HomePlayerIds, Does.Contain(newMan),
                "the substitute has to be in the picture once he is on");
            Assert.That(r.Positions!.HomeShirts.Distinct().Count(), Is.EqualTo(r.Positions!.HomeShirts.Length),
                "and he takes a shirt nobody else is wearing");
        }

        /// <summary>The whole thing is still a pure function of (plan, seed).</summary>
        [Test]
        public void TheInvertedEngine_IsStillDeterministic()
        {
            ulong first = MatchReportHasher.Hash(Match(5));
            ulong again = MatchReportHasher.Hash(Match(5));
            ulong other = MatchReportHasher.Hash(Match(6));

            Assert.That(again, Is.EqualTo(first), "the same seed has to play the same match");
            Assert.That(other, Is.Not.EqualTo(first), "and a different one a different match");
        }

        // ------------------------------------------------------------ machinery

        /// <summary>A match between two mid-table sides, seeded off the index so a sweep replays.</summary>
        private static MatchReport Match(int index)
        {
            Club home = _league.Clubs[(2 * index) % _league.Clubs.Count];
            Club away = _league.Clubs[(2 * index + 1) % _league.Clubs.Count];
            return new MatchEngine().Simulate(
                LineupSelector.BestEleven(home), LineupSelector.BestEleven(away),
                new Pcg32(31_000UL + (ulong)index));
        }

        /// <summary>
        /// Goals scored by the home side over a sweep, with its outfielders' finishing set to
        /// <paramref name="shooting"/>. The squads are the fixture's own, so every attribute this
        /// touches is put back before it returns: a test that leaves a league of 95-rated finishers
        /// behind it poisons every other test in the file, and which order they run in is not ours
        /// to decide.
        /// </summary>
        private static int GoalsOver(int matches, int shooting)
        {
            int goals = 0;
            for (int i = 0; i < matches; i++)
            {
                Club home = _league.Clubs[(2 * i) % _league.Clubs.Count];
                Club away = _league.Clubs[(2 * i + 1) % _league.Clubs.Count];
                Lineup eleven = LineupSelector.BestEleven(home);

                var saved = new List<(Player Man, int Shooting, int Technique)>();
                foreach (LineupSlot slot in eleven.Slots)
                {
                    if (slot.Role == PositionRole.Goalkeeper) continue;
                    saved.Add((slot.Player, slot.Player.Attributes.Shooting, slot.Player.Attributes.Technique));
                    slot.Player.Attributes.Shooting = shooting;
                    slot.Player.Attributes.Technique = shooting;
                }

                goals += new MatchEngine().Simulate(
                    eleven, LineupSelector.BestEleven(away),
                    new Pcg32(61_000UL + (ulong)i)).HomeGoals;

                foreach ((Player man, int s, int t) in saved)
                {
                    man.Attributes.Shooting = s;
                    man.Attributes.Technique = t;
                }
            }

            return goals;
        }

        /// <summary>Goals conceded by the home side over a sweep, with its keeper's Goalkeeping set (and put back).</summary>
        private static int ConcededOver(int matches, int goalkeeping)
        {
            int conceded = 0;
            for (int i = 0; i < matches; i++)
            {
                Club home = _league.Clubs[(2 * i) % _league.Clubs.Count];
                Club away = _league.Clubs[(2 * i + 1) % _league.Clubs.Count];
                Lineup eleven = LineupSelector.BestEleven(home);

                Player keeper = eleven.Slots.First(s => s.Role == PositionRole.Goalkeeper).Player;
                int was = keeper.Attributes.Goalkeeping;
                keeper.Attributes.Goalkeeping = goalkeeping;

                conceded += new MatchEngine().Simulate(
                    eleven, LineupSelector.BestEleven(away),
                    new Pcg32(62_000UL + (ulong)i)).AwayGoals;

                keeper.Attributes.Goalkeeping = was;
            }

            return conceded;
        }

        /// <summary>Replaces the first non-GK starter with a squad player not already in the XI.</summary>
        private static Lineup WithOneSubstitution(Club club, Lineup eleven)
        {
            var ids = new HashSet<int>(eleven.Slots.Select(s => s.Player.Id));
            Player bench = club.Squad.Players.First(p => !ids.Contains(p.Id));

            var result = new Lineup { ClubId = eleven.ClubId };
            bool swapped = false;
            foreach (LineupSlot slot in eleven.Slots)
            {
                if (!swapped && slot.Role != PositionRole.Goalkeeper)
                {
                    result.Slots.Add(new LineupSlot { Role = slot.Role, Player = bench });
                    swapped = true;
                }
                else
                {
                    result.Slots.Add(new LineupSlot { Role = slot.Role, Player = slot.Player });
                }
            }

            return result;
        }
    }
}
