using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>一条结构化日志。</summary>
public sealed record VisionLogEntry(
    DateTimeOffset Timestamp,
    VisionLogLevel Level,
    string? Source,
    string Message,
    string? StationId = null,
    string? FlowId = null,
    string? DeviceId = null,
    string? Exception = null);

/// <summary>日志 UI Sink：环形缓冲 + 批量刷新，日志栏唯一的数据源。</summary>
public interface IVisionLogSink
{
    int Capacity { get; }

    int Count { get; }

    void Publish(VisionLogEntry entry);

    IReadOnlyList<VisionLogEntry> Snapshot();

    void Clear();

    /// <summary>条目入缓冲时触发（可能在任意线程）。</summary>
    event EventHandler<VisionLogEntry>? EntryAppended;

    event EventHandler? Cleared;
}

/// <summary>日志文件目录解析。</summary>
public interface ILogPathProvider
{
    string LogDirectory { get; }

    string DatabaseFilePath { get; }

    string ProjectsRoot { get; }

    string PluginsDirectory { get; }

    string ApplicationRoot { get; }
}
