using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Persistence;
using Fts.Views;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Market;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Club screen (task 5.5): shows the club's cash balance and this season's income/expense
    /// breakdown, and lets the user upgrade the four facilities (stadium → gate income, training →
    /// development speed, scouting → scout level/slots, academy → youth quality). An upgrade is
    /// paid from the operating balance; the scouting upgrade also rebuilds the scout department
    /// from the new tier (so the Scouting screen's capacity/speed update). Lives in the Game scope.
    /// </summary>
    public sealed class ClubScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly ILocalizationService _loc;
        private readonly BalanceConfig _config = new BalanceConfig();
        private readonly ClubView _view;

        private Club _club;

        public VisualElement View => _view.Root;

        public ClubScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ISaveRepository saveRepository,
            ILocalizationService loc)
        {
            _navigator = navigator;
            _career = career;
            _saveRepository = saveRepository;
            _loc = loc;
            _view = new ClubView(loc.Tr);
        }

        public void Enter()
        {
            _view.UpgradeClicked += OnUpgrade;
            _view.BackClicked += OnBack;

            _club = _career.GetUserClub();
            _view.SetHeader(_loc.Tr("club.header", _club.Name));
            _view.SetStatus(string.Empty);
            Refresh();
        }

        public void Exit()
        {
            _view.UpgradeClicked -= OnUpgrade;
            _view.BackClicked -= OnBack;
        }

        private void OnBack() => _navigator.Pop();

        private void OnUpgrade(int index)
        {
            var type = (FacilityType)index;
            int tier = _club.Facilities.TierOf(type);
            if (tier >= _config.Finance.MaxFacilityTier)
            {
                _view.SetStatus(_loc.Tr("club.status.maxed"));
                return;
            }

            long cost = FacilityEffects.UpgradeCost(tier, _config.Finance);
            if (_club.Finances.Balance < cost)
            {
                _view.SetStatus(_loc.Tr("club.status.cant_afford", MoneyFormat.Short(cost)));
                return;
            }

            _club.Finances.Balance -= cost;
            int newTier = tier + 1;
            if (type == FacilityType.Scouting)
                FacilitySync.ApplyScoutingTier(_club, newTier, _config); // rebuilds the scout dept
            else
                _club.Facilities.SetTier(type, newTier);

            _saveRepository.Save(_career);
            _view.SetStatus(_loc.Tr("club.status.upgraded", FacilityName(type), newTier));
            Refresh();
        }

        private void Refresh()
        {
            Finances f = _club.Finances;
            _view.SetFinances(
                _loc.Tr("club.balance", MoneyFormat.Short(f.Balance)),
                _loc.Tr("club.income", MoneyFormat.Short(f.SeasonGateIncome),
                        MoneyFormat.Short(f.SeasonSponsorIncome), MoneyFormat.Short(f.SeasonPrizeIncome)),
                _loc.Tr("club.expense", MoneyFormat.Short(f.SeasonWageExpense)),
                _loc.Tr("club.net", Signed(f.SeasonNet)));

            var rows = new List<FacilityRowVm>
            {
                Row(FacilityType.Stadium),
                Row(FacilityType.Training),
                Row(FacilityType.Scouting),
                Row(FacilityType.Academy)
            };
            _view.SetFacilities(rows);
        }

        private FacilityRowVm Row(FacilityType type)
        {
            int tier = _club.Facilities.TierOf(type);
            bool maxed = tier >= _config.Finance.MaxFacilityTier;
            long cost = FacilityEffects.UpgradeCost(tier, _config.Finance);

            return new FacilityRowVm
            {
                Index = (int)type,
                Name = FacilityName(type),
                Tier = _loc.Tr("club.tier", tier, _config.Finance.MaxFacilityTier),
                Effect = EffectText(type, tier),
                ActionLabel = maxed ? _loc.Tr("club.max") : _loc.Tr("club.upgrade", MoneyFormat.Short(cost)),
                CanUpgrade = !maxed && _club.Finances.Balance >= cost
            };
        }

        private string EffectText(FacilityType type, int tier)
        {
            switch (type)
            {
                case FacilityType.Stadium:
                    int capacity = FacilityEffects.StadiumCapacity(tier, _config.Finance);
                    return _loc.Tr("club.effect.stadium", capacity / 1000);
                case FacilityType.Training:
                    return _loc.Tr("club.effect.training", DevelopmentUpliftPercent(tier));
                case FacilityType.Scouting:
                    return _loc.Tr("club.effect.scouting", FacilityEffects.ScoutLevel(tier, _config), tier);
                default:
                    return _loc.Tr("club.effect.academy", FacilityEffects.AcademyRating(tier, _config.Finance));
            }
        }

        /// <summary>Development-speed uplift over the neutral baseline, in percent (tier 1 → +0%).</summary>
        private int DevelopmentUpliftPercent(int tier)
        {
            DevelopmentBalance d = _config.Development;
            int level = FacilityEffects.TrainingFacilityLevel(tier, _config);
            int neutral = d.FacilityNeutralLevel > 0 ? d.FacilityNeutralLevel : 1;
            int factor = 1000 + (level - neutral) * d.FacilityGrowthSwingPermille / neutral; // mirrors DevelopmentModel
            return (factor - 1000) / 10;
        }

        private string FacilityName(FacilityType type) =>
            _loc.Tr("club.facility." + type.ToString().ToLowerInvariant());

        private string Signed(long amount) =>
            amount >= 0 ? MoneyFormat.Short(amount) : "-" + MoneyFormat.Short(-amount);
    }
}
