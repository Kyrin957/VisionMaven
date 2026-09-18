using System.Collections.Concurrent;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Infrastructure.Logging;

/// <summary>日志 UI Sink：环形缓冲上限 5000 条，折叠时暂停刷新但仍入缓冲。</summary>
public sealed class VisionLogSink : IVisionLogSink
{
    private const int DefaultCapacity = 5000;

    private readonly ConcurrentQueue<VisionLogEntry> _entries = new();

    public int Capacity { get; } = DefaultCapacity;

    public int Count => _entries.Count;

    public event EventHandler<VisionLogEntry>? EntryAppended;

    public event EventHandler? Cleared;

    public void Publish(VisionLogEntry entry)
    {
        _entries.Enqueue(entry);

        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
            // 丢弃最旧条目，保持窗口恒定。
        }

        EntryAppended?.Invoke(this, entry);
    }

    public IReadOnlyList<VisionLogEntry> Snapshot() => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
            // 清空缓冲。
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }
}
