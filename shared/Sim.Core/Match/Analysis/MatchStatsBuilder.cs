using System.Collections.Generic;
using Sim.Core.Config;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// Reads a played match and writes down what every man did (engine phase 7,
    /// docs/engine/MATCH_ENGINE_PLAN.md §4).
    ///
    /// It is the per-player half of <see cref="MatchAnalyzer"/>, and it holds the same contract:
    /// it consumes a finished <see cref="MatchReport"/> and its <see cref="PositionStream"/>,
    /// touches neither, draws no randomness, and therefore cannot move a result. What it produces
    /// is attached to the report as <see cref="MatchReport.Stats"/> and is deliberately left out of
    /// <see cref="MatchReportHasher"/>: a golden master is the hash of what HAPPENED, and this is
    /// a reading of it.
    ///
    /// Everything is counted off three things the stream already carries:
    ///   * the ACTION list — who struck, who passed to whom, who tackled, who was booked;
    ///   * the OWNER track — who had the ball on each frame, which is how a pass is judged to have
    ///     arrived and how a man who lost the ball is found;
    ///   * the POSITION arrays — ground covered and where he spent the match.
    /// Plus <see cref="PositionStream.Changes"/>, which is what makes the MINUTES exact: without it
    /// a substitute's slot says he played ninety.
    ///
    /// Integer arithmetic throughout (one integer square root per man per frame for the ground he
    /// covers), because a rating may be fed back into development and has to reproduce bit for bit
    /// on .NET, Mono and IL2CPP.
    /// </summary>
    public static class MatchStatsBuilder
    {
        /// <summary>Decimetres in a metre.</summary>
        private const int DmPerM = 10;

        /// <summary>How far back the owner track is read to find the man a tackle took the ball off.</summary>
        private const int DuelLookbackFrames = 6;

        /// <summary>
        /// The three lines a man is compared against for the mark (see ClassifyLines), and how many
        /// outfielders go in the first two of them. Deliberately not a formation: it is a ranking of
        /// the ten by how deep they played.
        /// </summary>
        private const int LineCount = 3;
        private const int DefenceSize = 4;
        private const int MidfieldSize = 3;

        /// <summary>
        /// Builds the performance data for a played match, or returns null when there is no picture
        /// to count (the fast path of <see cref="MatchEngine"/>, or a report whose stream has been
        /// stripped).
        /// </summary>
        public static MatchStats? Build(MatchReport report, PerformanceBalance cfg)
        {
            if (report == null) return null;
            PositionStream? stream = report.Positions;
            if (stream == null || stream.PlayerCount <= 0 || stream.TickCount <= 0) return null;

            var build = new Builder(report, stream, cfg);
            return build.Run();
        }

        /// <summary>
        /// One match's worth of state. A class rather than a pile of ref parameters because the
        /// walk needs the spell table, the keepers and the two team totals at the same time.
        /// </summary>
        private sealed class Builder
        {
            private readonly MatchReport _report;
            private readonly PositionStream _stream;
            private readonly PerformanceBalance _cfg;

            private readonly int _n;
            private readonly int _frames;
            private readonly int _fpm;

            /// <summary>Every spell in every slot, side major: [side][slot] is a list in frame order.</summary>
            private readonly List<Spell>[] _spells;

            /// <summary>The slot each side keeps goal in, inferred from the positions.</summary>
            private readonly int[] _keeperSlot = new int[2];

            private readonly TeamMatchStats[] _teams = new TeamMatchStats[2];

            /// <summary>Frames on which each side held the ball.</summary>
            private readonly int[] _possession = new int[2];

            /// <summary>Ball-in-thirds, counted from the home side's point of view.</summary>
            private int _homeThird, _middleThird, _awayThird;

            /// <summary>Block extent accumulators: [side] while defending and while attacking.</summary>
            private readonly ShapeSum[] _defending = { new ShapeSum(), new ShapeSum() };
            private readonly ShapeSum[] _attacking = { new ShapeSum(), new ShapeSum() };

            /// <summary>The last pass each side played and whether its receiver still has the ball to strike.</summary>
            private readonly int[] _lastPasser = { -1, -1 };
            private readonly int[] _lastReceiver = { -1, -1 };
            private readonly int[] _lastPassFrame = { -1, -1 };

            /// <summary>The man whose pass set up the strike that is in flight, per side.</summary>
            private readonly int[] _assistPasser = { -1, -1 };

            /// <summary>A penalty has been awarded to this side and not yet struck.</summary>
            private readonly bool[] _penaltyDue = { false, false };

            /// <summary>The pass map, keyed by side and the two slots.</summary>
            private readonly Dictionary<int, PassLink>[] _passMap =
            {
                new Dictionary<int, PassLink>(),
                new Dictionary<int, PassLink>()
            };

            public Builder(MatchReport report, PositionStream stream, PerformanceBalance cfg)
            {
                _report = report;
                _stream = stream;
                _cfg = cfg;
                _n = stream.PlayerCount;
                _frames = stream.TickCount;
                _fpm = stream.TicksPerMinute > 0 ? stream.TicksPerMinute : 1;
                _spells = new List<Spell>[2 * _n];
                _teams[0] = new TeamMatchStats { ClubId = report.HomeClubId, Home = true, Goals = report.HomeGoals };
                _teams[1] = new TeamMatchStats { ClubId = report.AwayClubId, Home = false, Goals = report.AwayGoals };
            }

            public MatchStats Run()
            {
                BuildSpells();
                FindKeepers();
                WalkFrames();
                WalkActions();
                Finish();

                var stats = new MatchStats
                {
                    Home = _teams[0],
                    Away = _teams[1],
                    Frames = _frames,
                    FramesPerMinute = _fpm
                };

                for (int side = 0; side < 2; side++)
                    for (int slot = 0; slot < _n; slot++)
                        foreach (Spell spell in _spells[side * _n + slot])
                            stats.Players.Add(spell.Stats);

                return stats;
            }

            // -------------------------------------------------------------- who was on the pitch

            /// <summary>
            /// The occupancy table. The stream's id array holds the LAST man in each slot, so the
            /// first man is recovered from the first change's outgoing player — which is exactly
            /// why <see cref="SlotChange"/> carries him.
            /// </summary>
            private void BuildSpells()
            {
                for (int side = 0; side < 2; side++)
                {
                    int[] ids = side == 0 ? _stream.HomePlayerIds : _stream.AwayPlayerIds;
                    int[] shirts = side == 0 ? _stream.HomeShirts : _stream.AwayShirts;

                    for (int slot = 0; slot < _n; slot++)
                    {
                        var list = new List<Spell>();
                        _spells[side * _n + slot] = list;

                        int firstId = slot < ids.Length ? ids[slot] : 0;
                        int firstShirt = slot < shirts.Length ? shirts[slot] : 0;
                        foreach (SlotChange change in _stream.Changes)
                        {
                            if (change.Slot != slot || change.Home != (side == 0)) continue;
                            firstId = change.OffPlayerId;
                            firstShirt = change.OffShirt;
                            break;
                        }

                        list.Add(NewSpell(side, slot, firstId, firstShirt, 0, _frames));

                        foreach (SlotChange change in _stream.Changes)
                        {
                            if (change.Slot != slot || change.Home != (side == 0)) continue;

                            int frame = Clamp(change.Frame, 0, _frames);
                            Spell previous = list[list.Count - 1];
                            if (frame < previous.From) frame = previous.From;
                            previous.End = frame;
                            list.Add(NewSpell(side, slot, change.OnPlayerId, change.OnShirt, frame, _frames));
                        }
                    }
                }

                // A man sent off leaves the pitch and his shirt is never refilled, so his spell
                // ends on the card. The frame itself stays inside it: the foul that earned the
                // card can be recorded on the very same frame.
                foreach (BallAction action in _stream.Actions)
                {
                    if (action.Kind != BallActionKind.RedCard) continue;

                    Spell? spell = At(action.Home ? 0 : 1, action.Slot, action.Tick);
                    if (spell == null) continue;

                    int end = Clamp(action.Tick + 1, spell.From, spell.End);
                    spell.End = end;
                    spell.SentOff = true;
                }
            }

            private Spell NewSpell(int side, int slot, int playerId, int shirt, int from, int end)
            {
                var stats = new PlayerMatchStats
                {
                    PlayerId = playerId,
                    ClubId = side == 0 ? _report.HomeClubId : _report.AwayClubId,
                    Home = side == 0,
                    Slot = slot,
                    Shirt = shirt
                };

                return new Spell(from, end, stats);
            }

            /// <summary>The man in a slot on a given frame, or null when nobody is (he has been sent off).</summary>
            private Spell? At(int side, int slot, int frame)
            {
                if (side < 0 || side > 1 || slot < 0 || slot >= _n) return null;

                List<Spell> list = _spells[side * _n + slot];
                for (int i = 0; i < list.Count; i++)
                    if (frame >= list[i].From && frame < list[i].End) return list[i];

                return null;
            }

            private PlayerMatchStats? StatsAt(int side, int slot, int frame) => At(side, slot, frame)?.Stats;

            /// <summary>
            /// The keeper is inferred, not asked for: over a match the man who stays nearest his
            /// own goal line IS the keeper, by a margin no outfielder comes near. Same rule as
            /// <see cref="MatchAnalyzer"/>, so the two instruments never disagree about who it is.
            /// </summary>
            private void FindKeepers()
            {
                for (int side = 0; side < 2; side++)
                {
                    int[] xy = side == 0 ? _stream.HomeXY : _stream.AwayXY;
                    int best = 0;
                    long bestDepth = long.MaxValue;

                    for (int slot = 0; slot < _n; slot++)
                    {
                        long sum = 0;
                        for (int t = 0; t < _frames; t++)
                        {
                            int x = _stream.PlayerX(xy, t, slot);
                            sum += side == 0 ? x : Pitch.LengthDm - x;
                        }

                        if (sum < bestDepth)
                        {
                            bestDepth = sum;
                            best = slot;
                        }
                    }

                    _keeperSlot[side] = best;
                    foreach (Spell spell in _spells[side * _n + best]) spell.Stats.Keeper = true;
                }
            }

            // -------------------------------------------------------------- the frames

            /// <summary>
            /// One pass over the picture: possession, territory, the shape of both blocks, and for
            /// every man the ground he covered and where he spent his match.
            /// </summary>
            private void WalkFrames()
            {
                var index = new int[2 * _n];

                for (int t = 0; t < _frames; t++)
                {
                    // Which spell each slot is on. The frames are walked in order, so this is a
                    // step forward rather than a search.
                    for (int k = 0; k < 2 * _n; k++)
                    {
                        List<Spell> list = _spells[k];
                        while (index[k] < list.Count - 1 && t >= list[index[k]].End) index[k]++;
                    }

                    int code = _stream.Owner.Length > t ? _stream.Owner[t] : PositionStream.NoOwner;
                    bool held = code != PositionStream.NoOwner;
                    bool ownerIsHome = false;
                    int ownerSlot = -1;
                    if (held) held = _stream.TryOwner(code, out ownerIsHome, out ownerSlot);

                    if (held)
                    {
                        int side = ownerIsHome ? 0 : 1;
                        _possession[side]++;

                        List<Spell> list = _spells[side * _n + Clamp(ownerSlot, 0, _n - 1)];
                        Spell holder = list[index[side * _n + Clamp(ownerSlot, 0, _n - 1)]];
                        if (t >= holder.From && t < holder.End) holder.Stats.PossessionFrames++;
                    }

                    int ballX = _stream.BallXY[t * 2];
                    if (ballX < Pitch.LengthDm / 3) _homeThird++;
                    else if (ballX < 2 * Pitch.LengthDm / 3) _middleThird++;
                    else _awayThird++;

                    for (int side = 0; side < 2; side++)
                    {
                        int[] xy = side == 0 ? _stream.HomeXY : _stream.AwayXY;
                        int keeper = _keeperSlot[side];

                        int minX = int.MaxValue, maxX = int.MinValue;
                        int minY = int.MaxValue, maxY = int.MinValue;
                        long depthSum = 0;
                        int counted = 0;

                        for (int slot = 0; slot < _n; slot++)
                        {
                            int k = side * _n + slot;
                            Spell spell = _spells[k][index[k]];
                            int x = _stream.PlayerX(xy, t, slot);
                            int y = _stream.PlayerY(xy, t, slot);

                            if (t >= spell.From && t < spell.End)
                            {
                                if (t > spell.From)
                                {
                                    long dx = x - _stream.PlayerX(xy, t - 1, slot);
                                    long dy = y - _stream.PlayerY(xy, t - 1, slot);

                                    // In TENTHS of a decimetre. An integer root truncates, and
                                    // ten thousand frames of truncation is most of a kilometre;
                                    // one more digit puts the loss under a hundred metres over a
                                    // whole match, which a reading in kilometres cannot see.
                                    spell.DistanceTenths += Sqrt((dx * dx + dy * dy) * 100);
                                }

                                spell.SumX += x;
                                spell.SumY += y;
                                spell.Samples++;
                            }

                            if (slot == keeper) continue;

                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                            depthSum += side == 0 ? x : Pitch.LengthDm - x;
                            counted++;
                        }

                        if (!held || counted <= 0) continue;

                        ShapeSum into = (ownerIsHome ? 0 : 1) == side ? _attacking[side] : _defending[side];
                        into.Samples++;
                        into.Width += maxY - minY;
                        into.Depth += maxX - minX;
                        into.Height += (int)(depthSum / counted);
                    }
                }
            }

            // -------------------------------------------------------------- the ball's story

            private void WalkActions()
            {
                List<BallAction> actions = _stream.Actions;
                int follow = _cfg.PassFollowMinutes * _fpm;
                int keyWindow = _cfg.KeyPassWindowSeconds * _fpm / 60;
                if (keyWindow < 1) keyWindow = 1;

                for (int i = 0; i < actions.Count; i++)
                {
                    BallAction a = actions[i];
                    int side = a.Home ? 0 : 1;
                    int other = 1 - side;
                    PlayerMatchStats? man = StatsAt(side, a.Slot, a.Tick);

                    switch (a.Kind)
                    {
                        case BallActionKind.Pass:
                        case BallActionKind.LongBall:
                        case BallActionKind.Cross:
                            {
                                bool arrived = PassArrived(actions, i, follow);
                                if (man != null)
                                {
                                    man.PassesAttempted++;
                                    if (arrived) man.PassesCompleted++;
                                    if (a.Kind == BallActionKind.LongBall) man.LongBalls++;
                                    if (a.Kind == BallActionKind.Cross) man.Crosses++;
                                }

                                _teams[side].PassesAttempted++;
                                if (arrived) _teams[side].PassesCompleted++;
                                Link(side, a.Slot, a.TargetSlot, arrived);

                                if (arrived && a.TargetSlot >= 0)
                                {
                                    _lastPasser[side] = a.Slot;
                                    _lastReceiver[side] = a.TargetSlot;
                                    _lastPassFrame[side] = a.Tick;
                                }
                                else
                                {
                                    _lastReceiver[side] = -1;
                                }

                                _lastReceiver[other] = -1;
                                break;
                            }

                        case BallActionKind.Dribble:
                            if (man != null) man.Carries++;
                            break;

                        case BallActionKind.Clearance:
                            if (man != null) man.Clearances++;
                            ClearMove();
                            break;

                        case BallActionKind.Interception:
                            if (man != null) man.Interceptions++;
                            ClearMove();
                            break;

                        case BallActionKind.Tackle:
                            {
                                if (man != null)
                                {
                                    man.Tackles++;
                                    man.DuelsWon++;
                                }

                                PlayerMatchStats? beaten = Dispossessed(a.Tick, other);
                                if (beaten != null) beaten.DuelsLost++;
                                ClearMove();
                                break;
                            }

                        case BallActionKind.Block:
                            if (man != null) man.Blocks++;
                            ClearMove();
                            break;

                        case BallActionKind.Shot:
                            {
                                bool onTarget = ShotWasOnTarget(actions, i);
                                int xg = _penaltyDue[side] ? _cfg.XgPenaltyPermille : Xg(a);
                                _penaltyDue[side] = false;

                                if (man != null)
                                {
                                    man.Shots++;
                                    if (onTarget) man.ShotsOnTarget++;
                                    man.XgPermille += xg;
                                }

                                _teams[side].Shots++;
                                if (onTarget) _teams[side].ShotsOnTarget++;
                                _teams[side].XgPermille += xg;

                                // The pass that found the striker, if it was his last touch and it
                                // was recent: a key pass now, an assist if this goes in.
                                _assistPasser[side] = -1;
                                if (_lastReceiver[side] == a.Slot && a.Tick - _lastPassFrame[side] <= keyWindow)
                                {
                                    PlayerMatchStats? passer = StatsAt(side, _lastPasser[side], _lastPassFrame[side]);
                                    if (passer != null && _lastPasser[side] != a.Slot)
                                    {
                                        passer.KeyPasses++;
                                        _assistPasser[side] = _lastPasser[side];
                                    }
                                }

                                _lastReceiver[side] = -1;
                                break;
                            }

                        case BallActionKind.Save:
                            if (man != null) man.Saves++;
                            ClearMove();
                            break;

                        case BallActionKind.Miss:
                            ClearMove();
                            break;

                        case BallActionKind.Goal:
                            {
                                if (man != null) man.Goals++;

                                PlayerMatchStats? keeper = StatsAt(other, _keeperSlot[other], a.Tick);
                                if (keeper != null) keeper.GoalsConceded++;

                                if (_assistPasser[side] >= 0)
                                {
                                    PlayerMatchStats? passer =
                                        StatsAt(side, _assistPasser[side], _lastPassFrame[side]);
                                    if (passer != null && passer != man) passer.Assists++;
                                }

                                ClearMove();
                                break;
                            }

                        case BallActionKind.Foul:
                            {
                                if (man != null) man.Fouls++;
                                _teams[side].Fouls++;

                                PlayerMatchStats? victim = StatsAt(other, a.TargetSlot, a.Tick);
                                if (victim != null) victim.FoulsSuffered++;
                                break;
                            }

                        case BallActionKind.YellowCard:
                            if (man != null) man.YellowCards++;
                            _teams[side].YellowCards++;
                            break;

                        case BallActionKind.RedCard:
                            if (man != null) man.RedCards++;
                            _teams[side].RedCards++;
                            break;

                        case BallActionKind.Offside:
                            if (man != null) man.Offsides++;
                            _teams[side].Offsides++;
                            ClearMove();
                            break;

                        case BallActionKind.Corner:
                            _teams[side].Corners++;
                            ClearMove();
                            break;

                        case BallActionKind.Penalty:
                            _penaltyDue[side] = true;
                            ClearMove();
                            break;

                        case BallActionKind.ThrowIn:
                        case BallActionKind.GoalKick:
                        case BallActionKind.FreeKick:
                        case BallActionKind.Kickoff:
                        case BallActionKind.HalfTime:
                            ClearMove();
                            break;
                    }
                }
            }

            /// <summary>
            /// The move is over: nobody is about to strike the ball off the last pass, and the
            /// pass that set up a strike in flight can no longer become an assist. Called by
            /// everything that breaks a move — a challenge, a block, a clearance, a restart, the
            /// whistle — which is what stops a pass from the first half turning into an assist for
            /// a scrambled goal in the second.
            /// </summary>
            private void ClearMove()
            {
                _lastReceiver[0] = -1;
                _lastReceiver[1] = -1;
                _assistPasser[0] = -1;
                _assistPasser[1] = -1;
            }

            /// <summary>
            /// The man a tackle took the ball off: the last carrier of the other side in the few
            /// frames before it. A tackle is recorded on the frame the challenge lands, by which
            /// time the owner track has already been cleared.
            /// </summary>
            private PlayerMatchStats? Dispossessed(int frame, int side)
            {
                int from = frame;
                if (from >= _stream.Owner.Length) from = _stream.Owner.Length - 1;

                for (int t = from; t >= 0 && t > from - DuelLookbackFrames; t--)
                {
                    int code = _stream.Owner[t];
                    if (code == PositionStream.NoOwner) continue;
                    if (!_stream.TryOwner(code, out bool home, out int slot)) continue;
                    if ((home ? 0 : 1) != side) continue;

                    return StatsAt(side, slot, t);
                }

                return null;
            }

            private void Link(int side, int from, int to, bool arrived)
            {
                if (from < 0 || to < 0) return;

                int key = from * 100 + to;
                if (!_passMap[side].TryGetValue(key, out PassLink? link))
                {
                    link = new PassLink { FromSlot = from, ToSlot = to };
                    _passMap[side][key] = link;
                }

                link.Attempted++;
                if (arrived) link.Completed++;
            }

            /// <summary>
            /// A pass is completed when the NEXT man to hold the ball plays for the side that
            /// struck it — the analyzer's rule, kept word for word so the two instruments cannot
            /// disagree about a pass.
            /// </summary>
            private bool PassArrived(List<BallAction> actions, int index, int follow)
            {
                int from = actions[index].Tick;
                bool byHome = actions[index].Home;

                int restartTick = int.MaxValue;
                for (int j = index + 1; j < actions.Count; j++)
                {
                    if (actions[j].Tick > from + follow) break;
                    if (IsRestart(actions[j].Kind))
                    {
                        restartTick = actions[j].Tick;
                        break;
                    }
                }

                int last = from + follow;
                if (last >= _stream.Owner.Length) last = _stream.Owner.Length - 1;

                for (int t = from + 1; t <= last; t++)
                {
                    if (t > restartTick) return false;
                    int code = _stream.Owner[t];
                    if (code == PositionStream.NoOwner) continue;
                    _stream.TryOwner(code, out bool ownerHome, out int _);
                    return ownerHome == byHome;
                }

                return false;
            }

            private static bool IsRestart(BallActionKind kind) =>
                kind == BallActionKind.ThrowIn
                || kind == BallActionKind.Corner
                || kind == BallActionKind.GoalKick
                || kind == BallActionKind.FreeKick
                || kind == BallActionKind.Penalty
                || kind == BallActionKind.Goal
                || kind == BallActionKind.Kickoff;

            /// <summary>
            /// A strike is on target when the thing that settles it is a goal or a save; the
            /// analyzer's rule again.
            /// </summary>
            private static bool ShotWasOnTarget(List<BallAction> actions, int index)
            {
                for (int j = index + 1; j < actions.Count; j++)
                {
                    BallActionKind kind = actions[j].Kind;
                    if (kind == BallActionKind.Goal || kind == BallActionKind.Save) return true;
                    if (kind == BallActionKind.Miss || kind == BallActionKind.Block) return false;
                    if (kind == BallActionKind.Shot) return false;
                }

                return false;
            }

            /// <summary>
            /// What the strike was worth, estimated from where it was taken: the ball's own
            /// position on the frame the strike was recorded, which is the only place the stream
            /// keeps it. Distance falls off as d^2/(h^2+d^2) and a tight angle takes a further
            /// share off it — the shape of a real xG surface, in integer arithmetic.
            ///
            /// It is an ESTIMATE of the chance, not the number the shot model rolled against: the
            /// pitch decides a strike from the striker's ability and the bodies in the way, and
            /// this reads the geometry back off the picture. That is the honest thing to publish
            /// on a match report, and it is why it is named for what it is.
            /// </summary>
            private int Xg(BallAction shot)
            {
                PitchPoint ball = _stream.BallAt(shot.Tick);
                long goalX = shot.Home ? Pitch.LengthDm : 0;
                long dx = ball.X - goalX;
                long dy = ball.Y - Pitch.CenterY;
                long distSq = dx * dx + dy * dy;

                long half = _cfg.XgHalfDistanceDm;
                long denominator = half * half + distSq;
                if (denominator <= 0) return 0;

                long value = (long)_cfg.XgPeakPermille * half * half / denominator;

                // The angle. Straight in front of goal it changes nothing; from the byline it
                // takes almost all of it away.
                long angleDenominator = distSq + _cfg.XgAngleWeight * dy * dy / 10;
                if (angleDenominator > 0) value = value * distSq / angleDenominator;

                if (value < 0) value = 0;
                if (value > _cfg.XgPeakPermille) value = _cfg.XgPeakPermille;
                return (int)value;
            }

            // -------------------------------------------------------------- the totals

            private void Finish()
            {
                int held = _possession[0] + _possession[1];
                int thirds = _homeThird + _middleThird + _awayThird;

                for (int side = 0; side < 2; side++)
                {
                    TeamMatchStats team = _teams[side];
                    team.PossessionPermille = held <= 0 ? 0 : 1000 * _possession[side] / held;

                    if (thirds > 0)
                    {
                        int own = side == 0 ? _homeThird : _awayThird;
                        int final = side == 0 ? _awayThird : _homeThird;
                        team.OwnThirdPermille = 1000 * own / thirds;
                        team.MiddleThirdPermille = 1000 * _middleThird / thirds;
                        team.FinalThirdPermille = 1000 * final / thirds;
                    }

                    team.DefendingWidthDm = _defending[side].AverageWidth;
                    team.DefendingDepthDm = _defending[side].AverageDepth;
                    team.DefendingHeightDm = _defending[side].AverageHeight;
                    team.AttackingWidthDm = _attacking[side].AverageWidth;
                    team.AttackingDepthDm = _attacking[side].AverageDepth;

                    foreach (KeyValuePair<int, PassLink> entry in _passMap[side])
                        team.PassMap.Add(entry.Value);

                    team.PassMap.Sort(CompareLinks);

                    for (int slot = 0; slot < _n; slot++)
                    {
                        foreach (Spell spell in _spells[side * _n + slot])
                        {
                            PlayerMatchStats stats = spell.Stats;
                            stats.DistanceDm = (int)(spell.DistanceTenths / 10);
                            stats.FromMinute = spell.From / _fpm;
                            stats.ToMinute = spell.End / _fpm;
                            if (spell.Samples > 0)
                            {
                                stats.AverageXDm = (int)(spell.SumX / spell.Samples);
                                stats.AverageYDm = (int)(spell.SumY / spell.Samples);
                            }
                        }
                    }
                }

                // The marks come LAST, because a mark is relative: the work off the ball is paid
                // on the difference from what this match asked of everybody else, and that number
                // does not exist until every man's minutes and actions are in.
                int[] line = ClassifyLines();
                var actions = new int[LineCount];
                var lost = new int[LineCount];
                for (int group = 0; group < LineCount; group++)
                {
                    actions[group] = AveragePer90(line, group, defensive: true);
                    lost[group] = AveragePer90(line, group, defensive: false);
                }

                for (int side = 0; side < 2; side++)
                    for (int slot = 0; slot < _n; slot++)
                    {
                        int group = line[side * _n + slot];
                        foreach (Spell spell in _spells[side * _n + slot])
                            spell.Stats.Rating = group < 0
                                ? MatchRatingModel.Rate(spell.Stats, _cfg)
                                : MatchRatingModel.Rate(spell.Stats, _cfg, actions[group], lost[group]);
                    }
            }

            /// <summary>
            /// What the average man OF HIS LINE did over ninety minutes: the work off the ball
            /// (tackles, interceptions, clearances) or the balls he was dispossessed of. Per ninety
            /// rather than raw, so a substitute's half hour does not drag the average down; keepers
            /// are left out because their match is a different job entirely.
            ///
            /// This is what makes the mark self-calibrating. Both figures are properties of the
            /// ENGINE — it produces some thirty recoveries a man where football has three — and a
            /// mark that reads them absolutely would have to be re-tuned every time the movement
            /// model changes. Read as a difference, it does not.
            ///
            /// BY LINE, and that is the second correction the measurement forced. Against the whole
            /// match's average, a forward is punished twice over — he recovers fewer balls than a
            /// centre-back AND gives away more of them, because that is what playing in front of
            /// the opponent's defence IS — and the first dump proved it: on both sides, every
            /// defender and midfielder came out above every forward, and a man who had just scored
            /// TWICE was marked below his own centre-half. A forward is now measured against
            /// forwards.
            /// </summary>
            private int AveragePer90(int[] line, int group, bool defensive)
            {
                long sum = 0;
                int counted = 0;

                for (int side = 0; side < 2; side++)
                {
                    for (int slot = 0; slot < _n; slot++)
                    {
                        if (line[side * _n + slot] != group) continue;

                        foreach (Spell spell in _spells[side * _n + slot])
                        {
                            PlayerMatchStats stats = spell.Stats;
                            int minutes = stats.MinutesPlayed;
                            if (minutes <= 0) continue;

                            long value = defensive ? stats.DefensiveActions : stats.DuelsLost;
                            sum += value * 90 / minutes;
                            counted++;
                        }
                    }
                }

                return counted <= 0 ? 0 : (int)(sum / counted);
            }

            /// <summary>
            /// Which line each man played in — read off where he actually SPENT the match rather
            /// than off a formation nobody sent us: the outfielders of a side are ranked by their
            /// average distance from the goal they defend, and split into the deepest four, the
            /// three in front of them and the three highest. Shape-agnostic on purpose (a 3-5-2 and
            /// a 4-3-3 both have men who defend, men who link and men who attack), deterministic
            /// (ties break on the slot), and it costs nothing: the average position is already
            /// counted. The keeper is his own case and gets -1.
            /// </summary>
            private int[] ClassifyLines()
            {
                var line = new int[2 * _n];
                var depth = new int[_n];
                var order = new int[_n];

                for (int side = 0; side < 2; side++)
                {
                    int count = 0;
                    for (int slot = 0; slot < _n; slot++)
                    {
                        line[side * _n + slot] = -1;
                        if (slot == _keeperSlot[side]) continue;

                        // The man's average X over all his spells, as depth from his own goal.
                        long sum = 0;
                        int samples = 0;
                        foreach (Spell spell in _spells[side * _n + slot])
                        {
                            if (spell.Samples <= 0) continue;
                            sum += (long)spell.Stats.AverageXDm * spell.Samples;
                            samples += spell.Samples;
                        }

                        int x = samples > 0 ? (int)(sum / samples) : Pitch.CenterX;
                        depth[count] = side == 0 ? x : Pitch.LengthDm - x;
                        order[count] = slot;
                        count++;
                    }

                    // Insertion sort by depth, ties on the slot: ten items, and it has to produce
                    // the same order on every runtime.
                    for (int i = 1; i < count; i++)
                    {
                        int d = depth[i], o = order[i], j = i - 1;
                        while (j >= 0 && (depth[j] > d || (depth[j] == d && order[j] > o)))
                        {
                            depth[j + 1] = depth[j];
                            order[j + 1] = order[j];
                            j--;
                        }

                        depth[j + 1] = d;
                        order[j + 1] = o;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        int group = i < DefenceSize ? 0 : (i < DefenceSize + MidfieldSize ? 1 : 2);
                        line[side * _n + order[i]] = group;
                    }
                }

                return line;
            }

            /// <summary>Busiest pair first, then by slot, so the map is stable across runtimes.</summary>
            private static int CompareLinks(PassLink a, PassLink b)
            {
                if (a.Attempted != b.Attempted) return b.Attempted - a.Attempted;
                if (a.FromSlot != b.FromSlot) return a.FromSlot - b.FromSlot;
                return a.ToSlot - b.ToSlot;
            }

            private static int Clamp(int value, int low, int high) =>
                value < low ? low : (value > high ? high : value);

            /// <summary>Integer square root (Newton). No Math.Sqrt: this number reaches a rating.</summary>
            private static int Sqrt(long value)
            {
                if (value <= 0) return 0;

                long x = value;
                long y = (x + 1) / 2;
                while (y < x)
                {
                    x = y;
                    y = (x + value / x) / 2;
                }

                return (int)x;
            }
        }

        /// <summary>One man's time in one slot, and what he did with it.</summary>
        private sealed class Spell
        {
            public readonly int From;
            public int End;
            public bool SentOff;
            public readonly PlayerMatchStats Stats;

            public long SumX;
            public long SumY;
            public int Samples;

            /// <summary>Ground covered, in tenths of a decimetre (see the note where it is summed).</summary>
            public long DistanceTenths;

            public Spell(int from, int end, PlayerMatchStats stats)
            {
                From = from;
                End = end;
                Stats = stats;
            }
        }

        /// <summary>Running totals of a block's extent over the frames it was sampled on.</summary>
        private sealed class ShapeSum
        {
            public int Samples;
            public long Width;
            public long Depth;
            public long Height;

            public int AverageWidth => Samples <= 0 ? 0 : (int)(Width / Samples);
            public int AverageDepth => Samples <= 0 ? 0 : (int)(Depth / Samples);
            public int AverageHeight => Samples <= 0 ? 0 : (int)(Height / Samples);
        }

        /// <summary>Metres from decimetres, for a caller that wants the reading in football's unit.</summary>
        public static double Metres(int decimetres) => decimetres / (double)DmPerM;
    }
}
