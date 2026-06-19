using System;
using System.Collections.Generic;
using Fts.Services.Messaging;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Fts.Services.Navigation
{
    /// <summary>
    /// Stack-based navigation over UI Toolkit screens (ARCHITECTURE.md §5.3).
    /// Each pushed screen gets its own child DI scope (Screen scope), created
    /// from the currently active parent scope (App or Game) and disposed on pop.
    /// </summary>
    public sealed class ScreenNavigator
    {
        private readonly IMessageBroker _broker;
        private readonly List<Entry> _stack = new List<Entry>();
        private VisualElement _root;
        private LifetimeScope _activeScope;

        public ScreenNavigator(IMessageBroker broker)
        {
            _broker = broker;
        }

        public int StackDepth => _stack.Count;

        /// <summary>Container element the screens are attached to (set once at startup).</summary>
        public void SetRoot(VisualElement root) => _root = root;

        /// <summary>Parent scope for new Screen scopes: App scope, or Game scope while a career is active.</summary>
        public void SetActiveScope(LifetimeScope scope) => _activeScope = scope;

        public void Push<TPresenter>() where TPresenter : class, IScreenPresenter
        {
            if (_root == null) throw new InvalidOperationException("ScreenNavigator root not set.");
            if (_activeScope == null) throw new InvalidOperationException("ScreenNavigator active scope not set.");

            var scope = _activeScope.CreateChild(builder => builder.Register<TPresenter>(Lifetime.Scoped));
            scope.name = $"ScreenScope-{typeof(TPresenter).Name}";
            var presenter = scope.Container.Resolve<TPresenter>();

            if (_stack.Count > 0)
                _stack[_stack.Count - 1].Presenter.View.style.display = DisplayStyle.None;

            _stack.Add(new Entry(scope, presenter));
            _root.Add(presenter.View);
            presenter.Enter();
            PublishChanged();
        }

        /// <summary>Pops the top screen. The root screen is never popped. Returns true if a screen was popped.</summary>
        public bool Pop()
        {
            if (_stack.Count <= 1)
                return false;

            var top = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);

            top.Presenter.Exit();
            _root.Remove(top.Presenter.View);
            top.Scope.Dispose();

            var revealed = _stack[_stack.Count - 1].Presenter;
            revealed.View.style.display = DisplayStyle.Flex;
            revealed.Reveal();
            PublishChanged();
            return true;
        }

        /// <summary>Pops everything above the root screen.</summary>
        public void PopToRoot()
        {
            while (Pop()) { }
        }

        /// <summary>
        /// Hardware/Esc back: lets the top screen intercept first, otherwise pops.
        /// Returns true if anything happened.
        /// </summary>
        public bool Back()
        {
            if (_stack.Count == 0)
                return false;

            if (_stack[_stack.Count - 1].Presenter.HandleBack())
                return true;

            return Pop();
        }

        private void PublishChanged()
        {
            if (_stack.Count == 0)
                return;
            var top = _stack[_stack.Count - 1];
            _broker.Publish(new ScreenChangedMessage(top.Presenter.GetType().Name, _stack.Count));
        }

        private readonly struct Entry
        {
            public readonly LifetimeScope Scope;
            public readonly IScreenPresenter Presenter;

            public Entry(LifetimeScope scope, IScreenPresenter presenter)
            {
                Scope = scope;
                Presenter = presenter;
            }
        }
    }
}
