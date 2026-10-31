using System;
using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Free-ball spec R3: a man changes the ball only through a touch within his reach — his feet
    /// within <see cref="MatchBalance.ControlRadiusDm"/> of it, the ball no higher than
    /// <see cref="MatchBalance.TouchHeightDm"/> (a header), a keeper's hands
    /// <see cref="MatchBalance.KeeperSaveReachDm"/> further and as much higher. Reads the ball events
    /// the engine stamps with the toucher's distance; a touch beyond reach is a defect, the
    /// magnetism R3 removes.
    /// </summary>
    public static class TouchReachAnalyzer
    {
        /// <summary>The touches and deflections of one match made from beyond reach.</summary>
        public static int BeyondReach(IReadOnlyList<BallEvent> events, MatchBalance cfg)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            int beyond = 0;
            foreach (BallEvent e in events)
                if (IsBeyondReach(e, cfg)) beyond++;
            return beyond;
        }

        public static bool IsBeyondReach(in BallEvent e, MatchBalance cfg)
        {
            if (e.Kind != BallEventKind.Touch && e.Kind != BallEventKind.Deflection) return false;
            return e.FromFeetMm > (cfg.ControlRadiusDm + (e.Keeper ? cfg.KeeperSaveReachDm : 0)) * 100
                   || e.ZDm > cfg.TouchHeightDm + (e.Keeper ? cfg.KeeperArmHeightDm : 0);
        }
    }
}
