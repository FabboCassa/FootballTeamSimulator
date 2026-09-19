using Fts.Infrastructure.Persistence.Entities;
using Sim.Core.Config;
using Sim.Core.Market;
using EntClub = Fts.Infrastructure.Persistence.Entities.Club;
using EntPlayer = Fts.Infrastructure.Persistence.Entities.Player;
using SimClub = Sim.Core.Domain.Club;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// Stature-based income AND wages for private-league clubs (task: stature-based income for private
/// leagues, R12 — spec docs/specs/realistic-club-economy.md:109-112 requires both on every resolved
/// round). Income books the SAME weekly gate/sponsor calculators Sim.Core uses for a career/ranked club
/// (<see cref="FinanceModel.GateReceipts"/>, <see cref="FinanceModel.WeeklySponsor"/>) so a club's
/// persistent <see cref="EntClub.Stature"/> (set once at <see cref="WorldFactory"/> generation) — not
/// just its draft-time budget, which every club starts equal (<see cref="LeagueService.DraftTransferBudget"/>)
/// — drives who gets richer as the season plays out. Wages debit the SAME per-player demand
/// <see cref="LeagueMarketEngine.DemandedWage(EntPlayer,EntClub)"/> already quotes at signing time (task:
/// wages set by the paying club, R7), summed over the current squad — a cost sink that otherwise never
/// existed once the one-off <see cref="LeagueMarketEngine.SigningCost"/> was paid. That signing cost is a
/// <see cref="EntClub.TransferBudget"/> expense paid once at acquisition (see <c>LeagueMarketService</c>);
/// this round wage bill only ever touches <see cref="EntClub.Balance"/>, a separate ledger the market
/// never spends from — so the two never double-count the same money. Deliberately a plain pure-ish helper
/// (mutates the given entities' <see cref="EntClub.Balance"/> in place, no I/O), so
/// <see cref="LeagueSeasonService"/> can call it once per resolved round and save everything together.
/// </summary>
public static class PrivateLeagueFinance
{
    /// <summary>A private-league world has no nation concept (<see cref="WorldFactory"/> always builds a
    /// single Division-1 league per world) — the same neutral convention <see cref="LeagueMarketEngine"/>
    /// uses for its own wage structure, held constant so it cancels out of every club's multiplier and
    /// only the club's own <see cref="EntClub.Stature"/> differentiates one club's revenue from another.</summary>
    private const int SingleDivisionLeagueLevel = 1;
    private const int NeutralEconomicReputation = 100;

    /// <summary>
    /// Books one round's income AND wages onto every club in <paramref name="clubsByGuid"/>: a weekly
    /// sponsor cheque for every club, plus gate receipts for whichever club played at home in
    /// <paramref name="roundFixtures"/>, minus its whole squad's demanded wage bill (via
    /// <paramref name="marketEngine"/>, the same instance <see cref="LeagueSeasonService"/> already built
    /// for this round's market window). A private-league club has no persisted
    /// <see cref="Sim.Core.Domain.Facilities"/>, so every club is costed at the tier-1 (default) stadium —
    /// only <see cref="EntClub.Stature"/> varies income between clubs.
    /// </summary>
    public static void AccrueRoundRevenue(
        IReadOnlyDictionary<Guid, EntClub> clubsByGuid,
        IReadOnlyList<LeagueFixture> roundFixtures,
        LeagueMarketEngine marketEngine,
        BalanceConfig cfg)
    {
        var homeClubIds = new HashSet<Guid>(roundFixtures.Select(f => f.HomeClubId));

        foreach ((Guid guid, EntClub club) in clubsByGuid)
        {
            var simClub = new SimClub { Stature = club.Stature };

            long income = FinanceModel.WeeklySponsor(
                simClub, SingleDivisionLeagueLevel, NeutralEconomicReputation, cfg);
            if (homeClubIds.Contains(guid))
                income += FinanceModel.GateReceipts(
                    simClub, SingleDivisionLeagueLevel, NeutralEconomicReputation, cfg);

            long wageBill = club.Players.Sum(p => marketEngine.DemandedWage(p, club));

            club.Balance += income - wageBill;
            // Board covers any shortfall (bankruptcy impossible) — mirrors Sim.Core's own
            // FinanceProgressor.AccrueWeek, which floors the same way at cfg.Finance.MinBalance.
            if (club.Balance < cfg.Finance.MinBalance)
                club.Balance = cfg.Finance.MinBalance;
        }
    }
}
