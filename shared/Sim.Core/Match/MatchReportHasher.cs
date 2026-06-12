namespace Sim.Core.Match
{
    /// <summary>
    /// Order-sensitive FNV-1a (64-bit) hash of a complete MatchReport,
    /// including the position stream. Uses only unsigned integer arithmetic
    /// on the report's integer fields, so the hash is bit-identical across
    /// .NET, Unity Mono and Unity IL2CPP - the basis of the cross-runtime
    /// determinism check (task 1.6) and of online replay verification.
    /// </summary>
    public static class MatchReportHasher
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Hash(MatchReport report)
        {
            ulong h = OffsetBasis;

            h = Mix(h, report.EngineVersion);
            h = Mix(h, report.HomeClubId);
            h = Mix(h, report.AwayClubId);
            h = Mix(h, report.HomeGoals);
            h = Mix(h, report.AwayGoals);

            h = Mix(h, report.Events.Count);
            foreach (MatchEvent e in report.Events)
            {
                h = Mix(h, e.Minute);
                h = Mix(h, (int)e.Type);
                h = Mix(h, e.ClubId);
                h = Mix(h, e.PlayerId);
            }

            PositionStream? stream = report.Positions;
            h = Mix(h, stream == null ? 0 : 1);
            if (stream != null)
            {
                h = Mix(h, stream.TicksPerMinute);
                h = Mix(h, stream.Frames.Count);
                foreach (PositionFrame f in stream.Frames)
                {
                    h = Mix(h, f.Tick);
                    h = Mix(h, f.Ball.X);
                    h = Mix(h, f.Ball.Y);
                    foreach (PitchPoint p in f.Home) { h = Mix(h, p.X); h = Mix(h, p.Y); }
                    foreach (PitchPoint p in f.Away) { h = Mix(h, p.X); h = Mix(h, p.Y); }
                }
            }

            return h;
        }

        /// <summary>Folds one 32-bit value into the hash, byte by byte (FNV-1a).</summary>
        private static ulong Mix(ulong h, int value)
        {
            unchecked
            {
                uint v = (uint)value;
                h = (h ^ (v & 0xFF)) * Prime;
                h = (h ^ ((v >> 8) & 0xFF)) * Prime;
                h = (h ^ ((v >> 16) & 0xFF)) * Prime;
                h = (h ^ ((v >> 24) & 0xFF)) * Prime;
                return h;
            }
        }
    }
}
