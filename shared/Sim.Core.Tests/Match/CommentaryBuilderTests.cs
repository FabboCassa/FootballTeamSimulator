using System;
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
    /// The commentary sentence builder (spec watchable-match-engine R14, R15): one sentence per event
    /// chain, per cut summary and per shout, each with its minute and icon, formatted through the
    /// client's own en and it string tables.
    /// </summary>
    [TestFixture]
    public class CommentaryBuilderTests
    {
        private const int Fpm = 120;
        private const int Frames = 90 * Fpm;
        private const int HomeClub = 11;
        private const int AwayClub = 22;

        private static readonly Dictionary<int, string> HomeNames = new Dictionary<int, string>
        {
            [5] = "Rossi", [7] = "Bianchi", [9] = "Verdi"
        };

        private static readonly Dictionary<int, string> AwayNames = new Dictionary<int, string>
        {
            [0] = "Neri", [4] = "Gialli"
        };

        private static Dictionary<string, string>? _en;
        private static Dictionary<string, string>? _it;

        private static Dictionary<string, string> En => _en ??= LoadLoc("en.json");
        private static Dictionary<string, string> It => _it ??= LoadLoc("it.json");

        // ------------------------------------------------------------------ helpers

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FootballTeamSimulator.sln")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new DirectoryNotFoundException("Repo root containing FootballTeamSimulator.sln not found");
        }

        private static Dictionary<string, string> LoadLoc(string file)
        {
            string path = Path.Combine(FindRepoRoot(), "client", "Assets", "Resources", "Localization", file);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }

        /// <summary>A missing key throws, so every sentence formatted here proves its keys exist.</summary>
        private static Func<string, object[], string> Tr(Dictionary<string, string> table) =>
            (key, args) => string.Format(table[key], args);

        private static string NameOf(bool home, int slot) =>
            (home ? HomeNames : AwayNames).TryGetValue(slot, out string? name) ? name : "#" + slot;

        private static string ClubOf(bool home) => home ? "Ulivara" : "Borgo";

        private static string Italian(CommentaryLine line) => CommentaryText.Format(line, Tr(It), NameOf, ClubOf);
        private static string English(CommentaryLine line) => CommentaryText.Format(line, Tr(En), NameOf, ClubOf);

        private static PositionStream Stream() => new PositionStream
        {
            TicksPerMinute = Fpm,
            PlayerCount = 11,
            LastTick = Frames - 1,
            BallXY = new int[Frames * 2],
            Owner = new int[Frames]
        };

        private static MatchReport Report(PositionStream s) =>
            new MatchReport { HomeClubId = HomeClub, AwayClubId = AwayClub, Positions = s };

        /// <summary>The first frame of match minute <paramref name="minute"/> (minute 1 is frames 0-119).</summary>
        private static int At(int minute) => (minute - 1) * Fpm;

        private static void Add(PositionStream s, int frame, BallActionKind kind, bool home, int slot, int target = -1) =>
            s.Actions.Add(new BallAction(frame, kind, home, slot, target));

        private static IReadOnlyList<CommentaryLine> Build(MatchReport report) =>
            CommentaryBuilder.Build(report, BroadcastTimeline.Empty);

        private static CommentaryLine Single(MatchReport report)
        {
            IReadOnlyList<CommentaryLine> lines = Build(report);
            Assert.That(lines, Has.Count.EqualTo(1), string.Join(" | ", lines.Select(English)));
            return lines[0];
        }

        // ------------------------------------------------------------------ the six chain types

        [Test]
        public void ASavedChain_ReadsPassCrossHeaderSave_InOneSentence()
        {
            PositionStream s = Stream();
            int f = At(34);
            Add(s, f, BallActionKind.Pass, true, 5, 7);
            Add(s, f + 4, BallActionKind.Cross, true, 7, 9);
            Add(s, f + 6, BallActionKind.Shot, true, 9);
            Add(s, f + 7, BallActionKind.Save, false, 0);

            CommentaryLine line = Single(Report(s));

            Assert.That(line.Minute, Is.EqualTo(34));
            Assert.That(line.Frame, Is.EqualTo(f + 7), "revealed when the save happens, not before");
            Assert.That(line.Icon, Is.EqualTo(CommentaryIcon.Save));
            Assert.That(line.Highlight, Is.False);
            Assert.That(Italian(line), Is.EqualTo("34' Rossi apre per Bianchi, cross, colpo di testa: parato"));
            Assert.That(English(line), Is.EqualTo("34' Rossi finds Bianchi, cross, header: saved"));
        }

        [Test]
        public void AGoalChain_NamesTheScorer_AndIsHighlighted()
        {
            PositionStream s = Stream();
            int f = At(12);
            Add(s, f, BallActionKind.Pass, true, 5, 9);
            Add(s, f + 3, BallActionKind.Dribble, true, 9);
            Add(s, f + 6, BallActionKind.Shot, true, 9);
            Add(s, f + 7, BallActionKind.Goal, true, 9);

            CommentaryLine line = Single(Report(s));

            Assert.That(line.Minute, Is.EqualTo(12));
            Assert.That(line.Icon, Is.EqualTo(CommentaryIcon.Goal));
            Assert.That(line.Highlight, Is.True);
            Assert.That(Italian(line), Is.EqualTo("12' Rossi apre per Verdi, salta l'uomo, tiro: gol di Verdi!"));
            Assert.That(English(line), Is.EqualTo("12' Rossi finds Verdi, beats his man, shot: Verdi scores!"));
        }

        [Test]
        public void ABlockedChain_NamesTheDefenderWhoChargedItDown()
        {
            PositionStream s = Stream();
            int f = At(40);
            Add(s, f, BallActionKind.LongBall, true, 5, 9);
            Add(s, f + 8, BallActionKind.Shot, true, 9);
            Add(s, f + 9, BallActionKind.Block, false, 4);

            CommentaryLine line = Single(Report(s));

            Assert.That(line.Icon, Is.EqualTo(CommentaryIcon.Block));
            Assert.That(Italian(line), Is.EqualTo("40' Rossi lancia Verdi, tiro: respinto da Gialli"));
            Assert.That(English(line), Is.EqualTo("40' Rossi goes long for Verdi, shot: blocked by Gialli"));
        }

        [Test]
        public void AMissedChain_FromACorner_NamesTheUnannouncedHeader()
        {
            // A corner's cross has no receiver, so the man who meets it has not been named yet.
            PositionStream s = Stream();
            int f = At(67);
            Add(s, f, BallActionKind.Corner, false, 4);
            Add(s, f + 10, BallActionKind.Cross, false, 4);
            Add(s, f + 13, BallActionKind.Shot, false, 0);
            Add(s, f + 14, BallActionKind.Miss, false, 0);

            CommentaryLine line = Single(Report(s));

            Assert.That(line.Icon, Is.EqualTo(CommentaryIcon.Miss));
            Assert.That(Italian(line), Is.EqualTo("67' Calcio d'angolo di Gialli, cross, colpo di testa di Neri: fuori"));
            Assert.That(English(line), Is.EqualTo("67' Gialli takes the corner, cross, Neri heads it: wide"));
        }

        [Test]
        public void AFoulAndItsFreeKick_AreOneSentenceEach()
        {
            PositionStream s = Stream();
            int f = At(52);
            Add(s, f, BallActionKind.Foul, false, 4, 7);
            Add(s, f, BallActionKind.YellowCard, false, 4);
            Add(s, f, BallActionKind.FreeKick, true, 7);
            Add(s, f + 10, BallActionKind.Pass, true, 7, 9);
            Add(s, f + 12, BallActionKind.Shot, true, 9);
            Add(s, f + 13, BallActionKind.Miss, true, 9);

            IReadOnlyList<CommentaryLine> lines = Build(Report(s));

            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[0].Icon, Is.EqualTo(CommentaryIcon.YellowCard));
            Assert.That(lines[0].Highlight, Is.True, "a card is highlighted");
            Assert.That(Italian(lines[0]), Is.EqualTo("52' Fallo di Gialli su Bianchi, ammonito: punizione"));
            Assert.That(English(lines[0]), Is.EqualTo("52' Gialli fouls Bianchi, booked: free kick"));

            Assert.That(lines[1].Icon, Is.EqualTo(CommentaryIcon.Miss));
            Assert.That(Italian(lines[1]), Is.EqualTo("52' Punizione di Bianchi, poi Verdi, tiro: fuori"));
            Assert.That(English(lines[1]), Is.EqualTo("52' Bianchi takes the free kick, on to Verdi, shot: wide"));
        }

        [Test]
        public void AFoulInTheBox_EndsInAPenalty_AndARedCardOutranksIt()
        {
            PositionStream s = Stream();
            int f = At(80);
            Add(s, f, BallActionKind.Foul, false, 4, 9);
            Add(s, f, BallActionKind.RedCard, false, 4);
            Add(s, f, BallActionKind.Penalty, true, 9);
            Add(s, f + 60, BallActionKind.Shot, true, 9);
            Add(s, f + 61, BallActionKind.Goal, true, 9);

            IReadOnlyList<CommentaryLine> lines = Build(Report(s));

            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[0].Icon, Is.EqualTo(CommentaryIcon.RedCard));
            Assert.That(lines[0].Highlight, Is.True);
            Assert.That(Italian(lines[0]), Is.EqualTo("80' Fallo di Gialli su Verdi, espulso: rigore!"));
            Assert.That(English(lines[0]), Is.EqualTo("80' Gialli fouls Verdi, sent off: penalty!"));
            Assert.That(Italian(lines[1]), Is.EqualTo("80' Rigore di Verdi, tiro: gol di Verdi!"));
        }

        [Test]
        public void ACounterAttack_WonInTheOwnHalfAndFinishedQuickly_IsCalledOneOut()
        {
            PositionStream s = Stream();
            int f = At(71);
            s.BallXY[f * 2] = 300; // home's own half
            Add(s, f, BallActionKind.Interception, true, 5);
            Add(s, f + 6, BallActionKind.Pass, true, 5, 9);
            Add(s, f + 16, BallActionKind.Shot, true, 9);
            Add(s, f + 17, BallActionKind.Save, false, 0);

            CommentaryLine line = Single(Report(s));

            Assert.That(line.Icon, Is.EqualTo(CommentaryIcon.Counter));
            Assert.That(Italian(line), Is.EqualTo("71' Contropiede, Rossi intercetta, poi Verdi, tiro: parato"));
            Assert.That(English(line), Is.EqualTo("71' Counter-attack, Rossi intercepts, on to Verdi, shot: saved"));
        }

        [Test]
        public void ACounterThatScores_KeepsTheGoalIcon()
        {
            PositionStream s = Stream();
            int f = At(20);
            s.BallXY[f * 2] = 900; // away's own half: away attacks toward X = 0
            Add(s, f, BallActionKind.Recovery, false, 4);
            Add(s, f + 10, BallActionKind.Shot, false, 4);
            Add(s, f + 11, BallActionKind.Goal, false, 4);

            CommentaryLine line = Single(Report(s));

            Assert.That(line.Icon, Is.EqualTo(CommentaryIcon.Goal));
            Assert.That(English(line), Is.EqualTo("20' Counter-attack, Gialli wins the ball, shot: Gialli scores!"));
        }

        [Test]
        public void ABallWonHighUp_OrAShotTooLateAfterTheWin_IsNoCounter()
        {
            PositionStream high = Stream();
            int f = At(30);
            high.BallXY[f * 2] = 800; // home's attacking half
            Add(high, f, BallActionKind.Recovery, true, 5);
            Add(high, f + 4, BallActionKind.Shot, true, 5);
            Add(high, f + 5, BallActionKind.Miss, true, 5);

            PositionStream slow = Stream();
            slow.BallXY[f * 2] = 300;
            Add(slow, f, BallActionKind.Recovery, true, 5);
            Add(slow, f + 20, BallActionKind.Pass, true, 5, 9);
            Add(slow, f + 60, BallActionKind.Shot, true, 9); // 30 s after the win
            Add(slow, f + 61, BallActionKind.Miss, true, 9);

            Assert.That(English(Single(Report(high))), Is.EqualTo("30' Rossi wins the ball, shot: wide"));
            Assert.That(Single(Report(slow)).Icon, Is.EqualTo(CommentaryIcon.Miss));
        }

        // ------------------------------------------------------------------ chain edges

        [Test]
        public void ALongMove_KeepsOnlyTheLastTouchesBeforeTheShot()
        {
            PositionStream s = Stream();
            int f = At(5);
            Add(s, f, BallActionKind.ThrowIn, true, 5);
            Add(s, f + 4, BallActionKind.Pass, true, 5, 7);
            Add(s, f + 8, BallActionKind.Pass, true, 7, 5);
            Add(s, f + 12, BallActionKind.Pass, true, 5, 7);
            Add(s, f + 16, BallActionKind.Pass, true, 7, 9);
            Add(s, f + 20, BallActionKind.Shot, true, 9);
            Add(s, f + 21, BallActionKind.Miss, true, 9);

            CommentaryLine line = Single(Report(s));

            Assert.That(line.Steps.Count, Is.LessThanOrEqualTo(CommentaryBuilder.MaxBuildUpSteps + 1));
            Assert.That(English(line), Is.EqualTo("5' Bianchi finds Rossi, on to Bianchi, on to Verdi, shot: wide"));
        }

        [Test]
        public void AReboundAfterASave_IsItsOwnSentence_AndOpponentTouchesEndAChain()
        {
            PositionStream s = Stream();
            int f = At(44);
            s.BallXY[(f + 4) * 2] = 800; // won high up: no counter
            Add(s, f, BallActionKind.Pass, false, 4, 0);
            Add(s, f + 4, BallActionKind.Interception, true, 5);
            Add(s, f + 8, BallActionKind.Shot, true, 5);
            Add(s, f + 9, BallActionKind.Save, false, 0);
            Add(s, f + 11, BallActionKind.Shot, true, 9);
            Add(s, f + 12, BallActionKind.Goal, true, 9);

            IReadOnlyList<CommentaryLine> lines = Build(Report(s));

            Assert.That(lines.Select(English), Is.EqualTo(new[]
            {
                "44' Rossi intercepts, shot: saved",
                "44' Verdi shoots: Verdi scores!"
            }));
        }

        [Test]
        public void AGoalWithNoStrikeOfItsOwn_StillGetsASentence()
        {
            // A blocked shot that goes in off the defender: the block already told the strike.
            PositionStream s = Stream();
            int f = At(88);
            Add(s, f, BallActionKind.Shot, true, 9);
            Add(s, f + 1, BallActionKind.Block, false, 4);
            Add(s, f + 3, BallActionKind.Goal, true, 9);

            IReadOnlyList<CommentaryLine> lines = Build(Report(s));

            Assert.That(lines.Select(Italian), Is.EqualTo(new[]
            {
                "88' Tiro di Verdi: respinto da Gialli",
                "88' Gol di Verdi!"
            }));
        }

        // ------------------------------------------------------------------ cut summaries and shouts

        [Test]
        public void EveryCutSummary_GivesExactlyOneSpanSentence()
        {
            var summaries = new[]
            {
                new CutSummary(At(23), At(28), PossessionSide.Home, CutZone.Middle, BallActionKind.Foul),
                new CutSummary(At(30), At(31), PossessionSide.Away, CutZone.Defensive, null),
                new CutSummary(At(46), At(48), PossessionSide.None, CutZone.Attacking, BallActionKind.ThrowIn)
            };
            var timeline = new BroadcastTimeline(Fpm, Frames, At(46), Array.Empty<BroadcastSegment>(), summaries);

            IReadOnlyList<CommentaryLine> lines = CommentaryBuilder.Build(Report(Stream()), timeline);

            Assert.That(lines, Has.Count.EqualTo(summaries.Length));
            Assert.That(lines.All(l => l.Icon == CommentaryIcon.Cut), Is.True);
            Assert.That(lines.Select(l => l.Frame), Is.EqualTo(summaries.Select(c => c.StartFrame)));
            Assert.That(lines.Select(English), Is.EqualTo(new[]
            {
                "23'-27' Ulivara keep the ball in midfield, then a foul",
                "30' Borgo keep the ball in their own half",
                "46'-47' A scrappy spell with nobody in control, then a throw-in"
            }));
            Assert.That(lines.Select(Italian), Is.EqualTo(new[]
            {
                "23'-27' Ulivara fa girare palla a centrocampo, poi un fallo",
                "30' Borgo gestisce palla nella propria metà campo",
                "46'-47' Fase spezzettata, nessuno tiene palla, poi una rimessa laterale"
            }));
        }

        [Test]
        public void EveryShout_GivesOneSentence_NamingTheBench()
        {
            MatchReport report = Report(Stream());
            report.Events.Add(new MatchEvent { Minute = 58, Type = MatchEventType.Shout, ClubId = AwayClub, Shout = TouchlineShout.PressHigh });
            report.Events.Add(new MatchEvent { Minute = 61, Type = MatchEventType.Shout, ClubId = HomeClub, Shout = TouchlineShout.KeepBall });
            report.Events.Add(new MatchEvent { Minute = 61, Type = MatchEventType.Goal, ClubId = HomeClub });

            IReadOnlyList<CommentaryLine> lines = Build(report);

            Assert.That(lines, Has.Count.EqualTo(2), "a goal event is told by its chain, not by the event list");
            Assert.That(lines.All(l => l.Icon == CommentaryIcon.Shout), Is.True);
            Assert.That(lines.Select(l => l.Minute), Is.EqualTo(new[] { 58, 61 }));
            Assert.That(English(lines[0]), Is.EqualTo("58' Borgo bench: “Press high!”"));
            Assert.That(Italian(lines[1]), Is.EqualTo("61' Panchina Ulivara: “Calma, teniamo palla!”"));
        }

        [Test]
        public void TheLines_AreInMatchOrder_WhateverTheirSource()
        {
            PositionStream s = Stream();
            Add(s, At(34) + 6, BallActionKind.Shot, true, 9);
            Add(s, At(34) + 7, BallActionKind.Miss, true, 9);
            MatchReport report = Report(s);
            report.Events.Add(new MatchEvent { Minute = 10, Type = MatchEventType.Shout, ClubId = HomeClub, Shout = TouchlineShout.Encourage });
            var timeline = new BroadcastTimeline(Fpm, Frames, At(46), Array.Empty<BroadcastSegment>(),
                new[] { new CutSummary(At(20), At(25), PossessionSide.Home, CutZone.Attacking, null) });

            IReadOnlyList<CommentaryLine> lines = CommentaryBuilder.Build(report, timeline);

            Assert.That(lines.Select(l => l.Icon), Is.EqualTo(new[]
            {
                CommentaryIcon.Shout, CommentaryIcon.Cut, CommentaryIcon.Miss
            }));
        }

        [Test]
        public void AReportWithoutAStream_StillTellsItsShouts()
        {
            var report = new MatchReport { HomeClubId = HomeClub, AwayClubId = AwayClub };
            report.Events.Add(new MatchEvent { Minute = 7, Type = MatchEventType.Shout, ClubId = HomeClub, Shout = TouchlineShout.Concentrate });

            CommentaryLine line = Single(report);

            Assert.That(English(line), Is.EqualTo("7' Ulivara bench: “Concentrate!”"));
        }

        // ------------------------------------------------------------------ localisation

        [Test]
        public void EveryCommentaryKey_IsInBothLanguages_WithTheSamePlaceholders()
        {
            var placeholder = new Regex(@"\{\d\}");
            Assert.Multiple(() =>
            {
                foreach (string key in CommentaryKeys.All)
                {
                    Assert.That(En.ContainsKey(key), Is.True, $"en misses {key}");
                    Assert.That(It.ContainsKey(key), Is.True, $"it misses {key}");
                    if (!En.ContainsKey(key) || !It.ContainsKey(key)) continue;

                    string[] enArgs = placeholder.Matches(En[key]).Select(m => m.Value).Distinct().OrderBy(v => v).ToArray();
                    string[] itArgs = placeholder.Matches(It[key]).Select(m => m.Value).Distinct().OrderBy(v => v).ToArray();
                    Assert.That(itArgs, Is.EqualTo(enArgs), $"placeholders of {key}");
                }
            });
        }

        [Test]
        public void TheStringTables_CoverTheSameKeys()
        {
            Assert.That(It.Keys.OrderBy(k => k, StringComparer.Ordinal),
                Is.EqualTo(En.Keys.OrderBy(k => k, StringComparer.Ordinal)));
        }

        [Test]
        public void ARealMatch_FormatsEveryLineInBothLanguages()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260926));
            Club home = league.Clubs[0], away = league.Clubs[1];
            var plan = new MatchPlan(new MatchInput(LineupSelector.BestEleven(home), LineupSelector.BestEleven(away)));
            MatchReport report = new MatchEngine(new BalanceConfig()).Simulate(plan, new Pcg32(4242));
            BroadcastTimeline timeline = new BroadcastDirector().Build(report);

            IReadOnlyList<CommentaryLine> lines = CommentaryBuilder.Build(report, timeline);

            int shotOutcomes = report.Positions!.Actions.Count(a =>
                a.Kind == BallActionKind.Goal || a.Kind == BallActionKind.Save
                || a.Kind == BallActionKind.Miss || a.Kind == BallActionKind.Block);
            int fouls = report.Positions.Actions.Count(a => a.Kind == BallActionKind.Foul);
            Assert.That(lines, Has.Count.EqualTo(shotOutcomes + fouls + timeline.Summaries.Count),
                "one sentence per chain outcome, per foul and per cut summary");
            Assert.That(lines.Count(l => l.Icon == CommentaryIcon.Goal), Is.EqualTo(report.HomeGoals + report.AwayGoals));

            string Player(bool side, int slot) => (side ? "H" : "A") + slot;
            foreach (CommentaryLine line in lines)
            {
                string en = CommentaryText.Format(line, Tr(En), Player, ClubOf);
                string it = CommentaryText.Format(line, Tr(It), Player, ClubOf);
                Assert.That(en, Does.Match(@"^\d+'(-\d+')? \S"), en);
                Assert.That(it, Does.Match(@"^\d+'(-\d+')? \S"), it);
                Assert.That(en + it, Does.Not.Contain("{"), en + " / " + it);
            }

            for (int i = 1; i < lines.Count; i++)
                Assert.That(lines[i].Frame, Is.GreaterThanOrEqualTo(lines[i - 1].Frame));
        }
    }
}
