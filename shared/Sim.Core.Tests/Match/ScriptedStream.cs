using Sim.Core.Match;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Hand-built position streams for the realism readings: 120 frames a minute (half a second a
    /// frame), keepers on their lines, each side's outfielders stacked ten metres off the centre
    /// spot, the ball on the spot and nobody on it.
    /// </summary>
    internal static class ScriptedStream
    {
        public const int Players = 11;
        public const int Keeper = 0;
        public const int Tpm = 120;
        public const int HomeStackX = 425;
        public const int AwayStackX = 625;

        public static PositionStream NewStream(int ticks)
        {
            var s = new PositionStream
            {
                TicksPerMinute = Tpm,
                PlayerCount = Players,
                LastTick = ticks - 1,
                BallXY = new int[ticks * 2],
                HomeXY = new int[ticks * Players * 2],
                AwayXY = new int[ticks * Players * 2],
                Owner = new int[ticks],
                HomePlayerIds = new int[Players],
                AwayPlayerIds = new int[Players],
                HomeShirts = new int[Players],
                AwayShirts = new int[Players]
            };

            for (int t = 0; t < ticks; t++)
            {
                Put(s, true, t, Keeper, 40, Pitch.CenterY);
                Put(s, false, t, Keeper, Pitch.LengthDm - 40, Pitch.CenterY);
                for (int i = 1; i < Players; i++)
                {
                    Put(s, true, t, i, HomeStackX, Pitch.CenterY);
                    Put(s, false, t, i, AwayStackX, Pitch.CenterY);
                }

                Ball(s, t, Pitch.CenterX, Pitch.CenterY);
            }

            return s;
        }

        public static void Put(PositionStream s, bool home, int tick, int slot, int x, int y)
        {
            int[] side = home ? s.HomeXY : s.AwayXY;
            side[(tick * Players + slot) * 2] = x;
            side[(tick * Players + slot) * 2 + 1] = y;
        }

        public static void Ball(PositionStream s, int tick, int x, int y)
        {
            s.BallXY[tick * 2] = x;
            s.BallXY[tick * 2 + 1] = y;
        }

        /// <summary>The man holds the ball from <paramref name="from"/> to <paramref name="to"/> inclusive, with the ball at (x, y).</summary>
        public static void Hold(PositionStream s, int from, int to, bool home, int slot, int x, int y)
        {
            for (int t = from; t <= to; t++)
            {
                s.Owner[t] = s.OwnerCode(home, slot);
                Ball(s, t, x, y);
            }
        }

        public static void Act(PositionStream s, int tick, BallActionKind kind, bool home, int slot = 1, int target = -1) =>
            s.Actions.Add(new BallAction(tick, kind, home, slot, target));
    }
}
