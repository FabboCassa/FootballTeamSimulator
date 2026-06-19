using System;

namespace Fts.Services.Messaging
{
    /// <summary>
    /// Lightweight typed pub/sub bus for cross-screen signals
    /// (e.g. DayAdvanced, TransferCompleted). See ARCHITECTURE.md §5.1.
    /// </summary>
    public interface IMessageBroker
    {
        void Publish<TMessage>(TMessage message);

        /// <summary>Subscribes to messages of type TMessage. Dispose the returned handle to unsubscribe.</summary>
        IDisposable Subscribe<TMessage>(Action<TMessage> handler);
    }
}
