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
                h = Mix(h, stream.PlayerCount);
                h = Mix(h, stream.LastTick);

                h = MixAll(h, stream.BallXY);
                h = MixAll(h, stream.HomeXY);
                h = MixAll(h, stream.AwayXY);
                h = MixAll(h, stream.Owner);
                h = MixAll(h, stream.HomePlayerIds);
                h = MixAll(h, stream.AwayPlayerIds);
                h = MixAll(h, stream.HomeShirts);
                h = MixAll(h, stream.AwayShirts);

                h = Mix(h, stream.Actions.Count);
                foreach (BallAction a in stream.Actions)
                {
                    h = Mix(h, a.Tick);
                    h = Mix(h, (int)a.Kind);
                    h = Mix(h, a.Home ? 1 : 0);
                    h = Mix(h, a.Slot);
                    h = Mix(h, a.TargetSlot);
                }
            }

            return h;
        }

        /// <summary>Folds a whole coordinate array in, length first.</summary>
        private static ulong MixAll(ulong h, int[] values)
        {
            h = Mix(h, values.Length);
            for (int i = 0; i < values.Length; i++) h = Mix(h, values[i]);
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
