using System;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The ball-continuity, shot-flight and keeper counts of one match (believable-match spec R2),
    /// both sides together. See <see cref="BallContinuityAnalyzer"/> for the definitions.
    ///
    /// Raw counts, sums and speed histograms only, so matches pool by adding them
    /// (<see cref="Plus"/>) and the percentiles read the pooled frames.
    /// </summary>
    public sealed class BallContinuityMetrics
    {
        /// <summary>Histogram resolution: bins per m/s.</summary>
        public const int BinsPerMps = 10;

        /// <summary>Speeds at or above this land in the last bin.</summary>
        public const int MaxMps = 100;

        private const int BinCount = MaxMps * BinsPerMps + 1;

        private readonly int[] _passSpeeds = new int[BinCount];
        private readonly int[] _shotSpeeds = new int[BinCount];

        public int Matches { get; set; } = 1;

        /// <summary>Frame-to-frame ball steps over 8 m, and those within a frame of a restart being filed.</summary>
        public int Jumps { get; set; }
        public int JumpsAtRestarts { get; set; }

        /// <summary>Restarts read, and those whose ball reached the spot faster than a transport can.</summary>
        public int Restarts { get; set; }
        public int PlacedRestarts { get; set; }

        /// <summary>Shots settled as a Miss, those whose ball crossed the goal line, and the summed gap of the rest.</summary>
        public int WideShots { get; set; }
        public int WideShotsCrossing { get; set; }
        public double WideShotShortSumM { get; set; }

        public int Goals { get; set; }
        public int GoalsBehindLine { get; set; }

        /// <summary>Keeper displacement from shot release to the ball reaching the line (or the save).</summary>
        public int SaveDisplacements { get; set; }
        public double SaveDisplacementSumM { get; set; }
        public int GoalDisplacements { get; set; }
        public double GoalDisplacementSumM { get; set; }

        /// <summary>Saves whose shot path could be read, and those with the keeper within 1.5 m of it.</summary>
        public int SavesRead { get; set; }
        public int SavesNear { get; set; }

        public void AddSpeed(bool shot, double mps)
        {
            int bin = mps <= 0 ? 0 : (int)(mps * BinsPerMps);
            if (bin >= BinCount) bin = BinCount - 1;
            (shot ? _shotSpeeds : _passSpeeds)[bin]++;
        }

        /// <summary>Flight frames read for the kind.</summary>
        public int SpeedFrames(bool shot)
        {
            int sum = 0;
            foreach (int c in shot ? _shotSpeeds : _passSpeeds) sum += c;
            return sum;
        }

        /// <summary>The <paramref name="p"/> quantile (0-1) of the kind's frame speeds, at its bin's middle; 0 with no frames.</summary>
        public double SpeedPercentile(bool shot, double p)
        {
            int[] bins = shot ? _shotSpeeds : _passSpeeds;
            int total = SpeedFrames(shot);
            if (total == 0) return 0;
            long rank = Math.Max(1, (long)Math.Ceiling(p * total));
            long seen = 0;
            for (int i = 0; i < bins.Length; i++)
            {
                seen += bins[i];
                if (seen >= rank) return (i + 0.5) / BinsPerMps;
            }

            return (bins.Length - 0.5) / BinsPerMps;
        }

        /// <summary>A new reading holding both: every count, sum and bin added, matches included.</summary>
        public BallContinuityMetrics Plus(BallContinuityMetrics o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            var sum = new BallContinuityMetrics
            {
                Matches = Matches + o.Matches,
                Jumps = Jumps + o.Jumps,
                JumpsAtRestarts = JumpsAtRestarts + o.JumpsAtRestarts,
                Restarts = Restarts + o.Restarts,
                PlacedRestarts = PlacedRestarts + o.PlacedRestarts,
                WideShots = WideShots + o.WideShots,
                WideShotsCrossing = WideShotsCrossing + o.WideShotsCrossing,
                WideShotShortSumM = WideShotShortSumM + o.WideShotShortSumM,
                Goals = Goals + o.Goals,
                GoalsBehindLine = GoalsBehindLine + o.GoalsBehindLine,
                SaveDisplacements = SaveDisplacements + o.SaveDisplacements,
                SaveDisplacementSumM = SaveDisplacementSumM + o.SaveDisplacementSumM,
                GoalDisplacements = GoalDisplacements + o.GoalDisplacements,
                GoalDisplacementSumM = GoalDisplacementSumM + o.GoalDisplacementSumM,
                SavesRead = SavesRead + o.SavesRead,
                SavesNear = SavesNear + o.SavesNear,
            };
            for (int i = 0; i < BinCount; i++)
            {
                sum._passSpeeds[i] = _passSpeeds[i] + o._passSpeeds[i];
                sum._shotSpeeds[i] = _shotSpeeds[i] + o._shotSpeeds[i];
            }

            return sum;
        }
    }
}
