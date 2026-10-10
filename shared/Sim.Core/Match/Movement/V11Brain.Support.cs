namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V11Brain
        {
            private const int MaxOffers = 2;

            private readonly V11SupportAngles _supportAngles;

            // Real-match spec R9, per side: who offers the short option, on which side of the
            // ball (+1/-1 across the pitch) and at which angle, and for which carrier it was chosen.
            private readonly int[] _offerer = new int[SideCount * MaxOffers];
            private readonly int[] _offerAcross = new int[SideCount * MaxOffers];
            private readonly int[] _offerAngle = new int[SideCount * MaxOffers];
            private readonly int[] _offerCarrier = new int[SideCount];

            private void BeginOffers()
            {
                for (int i = 0; i < _offerer.Length; i++) _offerer[i] = -1;
                for (int side = 0; side < SideCount; side++) _offerCarrier[side] = -1;
            }

            /// <summary>
            /// Who offers the short option, once a team-brain beat or for a new man on the ball: in
            /// the build-up and the progression, the men nearest the ball, the first on his own side
            /// of it and the second on the other, each at the best angle there (V11SupportAngles).
            /// Nobody offers to a loose ball, a dead one or the other side's.
            /// </summary>
            private void UpdateOffers(int tick, int side)
            {
                MatchBall ball = _sim._ball;
                bool offering = !ball.Dead && ball.OwnerSide == side && Building(side);
                if (!offering)
                {
                    for (int i = 0; i < MaxOffers; i++) _offerer[side * MaxOffers + i] = -1;
                    _offerCarrier[side] = -1;
                    return;
                }

                int brain = _sim._cfg.TeamBrainTicks < 1 ? 1 : _sim._cfg.TeamBrainTicks;
                if (_offerCarrier[side] == ball.OwnerSlot && tick % brain != 0) return;
                _offerCarrier[side] = ball.OwnerSlot;

                int n = _sim._n;
                bool home = side == 0;
                int bx = U.Dm(ball.X), by = U.Dm(ball.Y);
                int offers = MovementGeometry.Clamp(_sim._cfg.V11SupportOffers, 0, MaxOffers);
                int across = 0;
                for (int i = 0; i < MaxOffers; i++)
                {
                    int o = side * MaxOffers + i;
                    _offerer[o] = i < offers ? NearestFree(side, ball, o) : -1;
                    if (_offerer[o] < 0) continue;

                    int k = side * n + _offerer[o];
                    int mx = U.Dm(_sim._px[k]), my = U.Dm(_sim._py[k]);
                    across = i == 0 ? V11SupportAngles.SideOf(by, my) : -across;
                    _offerAcross[o] = across;
                    _offerAngle[o] = _supportAngles.BestAngle(home, bx, by, across, mx, my,
                        _foeX[side], _foeY[side], _foeCount[side]);
                    if (_offerAngle[o] == V11SupportAngles.NoAngle) _offerer[o] = -1;
                }
            }

            /// <summary>
            /// Whether this side is building, where the short option is offered and used (R9): it
            /// has the ball in play short of the final third — in the build-up, the progression, or
            /// the transition that has just won it there.
            /// </summary>
            private bool Building(int side)
            {
                TeamPhase phase = _phases.PhaseOf(side);
                if (phase == TeamPhase.BuildUp || phase == TeamPhase.Progression) return true;
                if (phase != TeamPhase.AttackTransition) return false;
                int x = U.Dm(_sim._ball.X);
                int depth = side == 0 ? x : Pitch.LengthDm - x;
                return depth < Pitch.LengthDm * _sim._cfg.PhaseFinalThirdStartPermille / 1000;
            }

            /// <summary>
            /// The outfielder nearest the ball who is not on it, not running in behind and not
            /// already offering. Read off where he stands AND where his place in the shape is
            /// (<see cref="MatchBalance.V11SupportOfferSpotPercent"/> of the second), so it is a man
            /// whose zone the ball is in who comes short, not one the last move left nearby.
            /// </summary>
            private int NearestFree(int side, MatchBall ball, int upTo)
            {
                int n = _sim._n;
                int best = -1;
                long bestDistance = long.MaxValue;
                int spotPercent = _sim._cfg.V11SupportOfferSpotPercent;
                bool home = side == 0;
                Tactics.TacticInstructions instructions = _sim._tactics[side].Instructions;
                TeamPhase phase = _phases.PhaseOf(side);
                for (int j = 0; j < n; j++)
                {
                    int k = side * n + j;
                    if (j == ball.OwnerSlot || j == _runner[side] || _sim._keeper[k] || _sim._sentOff[k]) continue;
                    bool taken = false;
                    for (int o = side * MaxOffers; o < upTo; o++) taken |= _offerer[o] == j;
                    if (taken) continue;

                    long d = U.DistanceSq(_sim._px[k], _sim._py[k], ball.X, ball.Y);
                    if (spotPercent > 0)
                    {
                        _positioning.PhaseSpot(SlotOf(side, j), home, phase, _expansion[side], instructions,
                            U.Dm(ball.X), U.Dm(ball.Y), out int sx, out int sy);
                        long fromSpot = U.DistanceSq(U.Units(sx), U.Units(sy), ball.X, ball.Y);
                        d = (d * (100 - spotPercent) + fromSpot * spotPercent) / 100;
                    }

                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = j;
                    }
                }

                return best;
            }

            /// <summary>Where this man offers the short option now, in units, when he is one of the offerers.</summary>
            private bool OfferTarget(int side, int slot, out int x, out int y)
            {
                x = 0;
                y = 0;
                for (int i = 0; i < MaxOffers; i++)
                {
                    int o = side * MaxOffers + i;
                    if (_offerer[o] != slot) continue;

                    MatchBall ball = _sim._ball;
                    if (!_supportAngles.Spot(side == 0, U.Dm(ball.X), U.Dm(ball.Y), _offerAcross[o], _offerAngle[o],
                            out int sx, out int sy))
                        return false;
                    x = U.Units(sx);
                    y = U.Units(sy);
                    return true;
                }

                return false;
            }
        }
    }
}
