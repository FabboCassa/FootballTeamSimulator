using System;
using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>One save as the renderer accents it at a playback position.</summary>
    public readonly struct SaveMoment
    {
        public SaveMoment(int saveFrame, int diveFrom, bool keeperHome, int keeperSlot, float strength)
        {
            SaveFrame = saveFrame;
            DiveFrom = diveFrom;
            KeeperHome = keeperHome;
            KeeperSlot = keeperSlot;
            Strength = strength;
        }

        /// <summary>The frame the keeper stops the ball on.</summary>
        public int SaveFrame { get; }

        /// <summary>The first frame of his movement toward the ball: the strike, within a bounded reach back.</summary>
        public int DiveFrom { get; }

        public bool KeeperHome { get; }
        public int KeeperSlot { get; }

        /// <summary>1 on the save frame, fading linearly to 0 when the accent ends.</summary>
        public float Strength { get; }
    }

    /// <summary>
    /// The visible keeper save (spec real-match-and-playing-styles R7): which save the renderer
    /// accents at a playback position, and over which frames it draws the keeper's movement toward
    /// the ball. Presentation only — it reads the stream and never changes a result.
    ///
    /// Every save is read once, in the constructor; <see cref="TryAt"/> is a binary search over
    /// plain arrays, stateless and allocation-free, so the renderer can ask it on every repaint and
    /// a seek needs no reset.
    ///
    /// The renderer mirrors the picture from the half-time whistle on (the change of ends), so a
    /// path drawn across that frame would be a stripe across the pitch: a second-half save never
    /// reaches back before the second half starts, and a first-half accent ends before the picture
    /// turns round.
    /// </summary>
    public sealed class SaveHighlight
    {
        /// <summary>How long the accent lasts, in match seconds, so a 2 fps and a 5 fps replay agree.</summary>
        public const double AccentSeconds = 2.0;

        /// <summary>The longest stretch of the keeper's path drawn before the save, in match seconds.</summary>
        public const double MaxDiveSeconds = 3.0;

        private readonly int[] _frames;
        private readonly int[] _diveFrom;
        private readonly bool[] _home;
        private readonly int[] _slot;

        /// <summary>The half-time whistle's frame; int.MaxValue when the stream has none.</summary>
        private readonly int _secondHalfFrom;

        public SaveHighlight(PositionStream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            int fpm = stream.TicksPerMinute > 0 ? stream.TicksPerMinute : 1;
            LengthFrames = Math.Max(1, (int)Math.Round(AccentSeconds * fpm / 60.0));
            MaxDiveFrames = Math.Max(1, (int)Math.Round(MaxDiveSeconds * fpm / 60.0));

            List<BallAction> actions = stream.Actions ?? new List<BallAction>();
            _secondHalfFrom = int.MaxValue;
            foreach (BallAction a in actions)
                if (a.Kind == BallActionKind.HalfTime)
                {
                    _secondHalfFrom = a.Tick;
                    break;
                }

            var saves = new List<int>();
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction a = actions[i];
                if (a.Kind == BallActionKind.Save && a.Slot >= 0 && a.Slot < stream.PlayerCount)
                    saves.Add(i);
            }

            _frames = new int[saves.Count];
            _diveFrom = new int[saves.Count];
            _home = new bool[saves.Count];
            _slot = new int[saves.Count];
            for (int k = 0; k < saves.Count; k++)
            {
                BallAction save = actions[saves[k]];
                _frames[k] = save.Tick;
                _diveFrom[k] = DiveStart(actions, saves[k], _secondHalfFrom);
                _home[k] = save.Home;
                _slot[k] = save.Slot;
            }
        }

        /// <summary>How many saves the stream records.</summary>
        public int Count => _frames.Length;

        /// <summary>How many frames the accent lasts.</summary>
        public int LengthFrames { get; }

        /// <summary>The longest reach back of the keeper's drawn path, in frames.</summary>
        public int MaxDiveFrames { get; }

        /// <summary>
        /// The save to accent at stream position <paramref name="position"/>: the latest one at or
        /// before it, while its accent lasts. False when no save is being accented.
        /// </summary>
        public bool TryAt(double position, out SaveMoment moment)
        {
            moment = default;
            int k = LatestAtOrBefore(position);
            if (k < 0)
                return false;

            double age = position - _frames[k];
            if (age >= LengthFrames)
                return false;

            // The renderer never interpolates across the whistle: from the frame before it, the
            // tokens already stand on the mirrored second-half frame while a first-half path would not.
            if (_frames[k] < _secondHalfFrom && position >= _secondHalfFrom - 1.0)
                return false;

            float strength = (float)(1.0 - age / LengthFrames);
            moment = new SaveMoment(_frames[k], _diveFrom[k], _home[k], _slot[k], strength);
            return true;
        }

        private int LatestAtOrBefore(double position)
        {
            int lo = 0, hi = _frames.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (_frames[mid] <= position)
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

        /// <summary>
        /// The strike this save stops (the latest shot by the other side, unless another outcome
        /// comes first), bounded to <see cref="MaxDiveFrames"/>; without one, a reach back as long
        /// as the accent; never before the half the save is in.
        /// </summary>
        private int DiveStart(List<BallAction> actions, int saveIndex, int secondHalfFrom)
        {
            BallAction save = actions[saveIndex];
            int start = save.Tick - LengthFrames;
            for (int j = saveIndex - 1; j >= 0; j--)
            {
                BallAction a = actions[j];
                if (a.Kind == BallActionKind.Shot)
                {
                    if (a.Home != save.Home)
                        start = a.Tick;
                    break;
                }

                if (a.Kind == BallActionKind.Goal || a.Kind == BallActionKind.Save
                    || a.Kind == BallActionKind.Miss || a.Kind == BallActionKind.Block)
                    break;
            }

            int earliest = save.Tick >= secondHalfFrom ? secondHalfFrom : 0;
            return Math.Max(earliest, Math.Max(start, save.Tick - MaxDiveFrames));
        }
    }
}
