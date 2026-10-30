using System;
using System.Collections.Generic;

namespace Sim.Core.Match.Analysis
{
    /// <summary>One outfielder's reversals in one match (see <see cref="OscillationAnalyzer"/>).</summary>
    public readonly struct PlayerOscillation
    {
        public bool Home { get; }
        public int Slot { get; }
        public int Reversals { get; }
        public double Minutes { get; }

        public double PerMinute => Minutes <= 0 ? 0 : Reversals / Minutes;

        public PlayerOscillation(bool home, int slot, int reversals, double minutes)
        {
            Home = home;
            Slot = slot;
            Reversals = reversals;
            Minutes = minutes;
        }
    }

    /// <summary>
    /// The oscillation readings of one match or of many pooled (free-ball spec R2): every man's
    /// reversals per player-minute, and the pool over all of them. A man is one entry per match.
    /// </summary>
    public sealed class OscillationMetrics
    {
        /// <summary>
        /// Players with fewer open-play minutes than this are left out of the per-player spread:
        /// a late substitute's one reversal in three minutes would read as an oscillator.
        /// </summary>
        public const double MinSpreadMinutes = 10;

        private readonly List<PlayerOscillation> _players = new List<PlayerOscillation>();

        public int Matches { get; set; } = 1;
        public IReadOnlyList<PlayerOscillation> Players => _players;
        public long Reversals { get; private set; }
        public double PlayerMinutes { get; private set; }

        /// <summary>Pooled over every man: the reading the R1 heading-reversal band is set on.</summary>
        public double ReversalsPerPlayerMinute => PlayerMinutes <= 0 ? 0 : Reversals / PlayerMinutes;

        public void Add(PlayerOscillation p)
        {
            _players.Add(p);
            Reversals += p.Reversals;
            PlayerMinutes += p.Minutes;
        }

        /// <summary>
        /// The per-player rate at quantile <paramref name="q"/> (0-1, nearest rank) over the men with
        /// at least <see cref="MinSpreadMinutes"/>; 0 when there is none.
        /// </summary>
        public double PlayerQuantile(double q)
        {
            List<double> rates = SpreadRates();
            if (rates.Count == 0) return 0;
            int rank = (int)Math.Ceiling(q * rates.Count) - 1;
            return rates[Math.Max(0, Math.Min(rates.Count - 1, rank))];
        }

        /// <summary>Share of the men with at least <see cref="MinSpreadMinutes"/> whose rate is above <paramref name="perMinute"/>.</summary>
        public double PlayersAbovePercent(double perMinute)
        {
            List<double> rates = SpreadRates();
            if (rates.Count == 0) return 0;
            int above = 0;
            foreach (double r in rates)
                if (r > perMinute) above++;
            return 100.0 * above / rates.Count;
        }

        /// <summary>Men in the per-player spread.</summary>
        public int SpreadPlayers => SpreadRates().Count;

        public OscillationMetrics Plus(OscillationMetrics o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            var sum = new OscillationMetrics { Matches = Matches + o.Matches };
            foreach (PlayerOscillation p in _players) sum.Add(p);
            foreach (PlayerOscillation p in o._players) sum.Add(p);
            return sum;
        }

        private List<double> SpreadRates()
        {
            var rates = new List<double>(_players.Count);
            foreach (PlayerOscillation p in _players)
                if (p.Minutes >= MinSpreadMinutes) rates.Add(p.PerMinute);
            rates.Sort();
            return rates;
        }
    }
}
