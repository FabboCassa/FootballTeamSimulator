using System.Collections.Concurrent;

namespace Fts.LoadTest;

/// <summary>
/// The "no lost bids" audit for the auction spike (Phase 9.6). Every bid the server ACCEPTED (200) is
/// recorded here with the amount we sent; after the spike each coach re-reads their lots and we compare.
///
/// The invariant: a lot's final high bid must be at least the highest amount the server ever accepted for
/// it. If it is lower, an accepted bid was overwritten by a concurrent one — which is exactly the failure
/// mode the 8.5/9.2b auctions are exposed to, since a ranked lot carries its leader on the row with no
/// optimistic-concurrency token (a documented v1 simplification). This is the check that would justify
/// adding one.
///
/// <see cref="Mismatches"/> is the softer signal: a 200 whose returned lot does not show OUR amount as the
/// high bid means the write and the read disagreed inside a single request.
/// </summary>
public sealed class BidLedger
{
    private readonly ConcurrentDictionary<string, long> _accepted = new();
    private readonly ConcurrentDictionary<string, long> _final = new();
    private int _acceptedBids;
    private int _mismatches;

    public void RecordAccepted(string lotId, long amount, long reportedHighBid)
    {
        Interlocked.Increment(ref _acceptedBids);
        if (reportedHighBid != amount) Interlocked.Increment(ref _mismatches);
        _accepted.AddOrUpdate(lotId, amount, (_, current) => Math.Max(current, amount));
    }

    /// <summary>The lot as the server reports it after the spike (several coaches see the same lot, so keep
    /// the highest reading).</summary>
    public void RecordFinal(string lotId, long highBid) =>
        _final.AddOrUpdate(lotId, highBid, (_, current) => Math.Max(current, highBid));

    public BidAudit Audit()
    {
        int verified = 0, missing = 0, lost = 0;
        long worstShortfall = 0;

        foreach (var pair in _accepted)
        {
            if (!_final.TryGetValue(pair.Key, out long finalHigh))
            {
                // The lot is no longer listed (settled or its window shut) - nothing to compare against.
                missing++;
                continue;
            }

            verified++;
            if (finalHigh < pair.Value)
            {
                lost++;
                long shortfall = pair.Value - finalHigh;
                if (shortfall > worstShortfall) worstShortfall = shortfall;
            }
        }

        return new BidAudit(
            Volatile.Read(ref _acceptedBids), _accepted.Count, verified, missing, lost,
            Volatile.Read(ref _mismatches), worstShortfall);
    }
}

public sealed record BidAudit(
    int AcceptedBids,
    int LotsBidOn,
    int LotsVerified,
    int LotsUnverifiable,
    int LostBids,
    int Mismatches,
    long WorstShortfall)
{
    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine($"-- bid audit: {AcceptedBids} accepted bids over {LotsBidOn} lots "
                          + $"({LotsVerified} re-read, {LotsUnverifiable} no longer listed)");
        Console.WriteLine($"   lost bids {LostBids}  (worst shortfall {WorstShortfall:N0})  "
                          + $"write/read mismatches {Mismatches}");
    }
}
