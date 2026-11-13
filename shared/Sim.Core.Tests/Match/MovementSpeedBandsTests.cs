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
    /// Issue #215: how the outfielders choose and reach their speed, read off whole matches against
    /// the reference v2 tracking bands (doc §10.5). Before it the engine ran 3.1% of its frames
    /// above 7 m/s (0.54-0.94) and 9% at 5.5-7 m/s (2.40-3.92), made 6.8 hard accelerations a
    /// player-minute (1.25-2.70) and walked 58% of the time (0.2-2 m/s, 34-51%) where real players
    /// jog: every sprint went flat out, and a man easing into his place crawled the last 15 m.
    /// The pitch harness's 4 seeds x 200 matches are the full reading; this is the same set-up
    /// over a dozen fixtures (fixed seeds, so the reading is exact, not a dice roll).
    /// </summary>
    [TestFixture]
    public class MovementSpeedBandsTests
    {
        private const int Matches = 12;
        private const ulong LeagueSeed = 20260803;

        private static MovementFeelMetrics _feel = null!;

        [OneTimeSetUp]
        public void PlayTheSample()
        {
            var cfg = new BalanceConfig();
            List<Club> clubs = new LeagueGenerator(new LeagueGenerationOptions { ClubCount = 20 }, cfg)
                .Generate(new Pcg32(LeagueSeed)).Clubs;
            var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                applyPositioning: true, generatePositions: true);
            var analyzer = new MovementFeelAnalyzer();

            MovementFeelMetrics? sum = null;
            for (int i = 0; i < Matches; i++)
            {
                MatchReport report = engine.Simulate(LineupSelector.BestEleven(clubs[2 * i % clubs.Count]),
                    LineupSelector.BestEleven(clubs[(2 * i + 1) % clubs.Count]), new Pcg32(LeagueSeed + 31_000 + (ulong)i));
                MovementFeelMetrics match = analyzer.Measure(report)!;
                sum = sum == null ? match : sum.Plus(match);
            }

            _feel = sum!;
            TestContext.Progress.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[movement-speed] {0} matches: speed % <0.2 {1:F2} | 0.2-2 {2:F2} | 2-4 {3:F2} | 4-5.5 {4:F2} | 5.5-7 {5:F2} | >7 {6:F2}; accelerations/pm {7:F2}; reversals/pm {8:F2}",
                Matches, _feel.SpeedPercent(0), _feel.SpeedPercent(1), _feel.SpeedPercent(2), _feel.SpeedPercent(3),
                _feel.SpeedPercent(4), _feel.SpeedPercent(5), _feel.AccelerationsPerPlayerMinute, _feel.ReversalsPerPlayerMinute));
        }

        private static IEnumerable<TestCaseData> SpeedBands()
        {
            yield return new TestCaseData(0, RealismReference.Tracking.SpeedBelow02Percent).SetName("Speed_Below02");
            yield return new TestCaseData(1, RealismReference.Tracking.Speed02To2Percent).SetName("Speed_02To2_Walking");
            yield return new TestCaseData(2, RealismReference.Tracking.Speed2To4Percent).SetName("Speed_2To4_Jogging");
            yield return new TestCaseData(3, RealismReference.Tracking.Speed4To55Percent).SetName("Speed_4To55_Running");
            yield return new TestCaseData(4, RealismReference.Tracking.Speed55To7Percent).SetName("Speed_55To7_HighSpeed");
            yield return new TestCaseData(5, RealismReference.Tracking.SpeedAbove7Percent).SetName("Speed_Above7_Sprinting");
        }

        [TestCaseSource(nameof(SpeedBands))]
        public void TheShareOfFramesAtEachSpeed_IsInsideTheReferenceV2Band(int band, RealismBand reference)
        {
            double share = _feel.SpeedPercent(band);

            Assert.That(share, Is.InRange(reference.Min, reference.Max), reference.Name);
        }

        [Test]
        public void HardAccelerationsPerPlayerMinute_AreInsideTheReferenceV2Band()
        {
            RealismBand reference = RealismReference.Tracking.AccelerationsPerPlayerMinute;

            Assert.That(_feel.AccelerationsPerPlayerMinute, Is.InRange(reference.Min, reference.Max), reference.Name);
        }

        [Test]
        public void HeadingReversals_StayInsideTheReferenceV2Band()
        {
            // The jog that replaced the crawl must not buy its speed with a man shuttling round his place.
            RealismBand reference = RealismReference.Tracking.ReversalsPerPlayerMinute;

            Assert.That(_feel.ReversalsPerPlayerMinute, Is.InRange(reference.Min, reference.Max), reference.Name);
        }
    }
}
