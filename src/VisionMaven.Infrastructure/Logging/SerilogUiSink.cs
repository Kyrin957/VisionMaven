using Serilog.Core;
using Serilog.Events;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Infrastructure.Logging;

/// <summary>把 Serilog 事件写入 UI 环形缓冲，并抽取工位 / 流程 / 设备结构化字段。</summary>
public sealed class SerilogUiSink : ILogEventSink
{
    private readonly IVisionLogSink _sink;

    public SerilogUiSink(IVisionLogSink sink)
    {
        _sink = sink;
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null)
        {
            return;
        }

        var entry = new VisionLogEntry(
            logEvent.Timestamp,
            Map(logEvent.Level),
            ReadScalar(logEvent, "SourceContext"),
            logEvent.RenderMessage(),
            ReadScalar(logEvent, "StationId"),
            ReadScalar(logEvent, "FlowId"),
            ReadScalar(logEvent, "DeviceId"),
            logEvent.Exception?.ToString());

        _sink.Publish(entry);
    }

    private static VisionLogLevel Map(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => VisionLogLevel.Verbose,
        LogEventLevel.Debug => VisionLogLevel.Debug,
        LogEventLevel.Information => VisionLogLevel.Information,
        LogEventLevel.Warning => VisionLogLevel.Warning,
        LogEventLevel.Error => VisionLogLevel.Error,
        _ => VisionLogLevel.Fatal
    };

    private static string? ReadScalar(LogEvent logEvent, string key)
    {
        if (!logEvent.Properties.TryGetValue(key, out var value))
        {
            return null;
        }

        return ReadText(value);
    }

    private static string? ReadText(LogEventPropertyValue? value) => value switch
    {
        null => null,
        ScalarValue scalar => scalar.Value?.ToString(),
        _ => value.ToString()
    };
}
