using System.Collections.Generic;
using System.Globalization;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Database bench screen (task 11.3, dev only — reached from the main menu when
    /// <see cref="DevFlags.WorldBench"/> is on).
    ///
    /// Task 11.1 shipped its measurement bench as a DESKTOP harness scenario, and closed leaving one
    /// thing open: the shipped default database size is Medium by judgement, because nobody has ever
    /// measured Small / Medium / Large on the targets that actually decide it — a WebGL build and a
    /// mid-range Android phone. A desktop tool cannot answer that. This can: it runs the same
    /// generation, the same Newtonsoft serialization and the same gzip the save writes, on whatever
    /// device the build is running on, and prints generation time, managed heap, save size and the
    /// cost of the new search index.
    ///
    /// Every result also goes to the player log, so on a phone you can pull the numbers with logcat
    /// instead of copying them off a screen.
    /// </summary>
    public sealed class WorldBenchScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly CareerFactory _factory;
        private readonly WorldBenchView _view;
        private readonly List<string> _lines = new List<string>();

        private bool _busy;

        public VisualElement View => _view.Root;

        public WorldBenchScreenPresenter(
            ScreenNavigator navigator,
            ILocalizationService loc,
            CareerFactory factory)
        {
            _navigator = navigator;
            _loc = loc;
            _factory = factory;
            _view = new WorldBenchView(loc.Tr);
        }

        public void Enter()
        {
            _view.PresetClicked += OnPreset;
            _view.ClearClicked += OnClear;
            _view.BackClicked += OnBack;
            Refresh();
        }

        public void Exit()
        {
            _view.PresetClicked -= OnPreset;
            _view.ClearClicked -= OnClear;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => Refresh();

        private void OnBack() => _navigator.Pop();

        private void OnClear()
        {
            _lines.Clear();
            Refresh();
        }

        /// <summary>
        /// Runs one preset. The generation blocks the frame — on a phone a Large world is seconds —
        /// so the screen paints "measuring…" first and starts the work on the next frame, otherwise
        /// the player taps a button and stares at a frozen screen with no explanation.
        /// </summary>
        private void OnPreset(int preset)
        {
            if (_busy)
                return;

            DatabaseSize size = preset == 0 ? DatabaseSize.Small
                : preset == 2 ? DatabaseSize.Large : DatabaseSize.Medium;

            _busy = true;
            _view.SetBusy(true);
            _view.SetHeader(_loc.Tr("bench.running", SizeName(size)));

            _view.Root.schedule.Execute(() =>
            {
                WorldBenchResult result = WorldBench.Run(_factory, size);
                string line = Format(result);

                _lines.Insert(0, line);
                Debug.Log("[world-bench] " + line);

                _busy = false;
                _view.SetBusy(false);
                Refresh();
            }).ExecuteLater(50);
        }

        private void Refresh()
        {
            _view.SetHeader(_loc.Tr("bench.title"));
            _view.SetLines(_lines);
        }

        private string Format(WorldBenchResult r)
        {
            return _loc.Tr("bench.result",
                SizeName(r.Size),
                r.Players.ToString("N0", CultureInfo.InvariantCulture),
                r.Clubs.ToString("N0", CultureInfo.InvariantCulture),
                r.GenerationMs.ToString("F0", CultureInfo.InvariantCulture),
                (r.HeapBytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture),
                (r.GzipBytes / 1024.0).ToString("F0", CultureInfo.InvariantCulture),
                r.IndexMs.ToString("F0", CultureInfo.InvariantCulture),
                r.SearchMs.ToString("F1", CultureInfo.InvariantCulture));
        }

        /// <summary>Reuses the career-setup wording so the bench names the presets exactly as the player picks them.</summary>
        private string SizeName(DatabaseSize size)
            => _loc.Tr("career_setup.db." + size.ToString().ToLowerInvariant());
    }
}
