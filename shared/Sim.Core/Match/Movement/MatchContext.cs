using Sim.Core.Config;
using Sim.Core.Random;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The state the referee and the restarts share with <see cref="MatchSimulator"/>: one per
    /// match, built in its setup. The per-player arrays are the simulator's own, held by
    /// reference, so there is exactly one copy of the pitch; what both sides WRITE — the dead
    /// ball and the strike in flight — lives only here.
    /// </summary>
    internal sealed class MatchContext
    {
        public readonly MatchBalance Cfg;
        public readonly IRandomSource Rng;
        public readonly MatchBall Ball;
        public readonly MatchScoresheet Sheet;
        public readonly int N;

        public readonly int[] Px;
        public readonly int[] Py;
        public readonly bool[] Keeper;
        public readonly bool[] SentOff;
        public readonly int[] SkShooting;
        public readonly int[] SkTechnique;
        public readonly int[] Receiver;

        public readonly int[] LastTouch;

        /// <summary>
        /// How far inside the touchline the shape, a pass and a run with the ball all stay
        /// (engine phase 5). A footballer's POSITION is never the line itself: he stands a stride
        /// inside it, because standing on it puts half of him off the pitch. Until this phase the
        /// widest man in an attacking block was simply clamped onto the touchline, and a ball
        /// played to him sat there in his feet for seconds at a time — which is most of what the
        /// harness was counting as "a held ball on a line of the pitch".
        /// </summary>
        public readonly int InsetU;

        // Dead ball: what it is, who takes it, and when it may be taken.
        public BallActionKind DeadKind;
        public int DeadSide = -1;
        public int DeadTaker = -1;
        public int DeadAt;

        /// <summary>
        /// Until this tick the man who has just put the ball back in play cannot be judged to
        /// have carried it out again. A throw-in is taken FROM the touchline, so the taker and
        /// the ball are both standing on a line the moment he collects it.
        /// </summary>
        public int RestartGrace;
        public int RestartTaker = -1;

        // A strike in flight. What it becomes is the ball's business, not a script's.
        public bool ShotLive;
        public bool ShotHome;
        public int ShotSlot;

        public MatchContext(
            MatchBalance cfg, IRandomSource rng, MatchBall ball, MatchScoresheet sheet, int n,
            int[] px, int[] py, bool[] keeper, bool[] sentOff,
            int[] skShooting, int[] skTechnique, int[] receiver, int[] lastTouch)
        {
            Cfg = cfg;
            Rng = rng;
            Ball = ball;
            Sheet = sheet;
            N = n;
            Px = px;
            Py = py;
            Keeper = keeper;
            SentOff = sentOff;
            SkShooting = skShooting;
            SkTechnique = skTechnique;
            Receiver = receiver;
            LastTouch = lastTouch;
            InsetU = U.Units(cfg.TouchlineInsetDm);
            if (InsetU < 0) InsetU = 0;
        }

        /// <summary>A place across the pitch that is ON the pitch — a stride inside the touchline.</summary>
        public int Inside(int y) => MovementGeometry.Clamp(y, InsetU, U.WidthU - InsetU);

        public int NearestTo(int side, int x, int y, bool includeKeeper)
        {
            int best = -1;
            long bestDistance = long.MaxValue;
            for (int i = 0; i < N; i++)
            {
                int k = side * N + i;
                if (SentOff[k]) continue;
                if (Keeper[k] && !includeKeeper) continue;

                long distance = U.DistanceSq(Px[k], Py[k], x, y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best >= 0 ? best : 0;
        }

        public int KeeperOf(int side)
        {
            for (int i = 0; i < N; i++)
                if (Keeper[side * N + i]) return i;
            return 0;
        }

        /// <summary>Who steps up for a penalty: the best striker of a ball still on the pitch.</summary>
        public int BestStriker(int side)
        {
            int best = -1, bestSkill = -1;
            for (int i = 0; i < N; i++)
            {
                int k = side * N + i;
                if (Keeper[k] || SentOff[k]) continue;
                int skill = BallSkill.Mix(SkShooting[k], 7, SkTechnique[k], 3);
                if (skill > bestSkill) { bestSkill = skill; best = i; }
            }

            return best >= 0 ? best : KeeperOf(side);
        }
    }
}
