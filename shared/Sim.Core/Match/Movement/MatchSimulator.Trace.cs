namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>
        /// Hears how the men it watches are moved, tick by tick (free-ball spec R7, issue #177).
        /// Null in production; attached, it only reads, so the match does not move.
        /// </summary>
        public IMovementSink? MovementSink { get; set; }

        // The man being traced this tick (-1 for nobody) and what has been heard of him so far.
        private int _traced = -1;
        private MovementRecord _trace;

        private void MoveTraced(int tick, int side, int slot)
        {
            int k = side * _n + slot;
            _traced = k;
            _trace = new MovementRecord
            {
                Tick = tick,
                Side = side,
                Slot = slot,
                BallXDm = U.Dm(_ball.X),
                BallYDm = U.Dm(_ball.Y),
                BallOwnerSide = _ball.OwnerSide,
                BallDead = _ball.Dead,
                OffsideLineXDm = _brain.LineX(side),
                Phase = _brain.PhaseOf(side),
                InPossession = _brain.InPossession(side),
                ExpansionPermille = _brain.Expansion(side)
            };
            _brain.Move(tick, side, slot);
            _trace.VelXCmPerS = CmPerS(_vx[k]);
            _trace.VelYCmPerS = CmPerS(_vy[k]);
            _trace.XDm = U.Dm(_px[k]);
            _trace.YDm = U.Dm(_py[k]);
            _traced = -1;
            MovementSink!.Record(_trace);
        }

        private int CmPerS(int unitsPerTick) => unitsPerTick * _cfg.TicksPerSecond * 10 / U.Scale;

        private void TraceJob(int k, MovementJob baseJob, MovementJob job)
        {
            if (k != _traced) return;
            _trace.BaseJob = baseJob;
            _trace.Job = job;
        }

        private void TraceJob(int k, MovementJob job)
        {
            if (k == _traced) _trace.Job = job;
        }

        private void TraceReread(int k)
        {
            if (k == _traced) _trace.Reread = true;
        }

        private void TraceSpot(int k, int rawSpotXDm, int spotXDm, int spotYDm)
        {
            if (k != _traced) return;
            _trace.RawSpotXDm = rawSpotXDm;
            _trace.SpotXDm = spotXDm;
            _trace.SpotYDm = spotYDm;
        }

        private void TraceTarget(int k, int tx, int ty)
        {
            if (k != _traced) return;
            _trace.TargetXDm = U.Dm(tx);
            _trace.TargetYDm = U.Dm(ty);
        }

        private void TraceSteer(int k, int tx, int ty, bool sprint, int wx, int wy, int pushedX, int pushedY)
        {
            if (k != _traced) return;
            TraceTarget(k, tx, ty);
            _trace.Steered = true;
            _trace.Sprint = sprint;
            _trace.WantXCmPerS = CmPerS(wx);
            _trace.WantYCmPerS = CmPerS(wy);
            _trace.PushedXCmPerS = CmPerS(pushedX);
            _trace.PushedYCmPerS = CmPerS(pushedY);
        }
    }
}
