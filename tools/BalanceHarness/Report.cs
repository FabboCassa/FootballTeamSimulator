using System.Globalization;

namespace Fts.BalanceHarness;

/// <summary>Formatting helpers. ASCII only on purpose: the output is read through Windows PowerShell 5.1,
/// which mangles anything else when a run is piped to a file.</summary>
internal static class Fmt
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>"206.2M EUR" / "625k EUR" / "25000 EUR" - a readable order of magnitude, not accounting.</summary>
    public static string Money(long v)
    {
        long a = Math.Abs(v);
        if (a >= 1_000_000) return (v / 1_000_000.0).ToString("0.##", Ci) + "M EUR";
        if (a >= 1_000) return (v / 1_000.0).ToString("0.#", Ci) + "k EUR";
        return v.ToString(Ci) + " EUR";
    }

    public static string Pct(double fraction) => (fraction * 100.0).ToString("0.0", Ci) + "%";

    public static string N(double v, int decimals = 2) =>
        decimals <= 0 ? v.ToString("0", Ci) : v.ToString("0." + new string('0', decimals), Ci);

    public static long Percentile(IReadOnlyList<long> sortedAscending, double p)
    {
        if (sortedAscending.Count == 0) return 0;
        double idx = (sortedAscending.Count - 1) * p;
        int lo = (int)Math.Floor(idx);
        int hi = (int)Math.Ceiling(idx);
        if (lo == hi) return sortedAscending[lo];
        double frac = idx - lo;
        return (long)Math.Round(sortedAscending[lo] + (sortedAscending[hi] - sortedAscending[lo]) * frac);
    }

    public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        if (sortedAscending.Count == 0) return 0;
        double idx = (sortedAscending.Count - 1) * p;
        int lo = (int)Math.Floor(idx);
        int hi = (int)Math.Ceiling(idx);
        if (lo == hi) return sortedAscending[lo];
        return sortedAscending[lo] + (sortedAscending[hi] - sortedAscending[lo]) * (idx - lo);
    }

    public static double Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double sum = 0;
        foreach (double v in values) sum += v;
        return sum / values.Count;
    }

    /// <summary>Spearman rank correlation - how well one ordering reproduces another. Ties are averaged,
    /// so a ladder that leaves many coaches on the same rating is not flattered.</summary>
    public static double RankCorrelation(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count != b.Count || a.Count < 2) return 0;
        double[] ra = Ranks(a);
        double[] rb = Ranks(b);

        double ma = Mean(ra), mb = Mean(rb);
        double num = 0, da = 0, db = 0;
        for (int i = 0; i < ra.Length; i++)
        {
            double xa = ra[i] - ma, xb = rb[i] - mb;
            num += xa * xb; da += xa * xa; db += xb * xb;
        }
        return da <= 0 || db <= 0 ? 0 : num / Math.Sqrt(da * db);
    }

    private static double[] Ranks(IReadOnlyList<double> values)
    {
        int n = values.Count;
        var idx = new int[n];
        for (int i = 0; i < n; i++) idx[i] = i;
        Array.Sort(idx, (x, y) => values[x].CompareTo(values[y]));

        var ranks = new double[n];
        int i2 = 0;
        while (i2 < n)
        {
            int j = i2;
            while (j + 1 < n && values[idx[j + 1]].Equals(values[idx[i2]])) j++;
            double shared = (i2 + j) / 2.0 + 1;
            for (int k = i2; k <= j; k++) ranks[idx[k]] = shared;
            i2 = j + 1;
        }
        return ranks;
    }
}

/// <summary>
/// PASS/FAIL lines in the shape the smoke scripts and the load test use, plus the process exit code.
/// A check is a claim about the balance that must hold whatever the exact calibration; anything whose
/// right value is a judgement call is printed as INFO instead, for the user to judge.
/// </summary>
internal sealed class CheckList
{
    private readonly List<(string Name, bool Ok, string Detail)> _checks = new();
    private readonly List<string> _info = new();

    public void Check(string name, bool ok, string detail) => _checks.Add((name, ok, detail));

    public void Info(string line) => _info.Add(line);

    public bool AllPassed
    {
        get
        {
            foreach (var c in _checks) if (!c.Ok) return false;
            return true;
        }
    }

    public int Failed
    {
        get
        {
            int n = 0;
            foreach (var c in _checks) if (!c.Ok) n++;
            return n;
        }
    }

    public int Count => _checks.Count;

    public void Print(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {title}: judgement calls (no pass/fail, for the user to weigh) ---");
        if (_info.Count == 0) Console.WriteLine("  (none)");
        foreach (string line in _info) Console.WriteLine("  " + line);

        Console.WriteLine();
        Console.WriteLine($"--- {title}: checks ---");
        foreach (var c in _checks)
            Console.WriteLine($"  [{(c.Ok ? "PASS" : "FAIL")}] {c.Name} - {c.Detail}");
        Console.WriteLine($"  {_checks.Count - Failed}/{_checks.Count} passed");
    }
}
