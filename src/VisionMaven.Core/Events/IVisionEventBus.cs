namespace VisionMaven.Core.Events;

/// <summary>
/// 进程内事件总线（Core 零依赖实现）。
/// Modules 既可订阅此总线，也可由 App 桥接到 Prism 的 <c>IEventAggregator</c>。
/// </summary>
public interface IVisionEventBus
{
    void Publish<TEvent>(TEvent payload)
        where TEvent : class;

    Task PublishAsync<TEvent>(TEvent payload)
        where TEvent : class;

    /// <summary>订阅；释放返回值即取消订阅。</summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : class;
}
