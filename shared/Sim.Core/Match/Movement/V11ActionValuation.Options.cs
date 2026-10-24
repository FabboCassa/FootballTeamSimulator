using System;

namespace Sim.Core.Match.Movement
{
    public sealed partial class V11ActionValuation
    {
        /// <summary>How many options the explained replay shows for a decision (R13).</summary>
        public const int ExplainedOptions = 3;

        /// <summary>
        /// The dearest options open to the man on the ball, dearest first, for the explained replay
        /// (believable-match spec R13): the carry, the clearance, the shot and the best ball to each
        /// team-mate, priced as <see cref="Choose"/> prices them. Fills <paramref name="top"/> and
        /// returns how many it holds. Read-only on the scene and allocation-free.
        /// </summary>
        public int TopOptions(V11Scene s, Span<V11Choice> top)
        {
            int count = 0;
            int carry = CarryValue(s, out int cx, out int cy);
            Offer(top, ref count, new V11Choice
            {
                Kind = V11ActionKind.Carry, Mate = -1, XDm = cx, YDm = cy,
                SafetyPermille = s.CarryKeepPermille, ValuePer10k = carry
            });
            Offer(top, ref count, new V11Choice
            {
                Kind = V11ActionKind.Clear, Mate = -1, XDm = s.ClearXDm, YDm = s.ClearYDm,
                SafetyPermille = _cfg.ClearanceRetentionPermille, ValuePer10k = ClearValue(s)
            });
            Offer(top, ref count, new V11Choice
            {
                Kind = V11ActionKind.Shot, Mate = -1, XDm = GoalX(s), YDm = Pitch.CenterY, ValuePer10k = ShotValue(s)
            });

            for (int m = 0; m < s.MateCount; m++)
            {
                PassValue(s, NoOption, 0, _models, out V11Choice pass, m);
                Offer(top, ref count, pass);
            }

            return count;
        }

        /// <summary>Inserts an open option into the dearest-first list; on a tie the one offered first stays ahead.</summary>
        private static void Offer(Span<V11Choice> top, ref int count, in V11Choice option)
        {
            if (option.ValuePer10k <= NoOption) return;

            int at = count;
            while (at > 0 && top[at - 1].ValuePer10k < option.ValuePer10k) at--;
            if (at >= top.Length) return;

            for (int i = Math.Min(count, top.Length - 1); i > at; i--) top[i] = top[i - 1];
            top[at] = option;
            if (count < top.Length) count++;
        }
    }
}
