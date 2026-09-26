using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R6 harness: with Brain = V11, free kicks within 35 m are shot directly AND crossed, both
    /// more than zero times, counted off the position stream (the kick that follows the
    /// FreeKick restart). The counts, corners, penalties and the goals that came straight from a
    /// direct free kick are printed for the eye.
    ///
    /// Statistically sensitive: a direct free kick needs a foul within about 27 m of the middle of
    /// the goal, and that is a few per match at most. 200 matches keep both counts far above zero.
    /// Run with --logger "console;verbosity=detailed" for the numbers.
    /// </summary>
    [TestFixture]
    [Category("Harness")]
    public class SetPieceHarnessTests
    {
        private const int Matches = 200;
        private const ulong FirstSeed = 35_000;

        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        [Test]
        public void Harness_V11_FreeKicksAreShotDirectlyAndCrossed()
        {
            MatchBalance cfg = new BalanceConfig().Match;
            cfg.Brain = MatchBrainVersion.V11;

            // Per match: in range, direct, direct goals, crossed, corners, penalties.
            var counts = new int[Matches, 6];

            Parallel.For(0, Matches, i =>
            {
                Club home = _league.Clubs[(2 * i) % _league.Clubs.Count];
                Club away = _league.Clubs[(2 * i + 1) % _league.Clubs.Count];
                PositionStream s = new MatchSimulator(cfg).Generate(
                    LineupSelector.BestEleven(home), LineupSelector.BestEleven(away),
                    new MatchReport { HomeClubId = home.Id, AwayClubId = away.Id },
                    new Pcg32(FirstSeed + (ulong)i), null);

                List<BallAction> a = s.Actions;
                for (int j = 0; j + 1 < a.Count; j++)
                {
                    if (a[j].Kind == BallActionKind.Corner) counts[i, 4]++;
                    if (a[j].Kind == BallActionKind.Penalty) counts[i, 5]++;
                    if (a[j].Kind != BallActionKind.FreeKick) continue;

                    int spot = a[j].Tick;
                    int goalX = a[j].Home ? Pitch.LengthDm : 0;
                    long dx = s.BallXY[spot * 2] - goalX, dy = s.BallXY[spot * 2 + 1] - Pitch.CenterY;
                    if (dx * dx + dy * dy >= (long)cfg.SetPieceRangeDm * cfg.SetPieceRangeDm) continue;

                    counts[i, 0]++;
                    BallAction kick = a[j + 1];
                    if (kick.Home != a[j].Home) continue;
                    if (kick.Kind == BallActionKind.Shot)
                    {
                        counts[i, 1]++;
                        if (j + 2 < a.Count && a[j + 2].Kind == BallActionKind.Goal) counts[i, 2]++;
                    }
                    else if (kick.Kind == BallActionKind.Cross)
                    {
                        counts[i, 3]++;
                    }
                }
            });

            var total = new int[6];
            for (int i = 0; i < Matches; i++)
                for (int c = 0; c < 6; c++) total[c] += counts[i, c];

            TestContext.Out.WriteLine($"V11 set pieces over {Matches} matches:");
            TestContext.Out.WriteLine($"  free kicks within 35 m  {total[0],6}  ({total[0] / (double)Matches:F2} a match)");
            TestContext.Out.WriteLine($"  shot directly           {total[1],6}  ({total[2]} scored)");
            TestContext.Out.WriteLine($"  crossed                 {total[3],6}");
            TestContext.Out.WriteLine($"  corners                 {total[4],6}  ({total[4] / (double)Matches:F2} a match)");
            TestContext.Out.WriteLine($"  penalties               {total[5],6}");

            Assert.That(total[1], Is.GreaterThan(0), "direct free-kick shots");
            Assert.That(total[3], Is.GreaterThan(0), "crossed free kicks");
        }
    }
}
