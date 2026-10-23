using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R16, issue #91: the transition and out-of-possession axes take effect. One axis at a time,
    /// the home side carries the extreme and the away side plays neutral over the same
    /// <see cref="Seeds"/> fixtures; each extreme must move its target metric away from the
    /// all-neutral reading by the documented minimum, in the football direction.
    ///
    /// The minimums sit at about half the shift measured at 64 seeds (the comment on each test),
    /// so that seed noise alone cannot carry an extreme across them; the printed lines are the
    /// numbers to judge. These are measurements over simulated matches: a change anywhere in the
    /// engine moves them, and the smallest margins (zonal marking, hold shape) are the fragile ones.
    /// The middle levels are the identity: <see cref="InstructionsTests"/> and the golden master
    /// pin that, since every middle level reads as no change and draws nothing.
    /// </summary>
    [TestFixture]
    public class DefendingAxesTests
    {
        private const int Seeds = 64;
        private const ulong FirstSeed = 9100;

        // Final third and wide channel (from the centre line), in decimetres.
        private const int FinalThirdDm = Pitch.LengthDm * 2 / 3;
        private const int WideChannelDm = 200;

        // A fast attack reaches the final third within this of the regain; the space left behind
        // a lost ball is read over this after the loss.
        private const int FastAttackSeconds = 10;
        private const int AfterLossSeconds = 5;

        private static readonly ConcurrentDictionary<TacticInstructions, Lazy<Reading>> Cache =
            new ConcurrentDictionary<TacticInstructions, Lazy<Reading>>();

        private static Club _home = null!;
        private static Club _away = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            _home = league.Clubs[9];
            _away = league.Clubs[10];
        }

        // ------------------------------------------------------------------ transitions

        [Test]
        public void OnBallLost_CounterPressWinsItBackHigh_RegroupDropsBehindTheBall()
        {
            Reading neutral = Neutral;
            Reading press = Of(onLost: OnBallLost.CounterPress);
            Reading regroup = Of(onLost: OnBallLost.Regroup);

            Print("on-loss", "high recoveries a match", press.HighRecoveries, neutral.HighRecoveries, regroup.HighRecoveries,
                "counter-press", "regroup");
            Print("on-loss", $"space behind the line in the {AfterLossSeconds} s after a loss, m",
                press.SpaceAfterLossM, neutral.SpaceAfterLossM, regroup.SpaceAfterLossM, "counter-press", "regroup");

            // Measured: high recoveries 23.05 vs 19.48; space after a loss 35.65 vs 39.76 m.
            Assert.That(press.HighRecoveries, Is.GreaterThan(neutral.HighRecoveries + 1.5),
                "a counter-pressing side wins the ball back in the other half more often");
            Assert.That(regroup.SpaceAfterLossM, Is.LessThan(neutral.SpaceAfterLossM - 2.0),
                "a side that regroups drops behind the ball as soon as it loses it");
        }

        [Test]
        public void OnBallWon_CounterGoesForward_HoldShapeDoesNot()
        {
            double neutral = Neutral.FastAttacks;
            double counter = Of(onWon: OnBallWon.Counter).FastAttacks;
            double hold = Of(onWon: OnBallWon.HoldShape).FastAttacks;

            Print("on-win", $"regains carried into the final third within {FastAttackSeconds} s, a match",
                counter, neutral, hold, "counter", "hold shape");

            // Measured: counter 37.64, neutral 23.34, hold shape 20.31.
            Assert.That(counter, Is.GreaterThan(neutral + 5.0), "a countering side gets into the final third quickly after a regain");
            Assert.That(hold, Is.LessThan(neutral - 1.5), "a side that holds its shape after a regain does it less");
        }

        // ------------------------------------------------------------------ out of possession

        [Test]
        public void LineOfEngagement_MovesWhereTheBallIsWonBack()
        {
            double neutral = Neutral.RecoveryHeightM;
            double high = Of(engagement: LineOfEngagement.High).RecoveryHeightM;
            double low = Of(engagement: LineOfEngagement.Low).RecoveryHeightM;

            Print("engagement", "where the ball is won back, m from own goal", high, neutral, low, "high", "low");

            // Measured: high 37.18, mid 32.19, low 29.03 m.
            Assert.That(high, Is.GreaterThan(neutral + 2.5), "a high line of engagement wins the ball further up");
            Assert.That(low, Is.LessThan(neutral - 1.5), "a low one wins it deeper");
        }

        [Test]
        public void DefensiveLine_VeryHighCatchesOffsidesAndLeavesSpaceBehind()
        {
            Reading neutral = Neutral;
            Reading high = Of(line: DefensiveLine.VeryHigh);
            Reading deep = Of(line: DefensiveLine.VeryDeep);

            Print("line", "offsides won a match", high.OffsidesWon, neutral.OffsidesWon, deep.OffsidesWon,
                "very high", "very deep");
            Print("line", "space behind the line while defending, m", high.SpaceBehindM, neutral.SpaceBehindM, deep.SpaceBehindM,
                "very high", "very deep");

            // Measured: offsides 3.59 / 1.80 / 0.83; space behind 36.40 / 33.01 / 26.92 m.
            Assert.That(high.OffsidesWon, Is.GreaterThan(neutral.OffsidesWon + 0.8), "a very high line catches more men offside");
            Assert.That(high.SpaceBehindM, Is.GreaterThan(neutral.SpaceBehindM + 1.5), "and leaves more space behind it");
            Assert.That(deep.OffsidesWon, Is.LessThan(neutral.OffsidesWon - 0.4), "a very deep line catches fewer");
            Assert.That(deep.SpaceBehindM, Is.LessThan(neutral.SpaceBehindM - 3.0), "and leaves less space behind it");
        }

        [Test]
        public void PressingIntensity_MovesWhereTheBallIsWonBack()
        {
            double neutral = Neutral.RecoveryHeightM;
            double high = Of(pressing: Pressing.High).RecoveryHeightM;
            double low = Of(pressing: Pressing.Low).RecoveryHeightM;

            Print("pressing", "where the ball is won back, m from own goal", high, neutral, low, "high", "low");

            // Measured: high 44.49, medium 32.19, low 26.81 m (the v11 axis, unchanged).
            Assert.That(high, Is.GreaterThan(neutral + 5.0), "a high press wins the ball further up");
            Assert.That(low, Is.LessThan(neutral - 2.5), "a low one wins it deeper");
        }

        [Test]
        public void Marking_ManOrientedStaysTightToItsMen_ZonalDoesNot()
        {
            double neutral = Neutral.TightPercent;
            double man = Of(marking: MarkingScheme.ManOriented).TightPercent;
            double zonal = Of(marking: MarkingScheme.Zonal).TightPercent;

            Print("marking", "defending outfielders within 3 m of an opponent, %", man, neutral, zonal, "man-oriented", "zonal");

            // Measured: man-oriented 20.47, mixed 15.60, zonal 14.49%.
            Assert.That(man, Is.GreaterThan(neutral + 2.0), "man-oriented marking stays tight to the opponents");
            Assert.That(zonal, Is.LessThan(neutral - 0.5), "zonal marking keeps its places instead");
        }

        [Test]
        public void PressDirection_ForceOutsideWinsItOnTheFlank_ForceInsideInTheMiddle()
        {
            double neutral = Neutral.WideRecoveryPercent;
            double outside = Of(direction: PressDirection.ForceOutside).WideRecoveryPercent;
            double inside = Of(direction: PressDirection.ForceInside).WideRecoveryPercent;

            Print("press-direction", "share of the ball won back in the wide channels, %", outside, neutral, inside,
                "force outside", "force inside");

            // Measured: force outside 16.31, none 8.58, force inside 4.40%.
            Assert.That(outside, Is.GreaterThan(neutral + 3.5), "forcing the play outside wins the ball on the flank");
            Assert.That(inside, Is.LessThan(neutral - 2.0), "forcing it inside wins it in the middle");
        }

        // ------------------------------------------------------------------ the bench

        private static Reading Neutral => Of();

        private static Reading Of(
            OnBallLost onLost = OnBallLost.Normal, OnBallWon onWon = OnBallWon.Normal,
            LineOfEngagement engagement = LineOfEngagement.Mid, DefensiveLine line = DefensiveLine.Normal,
            Pressing pressing = Pressing.Medium, MarkingScheme marking = MarkingScheme.Mixed,
            PressDirection direction = PressDirection.None)
        {
            var instructions = new TacticInstructions(
                Mentality.Balanced, PassingDirectness.Mixed, Tempo.Normal, Width.Normal,
                BuildUp.Mixed, FinalThirdApproach.Mixed, ShotSelection.Normal, DribbleFrequency.Normal,
                onLost, onWon, engagement, line, pressing, marking, direction);
            return Cache.GetOrAdd(instructions, i => new Lazy<Reading>(() => Measure(i))).Value;
        }

        private static void Print(string tag, string metric, double extreme, double neutral, double other,
            string extremeName, string otherName) =>
            TestContext.Out.WriteLine(
                $"[axes-{tag}] {metric}: {extremeName} {extreme:F2}   neutral {neutral:F2}   {otherName} {other:F2}");

        /// <summary>Plays the fixtures with the home side on these instructions and reads it off the finished matches.</summary>
        private static Reading Measure(TacticInstructions instructions)
        {
            var parts = new Reading[Seeds];
            Parallel.For(0, Seeds, i =>
            {
                var cfg = new BalanceConfig();
                MatchReport report = new MatchEngine().Simulate(
                    LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away),
                    new Pcg32(FirstSeed + (ulong)i),
                    new MatchTactics(
                        new TacticContext(new Tactic(Formation.F433, instructions), cfg.Tactics.FamiliarityMax),
                        TacticContext.Neutral(cfg.Tactics.FamiliarityMax)));
                MatchMetrics? metrics = new MatchAnalyzer().Measure(report);
                parts[i] = new Reading();
                if (metrics != null && report.Positions != null) parts[i].Add(metrics, report.Positions);
            });

            var reading = new Reading();
            foreach (Reading part in parts) reading.Merge(part);
            return reading;
        }

        /// <summary>The home side's readings, summed over the matches and averaged on read. Home defends x = 0.</summary>
        private sealed class Reading
        {
            private int _matches, _recoveries, _wideRecoveries, _defendingFrames, _lossFrames;
            private double _highRecoveries, _recoveryHeight, _fastAttacks, _offsidesWon, _tight;
            private double _space, _lossSpace;

            public double HighRecoveries => PerMatch(_highRecoveries);
            public double RecoveryHeightM => _recoveries == 0 ? 0 : _recoveryHeight / _recoveries / 10.0;
            public double WideRecoveryPercent => _recoveries == 0 ? 0 : 100.0 * _wideRecoveries / _recoveries;
            public double FastAttacks => PerMatch(_fastAttacks);
            public double OffsidesWon => PerMatch(_offsidesWon);
            public double TightPercent => PerMatch(_tight);
            public double SpaceBehindM => _defendingFrames == 0 ? 0 : _space / _defendingFrames / 10.0;
            public double SpaceAfterLossM => _lossFrames == 0 ? 0 : _lossSpace / _lossFrames / 10.0;

            private double PerMatch(double total) => _matches == 0 ? 0 : total / _matches;

            public void Add(MatchMetrics metrics, PositionStream s)
            {
                s.Unpack();
                _matches++;
                _offsidesWon += metrics.Away.Offsides;
                _tight += metrics.Home.Defending.WithinThreeMetresOfOpponentPercent;
                _highRecoveries += MatchWindow.HighRecoveries(s, true, 0, int.MaxValue / s.TicksPerMinute);

                int fastWindow = s.TicksPerMinute * FastAttackSeconds / 60;
                int lossWindow = s.TicksPerMinute * AfterLossSeconds / 60;
                foreach (BallAction a in s.Actions)
                {
                    if (a.Kind != BallActionKind.Recovery && a.Kind != BallActionKind.Interception) continue;
                    if (a.Home) AddRegain(s, a.Tick, fastWindow);
                    else AddLoss(s, a.Tick, lossWindow);
                }

                for (int t = 0; t < s.Owner.Length; t++)
                {
                    if (!s.TryOwner(s.Owner[t], out bool home, out int _) || home) continue;
                    _space += LastLineDepth(s, t);
                    _defendingFrames++;
                }
            }

            public void Merge(Reading o)
            {
                _matches += o._matches;
                _recoveries += o._recoveries;
                _wideRecoveries += o._wideRecoveries;
                _defendingFrames += o._defendingFrames;
                _lossFrames += o._lossFrames;
                _highRecoveries += o._highRecoveries;
                _recoveryHeight += o._recoveryHeight;
                _fastAttacks += o._fastAttacks;
                _offsidesWon += o._offsidesWon;
                _tight += o._tight;
                _space += o._space;
                _lossSpace += o._lossSpace;
            }

            private void AddRegain(PositionStream s, int tick, int window)
            {
                int x = s.BallXY[tick * 2], dy = s.BallXY[tick * 2 + 1] - Pitch.CenterY;
                _recoveryHeight += x;
                _recoveries++;
                if (dy >= WideChannelDm || dy <= -WideChannelDm) _wideRecoveries++;
                if (x < FinalThirdDm && ReachesFinalThird(s, tick, window)) _fastAttacks++;
            }

            /// <summary>The home side's last line over the window after it lost the ball, while the away side keeps it.</summary>
            private void AddLoss(PositionStream s, int tick, int window)
            {
                int end = Math.Min(tick + window, s.Owner.Length - 1);
                for (int t = tick + 1; t <= end; t++)
                {
                    if (s.TryOwner(s.Owner[t], out bool home, out int _) && home) return;
                    _lossSpace += LastLineDepth(s, t);
                    _lossFrames++;
                }
            }

            /// <summary>Whether the home side carries a regain into the final third before the away side has it.</summary>
            private static bool ReachesFinalThird(PositionStream s, int from, int window)
            {
                int end = Math.Min(from + window, s.Owner.Length - 1);
                for (int t = from; t <= end; t++)
                {
                    if (s.TryOwner(s.Owner[t], out bool home, out int _) && !home) return false;
                    if (s.BallXY[t * 2] >= FinalThirdDm) return true;
                }

                return false;
            }

            /// <summary>The home side's last line: its second-deepest man (the keeper is usually the deepest).</summary>
            private static int LastLineDepth(PositionStream s, int t)
            {
                int deepest = int.MaxValue, second = int.MaxValue;
                for (int slot = 0; slot < s.PlayerCount; slot++)
                {
                    int x = s.HomeAt(t, slot).X;
                    if (x < deepest) { second = deepest; deepest = x; }
                    else if (x < second) second = x;
                }

                return second;
            }
        }
    }
}
