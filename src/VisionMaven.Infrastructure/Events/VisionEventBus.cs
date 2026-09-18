using System.Collections.Concurrent;
using VisionMaven.Core.Events;

namespace VisionMaven.Infrastructure.Events;

/// <summary>进程内事件总线：订阅者异常被隔离，不影响发布方。</summary>
public sealed class VisionEventBus : IVisionEventBus
{
    private readonly ConcurrentDictionary<Type, List<Subscription>> _subscriptions = new();
    private readonly object _sync = new();

    public void Publish<TEvent>(TEvent payload)
        where TEvent : class
    {
        foreach (var handler in Snapshot(typeof(TEvent)))
        {
            try
            {
                ((Action<TEvent>)handler).Invoke(payload);
            }
            catch
            {
                // 订阅者异常不得影响发布方，交由订阅者自行记录日志。
            }
        }
    }

    public Task PublishAsync<TEvent>(TEvent payload)
        where TEvent : class
    {
        var handlers = Snapshot(typeof(TEvent));
        if (handlers.Count == 0)
        {
            return Task.CompletedTask;
        }

        var tasks = new List<Task>(handlers.Count);
        foreach (var handler in handlers)
        {
            try
            {
                ((Action<TEvent>)handler).Invoke(payload);
            }
            catch
            {
                // 同上，逐订阅者隔离。
            }
        }

        return Task.WhenAll(tasks);
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var list = _subscriptions.GetOrAdd(typeof(TEvent), _ => new List<Subscription>());
        var subscription = new Subscription(handler);
        lock (_sync)
        {
            list.Add(subscription);
        }

        return new Unsubscriber(list, subscription, _sync);
    }

    private IReadOnlyList<Delegate> Snapshot(Type eventType)
    {
        if (!_subscriptions.TryGetValue(eventType, out var list))
        {
            return Array.Empty<Delegate>();
        }

        lock (_sync)
        {
            return list.Select(item => item.Handler).ToArray();
        }
    }

    private sealed record Subscription(Delegate Handler);

    private sealed class Unsubscriber : IDisposable
    {
        private readonly List<Subscription> _list;
        private readonly Subscription _subscription;
        private readonly object _sync;
        private bool _disposed;

        public Unsubscriber(List<Subscription> list, Subscription subscription, object sync)
        {
            _list = list;
            _subscription = subscription;
            _sync = sync;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (_sync)
            {
                _list.Remove(_subscription);
            }
        }
    }
}
