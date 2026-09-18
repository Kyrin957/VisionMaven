using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>单个工位的运行时：独立 Task + CancellationToken，可单独启停。</summary>
public interface IStationRuntime : IAsyncDisposable
{
    StationConfig Config { get; }

    StationState State { get; }

    StationStatistics Statistics { get; }

    Task StartAsync(CancellationToken ct);

    Task StopAsync();

    /// <summary>软件触发一次检测（单次触发按钮）。</summary>
    Task<bool> TriggerOnceAsync(CancellationToken ct);

    /// <summary>复位报警状态。</summary>
    Task ResetAsync();

    /// <summary>在指定图像源上直接执行一次流程，用于试运行与参数设置页。</summary>
    Task<FlowRunOutcome> RunOnceAsync(IImageFrame frame, IReadOnlyDictionary<string, string>? parameterOverrides, CancellationToken ct);

    event EventHandler<StationStateChangedEventArgs> StateChanged;

    event EventHandler<StationStatisticsChangedEventArgs> StatisticsChanged;

    /// <summary>最近一次完成检测的叠加图与结果，供首页预览。</summary>
    event EventHandler<StationPreviewEventArgs> PreviewUpdated;
}

/// <summary>工位状态变化参数。</summary>
public sealed class StationStateChangedEventArgs : EventArgs
{
    public StationStateChangedEventArgs(string stationId, StationState oldState, StationState newState, string? message = null)
    {
        StationId = stationId;
        OldState = oldState;
        NewState = newState;
        Message = message;
    }

    public string StationId { get; }

    public StationState OldState { get; }

    public StationState NewState { get; }

    public string? Message { get; }
}

/// <summary>工位统计变化参数。</summary>
public sealed class StationStatisticsChangedEventArgs : EventArgs
{
    public StationStatisticsChangedEventArgs(StationStatistics statistics)
    {
        Statistics = statistics;
    }

    public StationStatistics Statistics { get; }
}

/// <summary>工位预览更新参数。</summary>
public sealed class StationPreviewEventArgs : EventArgs
{
    public StationPreviewEventArgs(string stationId, InspectionRecord record, object? overlayImage)
    {
        StationId = stationId;
        Record = record;
        OverlayImage = overlayImage;
    }

    public string StationId { get; }

    public InspectionRecord Record { get; }

    /// <summary>叠加了检测框的图像（运行时为 <c>Mat</c>），由接收方负责释放。</summary>
    public object? OverlayImage { get; }
}

/// <summary>一次流程执行的结果。</summary>
public sealed record FlowRunOutcome(
    string FlowId,
    string StationId,
    InspectionResult Result,
    double Score,
    double ElapsedMs,
    IReadOnlyList<NodeTiming> Timings,
    IReadOnlyList<Detection> Detections,
    string? ErrorCode,
    string? ErrorMessage,
    object? OverlayImage)
{
    public bool Success => ErrorCode is null;
}

/// <summary>多工位调度：按工程配置创建并管理全部工位运行时。</summary>
public interface IStationRuntimeFactory
{
    IStationRuntime Create(ProjectConfig project, StationConfig station);
}

/// <summary>全局运行时协调器：首页与状态栏的启停入口。</summary>
public interface IRuntimeCoordinator : IAsyncDisposable
{
    ProjectConfig? Project { get; }

    bool IsRunning { get; }

    IReadOnlyList<IStationRuntime> Stations { get; }

    IReadOnlyList<AlarmRecord> ActiveAlarms { get; }

    RuntimeSnapshot Snapshot { get; }

    /// <summary>按工程配置装配全部工位（不启动）。</summary>
    Task LoadProjectAsync(ProjectConfig project, CancellationToken ct);

    Task StartAsync(CancellationToken ct);

    Task StopAsync();

    Task ResetAsync();

    Task<bool> TriggerAsync(string stationId, CancellationToken ct);

    Task AcknowledgeAlarmAsync(long alarmId);

    Task AcknowledgeAllAlarmsAsync();

    /// <summary>按策略清理过期数据（检测记录 / 图片 / 报警）。</summary>
    Task<int> CleanupAsync(CancellationToken ct);

    event EventHandler<StationStateChangedEventArgs> StationStateChanged;

    event EventHandler<StationStatisticsChangedEventArgs> StatisticsChanged;

    event EventHandler<AlarmRaisedEventArgs> AlarmRaised;

    event EventHandler? SnapshotChanged;
}

/// <summary>报警产生参数。</summary>
public sealed class AlarmRaisedEventArgs : EventArgs
{
    public AlarmRaisedEventArgs(AlarmRecord alarm)
    {
        Alarm = alarm;
    }

    public AlarmRecord Alarm { get; }
}

/// <summary>流程引擎：在给定上下文上按拓扑执行节点。</summary>
public interface IFlowEngine
{
    /// <summary>校验流程定义的拓扑结构。</summary>
    FlowValidationResult Validate(FlowDefinitionConfig flow);

    /// <summary>按拓扑执行流程。</summary>
    Task<FlowRunOutcome> ExecuteAsync(
        FlowDefinitionConfig flow,
        string stationId,
        IReadOnlyDictionary<string, string> stationParameters,
        IReadOnlyDictionary<string, string>? parameterOverrides,
        object? initialFrame,
        CancellationToken ct);
}

/// <summary>图像叠加渲染：把检测框与文字画到图像上，供预览与落盘。</summary>
public interface IOverlayRenderer
{
    /// <summary>返回叠加后的图像（运行时为 <c>Mat</c>），由调用方释放。</summary>
    object Render(
        object sourceImage,
        IReadOnlyList<Detection> detections,
        InspectionResult result,
        double score,
        IReadOnlyList<string>? extraLines = null);
}
