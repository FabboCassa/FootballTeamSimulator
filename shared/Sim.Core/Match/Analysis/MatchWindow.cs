namespace Sim.Core.Match.Analysis
{
    /// <summary>The score from one side's point of view.</summary>
    public enum ScoreState { Trailing = 0, Level = 1, Leading = 2 }

    /// <summary>
    /// Readings of one side over a window of whole minutes of a finished position stream, for the
    /// effect measurements of the watchable-match spec (R10, R11). A window [from, to) covers the
    /// frames from the first of minute <c>from</c> to the last of minute <c>to - 1</c> — the span a
    /// change or shout injected at minute <c>from</c> is in force for. Home attacks the far goal
    /// (x = pitch length) for the whole match. Read-only: it can never move a result.
    /// </summary>
    public static class MatchWindow
    {
        public static ScoreState StateOf(int own, int against) =>
            own > against ? ScoreState.Leading : own < against ? ScoreState.Trailing : ScoreState.Level;

        /// <summary>The side's score state as minute <paramref name="minute"/> starts (goals of earlier minutes).</summary>
        public static ScoreState StateAt(PositionStream s, bool home, int minute) =>
            StateOf(Goals(s, home, 0, minute), Goals(s, !home, 0, minute));

        public static int Goals(PositionStream s, bool home, int from, int to) =>
            Count(s, home, from, to, BallActionKind.Goal);

        public static int GoalDifference(PositionStream s, bool home, int from, int to) =>
            Goals(s, home, from, to) - Goals(s, !home, from, to);

        public static int Shots(PositionStream s, bool home, int from, int to) =>
            Count(s, home, from, to, BallActionKind.Shot);

        /// <summary>Balls the side won back (recovery or interception) in the opponent's half.</summary>
        public static int HighRecoveries(PositionStream s, bool home, int from, int to) =>
            Recoveries(s, home, from, to, highOnly: true);

        /// <summary>Balls the side lost: the opponent's recoveries and interceptions, anywhere.</summary>
        public static int BallLosses(PositionStream s, bool home, int from, int to) =>
            Recoveries(s, !home, from, to, highOnly: false);

        /// <summary>Balls the side lost in its own half — the opponent's high recoveries.</summary>
        public static int OwnHalfLosses(PositionStream s, bool home, int from, int to) =>
            Recoveries(s, !home, from, to, highOnly: true);

        /// <summary>Frames the side held the ball over frames either side did; 0.5 when nobody did.</summary>
        public static double PossessionShare(PositionStream s, bool home, int from, int to)
        {
            s.Unpack();
            int first = Frame(s, from), end = Frame(s, to);
            if (end > s.Owner.Length) end = s.Owner.Length;

            int own = 0, any = 0;
            for (int t = first; t < end; t++)
            {
                if (!s.TryOwner(s.Owner[t], out bool ownerHome, out int _)) continue;
                any++;
                if (ownerHome == home) own++;
            }

            return any == 0 ? 0.5 : (double)own / any;
        }

        private static int Count(PositionStream s, bool home, int from, int to, BallActionKind kind)
        {
            int first = Frame(s, from), end = Frame(s, to), n = 0;
            foreach (BallAction a in s.Actions)
                if (a.Kind == kind && a.Home == home && a.Tick >= first && a.Tick < end) n++;
            return n;
        }

        private static int Recoveries(PositionStream s, bool home, int from, int to, bool highOnly)
        {
            s.Unpack();
            int first = Frame(s, from), end = Frame(s, to), n = 0;
            foreach (BallAction a in s.Actions)
            {
                if (a.Home != home || a.Tick < first || a.Tick >= end) continue;
                if (a.Kind != BallActionKind.Recovery && a.Kind != BallActionKind.Interception) continue;
                if (highOnly && !InOpponentHalf(home, s.BallXY[a.Tick * 2])) continue;
                n++;
            }

            return n;
        }

        private static bool InOpponentHalf(bool home, int x) => home ? x > Pitch.CenterX : x < Pitch.CenterX;

        private static int Frame(PositionStream s, int minute) => minute * s.TicksPerMinute;
    }
}
