using System;
using System.Collections.Generic;

namespace Fts.Services.Messaging
{
    /// <summary>
    /// Minimal main-thread message bus. Handlers run synchronously in
    /// subscription order. Publishing iterates a snapshot, so handlers may
    /// safely subscribe/unsubscribe during a publish.
    /// </summary>
    public sealed class MessageBroker : IMessageBroker
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        public void Publish<TMessage>(TMessage message)
        {
            if (!_handlers.TryGetValue(typeof(TMessage), out var list) || list.Count == 0)
                return;

            var snapshot = list.ToArray();
            foreach (var handler in snapshot)
                ((Action<TMessage>)handler).Invoke(message);
        }

        public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (!_handlers.TryGetValue(typeof(TMessage), out var list))
            {
                list = new List<Delegate>();
                _handlers.Add(typeof(TMessage), list);
            }

            list.Add(handler);
            return new Subscription(this, typeof(TMessage), handler);
        }

        private void Unsubscribe(Type messageType, Delegate handler)
        {
            if (_handlers.TryGetValue(messageType, out var list))
                list.Remove(handler);
        }

        private sealed class Subscription : IDisposable
        {
            private MessageBroker _broker;
            private readonly Type _messageType;
            private readonly Delegate _handler;

            public Subscription(MessageBroker broker, Type messageType, Delegate handler)
            {
                _broker = broker;
                _messageType = messageType;
                _handler = handler;
            }

            public void Dispose()
            {
                _broker?.Unsubscribe(_messageType, _handler);
                _broker = null;
            }
        }
    }
}
