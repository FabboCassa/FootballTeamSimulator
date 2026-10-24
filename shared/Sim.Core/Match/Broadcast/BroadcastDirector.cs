using System.Collections.Generic;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// Turns a finished <see cref="MatchReport"/> into a playback timeline (spec R12): open play
    /// live (1.3x real time), dead time at 2x, sterile possession and the rest cut, key events
    /// always shown, each half landing on <see cref="BroadcastSettings.TargetSecondsPerHalf"/> of
    /// playback at 1x.
    ///
    /// The budget is spent by growing lead-ins BACKWARDS from the key events: a frame is cheaper the
    /// closer it is to the next one, and frames are bought cheapest-first until the half is full,
    /// so every build-up grows by the same amount and stays one contiguous clip. The cut gets more
    /// or less aggressive with the match, while a key event is never cut.
    ///
    /// Pure and integer-only: it reads the report, writes nothing back, and the same report always
    /// gives the same timeline, so a server and every client can compute it independently.
    /// </summary>
    public sealed class BroadcastDirector
    {
        // Selection keys pack (score, frame) into one long; frames stay below 2^FrameBits.
        private const int FrameBits = 24;
        private const long FrameMask = (1L << FrameBits) - 1;

        private readonly BroadcastSettings _settings;

        public BroadcastDirector(BroadcastSettings? settings = null)
        {
            _settings = settings ?? new BroadcastSettings();
        }

        public BroadcastTimeline Build(MatchReport report)
        {
            PositionStream? s = report?.Positions?.Unpack();
            if (s == null || s.TickCount == 0 || s.TicksPerMinute <= 0 || s.TickCount > FrameMask)
                return BroadcastTimeline.Empty;

            FrameReading reading = FrameClassifier.Read(s, _settings);
            var rates = new PlaybackRate[s.TickCount];
            long halfUnits = (long)_settings.TargetSecondsPerHalf * s.TicksPerMinute * PlaybackRates.UnitsPerRealFrame / 60;
            SpendHalf(reading, rates, 0, reading.SecondHalfStart, halfUnits);
            SpendHalf(reading, rates, reading.SecondHalfStart, rates.Length, halfUnits);

            IReadOnlyList<BroadcastSegment> segments = Merge(rates);
            return new BroadcastTimeline(s.TicksPerMinute, s.TickCount, reading.SecondHalfStart,
                segments, CutSummaryBuilder.Build(s, segments));
        }

        /// <summary>
        /// Fills frames [from, to) up to <paramref name="budget"/> cost units of 1x playback
        /// (see <see cref="PlaybackRates"/>: a live frame costs 20, a dead-time frame 13).
        /// </summary>
        private static void SpendHalf(FrameReading r, PlaybackRate[] rates, int from, int to, long budget)
        {
            if (to <= from) return;

            long spent = 0;
            for (int f = from; f < to; f++)
                if (r.Key[f])
                {
                    rates[f] = r.ShownRate(f);
                    spent += PlaybackRates.CostOf(rates[f]);
                }

            // Frames with no anchor ahead come after every lead-in; sterile ones after everything.
            long far = 4L * rates.Length;
            long sterile = 8L * rates.Length;

            var keys = new List<long>(to - from);
            long nearest = long.MaxValue;
            for (int f = to - 1; f >= from; f--)
            {
                if (r.Anchor[f]) nearest = f;
                if (r.Key[f] || r.Classes[f] == FrameClass.Idle) continue;

                long score = nearest == long.MaxValue ? far + (to - f) : nearest - f;
                if (r.Classes[f] == FrameClass.Sterile) score += sterile;
                keys.Add(score << FrameBits | (long)f);
            }

            keys.Sort();
            foreach (long key in keys)
            {
                int f = (int)(key & FrameMask);
                PlaybackRate rate = r.ShownRate(f);
                int cost = PlaybackRates.CostOf(rate);
                if (spent + cost > budget) break;
                spent += cost;
                rates[f] = rate;
            }
        }

        private static IReadOnlyList<BroadcastSegment> Merge(PlaybackRate[] rates)
        {
            var segments = new List<BroadcastSegment>();
            int start = 0;
            for (int f = 1; f <= rates.Length; f++)
            {
                if (f < rates.Length && rates[f] == rates[start]) continue;
                segments.Add(new BroadcastSegment(start, f, rates[start]));
                start = f;
            }

            return segments;
        }
    }
}
