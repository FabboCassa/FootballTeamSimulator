using System;
using System.Collections.Generic;

namespace Sim.Core.Match
{
    /// <summary>
    /// What the ball is doing at a given tick — the commentary track of the replay
    /// (task 13.1). Presentation-only, like the positions themselves: the result
    /// model (1.4) owns the score and the event timeline, and a BallAction never
    /// feeds back into it.
    /// </summary>
    public enum BallActionKind
    {
        Kickoff = 0,
        Pass = 1,
        LongBall = 2,
        Cross = 3,
        Dribble = 4,
        Tackle = 5,
        Interception = 6,
        Clearance = 7,
        Shot = 8,
        Save = 9,
        Goal = 10,
        Miss = 11,
        Corner = 12,
        ThrowIn = 13,
        GoalKick = 14,
        FreeKick = 15
    }

    /// <summary>
    /// One thing that happens to the ball, at one tick. <see cref="Slot"/> is the lineup
    /// slot of the player doing it and <see cref="TargetSlot"/> the intended receiver
    /// (-1 when there is none: a shot, a clearance, a ball out of play).
    /// </summary>
    public struct BallAction
    {
        public int Tick { get; set; }
        public BallActionKind Kind { get; set; }
        /// <summary>True when the home side performs the action.</summary>
        public bool Home { get; set; }
        public int Slot { get; set; }
        public int TargetSlot { get; set; }

        public BallAction(int tick, BallActionKind kind, bool home, int slot, int targetSlot)
        {
            Tick = tick;
            Kind = kind;
            Home = home;
            Slot = slot;
            TargetSlot = targetSlot;
        }
    }

    /// <summary>
    /// Replayable top-down movement of players and ball (tasks 1.5 · 13.1). Tick 0 is
    /// kickoff; the last tick is minute 90. An event at minute M resolves exactly at
    /// tick M * TicksPerMinute, where the ball sits on the shooter's feet. Derived
    /// deterministically from (seed + lineups) *after* every outcome roll, so it can
    /// never change a result.
    ///
    /// Storage is deliberately flat: three int arrays of decimetre coordinates rather
    /// than a list of objects with named X/Y properties. The stream is persisted as
    /// JSON per fixture and pushed to live clients on every poll, and the flat form
    /// serializes roughly 2.5x smaller — which is what pays for the higher tick rate
    /// the movement model needs (see BalanceConfig.MatchBalance.TicksPerMinute).
    /// Indexing: ball tick t at [2t, 2t+1]; player i of a side at
    /// [(t * PlayerCount + i) * 2, ... + 1].
    /// </summary>
    public sealed class PositionStream
    {
        /// <summary>Position snapshots per match minute.</summary>
        public int TicksPerMinute { get; set; }

        /// <summary>Players per side (<see cref="Lineup.Size"/>).</summary>
        public int PlayerCount { get; set; }

        /// <summary>Index of the final tick (minute 90). Frame count is LastTick + 1.</summary>
        public int LastTick { get; set; }

        public int[] BallXY { get; set; } = Array.Empty<int>();
        public int[] HomeXY { get; set; } = Array.Empty<int>();
        public int[] AwayXY { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Who holds the ball at each tick: 0 = loose/in flight/dead, 1..PlayerCount the
        /// home slot, PlayerCount+1..2*PlayerCount the away slot. Lets the renderer ring
        /// the carrier without re-deriving possession from the action list.
        /// </summary>
        public int[] Owner { get; set; } = Array.Empty<int>();

        /// <summary>Player ids by slot, so a client holding the squad can name them.</summary>
        public int[] HomePlayerIds { get; set; } = Array.Empty<int>();
        public int[] AwayPlayerIds { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Shirt numbers by slot. Carried in the stream because an online replay reaches
        /// the client as a bare MatchReport — without the lineups, a number is the only
        /// way the commentary can name anybody.
        /// </summary>
        public int[] HomeShirts { get; set; } = Array.Empty<int>();
        public int[] AwayShirts { get; set; } = Array.Empty<int>();

        /// <summary>The ball's story, in tick order.</summary>
        public List<BallAction> Actions { get; set; } = new List<BallAction>();

        // ------------------------------------------------------------- accessors

        /// <summary>Number of frames in the stream (0 when it is empty).</summary>
        public int TickCount => BallXY.Length / 2;

        /// <summary>Tick at which an event with the given minute resolves.</summary>
        public int TickOfMinute(int minute) => minute * TicksPerMinute;

        public PitchPoint BallAt(int tick) => new PitchPoint(BallXY[tick * 2], BallXY[tick * 2 + 1]);

        public PitchPoint HomeAt(int tick, int slot) => PlayerAt(HomeXY, tick, slot);

        public PitchPoint AwayAt(int tick, int slot) => PlayerAt(AwayXY, tick, slot);

        /// <summary>Position of one player, from the side's flat array.</summary>
        public PitchPoint PlayerAt(int[] side, int tick, int slot)
        {
            int i = (tick * PlayerCount + slot) * 2;
            return new PitchPoint(side[i], side[i + 1]);
        }

        /// <summary>Positions of one side at one tick, or of the ball, without allocating a point.</summary>
        public int PlayerX(int[] side, int tick, int slot) => side[(tick * PlayerCount + slot) * 2];
        public int PlayerY(int[] side, int tick, int slot) => side[(tick * PlayerCount + slot) * 2 + 1];

        // ------------------------------------------------------------- owner codes

        public const int NoOwner = 0;

        /// <summary>Encodes a carrier as a single int (see <see cref="Owner"/>).</summary>
        public int OwnerCode(bool home, int slot) => (home ? 0 : PlayerCount) + slot + 1;

        /// <summary>Decodes an owner code. Returns false when the ball is loose.</summary>
        public bool TryOwner(int code, out bool home, out int slot)
        {
            home = code <= PlayerCount;
            slot = home ? code - 1 : code - PlayerCount - 1;
            return code != NoOwner;
        }
    }
}
