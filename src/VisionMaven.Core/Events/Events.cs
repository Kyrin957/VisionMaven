using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Events;

/// <summary>一帧图像到达（订阅方不持有帧所有权）。</summary>
public sealed record FrameArrivedEvent(string DeviceId, string StationId, DateTimeOffset Timestamp, long SequenceNo);

/// <summary>设备状态迁移。</summary>
public sealed record DeviceStateChangedEvent(string DeviceId, DeviceState OldState, DeviceState NewState, string? Message);

/// <summary>一次检测完成。</summary>
public sealed record InspectionCompletedEvent(InspectionRecord Record);

/// <summary>产生报警。</summary>
public sealed record AlarmRaisedEvent(AlarmRecord Alarm);

/// <summary>报警被确认。</summary>
public sealed record AlarmAcknowledgedEvent(long AlarmId, string AcknowledgedBy);

/// <summary>工位状态迁移。</summary>
public sealed record StationStateChangedEvent(string StationId, StationState OldState, StationState NewState);

/// <summary>工位统计刷新（1Hz）。</summary>
public sealed record StationStatisticsChangedEvent(StationStatistics Statistics);

/// <summary>运行快照刷新，供底部状态栏使用。</summary>
public sealed record RuntimeSnapshotChangedEvent(RuntimeSnapshot Snapshot);

/// <summary>当前工程切换。</summary>
public sealed record ProjectOpenedEvent(string ProjectId, string Name);

/// <summary>当前工程关闭。</summary>
public sealed record ProjectClosedEvent(string ProjectId);

/// <summary>工程配置已保存。</summary>
public sealed record ProjectSavedEvent(string ProjectId);

/// <summary>工程配置被修改（内存态），用于标题栏与保存按钮的脏标记。</summary>
public sealed record ProjectDirtyChangedEvent(string ProjectId, bool IsDirty);

/// <summary>驱动插件诊断结果。</summary>
public sealed record DriverDiagnosticsEvent(IReadOnlyList<DriverDiagnostic> Diagnostics);

/// <summary>日志条目产生（用于跨模块转发）。</summary>
public sealed record LogEntryPublishedEvent(VisionLogEntry Entry);

/// <summary>模型库变更。</summary>
public sealed record ModelLibraryChangedEvent(string ProjectId);

/// <summary>流程定义变更。</summary>
public sealed record FlowDefinitionChangedEvent(string ProjectId, string FlowId);

/// <summary>设备列表变更。</summary>
public sealed record DeviceListChangedEvent(string ProjectId, string DeviceId);
