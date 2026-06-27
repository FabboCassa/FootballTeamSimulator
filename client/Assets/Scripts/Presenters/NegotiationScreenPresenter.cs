using System;
using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Market;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Offer/counteroffer dialog (task 5.3) driving the Sim.Core <see cref="NegotiationModel"/>
    /// step API as a HUMAN party. BUY: the user raises his offer; the AI seller accepts, counters,
    /// or — on a lowball — loses patience and breaks off. SELL: the user names an ask; the AI buyer
    /// accepts, bids back, or walks away. Each side has limited patience (MaxNegotiationRounds
    /// offers); a too-low offer ends the talks immediately. On agreement the deal is executed
    /// through <see cref="LocalMarketService"/> (budgets move, squads change, the news logs it).
    /// </summary>
    public sealed class NegotiationScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly LocalMarketService _market;
        private readonly MarketTarget _target;
        private readonly ILocalizationService _loc;
        private readonly OverlayHost _overlay;
        private readonly NegotiationView _view;
        private readonly TransferBalance _cfg = new BalanceConfig().Transfer;

        private Player _player;
        private Club _counterparty;   // seller (buy) or buyer (sell)
        private Club _userClub;
        private bool _selling;

        private long _value;
        private long _amount;         // the adjustable number: user's offer (buy) / user's ask (sell)
        private int _round;
        private bool _dealDone;
        private bool _brokenOff;

        // Buy state.
        private long _sellerAsk;
        private long _budget;

        // Sell state.
        private long _buyerMax;
        private long _buyerOffer;

        private bool Over => _dealDone || _brokenOff;

        public VisualElement View => _view.Root;

        public NegotiationScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            LocalMarketService market,
            MarketTarget target,
            ILocalizationService loc,
            OverlayHost overlay)
        {
            _navigator = navigator;
            _career = career;
            _market = market;
            _target = target;
            _loc = loc;
            _overlay = overlay;
            _view = new NegotiationView(loc.Tr);
        }

        public void Enter()
        {
            _view.DecrementClicked += OnDecrement;
            _view.IncrementClicked += OnIncrement;
            _view.AmountTyped += OnAmountTyped;
            _view.PrimaryClicked += OnPrimary;
            _view.SecondaryClicked += OnSecondary;
            _view.BackClicked += OnBack;

            _selling = _target.Mode == NegotiationMode.Sell;
            _player = _career.FindPlayer(_target.PlayerId);
            _counterparty = _career.FindClub(_target.CounterpartyClubId);
            _userClub = _career.GetUserClub();

            if (_player == null || _counterparty == null || _userClub == null)
            {
                _brokenOff = true;
                _view.SetTitle(_loc.Tr("negotiation.title"));
                _view.SetInfo(Array.Empty<string>());
                _view.SetStatus(_loc.Tr("negotiation.status.error"));
                _view.SetAmountCaption(_loc.Tr("negotiation.your_amount"));
                _view.SetPatience(string.Empty);
                _view.SetPrimary(string.Empty, false);
                _view.SetSecondary(string.Empty, false);
                _view.SetAdjustEnabled(false);
                return;
            }

            Setup();
            Render();
        }

        public void Exit()
        {
            _view.DecrementClicked -= OnDecrement;
            _view.IncrementClicked -= OnIncrement;
            _view.AmountTyped -= OnAmountTyped;
            _view.PrimaryClicked -= OnPrimary;
            _view.SecondaryClicked -= OnSecondary;
            _view.BackClicked -= OnBack;
        }

        private void Setup()
        {
            _round = 0;

            if (_selling)
            {
                _value = _market.ValueOf(_player, _userClub.Id);
                PlayerImportance importance = _market.UserImportance(_player);

                if (!SquadAnalysis.Analyze(_userClub, _cfg).CanSell(_player, _cfg))
                {
                    _brokenOff = true;
                    _view.SetStatus(_loc.Tr("negotiation.status.cannot_sell"));
                }

                PersonalityProfile buyerProfile = ClubPersonalities.Profile(ClubPersonalities.For(_counterparty.Id, _career.Seed));
                PersonalityProfile userProfile = ClubPersonalities.Profile(ClubPersonalities.For(_userClub.Id, _career.Seed));
                _buyerMax = NegotiationModel.BuyerMaxPrice(_value, buyerProfile, _counterparty.TransferBudget, _cfg);
                _amount = NegotiationModel.AskingPrice(_value, importance, userProfile, _cfg);
                _buyerOffer = NegotiationModel.OpeningOffer(_amount, _buyerMax, _cfg);

                if (!Over)
                    _view.SetStatus(_loc.Tr("negotiation.status.intro_sell", _counterparty.Name));
            }
            else
            {
                _value = _market.ValueOf(_player, _counterparty.Id);
                PlayerImportance importance = SquadAnalysis.Analyze(_counterparty, _cfg).ImportanceOf(_player);

                if (!SquadAnalysis.Analyze(_counterparty, _cfg).CanSell(_player, _cfg))
                {
                    _brokenOff = true;
                    _view.SetStatus(_loc.Tr("negotiation.status.not_for_sale", _counterparty.Name));
                }

                PersonalityProfile sellerProfile = ClubPersonalities.Profile(ClubPersonalities.For(_counterparty.Id, _career.Seed));
                _budget = _userClub.TransferBudget;
                _sellerAsk = NegotiationModel.AskingPrice(_value, importance, sellerProfile, _cfg);
                _amount = NegotiationModel.OpeningOffer(_sellerAsk, _budget, _cfg);

                if (!Over)
                    _view.SetStatus(_loc.Tr("negotiation.status.intro_buy", _player.FullName));
            }
        }

        private void OnDecrement()
        {
            if (Over) return;
            _amount -= Step();
            if (_amount < Step()) _amount = Step();
            SyncAmount();
        }

        private void OnIncrement()
        {
            if (Over) return;
            _amount += Step();
            if (!_selling && _amount > _budget) _amount = _budget; // a buyer can't exceed his budget
            SyncAmount();
        }

        private void OnAmountTyped(long value)
        {
            if (Over) return;
            _amount = value;
            // Don't rewrite the field while the user types — just refresh the hint + buttons.
            _view.SetAmountFormatted(MoneyFormat.Short(_amount));
            UpdateActions();
        }

        private void OnPrimary()
        {
            if (Over) return;
            if (_selling) CounterAsSeller();
            else OfferAsBuyer();
        }

        private void OnSecondary()
        {
            if (Over) return;
            if (_selling) TrySell(_buyerOffer);
            else TryBuy(_sellerAsk);
        }

        // ── Buying (user offers, AI seller responds) ────────────────────────────────────

        private void OfferAsBuyer()
        {
            _round++;
            SellerResponse r = NegotiationModel.EvaluateOffer(_sellerAsk, _amount, MinSaleBuy(), _cfg);

            switch (r.Decision)
            {
                case SellerDecision.Accept:
                    TryBuy(r.CounterAsk);
                    return;
                case SellerDecision.Reject:
                    // Lowball: the seller loses patience and ends the talks.
                    _brokenOff = true;
                    _view.SetStatus(_loc.Tr("negotiation.status.broke_off", _counterparty.Name));
                    break;
                default: // Counter
                    _sellerAsk = r.CounterAsk;
                    if (_round >= _cfg.MaxNegotiationRounds)
                    {
                        _brokenOff = true;
                        _view.SetStatus(_loc.Tr("negotiation.status.ended", _counterparty.Name));
                    }
                    else
                    {
                        _view.SetStatus(_loc.Tr("negotiation.status.counter", MoneyFormat.Short(_sellerAsk)));
                    }
                    break;
            }
            Render();
        }

        private void TryBuy(long fee)
        {
            if (fee > _budget)
            {
                _view.SetStatus(_loc.Tr("negotiation.status.error"));
                Render();
                return;
            }

            if (_market.ExecuteUserPurchase(_player, _counterparty, fee))
            {
                _dealDone = true;
                _view.SetStatus(_loc.Tr("negotiation.status.bought", _player.FullName, MoneyFormat.Short(fee)));
                Dialogs.Toast(_overlay, _loc, "market.toast.bought", _player.FullName);
            }
            else
            {
                _view.SetStatus(_loc.Tr("negotiation.status.error"));
            }
            Render();
        }

        // ── Selling (user names ask, AI buyer responds) ─────────────────────────────────

        private void CounterAsSeller()
        {
            _round++;
            BuyerResponse b = NegotiationModel.RespondToCounter(_buyerOffer, _amount, _buyerMax, _cfg);

            switch (b.Decision)
            {
                case BuyerDecision.Accept:
                    TrySell(b.Offer);
                    return;
                case BuyerDecision.GiveUp:
                    _brokenOff = true;
                    _view.SetStatus(_loc.Tr("negotiation.status.gaveup", _counterparty.Name));
                    break;
                default: // Offer
                    _buyerOffer = b.Offer;
                    if (_round >= _cfg.MaxNegotiationRounds)
                    {
                        _brokenOff = true;
                        _view.SetStatus(_loc.Tr("negotiation.status.ended", _counterparty.Name));
                    }
                    else
                    {
                        _view.SetStatus(_loc.Tr("negotiation.status.buyer_raised", MoneyFormat.Short(_buyerOffer)));
                    }
                    break;
            }
            Render();
        }

        private void TrySell(long fee)
        {
            if (fee <= 0)
            {
                _view.SetStatus(_loc.Tr("negotiation.status.error"));
                Render();
                return;
            }

            if (_market.ExecuteUserSale(_player, _counterparty, fee))
            {
                _dealDone = true;
                _view.SetStatus(_loc.Tr("negotiation.status.sold", _player.FullName, MoneyFormat.Short(fee)));
                Dialogs.Toast(_overlay, _loc, "market.toast.sold", _player.FullName);
            }
            else
            {
                _view.SetStatus(_loc.Tr("negotiation.status.error"));
            }
            Render();
        }

        private long MinSaleBuy()
        {
            PlayerImportance importance = SquadAnalysis.Analyze(_counterparty, _cfg).ImportanceOf(_player);
            return NegotiationModel.MinSalePrice(_value, importance, _cfg);
        }

        private void OnBack() => _navigator.Pop();

        // ── Rendering ───────────────────────────────────────────────────────────────────

        private void Render()
        {
            _view.SetTitle(_loc.Tr(_selling ? "negotiation.title_sell" : "negotiation.title_buy"));
            _view.SetInfo(BuildInfo());
            _view.SetAmountCaption(_loc.Tr(_selling ? "negotiation.your_ask" : "negotiation.your_offer"));
            SyncAmount();
            _view.SetPatience(Over ? string.Empty : _loc.Tr("negotiation.offers_left", _cfg.MaxNegotiationRounds - _round));
            _view.SetAdjustEnabled(!Over);
        }

        private List<string> BuildInfo()
        {
            var lines = new List<string>
            {
                _loc.Tr("negotiation.player", _player.FullName, RoleName(_player.Role), _player.Age, PlayerRating.Overall(_player)),
                _loc.Tr(_selling ? "negotiation.to" : "negotiation.from", _counterparty.Name),
                _loc.Tr("negotiation.value", MoneyFormat.Short(_value))
            };
            if (_selling)
            {
                lines.Add(_loc.Tr("negotiation.buyer_offer", MoneyFormat.Short(_buyerOffer)));
            }
            else
            {
                lines.Add(_loc.Tr("negotiation.ask", MoneyFormat.Short(_sellerAsk)));
                lines.Add(_loc.Tr("negotiation.budget", MoneyFormat.Short(_budget)));
            }
            return lines;
        }

        /// <summary>Pushes the current amount to the field + formatted hint and refreshes the action buttons.</summary>
        private void SyncAmount()
        {
            _view.SetAmountRaw(_amount);
            _view.SetAmountFormatted(MoneyFormat.Short(_amount));
            UpdateActions();
        }

        private void UpdateActions()
        {
            bool canHaggle = !Over && _round < _cfg.MaxNegotiationRounds;
            if (_selling)
            {
                _view.SetPrimary(_loc.Tr("negotiation.counter"), canHaggle && _amount > 0);
                _view.SetSecondary(_loc.Tr("negotiation.accept_offer", MoneyFormat.Short(_buyerOffer)), !Over && _buyerOffer > 0);
            }
            else
            {
                _view.SetPrimary(_loc.Tr("negotiation.make_offer"), canHaggle && _amount > 0 && _amount <= _budget);
                _view.SetSecondary(_loc.Tr("negotiation.accept_ask", MoneyFormat.Short(_sellerAsk)), !Over && _sellerAsk <= _budget);
            }
        }

        /// <summary>A proportional adjustment step (~5% of the current amount, rounded, minimum €25k).</summary>
        private long Step()
        {
            long step = _amount / 20 / 25_000 * 25_000;
            return step < 25_000 ? 25_000 : step;
        }

        private string RoleName(PositionRole role) =>
            _loc.Tr("role." + role.ToString().ToLowerInvariant());
    }
}
