namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The central-zone counts of one match per side (see <see cref="CentralZoneAnalyzer"/>): on-ball
    /// spells touching the opponent's penalty area, spells reaching the central zone, and those of
    /// them with no defender within 3 m. Raw counts, so matches pool by adding them.
    /// </summary>
    public sealed class CentralZoneMetrics
    {
        private readonly int[] _box = new int[2];
        private readonly int[] _zone = new int[2];
        private readonly int[] _unpressed = new int[2];

        // Issue #210, per side and ChanceSource: box and zone arrivals (and of them the pressed ones),
        // and the shots, goals and location xG (permille) of the spells each source labels.
        private readonly int[,] _boxBy = new int[2, CentralZoneAnalyzer.SourceCount];
        private readonly int[,] _boxPressedBy = new int[2, CentralZoneAnalyzer.SourceCount];
        private readonly int[,] _zoneBy = new int[2, CentralZoneAnalyzer.SourceCount];
        private readonly int[,] _zonePressedBy = new int[2, CentralZoneAnalyzer.SourceCount];
        private readonly int[,] _shotsBy = new int[2, CentralZoneAnalyzer.SourceCount];
        private readonly int[,] _goalsBy = new int[2, CentralZoneAnalyzer.SourceCount];
        private readonly int[,] _xgBy = new int[2, CentralZoneAnalyzer.SourceCount];
        private readonly int[,] _carryFrom = new int[2, CentralZoneAnalyzer.SourceCount];
        private readonly int[,] _carryFromBand = new int[2, CarryBands];

        // Shots from open-play spells that never touched the box (their goals, location xG), and goals
        // off a set-piece taker's own shot.
        private readonly int[] _outsideShots = new int[2], _outsideGoals = new int[2], _outsideXg = new int[2], _restartGoals = new int[2];

        /// <summary>Bands of where a carry into the zone began, from the goal centre: under 20 m, 20-25 m, 25 m and over.</summary>
        public const int CarryBands = 3;

        /// <summary>On-ball spells of this side with a frame inside the opponent's penalty area.</summary>
        public int BoxSpells(bool home) => _box[home ? 0 : 1];

        /// <summary>On-ball spells of this side that reached the central zone.</summary>
        public int ZoneSpells(bool home) => _zone[home ? 0 : 1];

        /// <summary>Of <see cref="ZoneSpells"/>, those with no outfield defender within 3 m on arrival.</summary>
        public int ZoneUnpressed(bool home) => _unpressed[home ? 0 : 1];

        /// <summary>Box spells of this side whose arrival in the box came from <paramref name="source"/>.</summary>
        public int BoxSpells(bool home, ChanceSource source) => _boxBy[home ? 0 : 1, (int)source];

        /// <summary>Of those, the ones with an outfield defender within 3 m on the first frame in the box.</summary>
        public int BoxPressed(bool home, ChanceSource source) => _boxPressedBy[home ? 0 : 1, (int)source];

        /// <summary>Zone spells of this side whose arrival in the zone came from <paramref name="source"/>.</summary>
        public int ZoneSpells(bool home, ChanceSource source) => _zoneBy[home ? 0 : 1, (int)source];

        /// <summary>Of those, the ones pressed on arrival (an outfield defender within 3 m).</summary>
        public int ZonePressed(bool home, ChanceSource source) => _zonePressedBy[home ? 0 : 1, (int)source];

        /// <summary>Shots ending a spell labelled <paramref name="source"/> (its zone arrival's, else its box arrival's).</summary>
        public int Shots(bool home, ChanceSource source) => _shotsBy[home ? 0 : 1, (int)source];

        /// <summary>Goals from those shots (rebounds of them count under the rebound's own spell).</summary>
        public int Goals(bool home, ChanceSource source) => _goalsBy[home ? 0 : 1, (int)source];

        /// <summary>Location xG of those shots, permille: no pressure and neutral finishing.</summary>
        public int XgPermille(bool home, ChanceSource source) => _xgBy[home ? 0 : 1, (int)source];

        public void AddBoxSource(bool home, ChanceSource source, bool pressed)
        {
            _boxBy[home ? 0 : 1, (int)source]++;
            if (pressed) _boxPressedBy[home ? 0 : 1, (int)source]++;
        }

        public void AddZoneSource(bool home, ChanceSource source, bool pressed)
        {
            _zoneBy[home ? 0 : 1, (int)source]++;
            if (pressed) _zonePressedBy[home ? 0 : 1, (int)source]++;
        }

        public void AddShot(bool home, ChanceSource source, int xgPermille)
        {
            _shotsBy[home ? 0 : 1, (int)source]++;
            _xgBy[home ? 0 : 1, (int)source] += xgPermille;
        }

        /// <summary>Zone spells carried in whose carrier got the ball from <paramref name="source"/>.</summary>
        public int ZoneCarriesFrom(bool home, ChanceSource source) => _carryFrom[home ? 0 : 1, (int)source];

        /// <summary>Zone spells carried in, by where the carry began (see <see cref="CarryBands"/>).</summary>
        public int ZoneCarriesFromBand(bool home, int band) => _carryFromBand[home ? 0 : 1, band];

        public void AddZoneCarry(bool home, ChanceSource gotItFrom, int startDm)
        {
            _carryFrom[home ? 0 : 1, (int)gotItFrom]++;
            _carryFromBand[home ? 0 : 1, startDm < 200 ? 0 : startDm < 250 ? 1 : 2]++;
        }

        /// <summary>Shots from open-play spells that never touched the box, their goals and location xG (permille).</summary>
        public int OutsideShots(bool home) => _outsideShots[home ? 0 : 1];
        public int OutsideGoals(bool home) => _outsideGoals[home ? 0 : 1];
        public int OutsideXgPermille(bool home) => _outsideXg[home ? 0 : 1];

        /// <summary>Goals off a set-piece taker's own shot (a penalty, a direct free kick).</summary>
        public int RestartGoals(bool home) => _restartGoals[home ? 0 : 1];

        public void AddOutsideShot(bool home, int xgPermille)
        {
            _outsideShots[home ? 0 : 1]++;
            _outsideXg[home ? 0 : 1] += xgPermille;
        }

        public void AddOutsideGoal(bool home) => _outsideGoals[home ? 0 : 1]++;

        public void AddRestartGoal(bool home) => _restartGoals[home ? 0 : 1]++;

        public void AddGoal(bool home, ChanceSource source) => _goalsBy[home ? 0 : 1, (int)source]++;

        public void Add(bool home, bool box, bool zone, bool unpressed)
        {
            int side = home ? 0 : 1;
            if (box) _box[side]++;
            if (!zone) return;
            _zone[side]++;
            if (unpressed) _unpressed[side]++;
        }
    }
}
