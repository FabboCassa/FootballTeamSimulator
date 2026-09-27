using System;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// How a stretch of the stream is played back. The value IS the speed factor over real time,
    /// so nothing here can be slower than real time; <see cref="Cut"/> is skipped entirely.
    /// </summary>
    public enum PlaybackRate
    {
        Cut = 0,
        RealTime = 1,
        Double = 2
    }

    /// <summary>Who had the ball through a cut span.</summary>
    public enum PossessionSide
    {
        None = 0,
        Home = 1,
        Away = 2
    }

    /// <summary>Where the ball mostly was during a cut span, seen from the side in possession.</summary>
    public enum CutZone
    {
        Defensive = 0,
        Middle = 1,
        Attacking = 2
    }

    /// <summary>Frames [<see cref="StartFrame"/>, <see cref="EndFrame"/>) of the stream, played at <see cref="Rate"/>.</summary>
    public readonly struct BroadcastSegment : IEquatable<BroadcastSegment>
    {
        public int StartFrame { get; }
        public int EndFrame { get; }
        public PlaybackRate Rate { get; }

        public BroadcastSegment(int startFrame, int endFrame, PlaybackRate rate)
        {
            StartFrame = startFrame;
            EndFrame = endFrame;
            Rate = rate;
        }

        public int Frames => EndFrame - StartFrame;

        public bool Equals(BroadcastSegment other) =>
            StartFrame == other.StartFrame && EndFrame == other.EndFrame && Rate == other.Rate;

        public override bool Equals(object? obj) => obj is BroadcastSegment other && Equals(other);

        public override int GetHashCode() => (StartFrame * 397 ^ EndFrame) * 397 ^ (int)Rate;

        public override string ToString() => $"[{StartFrame},{EndFrame}) {Rate}";
    }

    /// <summary>
    /// The one line a viewer gets for a cut span of a match minute or more (R14): who had the
    /// ball, where, and the stoppage that happened in it (null when play never stopped).
    /// </summary>
    public readonly struct CutSummary : IEquatable<CutSummary>
    {
        public int StartFrame { get; }
        public int EndFrame { get; }
        public PossessionSide Possession { get; }
        public CutZone Zone { get; }
        public BallActionKind? Stoppage { get; }

        public CutSummary(int startFrame, int endFrame, PossessionSide possession, CutZone zone, BallActionKind? stoppage)
        {
            StartFrame = startFrame;
            EndFrame = endFrame;
            Possession = possession;
            Zone = zone;
            Stoppage = stoppage;
        }

        public bool Equals(CutSummary other) =>
            StartFrame == other.StartFrame && EndFrame == other.EndFrame && Possession == other.Possession
            && Zone == other.Zone && Stoppage == other.Stoppage;

        public override bool Equals(object? obj) => obj is CutSummary other && Equals(other);

        public override int GetHashCode() =>
            ((StartFrame * 397 ^ EndFrame) * 397 ^ (int)Possession) * 397 ^ (int)Zone;

        public override string ToString() => $"[{StartFrame},{EndFrame}) {Possession} {Zone} {Stoppage}";
    }
}
