using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Broadcast;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Toasts and commentary on the action frame (spec believable-match R10): every key moment of a
    /// watched match is placed on the stream frame its ball action happened on, not at the end of its
    /// minute, and stamped with the minute the clock shows on that frame.
    /// </summary>
    [TestFixture]
    public class MatchMomentsTests
    {
        private const int Fpm = 120;
        private const int HomeClub = 11;
        private const int AwayClub = 22;

        private static MatchReport Report(List<BallAction> actions, List<SlotChange>? changes = null, params MatchEvent[] events)
        {
            var stream = new PositionStream
            {
                TicksPerMinute = Fpm,
                PlayerCount = 11,
                LastTick = 90 * Fpm,
                HomePlayerIds = Enumerable.Range(100, 11).ToArray(),
                AwayPlayerIds = Enumerable.Range(200, 11).ToArray(),
                Actions = actions,
                Changes = changes ?? new List<SlotChange>()
            };

            return new MatchReport
            {
                HomeClubId = HomeClub,
                AwayClubId = AwayClub,
                Events = events.ToList(),
                Positions = stream
            };
        }

        private static MatchEvent Event(int minute, MatchEventType type, int club, int player = 0) =>
            new MatchEvent { Minute = minute, Type = type, ClubId = club, PlayerId = player };

        [Test]
        public void AChance_FiresOnTheFrameOfItsOutcome_NotAtTheEndOfItsMinute()
        {
            // Struck at 250, wide at 262: minute 3 (frames 240..359). The old toast waited for frame 360.
            var actions = new List<BallAction>
            {
                new BallAction(250, BallActionKind.Shot, true, 9, -1),
                new BallAction(262, BallActionKind.Miss, true, 9, -1)
            };
            MatchReport report = Report(actions, null, Event(3, MatchEventType.ChanceMissed, HomeClub, 109));

            MatchMoment m = MatchMoments.Of(report).Single();

            Assert.That(m.Frame, Is.EqualTo(262));
            Assert.That(m.Minute, Is.EqualTo(3));
            Assert.That(m.Kind, Is.EqualTo(MomentKind.Missed));
            Assert.That(m.Home, Is.True);
            Assert.That(m.PlayerId, Is.EqualTo(109));
            Assert.That(m.Event, Is.SameAs(report.Events[0]));
        }

        [Test]
        public void EveryKeyMoment_MapsToItsActionFrame()
        {
            var actions = new List<BallAction>
            {
                new BallAction(480, BallActionKind.Shot, true, 9, -1),
                new BallAction(500, BallActionKind.Save, false, 0, -1),        // away keeper saves a home shot, minute 5
                new BallAction(690, BallActionKind.Shot, true, 7, -1),
                new BallAction(700, BallActionKind.Goal, true, 7, -1),         // minute 6
                new BallAction(890, BallActionKind.Shot, false, 4, -1),
                new BallAction(900, BallActionKind.Block, true, 5, -1),        // home blocks an away shot, minute 8
                new BallAction(1000, BallActionKind.Foul, false, 4, 9),
                new BallAction(1000, BallActionKind.YellowCard, false, 4, -1),
                new BallAction(1000, BallActionKind.Penalty, true, 9, -1),
                new BallAction(1100, BallActionKind.Foul, true, 3, 2),
                new BallAction(1100, BallActionKind.RedCard, true, 3, -1)
            };
            var changes = new List<SlotChange> { new SlotChange(1200, true, 9, 150, 109, 19, 9) };
            MatchReport report = Report(actions, changes,
                Event(5, MatchEventType.ChanceSaved, HomeClub, 109),
                Event(6, MatchEventType.Goal, HomeClub, 107),
                Event(8, MatchEventType.ChanceMissed, AwayClub, 204),
                new MatchEvent { Minute = 12, Type = MatchEventType.Shout, ClubId = AwayClub, Shout = TouchlineShout.PressHigh });

            MatchMoment[] moments = MatchMoments.Of(report);

            Assert.That(moments.Select(m => (m.Kind, m.Frame, m.Home)), Is.EqualTo(new[]
            {
                (MomentKind.Saved, 500, true),
                (MomentKind.Goal, 700, true),
                (MomentKind.Missed, 900, false),
                (MomentKind.YellowCard, 1000, false),
                (MomentKind.Penalty, 1000, true),
                (MomentKind.RedCard, 1100, true),
                (MomentKind.Substitution, 1200, true),
                (MomentKind.Shout, 11 * Fpm, false) // no action of its own: the first frame of its minute
            }));

            foreach (MatchMoment m in moments)
                Assert.That(m.Minute, Is.EqualTo(MatchClock.MinuteOf(m.Frame, Fpm)), m.Kind.ToString());

            Assert.That(moments.Single(m => m.Kind == MomentKind.YellowCard).PlayerId, Is.EqualTo(204));
            Assert.That(moments.Single(m => m.Kind == MomentKind.RedCard).PlayerId, Is.EqualTo(103));
            // The penalty taker was substituted later: the moment names the man on the pitch at the time.
            Assert.That(moments.Single(m => m.Kind == MomentKind.Penalty).PlayerId, Is.EqualTo(109));
            MatchMoment sub = moments.Single(m => m.Kind == MomentKind.Substitution);
            Assert.That(sub.PlayerId, Is.EqualTo(150));
            Assert.That(sub.Change.OffPlayerId, Is.EqualTo(109));
            Assert.That(sub.Event, Is.Null);
        }

        [Test]
        public void TwoChancesOfOneSideInOneMinute_TakeTwoDistinctFrames()
        {
            var actions = new List<BallAction>
            {
                new BallAction(130, BallActionKind.Miss, true, 9, -1),
                new BallAction(170, BallActionKind.Miss, true, 7, -1)
            };
            MatchReport report = Report(actions, null,
                Event(2, MatchEventType.ChanceMissed, HomeClub, 109),
                Event(2, MatchEventType.ChanceMissed, HomeClub, 107));

            Assert.That(MatchMoments.Of(report).Select(m => m.Frame), Is.EqualTo(new[] { 130, 170 }));
        }

        [Test]
        public void AnOutcomeRoundedOntoTheNextMinute_StillMatchesItsEvent()
        {
            // Struck in the last tick of minute 4, filed on frame 480 — the first frame of minute 5.
            var actions = new List<BallAction> { new BallAction(480, BallActionKind.Goal, false, 9, -1) };
            MatchReport report = Report(actions, null, Event(4, MatchEventType.Goal, AwayClub, 209));

            MatchMoment m = MatchMoments.Of(report).Single();

            Assert.That(m.Frame, Is.EqualTo(480));
            Assert.That(m.Minute, Is.EqualTo(5), "the toast reads the minute the clock shows on its frame");
        }

        [Test]
        public void AReportWithoutAStream_FallsBackToItsRecordedMinute()
        {
            var report = new MatchReport
            {
                HomeClubId = HomeClub,
                AwayClubId = AwayClub,
                Events = { Event(17, MatchEventType.Goal, HomeClub, 1) }
            };

            MatchMoment m = MatchMoments.Of(report).Single();

            Assert.That(m.Minute, Is.EqualTo(17));
            Assert.That(m.Kind, Is.EqualTo(MomentKind.Goal));
        }

        [Test]
        public void TheClock_ReadsTheMinuteInProgress_LikeTheScoresheet()
        {
            Assert.That(MatchClock.MinuteOf(0, Fpm), Is.EqualTo(1));
            Assert.That(MatchClock.MinuteOf(Fpm - 1, Fpm), Is.EqualTo(1));
            Assert.That(MatchClock.MinuteOf(Fpm, Fpm), Is.EqualTo(2));
            Assert.That(MatchClock.MinuteOf(90 * Fpm, Fpm), Is.EqualTo(90));
            Assert.That(MatchClock.InProgress(0), Is.EqualTo(1));
            Assert.That(MatchClock.InProgress(1), Is.EqualTo(2));
            Assert.That(MatchClock.InProgress(BroadcastPlayback.FullTimeMinute), Is.EqualTo(90));
            Assert.That(MatchClock.FirstFrameOf(3, Fpm), Is.EqualTo(2 * Fpm));
        }

        [Test]
        public void ARealMatch_PutsEveryKeyMomentOnItsActionFrame_AndAgreesWithTheCommentary()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20261024));
            Club home = league.Clubs[3], away = league.Clubs[6];
            var plan = new MatchPlan(new MatchInput(LineupSelector.BestEleven(home), LineupSelector.BestEleven(away)));
            MatchReport report = new MatchEngine(new BalanceConfig()).Simulate(plan, new Pcg32(151));
            PositionStream s = report.Positions!;
            int fpm = s.TicksPerMinute;

            MatchMoment[] moments = MatchMoments.Of(report);

            var outcomes = s.Actions.Where(a => a.Kind == BallActionKind.Goal || a.Kind == BallActionKind.Save
                || a.Kind == BallActionKind.Miss || a.Kind == BallActionKind.Block).ToList();
            List<MatchMoment> chances = moments.Where(m => m.Event != null && m.Kind != MomentKind.Shout).ToList();
            Assert.That(chances, Has.Count.EqualTo(report.Events.Count(e => e.Type != MatchEventType.Shout)));
            Assert.That(chances, Has.Count.EqualTo(outcomes.Count), "one outcome action per chance event");
            Assert.That(chances.Count(m => m.Kind == MomentKind.Goal), Is.EqualTo(report.HomeGoals + report.AwayGoals));

            // Each chance sits within one frame of a distinct outcome of its own kind and side.
            var taken = new HashSet<int>();
            foreach (MatchMoment m in chances)
            {
                int hit = -1;
                for (int j = 0; j < outcomes.Count && hit < 0; j++)
                    if (!taken.Contains(j) && System.Math.Abs(outcomes[j].Tick - m.Frame) <= 1 && Fits(m, outcomes[j]))
                        hit = j;
                Assert.That(hit, Is.GreaterThanOrEqualTo(0), $"{m.Kind} at {m.Event!.Minute}' has no action on frame {m.Frame}");
                taken.Add(hit);
            }

            foreach (BallActionKind kind in new[] { BallActionKind.YellowCard, BallActionKind.RedCard, BallActionKind.Penalty })
            {
                int[] frames = s.Actions.Where(a => a.Kind == kind).Select(a => a.Tick).ToArray();
                Assert.That(moments.Where(m => m.Kind == KindOf(kind)).Select(m => m.Frame), Is.EqualTo(frames), kind.ToString());
            }

            Assert.That(moments.Where(m => m.Kind == MomentKind.Substitution).Select(m => m.Frame),
                Is.EqualTo(s.Changes.Select(c => c.Frame)));

            for (int i = 1; i < moments.Length; i++)
                Assert.That(moments[i].Frame, Is.GreaterThanOrEqualTo(moments[i - 1].Frame), "in playback order");

            // The clock, the toast and the commentary read the same minute for the same moment.
            IReadOnlyList<CommentaryLine> lines = CommentaryBuilder.Build(report, new BroadcastDirector().Build(report));
            foreach (MatchMoment m in moments)
            {
                Assert.That(m.Minute, Is.EqualTo(MatchClock.MinuteOf(m.Frame, fpm)));
                if (m.Kind == MomentKind.Goal)
                    Assert.That(lines.Any(l => l.Icon == CommentaryIcon.Goal && l.Frame == m.Frame && l.Minute == m.Minute),
                        $"goal at frame {m.Frame} has no commentary line on its frame and minute");
            }
        }

        [TestCase("en.json")]
        [TestCase("it.json")]
        public void TheNewToasts_AreInBothStringTables_WithTheirPlaceholders(string file)
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FootballTeamSimulator.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null, "repo root containing FootballTeamSimulator.sln");
            string path = Path.Combine(dir!, "client", "Assets", "Resources", "Localization", file);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            Dictionary<string, string> table = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");

            var keys = new[]
            {
                ("match.event.yellowcard", 3), ("match.event.redcard", 3),
                ("match.event.penalty", 3), ("match.event.substitution", 4)
            };
            var placeholder = new Regex(@"\{\d+\}");
            foreach ((string key, int args) in keys)
            {
                Assert.That(table.TryGetValue(key, out string? text), Is.True, $"{file} misses {key}");
                string[] found = placeholder.Matches(text!).Select(m => m.Value).Distinct().OrderBy(v => v).ToArray();
                Assert.That(found, Is.EqualTo(Enumerable.Range(0, args).Select(i => "{" + i + "}")), $"{key} in {file}");
            }
        }

        private static bool Fits(MatchMoment m, BallAction a)
        {
            switch (m.Kind)
            {
                case MomentKind.Goal: return a.Kind == BallActionKind.Goal && a.Home == m.Home;
                case MomentKind.Saved: return a.Kind == BallActionKind.Save && a.Home != m.Home;
                default:
                    return (a.Kind == BallActionKind.Miss && a.Home == m.Home)
                        || (a.Kind == BallActionKind.Block && a.Home != m.Home);
            }
        }

        private static MomentKind KindOf(BallActionKind kind) =>
            kind == BallActionKind.YellowCard ? MomentKind.YellowCard
            : kind == BallActionKind.RedCard ? MomentKind.RedCard
            : MomentKind.Penalty;
    }
}
