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
        FreeKick = 15,

        // The referee (engine phase 5). The whistle and the card are recorded as their own
        // actions, and the RESTART that follows is a FreeKick or a Penalty like any other, so
        // every consumer that already knows how to draw a restart keeps working. Appended at
        // the end of the enum on purpose: a replay stored before this phase carries no value
        // above 15, so its actions still mean what they meant.
        Offside = 16,
        Foul = 17,
        YellowCard = 18,
        RedCard = 19,
        Penalty = 20,

        /// <summary>The whistle at the end of the first half. The second half's Kickoff follows it.</summary>
        HalfTime = 21,

        /// <summary>
        /// A strike charged down by a defender who threw himself in front of it (engine phase 6).
        /// It is neither on target nor off it — football counts a blocked shot as its own thing —
        /// and where the ball goes off him is then anybody's. Appended at the end for the same
        /// reason the referee's actions were: a replay stored before this phase carries no value
        /// above 21, so its actions still mean what they meant.
        /// </summary>
        Block = 22
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
    /// Replayable top-down movement of players and ball (tasks 1.5 · 13.1 · engine phase 1).
    /// Tick 0 is kickoff; the last tick is minute 90. An event at minute M resolves at about
    /// tick M * TicksPerMinute, where the ball sits on the shooter's feet. Derived
    /// deterministically from (seed + lineups) *after* every outcome roll, so it can
    /// never change a result.
    ///
    /// A "tick" HERE IS A FRAME, not a step of the simulation. Since engine phase 1 the model
    /// runs at 10 Hz and the stream is written every fifth step, so one frame is 500 ms of match
    /// time and there are 120 of them a minute. Everything that reads the stream — the renderer,
    /// the analyzer, the tests, a BallAction's own Tick — is in this one index space, which is
    /// why the simulator maps its ticks into frames before it writes anything down.
    ///
    /// Storage is deliberately flat: three int arrays of decimetre coordinates rather
    /// than a list of objects with named X/Y properties. The stream is persisted as
    /// JSON per fixture and pushed to live clients on every poll, and the flat form
    /// serializes roughly 2.5x smaller — which is what pays for the frame rate the
    /// movement model needs (see BalanceConfig.MatchBalance.FramesPerMinute).
    /// Indexing: ball tick t at [2t, 2t+1]; player i of a side at
    /// [(t * PlayerCount + i) * 2, ... + 1].
    /// </summary>
    public sealed class PositionStream
    {
        /// <summary>Frames per match minute (120 since engine phase 1; it was 12, and 4 before that).</summary>
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

        // ------------------------------------------------------------- the wire form

        // A ninety-minute stream is half a million small integers. Written as decimal ASCII
        // inside a JSON array — which is what a stored replay was until engine phase 2 — that is
        // 1.9 MB a match against the 2 KB the rest of the report weighs, and a ten-club league
        // season is 180 MB of Postgres text.
        //
        // So the four integer tracks travel PACKED: per-lane delta, zigzag, varint, base64.
        // "Per-lane" because the arrays interleave frames — a man's X sits every stride values —
        // and it is HIS movement that is small: in half a second nobody covers more than a few
        // decimetres, so almost every delta fits in ONE byte. That is the whole trick, and it is
        // why this beats the int16 encoding the plan originally proposed (measured: 649 KB
        // against 1294 KB — a fixed two bytes a value cannot beat a variable one).
        //
        // The ACTION list is deliberately left as readable JSON: it is the commentary track, it
        // weighs a fifteenth of the positions, and being able to read it in a stored replay is
        // worth more than the bytes.
        //
        // Packing is EXPLICIT and symmetric — Pack() before serializing, Unpack() after parsing —
        // rather than a serializer attribute, because Sim.Core has no package references (the
        // server writes with System.Text.Json and the client reads with Newtonsoft, and neither
        // one's attributes may enter here). A stream that was never packed carries empty strings
        // and Unpack() is a no-op, so replays stored in the old shape still read.

        public string BallPacked { get; set; } = "";
        public string HomePacked { get; set; } = "";
        public string AwayPacked { get; set; } = "";
        public string OwnerPacked { get; set; } = "";

        /// <summary>Lanes in each track: the ball has X and Y, a side has both per player.</summary>
        private int SideStride => PlayerCount * 2;

        /// <summary>
        /// Moves the four integer tracks into their packed form, leaving the arrays empty. Call
        /// it immediately before serializing a report for storage or for the wire.
        /// </summary>
        public PositionStream Pack()
        {
            if (BallXY.Length == 0 && HomeXY.Length == 0 && AwayXY.Length == 0 && Owner.Length == 0)
                return this;

            BallPacked = Encode(BallXY, 2);
            HomePacked = Encode(HomeXY, SideStride);
            AwayPacked = Encode(AwayXY, SideStride);
            OwnerPacked = Encode(Owner, 1);

            BallXY = Array.Empty<int>();
            HomeXY = Array.Empty<int>();
            AwayXY = Array.Empty<int>();
            Owner = Array.Empty<int>();
            return this;
        }

        /// <summary>
        /// Restores the arrays from the packed form. A no-op on a stream that is already carrying
        /// them, so it is always safe to call after parsing — including on a replay stored before
        /// the packed form existed.
        /// </summary>
        public PositionStream Unpack()
        {
            if (BallPacked.Length == 0 && HomePacked.Length == 0
                && AwayPacked.Length == 0 && OwnerPacked.Length == 0)
                return this;

            BallXY = Decode(BallPacked, 2);
            HomeXY = Decode(HomePacked, SideStride);
            AwayXY = Decode(AwayPacked, SideStride);
            Owner = Decode(OwnerPacked, 1);

            BallPacked = "";
            HomePacked = "";
            AwayPacked = "";
            OwnerPacked = "";
            return this;
        }

        /// <summary>
        /// Per-lane delta, zigzag (so a step backwards costs the same as a step forwards), then
        /// varint. Integer-only and byte-identical on every runtime.
        /// </summary>
        private static string Encode(int[] values, int stride)
        {
            if (values.Length == 0) return "";
            if (stride < 1) stride = 1;

            var previous = new int[stride];
            var bytes = new List<byte>(values.Length + values.Length / 4);
            for (int i = 0; i < values.Length; i++)
            {
                int lane = i % stride;
                int delta = values[i] - previous[lane];
                previous[lane] = values[i];

                uint zig = (uint)((delta << 1) ^ (delta >> 31));
                while (zig >= 0x80)
                {
                    bytes.Add((byte)(zig | 0x80));
                    zig >>= 7;
                }

                bytes.Add((byte)zig);
            }

            return Convert.ToBase64String(bytes.ToArray());
        }

        private static int[] Decode(string packed, int stride)
        {
            if (string.IsNullOrEmpty(packed)) return Array.Empty<int>();
            if (stride < 1) stride = 1;

            byte[] bytes = Convert.FromBase64String(packed);
            var previous = new int[stride];
            var values = new List<int>(bytes.Length);

            int at = 0;
            while (at < bytes.Length)
            {
                uint zig = 0;
                int shift = 0;
                while (at < bytes.Length)
                {
                    byte b = bytes[at++];
                    zig |= (uint)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0) break;
                    shift += 7;
                }

                int delta = (int)(zig >> 1) ^ -(int)(zig & 1);
                int lane = values.Count % stride;
                int value = previous[lane] + delta;
                previous[lane] = value;
                values.Add(value);
            }

            return values.ToArray();
        }

        // ------------------------------------------------------------- accessors

        /// <summary>Number of frames in the stream (0 when it is empty).</summary>
        public int TickCount => Unpack().BallXY.Length / 2;

        /// <summary>Tick at which an event with the given minute resolves.</summary>
        public int TickOfMinute(int minute) => minute * TicksPerMinute;

        // The three that a renderer reaches for unpack first, so a stream that arrived packed
        // and whose owner forgot to say so draws itself instead of drawing nothing.
        public PitchPoint BallAt(int tick)
        {
            int[] ball = Unpack().BallXY;
            return new PitchPoint(ball[tick * 2], ball[tick * 2 + 1]);
        }

        public PitchPoint HomeAt(int tick, int slot) => PlayerAt(Unpack().HomeXY, tick, slot);

        public PitchPoint AwayAt(int tick, int slot) => PlayerAt(Unpack().AwayXY, tick, slot);

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
