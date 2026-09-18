using Serilog;
using Serilog.Events;
using VisionMaven.Core.Abstractions;

namespace VisionMaven.Infrastructure.Logging;

/// <summary>Serilog 装配：文件轮转 + UI Sink（+ Debug 构建的控制台 Sink）。</summary>
public static class LoggingBootstrapper
{
    /// <summary>日志行格式，见开发文档 [14.5]。</summary>
    public const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static Serilog.ILogger Configure(
        ILogPathProvider paths,
        IVisionLogSink uiSink,
        bool includeConsole,
        string minimumLevel = "Debug")
    {
        var level = Enum.TryParse<LogEventLevel>(minimumLevel, true, out var parsed)
            ? parsed
            : LogEventLevel.Debug;

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .Enrich.FromLogContext()
            .WriteTo.Sink(new SerilogUiSink(uiSink))
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "visionmaven-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 50L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: OutputTemplate,
                shared: false);

        if (includeConsole)
        {
            configuration = configuration.WriteTo.Console(outputTemplate: OutputTemplate);
        }

        return configuration.CreateLogger();
    }
}
