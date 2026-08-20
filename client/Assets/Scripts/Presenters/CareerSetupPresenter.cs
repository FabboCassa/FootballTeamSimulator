using System.Collections.Generic;
using System.Text;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Difficulty;
using Sim.Core.Domain;
using Sim.Core.Generation;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// New-career setup (task 11.1b): the player chooses the WORLD — which nation he plays in, how
    /// deep that nation's pyramid runs at full detail, and how much of the rest of the planet is
    /// loaded around it — then a difficulty, then a division, then a club.
    ///
    /// The division step exists because a three-tier nation is seventy-odd clubs: dumping them all
    /// into one list made the choice unreadable. Picking the division first also makes the thing the
    /// player is actually choosing — what level he starts at — an explicit decision instead of a
    /// prefix on a button.
    ///
    /// Only the playable nation is generated while the player is browsing
    /// (<see cref="CareerFactory.GeneratePreview"/>); the full world is built once, at the moment a
    /// club is picked. The clubs are identical either way — ids and names depend on
    /// (seed, nation, tier) and never on the database size — so what the player picked is exactly
    /// what he gets.
    ///
    /// The scope is fixed for the save (the Football Manager rule the user chose): the world is a
    /// function of seed + scope, so letting it change mid-career would silently renumber it.
    /// </summary>
    public sealed class CareerSetupPresenter : IScreenPresenter
    {
        private enum Mode
        {
            Leagues,
            Clubs,
            Nations
        }

        private readonly CareerFactory _factory;
        private readonly IGameSessionService _session;
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly CareerSetupView _view;
        private readonly List<NationProfile> _atlas = CareerFactory.Atlas();

        private ulong _seed;
        private World _preview;
        private int _nationIndex;
        private int _tiers = CareerFactory.DefaultTiers;
        private Mode _mode = Mode.Leagues;
        private DatabaseSize _size = DatabaseSize.Medium;
        private DifficultyLevel _difficulty = DifficultyLevel.Normal;

        public VisualElement View => _view.Root;

        public CareerSetupPresenter(
            CareerFactory factory,
            IGameSessionService session,
            ScreenNavigator navigator,
            ILocalizationService loc)
        {
            _factory = factory;
            _session = session;
            _navigator = navigator;
            _loc = loc;
            _view = new CareerSetupView(loc.Tr);
            _nationIndex = IndexOf(CareerFactory.DefaultNation);
        }

        public void Enter()
        {
            _view.ClubSelected += OnClubSelected;
            _view.RerollClicked += Reroll;
            _view.BackClicked += OnBack;
            _view.DifficultySelected += OnDifficultySelected;
            _view.NationPickerRequested += OnNationPickerRequested;
            _view.NationSelected += OnNationSelected;
            _view.TiersCycled += OnTiersCycled;
            _view.DatabaseSizeSelected += OnDatabaseSizeSelected;
            _view.LeagueSelected += OnLeagueSelected;
            _view.BackToLeaguesClicked += ShowLeagues;

            _view.SetDifficulty((int)_difficulty);
            _view.SetDatabaseSize((int)_size);
            Reroll();
        }

        public void Exit()
        {
            _view.ClubSelected -= OnClubSelected;
            _view.RerollClicked -= Reroll;
            _view.BackClicked -= OnBack;
            _view.DifficultySelected -= OnDifficultySelected;
            _view.NationPickerRequested -= OnNationPickerRequested;
            _view.NationSelected -= OnNationSelected;
            _view.TiersCycled -= OnTiersCycled;
            _view.DatabaseSizeSelected -= OnDatabaseSizeSelected;
            _view.LeagueSelected -= OnLeagueSelected;
            _view.BackToLeaguesClicked -= ShowLeagues;
        }

        private NationProfile Nation => _atlas[_nationIndex];

        private void OnDifficultySelected(int level)
        {
            _difficulty = (DifficultyLevel)level;
            _view.SetDifficulty(level);
        }

        private void OnDatabaseSizeSelected(int size)
        {
            // Nothing to regenerate: the database size changes what exists AROUND the player's
            // nation, never his own divisions or the clubs he is choosing from.
            _size = (DatabaseSize)size;
            _view.SetDatabaseSize(size);
        }

        private void OnNationPickerRequested()
        {
            _mode = Mode.Nations;

            var names = new List<string>(_atlas.Count);
            foreach (NationProfile profile in _atlas)
                names.Add(_loc.Tr("career_setup.nation_option", profile.Name, profile.Divisions.Count));

            _view.SetNations(names);
        }

        private void OnNationSelected(int index)
        {
            if (index < 0 || index >= _atlas.Count)
                return;

            _nationIndex = index;
            if (_tiers > Nation.Divisions.Count)
                _tiers = Nation.Divisions.Count;

            Refresh();
        }

        private void OnTiersCycled()
        {
            _tiers = _tiers >= Nation.Divisions.Count ? 1 : _tiers + 1;
            Refresh();
        }

        private void Reroll()
        {
            _seed = _factory.NewSeed();
            Refresh();
        }

        /// <summary>Regenerates the preview for the current (seed, nation, tiers) and redraws.</summary>
        private void Refresh()
        {
            _preview = _factory.GeneratePreview(_seed, BuildScope());

            _view.SetNation(_loc.Tr("career_setup.nation", Nation.Name));
            _view.SetTiers(_loc.Tr("career_setup.tiers", _tiers, Nation.Divisions.Count));

            var summary = new StringBuilder();
            foreach (League league in _preview.PlayableLeagues())
            {
                if (summary.Length > 0) summary.Append("  ·  ");
                summary.Append(league.Name);
            }

            _view.SetSummary(summary.ToString());
            ShowLeagues();
        }

        private void ShowLeagues()
        {
            List<League> leagues = _preview.PlayableLeagues();

            // One division to choose from is not a choice — go straight to its clubs, with no way
            // back up to a list of one.
            if (leagues.Count == 1)
            {
                ShowClubs(leagues[0], false);
                return;
            }

            _mode = Mode.Leagues;

            var rows = new List<KeyValuePair<int, string>>(leagues.Count);
            foreach (League league in leagues)
            {
                rows.Add(new KeyValuePair<int, string>(
                    league.Id,
                    _loc.Tr("career_setup.league_option", league.Name, league.Clubs.Count)));
            }

            _view.SetLeagues(rows);
        }

        private void OnLeagueSelected(int leagueId)
        {
            foreach (League league in _preview.PlayableLeagues())
            {
                if (league.Id != leagueId)
                    continue;

                ShowClubs(league, true);
                return;
            }
        }

        private void ShowClubs(League league, bool canGoBack)
        {
            _mode = Mode.Clubs;

            var clubs = new List<KeyValuePair<int, string>>(league.Clubs.Count);
            foreach (Club club in league.Clubs)
                clubs.Add(new KeyValuePair<int, string>(club.Id, club.Name));

            _view.SetClubs(league.Name, clubs, canGoBack);
        }

        private WorldScope BuildScope()
        {
            var scope = new WorldScope { Size = _size };
            scope.Playable.Add(new PlayableNation { Code = Nation.Code, PlayableTiers = _tiers });
            return scope;
        }

        private void OnClubSelected(int clubId)
        {
            if (_mode != Mode.Clubs)
                return;

            // Now — and only now — the full world is built at the chosen database size.
            World world = _factory.GenerateWorld(_seed, BuildScope());
            CareerState state = _factory.Create(_seed, world, clubId, _difficulty);

            // Pop the setup screen first; StartNewCareer then pushes the Hub.
            _navigator.Pop();
            _session.StartNewCareer(state);
        }

        private void OnBack() => _navigator.Pop();

        private int IndexOf(string code)
        {
            for (int i = 0; i < _atlas.Count; i++)
            {
                if (_atlas[i].Code == code)
                    return i;
            }

            return 0;
        }
    }
}
