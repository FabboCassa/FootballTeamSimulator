using System;
using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>One strike as the renderer draws its line at a playback position.</summary>
    public readonly struct ShotMoment
    {
        public ShotMoment(int strikeFrame, bool shooterHome, int shooterSlot, BallActionKind outcome, int outcomeFrame, float strength)
        {
            StrikeFrame = strikeFrame;
            ShooterHome = shooterHome;
            ShooterSlot = shooterSlot;
            Outcome = outcome;
            OutcomeFrame = outcomeFrame;
            Strength = strength;
        }

        /// <summary>The frame the ball is struck on: the line starts at the shooter there.</summary>
        public int StrikeFrame { get; }

        public bool ShooterHome { get; }
        public int ShooterSlot { get; }

        /// <summary>The action that ended the flight; <see cref="BallActionKind.Shot"/> when none did in time.</summary>
        public BallActionKind Outcome { get; }

        /// <summary>The frame of <see cref="Outcome"/>; -1 when the flight was never settled.</summary>
        public int OutcomeFrame { get; }

        /// <summary>1 while the ball is in flight, fading to 0 while it runs on into the net or past the line.</summary>
        public float Strength { get; }
    }

    /// <summary>The net a goal went into, in stream decimetres, as the renderer lights it.</summary>
    public readonly struct NetMoment
    {
        public NetMoment(int goalFrame, int lineX, int backX, int minY, int maxY, float strength)
        {
            GoalFrame = goalFrame;
            LineX = lineX;
            BackX = backX;
            MinY = minY;
            MaxY = maxY;
            Strength = strength;
        }

        public int GoalFrame { get; }

        /// <summary>The goal line, and the back of the net beyond it.</summary>
        public int LineX { get; }
        public int BackX { get; }

        /// <summary>The posts.</summary>
        public int MinY { get; }
        public int MaxY { get; }

        /// <summary>1 on the goal frame, fading linearly to 0 when the highlight ends.</summary>
        public float Strength { get; }
    }

    /// <summary>
    /// The readable strike (spec believable-match R12): over which frames the renderer draws a
    /// shot's line from the shooter to the ball, and which net a goal lights. Presentation only —
    /// it reads the stream and never changes a result.
    ///
    /// A shot's line lasts until the next action ends the flight. A goal or a wide shot keeps it
    /// a little longer, fading, because the ball runs on into the net or past the line (R5) and
    /// that is what the line has to show; a save, a block or anything else ends it there, because
    /// what the ball does next is a rebound and not the shot.
    ///
    /// Like <see cref="SaveHighlight"/>, everything is read once in the constructor and the
    /// queries are binary searches over plain arrays: stateless and allocation-free, so the
    /// renderer asks on every repaint and a seek needs no reset; and nothing first-half is drawn
    /// once the picture has turned round for the second.
    /// </summary>
    public sealed class ShotHighlight
    {
        /// <summary>How long a goal's or a wide shot's line stays after the ball crosses the line, in match seconds.</summary>
        public const double HoldSeconds = 1.0;

        /// <summary>The longest a strike nothing settles keeps its line, in match seconds.</summary>
        public const double MaxFlightSeconds = 4.0;

        /// <summary>How long a goal lights the net, in match seconds.</summary>
        public const double NetSeconds = 2.0;

        private readonly int[] _strike;
        private readonly bool[] _home;
        private readonly int[] _slot;
        private readonly BallActionKind[] _outcome;
        private readonly int[] _outcomeFrame;
        private readonly int[] _flightEnd;
        private readonly int[] _hold;

        private readonly int[] _goalFrame;
        private readonly bool[] _goalHome;

        private readonly int _netDepthDm;

        /// <summary>The half-time whistle's frame; int.MaxValue when the stream has none.</summary>
        private readonly int _secondHalfFrom;

        public ShotHighlight(PositionStream stream, MatchBalance? cfg = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            _netDepthDm = (cfg ?? new BalanceConfig().Match).GoalNetDepthDm;
            int fpm = stream.TicksPerMinute > 0 ? stream.TicksPerMinute : 1;
            HoldFrames = Frames(HoldSeconds, fpm);
            MaxFlightFrames = Frames(MaxFlightSeconds, fpm);
            NetFrames = Frames(NetSeconds, fpm);

            List<BallAction> actions = stream.Actions ?? new List<BallAction>();
            _secondHalfFrom = int.MaxValue;
            var shots = new List<int>();
            var goals = new List<int>();
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction a = actions[i];
                if (a.Kind == BallActionKind.HalfTime && _secondHalfFrom == int.MaxValue)
                    _secondHalfFrom = a.Tick;
                else if (a.Kind == BallActionKind.Shot && a.Slot >= 0 && a.Slot < stream.PlayerCount)
                    shots.Add(i);
                else if (a.Kind == BallActionKind.Goal)
                    goals.Add(i);
            }

            _strike = new int[shots.Count];
            _home = new bool[shots.Count];
            _slot = new int[shots.Count];
            _outcome = new BallActionKind[shots.Count];
            _outcomeFrame = new int[shots.Count];
            _flightEnd = new int[shots.Count];
            _hold = new int[shots.Count];
            for (int k = 0; k < shots.Count; k++)
            {
                int i = shots[k];
                BallAction shot = actions[i];
                _strike[k] = shot.Tick;
                _home[k] = shot.Home;
                _slot[k] = shot.Slot;

                int longest = shot.Tick + MaxFlightFrames;
                if (i + 1 < actions.Count && actions[i + 1].Tick <= longest)
                {
                    BallAction next = actions[i + 1];
                    _outcome[k] = next.Kind;
                    _outcomeFrame[k] = next.Tick;
                    _flightEnd[k] = next.Tick;
                    _hold[k] = next.Kind == BallActionKind.Goal || next.Kind == BallActionKind.Miss ? HoldFrames : 0;
                }
                else
                {
                    _outcome[k] = BallActionKind.Shot;
                    _outcomeFrame[k] = -1;
                    _flightEnd[k] = longest;
                }
            }

            _goalFrame = new int[goals.Count];
            _goalHome = new bool[goals.Count];
            for (int k = 0; k < goals.Count; k++)
            {
                _goalFrame[k] = actions[goals[k]].Tick;
                _goalHome[k] = actions[goals[k]].Home;
            }
        }

        public int ShotCount => _strike.Length;
        public int GoalCount => _goalFrame.Length;

        /// <summary>How many frames a goal's or a wide shot's line stays after the ball crosses the line.</summary>
        public int HoldFrames { get; }

        /// <summary>How many frames a strike nothing settles keeps its line.</summary>
        public int MaxFlightFrames { get; }

        /// <summary>How many frames a goal lights the net.</summary>
        public int NetFrames { get; }

        /// <summary>The strike whose line to draw at stream position <paramref name="position"/>; false when none.</summary>
        public bool TryShotAt(double position, out ShotMoment moment)
        {
            moment = default;
            int k = LatestAtOrBefore(_strike, position);
            if (k < 0 || Mirrored(_strike[k], position))
                return false;

            double past = position - _flightEnd[k];
            if (past > _hold[k] || (_hold[k] == 0 && past > 0))
                return false;

            float strength = past <= 0 ? 1f : (float)(1.0 - past / _hold[k]);
            if (strength <= 0f)
                return false;

            moment = new ShotMoment(_strike[k], _home[k], _slot[k], _outcome[k], _outcomeFrame[k], strength);
            return true;
        }

        /// <summary>The net to light at stream position <paramref name="position"/>: the latest goal while its highlight lasts.</summary>
        public bool TryNetAt(double position, out NetMoment moment)
        {
            moment = default;
            int k = LatestAtOrBefore(_goalFrame, position);
            if (k < 0 || Mirrored(_goalFrame[k], position))
                return false;

            double age = position - _goalFrame[k];
            if (age >= NetFrames)
                return false;

            // The scorers attack this line; the stream is never mirrored, only the picture.
            int lineX = MovementGeometry.AttackedGoalX(_goalHome[k]);
            int backX = lineX == 0 ? -_netDepthDm : lineX + _netDepthDm;
            moment = new NetMoment(_goalFrame[k], lineX, backX,
                Pitch.CenterY - MovementGeometry.GoalHalfWidthDm, Pitch.CenterY + MovementGeometry.GoalHalfWidthDm,
                (float)(1.0 - age / NetFrames));
            return true;
        }

        /// <summary>
        /// The renderer never interpolates across the whistle: from the frame before it, the
        /// picture already stands on the mirrored second half, where a first-half mark would not.
        /// </summary>
        private bool Mirrored(int frame, double position) => frame < _secondHalfFrom && position >= _secondHalfFrom - 1.0;

        private static int Frames(double seconds, int fpm) => Math.Max(1, (int)Math.Round(seconds * fpm / 60.0));

        private static int LatestAtOrBefore(int[] frames, double position)
        {
            int lo = 0, hi = frames.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (frames[mid] <= position)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return found;
        }
    }
}
