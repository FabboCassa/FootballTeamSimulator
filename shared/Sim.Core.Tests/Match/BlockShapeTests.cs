using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The shape (engine phase 2 — docs/engine/MATCH_ENGINE_PLAN.md §4). Two claims are pinned
    /// here, and they are the two the phase exists to make true:
    ///
    ///   • a LINE is laid out as a line. The width used to be spread one role at a time, each
    ///     group over the whole pitch, which put a 4-3-3's two centre-backs thirty-four metres
    ///     apart with a hole between them (§1.3). A back four is now FB · CB · CB · FB across
    ///     the pitch, and the test says so in metres.
    ///   • a BLOCK is a block. Where a man stands comes from his line's height, the block's
    ///     width and its CAPPED slide toward the ball — so the team translates across the pitch
    ///     instead of collapsing into the ball's channel (§1.4), and it never empties its own
    ///     half of the pitch to do it.
    ///
    /// Everything is read out of the config rather than written down, so the test stays true
    /// when the shape is next tuned; what it fixes is the SHAPE, not the calibration.
    /// </summary>
    [TestFixture]
    public class BlockShapeTests
    {
        private static readonly BalanceConfig Config = new BalanceConfig();
        private static MatchBalance Cfg => Config.Match;

        private static League _league = null!;
        private static Club _midA = null!;
        private static Club _midB = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
            _midA = _league.Clubs[9];
            _midB = _league.Clubs[10];
        }

        private static MatchReport Play(ulong seed) => new MatchEngine().Simulate(
            LineupSelector.BestEleven(_midA), LineupSelector.BestEleven(_midB), new Pcg32(seed));

        private static int YDm(int permille) => permille * Pitch.WidthDm / 1000;

        // ------------------------------------------------------------ the formation is lines

        [Test]
        public void EveryLine_IsLaidOutAcrossThePitch_NotOneRoleAtATime()
        {
            foreach (Formation f in Formations.All)
            {
                PositionRole[] roles = Formations.Roles(f);

                var byLine = new Dictionary<int, List<int>>();
                for (int i = 0; i < roles.Length; i++)
                {
                    if (roles[i] == PositionRole.Goalkeeper) continue;
                    int line = FormationGeometry.LineOf(roles[i], Cfg);
                    if (!byLine.TryGetValue(line, out List<int>? men)) byLine[line] = men = new List<int>();
                    men.Add(YDm(FormationGeometry.AnchorY(roles, i, Cfg)));
                }

                foreach (KeyValuePair<int, List<int>> line in byLine)
                {
                    List<int> ys = line.Value;
                    ys.Sort();

                    // Nobody stands on the touchline, and nobody stands on a team-mate.
                    Assert.That(ys[0], Is.GreaterThanOrEqualTo(Cfg.WideRoleYMarginDm),
                        $"{f} line {line.Key}: the widest man is off the pitch");
                    Assert.That(ys[^1], Is.LessThanOrEqualTo(Pitch.WidthDm - Cfg.WideRoleYMarginDm),
                        $"{f} line {line.Key}: the widest man is off the pitch");

                    for (int i = 1; i < ys.Count; i++)
                        Assert.That(ys[i] - ys[i - 1], Is.GreaterThan(40),
                            $"{f} line {line.Key}: two men within four metres of each other is not a line");

                    // A line of four or more spans the pitch: its outside men ARE its wide
                    // players, whatever the roles are called (a 4-4-2's wide midfielders).
                    if (ys.Count >= Cfg.LineWideFromMembers)
                        Assert.That(ys[^1] - ys[0], Is.GreaterThanOrEqualTo(Pitch.WidthDm - 2 * Cfg.WideRoleYMarginDm - 20),
                            $"{f} line {line.Key}: a flat four has to span the pitch");
                }
            }
        }

        [Test]
        public void TheBackFour_IsALine_NotASmile()
        {
            // The reading that named the bug: in a 4-3-3 the two centre-backs sat 250 and 750
            // permille apart — thirty-four metres, with the whole of the middle empty between
            // them. A pair of centre-backs stands closer together than a centre-back and his
            // full-back, which is what "a line" means.
            PositionRole[] roles = Formations.Roles(Formation.F433);
            var backs = new List<(PositionRole Role, int Y)>();
            for (int i = 0; i < roles.Length; i++)
                if (roles[i] == PositionRole.CentreBack || roles[i] == PositionRole.FullBack)
                    backs.Add((roles[i], YDm(FormationGeometry.AnchorY(roles, i, Cfg))));

            List<int> centres = backs.Where(b => b.Role == PositionRole.CentreBack).Select(b => b.Y).OrderBy(y => y).ToList();
            List<int> fulls = backs.Where(b => b.Role == PositionRole.FullBack).Select(b => b.Y).OrderBy(y => y).ToList();

            Assert.That(centres[1] - centres[0], Is.LessThanOrEqualTo(200),
                "two centre-backs stand within twenty metres of each other");
            Assert.That(fulls[0], Is.LessThan(centres[0]), "the full-backs are OUTSIDE the centre-backs");
            Assert.That(fulls[1], Is.GreaterThan(centres[1]), "the full-backs are OUTSIDE the centre-backs");
        }

        [Test]
        public void LineRank_CountsTheLinesTheShapeActuallyHas()
        {
            // The block's depth follows from the shape rather than from a table: a flat 4-4-2 has
            // three lines and defends compactly, a 4-2-3-1 has four.
            foreach (Formation f in Formations.All)
            {
                PositionRole[] roles = Formations.Roles(f);
                var ranks = new HashSet<int>();
                int lines = 0;
                for (int i = 0; i < roles.Length; i++)
                {
                    if (roles[i] == PositionRole.Goalkeeper) continue;
                    ranks.Add(FormationGeometry.LineRank(roles, i, Cfg, out lines));
                }

                Assert.That(ranks.Count, Is.EqualTo(lines), $"{f}: every line the shape has must be occupied");
                Assert.That(ranks.Min(), Is.Zero, $"{f}: the back line is rank 0");
                Assert.That(ranks.Max(), Is.EqualTo(lines - 1), $"{f}: the ranks must be contiguous");
                Assert.That(lines, Is.InRange(3, 5), $"{f}: {lines} lines is not a football shape");
            }
        }

        // ------------------------------------------------------------ the block on the pitch

        [Test]
        public void Kickoff_PutsBothSidesInTheirOwnHalf()
        {
            // Law 8. Before phase 2 the two blocks simply overlapped across the middle of the
            // pitch and the kickoff frame was legal only by accident.
            PositionStream stream = Play(21).Positions!;
            Lineup home = LineupSelector.BestEleven(_midA);
            Lineup away = LineupSelector.BestEleven(_midB);

            // The taker stands ON the centre spot, so the halfway line itself is allowed.
            for (int i = 0; i < stream.PlayerCount; i++)
            {
                Assert.That(stream.HomeAt(0, i).X, Is.LessThanOrEqualTo(Pitch.CenterX),
                    $"home {home.Slots[i].Role} is in the opponents' half at the kickoff");
                Assert.That(stream.AwayAt(0, i).X, Is.GreaterThanOrEqualTo(Pitch.CenterX),
                    $"away {away.Slots[i].Role} is in the opponents' half at the kickoff");
            }
        }

        [Test]
        public void TheBlockSlides_ByATranslation_AndTheSlideIsCapped()
        {
            // What this replaced was a per-player lerp toward the ball's Y with no cap at all
            // (§1.4): the man furthest from the ball moved MORE than the man nearest it, so the
            // team did not slide across, it squeezed toward the ball's channel — and how far its
            // centre ended up from the middle of the pitch was whatever the arithmetic produced.
            // Now the slide is a translation of the whole shape and the cap is an invariant, so
            // it can be asserted rather than hoped for.
            PositionStream stream = Play(22).Positions!;

            int frames = 0, over = 0, worst = 0;
            for (int t = 0; t <= stream.LastTick; t += 10)
            {
                for (int side = 0; side < 2; side++)
                {
                    long sumY = 0;
                    for (int i = 0; i < stream.PlayerCount; i++)
                        sumY += (side == 0 ? stream.HomeAt(t, i) : stream.AwayAt(t, i)).Y;

                    int off = (int)(sumY / stream.PlayerCount) - Pitch.CenterY;
                    if (off < 0) off = -off;
                    if (off > worst) worst = off;
                    frames++;

                    // The cap plus the room a man going for the ball is allowed to take himself.
                    if (off > Cfg.BlockLateralShiftMaxDm + 60) over++;
                }
            }

            TestContext.Out.WriteLine($"[shape] the team's centre is worst {worst / 10.0:F1} m off the middle of the pitch");
            Assert.That(100.0 * over / frames, Is.LessThan(5),
                "a team whose centre leaves the middle of the pitch is following the ball, not sliding with it");
        }

        [Test]
        public void TheShape_KeepsAFootballWidthAndDepth()
        {
            // The bands the phase claims: a professional block attacks forty to sixty metres
            // wide and thirty to fifty deep. The DEFENDING bands are not claimed here and are
            // not closed — until the marking is zonal (phase 3) a defending side's shape is the
            // attacking side's shape with a five-metre offset, which is a marking problem and
            // not a shape one.
            var metrics = new Sim.Core.Match.Analysis.MatchAnalyzer().Measure(Play(23))!;
            Sim.Core.Match.Analysis.ShapeMetrics attacking = metrics.Home.Attacking;

            TestContext.Out.WriteLine(
                $"[shape] attacking width {attacking.WidthM:F1} m  depth {attacking.DepthM:F1} m  " +
                $"back line spread {attacking.BackLineSpreadM:F1} m");

            Assert.That(attacking.WidthM, Is.InRange(35.0, 60.0), "the attacking block's width");
            Assert.That(attacking.DepthM, Is.InRange(28.0, 55.0), "the attacking block's depth");
        }
    }
}
