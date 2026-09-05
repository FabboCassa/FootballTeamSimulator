using System;
using System.Collections.Generic;
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
    /// The laws of the game (engine phase 5 — docs/engine/MATCH_ENGINE_PLAN.md §12).
    ///
    /// Four of the five readings the pitch harness still had outside the band real football
    /// produces were the referee's, and they were all zero or near it for the same reason: nobody
    /// was refereeing. A ball only went out of play while it was LOOSE — a man carrying it over
    /// the touchline was quietly clamped back inside, which the harness counted as fifty ticks a
    /// match of a held ball resting on a line of the pitch — there was no offside line at all, and
    /// a challenge could only be won or lost, never mistimed. So: 18 throw-ins a match against
    /// football's 30-50, 0.3 corners against 8-13, no offsides, and no fouls.
    ///
    /// Everything below is read off the POSITION STREAM, the same way the balance harness reads
    /// it, so a test here cannot pass by agreeing with an internal number that is itself wrong.
    /// What is pinned is the law, not the calibration: that the restart goes the right way, that
    /// the flag belongs to the passing side, that a foul stops the game and gives the ball back,
    /// that a sent-off man takes no further part — and, the one reading that is a number, that
    /// each of the four is inside football's band.
    ///
    /// And ONE thing the referee must never do: change the score. The strike does not decide the
    /// goal until phase 6, so the picture and the 1.4 result model still have to agree on every
    /// goal in the match.
    /// </summary>
    [TestFixture]
    public class RefereeTests
    {
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        // ------------------------------------------------------------ the ball out of play

        /// <summary>
        /// THE §1.7 closure. A ball at a player's feet is out the moment it crosses a line, and the
        /// restart is a throw-in, a corner or a goal kick from the point it actually crossed. Two
        /// things are asserted: the ball is NEVER resting on a line of the pitch while somebody
        /// holds it (the harness's own contract check, which failed at fifty ticks a match before
        /// this phase), and the restarts land in the numbers football produces.
        /// </summary>
        [Test]
        public void TheBallGoesOut_AndTheRestartsAreFootballs()
        {
            var analyzer = new MatchAnalyzer();
            double throwIns = 0, corners = 0, goalKicks = 0;
            long onALine = 0;

            // Thirty rather than a dozen: these are counts of rare events and the bands are narrow,
            // so a handful of matches would be judging the noise rather than the engine. The band
            // proper is the pitch harness's (200 matches); what is pinned here is that a normal
            // sample of matches lands in it.
            const int matches = 30;

            for (int i = 0; i < matches; i++)
            {
                MatchMetrics m = analyzer.Measure(Match(i))!;
                throwIns += m.TotalThrowIns;
                corners += m.TotalCorners;
                goalKicks += m.Home.GoalKicks + m.Away.GoalKicks;
                onALine += m.BallOnLineWhileHeldTicks;
            }

            TestContext.Out.WriteLine(
                $"[laws-restarts] {throwIns / matches:F1} throw-ins · {corners / matches:F1} corners · " +
                $"{goalKicks / matches:F1} goal kicks a match, and {onALine} frames in {matches} matches " +
                "with a held ball on a line");

            Assert.That(onALine, Is.Zero,
                "the laws call a ball on a line a throw-in: it must never be sitting there in somebody's feet");
            Assert.That(throwIns / matches, Is.InRange(30.0, 50.0), "real football takes 30-50 throw-ins a match");
            Assert.That(corners / matches, Is.InRange(8.0, 13.0), "and wins 8-13 corners");
            Assert.That(goalKicks / matches, Is.InRange(8.0, 24.0), "and its keeper takes 8-24 goal kicks");
        }

        /// <summary>
        /// A restart is always awarded AGAINST the side that put the ball out. Checked through the
        /// stream: the man who last touched the ball before a throw-in or a corner is never of the
        /// side that gets to take it.
        /// </summary>
        [Test]
        public void ARestart_NeverGoesToTheSideThatPutItOut()
        {
            int checkedRestarts = 0;

            for (int i = 0; i < 8; i++)
            {
                PositionStream stream = Match(i).Positions!.Unpack();
                List<BallAction> actions = stream.Actions;

                for (int a = 1; a < actions.Count; a++)
                {
                    BallActionKind kind = actions[a].Kind;
                    if (kind != BallActionKind.ThrowIn && kind != BallActionKind.Corner) continue;

                    // The touch before it. A SAVE is deliberately not in the list: it is recorded
                    // for whatever the timeline said the strike was, including a shot that ran wide
                    // without the keeper ever getting near it, so it does not tell you whose touch
                    // was the last one on the ball.
                    BallAction before = actions[a - 1];
                    if (!IsTouch(before.Kind)) continue;

                    checkedRestarts++;
                    Assert.That(actions[a].Home, Is.Not.EqualTo(before.Home),
                        $"{kind} after a {before.Kind} by the same side: the restart went the wrong way");
                }
            }

            Assert.That(checkedRestarts, Is.GreaterThan(100), "sanity: enough restarts sampled");
        }

        // ------------------------------------------------------------ offside

        /// <summary>
        /// Law 11. The flag is raised at the moment the ball is played and answered only if one of
        /// the men beyond the line then touches it; the free kick goes the other way, from where he
        /// was standing. Two law properties are checked on top of the count: the offside is always
        /// given against the side that was passing, and it is never given in that side's own half —
        /// you cannot be offside in your own half.
        /// </summary>
        [Test]
        public void Offside_IsGiven_AgainstTheAttackingSide_AndNeverInItsOwnHalf()
        {
            var analyzer = new MatchAnalyzer();
            double offsides = 0;
            int checkedFlags = 0;
            const int matches = 12;

            for (int i = 0; i < matches; i++)
            {
                MatchReport report = Match(i);
                offsides += analyzer.Measure(report)!.TotalOffsides;

                PositionStream stream = report.Positions!.Unpack();
                List<BallAction> actions = stream.Actions;
                int frames = stream.TickCount;

                for (int a = 0; a < actions.Count; a++)
                {
                    if (actions[a].Kind != BallActionKind.Offside) continue;
                    checkedFlags++;

                    Assert.That(a + 1, Is.LessThan(actions.Count), "an offside is always followed by its free kick");
                    Assert.That(actions[a + 1].Kind, Is.EqualTo(BallActionKind.FreeKick),
                        "the flag has to produce a free kick");
                    Assert.That(actions[a + 1].Home, Is.Not.EqualTo(actions[a].Home),
                        "and the free kick goes to the other side");

                    // The ball is placed on the spot for the free kick, so the stream says where
                    // the offence was: in the offending side's ATTACKING half, always.
                    int frame = Math.Min(actions[a + 1].Tick, frames - 1);
                    int spotX = stream.BallXY[frame * 2];
                    int depth = actions[a].Home ? spotX : Pitch.LengthDm - spotX;
                    Assert.That(depth, Is.GreaterThanOrEqualTo(Pitch.CenterX - 1),
                        "nobody is ever offside in his own half");
                }
            }

            TestContext.Out.WriteLine(
                $"[laws-offside] {offsides / matches:F1} offsides a match, {checkedFlags} flags checked");

            Assert.That(offsides / matches, Is.InRange(1.5, 5.0), "real football flags 1.5-5 offsides a match");
        }

        // ------------------------------------------------------------ fouls, cards, penalties

        /// <summary>
        /// Law 12. A mistimed challenge stops the game, the ball goes back to the side that was
        /// fouled, and the cards and penalties come out at football's rate.
        /// </summary>
        [Test]
        public void AFoul_StopsTheGame_AndGivesTheBallBack()
        {
            var analyzer = new MatchAnalyzer();
            double fouls = 0, yellows = 0, reds = 0, penalties = 0;
            int checkedFouls = 0;

            // More matches than the other readings: a foul count is the noisiest of the four, and a
            // band this narrow should not be judged on a dozen games.
            const int matches = 20;

            for (int i = 0; i < matches; i++)
            {
                MatchReport report = Match(i);
                MatchMetrics m = analyzer.Measure(report)!;
                fouls += m.TotalFouls;
                yellows += m.TotalYellowCards;
                reds += m.TotalRedCards;
                penalties += m.TotalPenalties;

                List<BallAction> actions = report.Positions!.Actions;
                for (int a = 0; a < actions.Count; a++)
                {
                    if (actions[a].Kind != BallActionKind.Foul) continue;
                    checkedFouls++;

                    // The card, if there is one, comes between the whistle and the restart.
                    int next = a + 1;
                    while (next < actions.Count
                           && (actions[next].Kind == BallActionKind.YellowCard
                               || actions[next].Kind == BallActionKind.RedCard))
                    {
                        Assert.That(actions[next].Home, Is.EqualTo(actions[a].Home),
                            "a card is shown to the man who committed the foul");
                        next++;
                    }

                    Assert.That(next, Is.LessThan(actions.Count), "a foul always has a restart after it");
                    Assert.That(
                        actions[next].Kind == BallActionKind.FreeKick
                        || actions[next].Kind == BallActionKind.Penalty,
                        "a foul is a free kick or a penalty");
                    Assert.That(actions[next].Home, Is.Not.EqualTo(actions[a].Home),
                        "and it is taken by the side that was fouled");
                }
            }

            TestContext.Out.WriteLine(
                $"[laws-fouls] {fouls / matches:F1} fouls · {yellows / matches:F2} yellows · " +
                $"{reds / matches:F2} reds · {penalties / matches:F2} penalties a match " +
                $"({checkedFouls} fouls checked)");

            Assert.That(fouls / matches, Is.InRange(18.0, 28.0), "real football gives away 18-28 fouls a match");
            Assert.That(yellows / matches, Is.InRange(1.5, 6.0), "and shows a handful of yellows");
            Assert.That(reds / matches, Is.LessThan(0.6), "a red card is a rare thing");
            Assert.That(penalties / matches, Is.LessThan(0.8), "and so is a penalty");
        }

        /// <summary>
        /// THE phase-5 assertion, and the counterpart of phase 4's `[ball-skill]`: the same
        /// twenty-two footballers, twice, with one side's Defending turned up and the other's
        /// turned down and everything else — Pace, Strength, Positioning, the formation, the
        /// instructions, the seed — identical. A worse tackler's foot arrives instead of the ball,
        /// so he gives away more free kicks and collects more cards. Before this phase Defending
        /// could only ever win the ball, never mistime the challenge.
        /// </summary>
        [Test]
        public void APoorTackler_GivesAwayMoreFreeKicks_ThanAGoodOne()
        {
            var analyzer = new MatchAnalyzer();
            double goodFouls = 0, poorFouls = 0, goodCards = 0, poorCards = 0;
            const int matches = 10;

            for (int i = 0; i < matches; i++)
            {
                Club good = Tacklers(_league.Clubs[(2 * i) % _league.Clubs.Count], 90);
                Club poor = Tacklers(_league.Clubs[(2 * i + 1) % _league.Clubs.Count], 20);

                MatchMetrics m = analyzer.Measure(new MatchEngine().Simulate(
                    LineupSelector.BestEleven(good), LineupSelector.BestEleven(poor),
                    new Pcg32(700UL + (ulong)i)))!;

                goodFouls += m.Home.Fouls;
                poorFouls += m.Away.Fouls;
                goodCards += m.Home.YellowCards + m.Home.RedCards;
                poorCards += m.Away.YellowCards + m.Away.RedCards;
            }

            TestContext.Out.WriteLine(
                $"[laws-tackling] a side of 90 tacklers gave away {goodFouls / matches:F1} fouls a match and " +
                $"{goodCards / matches:F2} cards; a side of 20 tacklers {poorFouls / matches:F1} and " +
                $"{poorCards / matches:F2}");

            Assert.That(poorFouls, Is.GreaterThan(goodFouls * 1.2),
                "a worse tackler's foot arrives instead of the ball: he fouls markedly more");
            Assert.That(poorCards, Is.GreaterThanOrEqualTo(goodCards),
                "and he collects at least as many cards for it");
        }

        /// <summary>
        /// A sent-off man leaves the field of play, and his side finishes the match with ten: he
        /// never touches the ball again, and he stands still on the touchline by the halfway line
        /// from the moment the card is shown.
        /// </summary>
        [Test]
        public void ASentOffMan_TakesNoFurtherPart()
        {
            int reds = 0;

            // Reds are rare, so this sweeps enough matches to be sure of finding some rather than
            // asserting on a single one.
            for (int i = 0; i < 40; i++)
            {
                PositionStream stream = Match(i).Positions!.Unpack();
                int frames = stream.TickCount;

                foreach (BallAction card in stream.Actions)
                {
                    if (card.Kind != BallActionKind.RedCard) continue;
                    reds++;

                    int owner = stream.OwnerCode(card.Home, card.Slot);
                    int[] side = card.Home ? stream.HomeXY : stream.AwayXY;
                    int touchline = card.Home ? 0 : Pitch.WidthDm;

                    for (int t = card.Tick + 1; t < frames; t++)
                        Assert.That(stream.Owner[t], Is.Not.EqualTo(owner),
                            "a sent-off man never touches the ball again");

                    // He WALKS off rather than vanishing — nothing in this engine may teleport — so
                    // where he has to be is at the touchline by the halfway line by the end of it,
                    // and standing still there.
                    if (card.Tick + 40 >= frames) continue;
                    for (int t = frames - 5; t < frames; t++)
                    {
                        Assert.That(stream.PlayerY(side, t, card.Slot), Is.EqualTo(touchline),
                            "a sent-off man ends up off the field of play");
                        Assert.That(stream.PlayerX(side, t, card.Slot), Is.EqualTo(Pitch.CenterX).Within(6),
                            "beside the halfway line");
                    }
                }
            }

            TestContext.Out.WriteLine($"[laws-cards] {reds} men sent off in 40 matches");
            Assert.That(reds, Is.GreaterThan(0), "sanity: the sweep has to contain at least one red card");
        }

        // ------------------------------------------------------------ half time

        /// <summary>
        /// Law 7. The second half is kicked off from the centre spot by the side that did NOT kick
        /// off the first, after the interval — and with both sides in their own half, which is what
        /// makes the restart legal rather than merely central.
        /// </summary>
        [Test]
        public void TheSecondHalf_IsKickedOffByTheOtherSide_FromTheCentre()
        {
            var cfg = new BalanceConfig().Match;
            int halfTime = 45 * cfg.FramesPerMinute;
            int checkedMatches = 0;

            for (int i = 0; i < 6; i++)
            {
                PositionStream stream = Match(i).Positions!.Unpack();

                // The whistle, and the kickoff that follows it. The whistle can be a moment late —
                // the referee does not blow with a strike in the air or a chance due — so it is
                // found rather than assumed, which is what the action is in the stream for.
                int whistleAt = -1, kickoffAt = -1;
                bool awayKicksOff = false;
                for (int a = 0; a < stream.Actions.Count; a++)
                {
                    if (stream.Actions[a].Kind != BallActionKind.HalfTime) continue;
                    whistleAt = stream.Actions[a].Tick;
                    for (int b = a + 1; b < stream.Actions.Count; b++)
                        if (stream.Actions[b].Kind == BallActionKind.Kickoff)
                        {
                            kickoffAt = stream.Actions[b].Tick;
                            awayKicksOff = !stream.Actions[b].Home;
                            break;
                        }

                    break;
                }

                Assert.That(whistleAt, Is.GreaterThanOrEqualTo(halfTime), "half-time is blown at the half-way point");
                Assert.That(kickoffAt, Is.GreaterThanOrEqualTo(whistleAt), "the second half has to be kicked off");
                Assert.That(awayKicksOff, Is.True,
                    "the away side kicks off the second half: the home side kicked off the first");

                int frame = whistleAt;
                Assert.That(stream.BallXY[frame * 2], Is.EqualTo(Pitch.CenterX).Within(1),
                    "from the centre spot");
                Assert.That(stream.BallXY[frame * 2 + 1], Is.EqualTo(Pitch.CenterY).Within(1));

                // Law 8: both sides in their own half while the ball is not yet in play. Read on the
                // LAST frame the ball is still sitting on the centre spot — that is the interval,
                // however long the pause is set to be, and by then the blocks have squeezed back
                // behind the halfway line and everybody caught in the wrong half has run home.
                int settled = frame;
                while (settled + 1 < stream.TickCount
                       && stream.BallXY[(settled + 1) * 2] == stream.BallXY[frame * 2]
                       && stream.BallXY[(settled + 1) * 2 + 1] == stream.BallXY[frame * 2 + 1])
                    settled++;

                // The interval can be cut short by the 1.4 timeline: a chance whose minute falls
                // inside it un-deads the ball for the strike, exactly as it does during a goal
                // celebration. When that happens there is no interval to read Law 8 on, so the
                // match is skipped rather than judged on a frame that is already in open play.
                if (settled < frame + cfg.HalfTimeTicks / cfg.StreamTicksPerFrame - 2) continue;

                // A man sent off before the interval is walking to the touchline and Law 8 has
                // nothing to say about him, so he is left out of the count.
                var walkingOff = new HashSet<int>();
                foreach (BallAction card in stream.Actions)
                    if (card.Kind == BallActionKind.RedCard && card.Tick <= settled)
                        walkingOff.Add(stream.OwnerCode(card.Home, card.Slot));

                for (int slot = 0; slot < stream.PlayerCount; slot++)
                {
                    if (!walkingOff.Contains(stream.OwnerCode(true, slot)))
                        Assert.That(stream.PlayerX(stream.HomeXY, settled, slot),
                            Is.LessThanOrEqualTo(Pitch.CenterX), "the home side stays in its own half");
                    if (!walkingOff.Contains(stream.OwnerCode(false, slot)))
                        Assert.That(stream.PlayerX(stream.AwayXY, settled, slot),
                            Is.GreaterThanOrEqualTo(Pitch.CenterX), "and the away side in its own");
                }

                checkedMatches++;
            }

            TestContext.Out.WriteLine($"[laws-halftime] the interval checked in {checkedMatches} matches");
            Assert.That(checkedMatches, Is.GreaterThan(2), "sanity: most intervals must run undisturbed");
        }

        // ------------------------------------------------------------ what the referee may NOT do

        /// <summary>
        /// The referee does not touch the score. Everything in this phase — the offsides, the
        /// fouls, the cards, the penalties, the blocked shots — is the PICTURE, and the goals still
        /// belong to the 1.4 result model until phase 6 inverts the causality. So the number of
        /// goals in the stream has to be the number on the scoresheet, in every match.
        /// </summary>
        [Test]
        public void TheReferee_NeverChangesTheScore()
        {
            var analyzer = new MatchAnalyzer();

            for (int i = 0; i < 24; i++)
            {
                MatchReport report = Match(i);
                MatchMetrics m = analyzer.Measure(report)!;
                Assert.That(m.GoalsAgree, Is.True,
                    $"match {i}: the picture put {m.StreamGoals} goals in the stream and the report " +
                    $"{report.HomeGoals + report.AwayGoals} on the scoresheet");
            }
        }

        [Test]
        public void TheLaws_AreStillDeterministic()
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

        /// <summary>Is this action a man TOUCHING the ball, rather than the referee's own book-keeping?</summary>
        private static bool IsTouch(BallActionKind kind) =>
            kind == BallActionKind.Pass || kind == BallActionKind.LongBall || kind == BallActionKind.Cross
            || kind == BallActionKind.Dribble || kind == BallActionKind.Clearance
            || kind == BallActionKind.Interception || kind == BallActionKind.Tackle
            || kind == BallActionKind.Shot;

        /// <summary>The same club with every outfielder's Defending set to one level.</summary>
        private static Club Tacklers(Club club, int level)
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

                clone.Attributes.Defending = level;
                clone.Condition.Form = player.Condition.Form;
                clone.Condition.Morale = player.Condition.Morale;
                copy.Squad.Players.Add(clone);
            }

            return copy;
        }
    }
}
