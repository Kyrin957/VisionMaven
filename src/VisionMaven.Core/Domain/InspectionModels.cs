namespace VisionMaven.Core.Domain;

/// <summary>一次检测记录（对应 <c>InspectionRecords</c> 表）。</summary>
public sealed class InspectionRecord
{
    public long Id { get; set; }

    public string ProjectId { get; set; } = string.Empty;

    public string StationId { get; set; } = string.Empty;

    public string CameraId { get; set; } = string.Empty;

    public long SequenceNo { get; set; }

    public InspectionResult Result { get; set; } = InspectionResult.Unknown;

    public double Score { get; set; }

    public double ElapsedMs { get; set; }

    public string? ImagePath { get; set; }

    public string? FlowId { get; set; }

    public DateTimeOffset InspectedAt { get; set; } = DateTimeOffset.Now;

    public List<DefectRecord> Defects { get; set; } = new();
}

/// <summary>缺陷明细（对应 <c>Defects</c> 表）。</summary>
public sealed class DefectRecord
{
    public long Id { get; set; }

    public long InspectionId { get; set; }

    public int ClassId { get; set; }

    public string ClassName { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}

/// <summary>报警记录（对应 <c>Alarms</c> 表）。</summary>
public sealed class AlarmRecord
{
    public long Id { get; set; }

    public string? StationId { get; set; }

    public string? DeviceId { get; set; }

    public AlarmLevel Level { get; set; } = AlarmLevel.Warning;

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset RaisedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public string? AcknowledgedBy { get; set; }

    public bool IsAcknowledged => AcknowledgedAt.HasValue;
}

/// <summary>操作审计（对应 <c>AuditLogs</c> 表）。</summary>
public sealed class AuditLog
{
    public long Id { get; set; }

    public long? UserId { get; set; }

    public string? UserName { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? Target { get; set; }

    public string? Detail { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.Now;

    public string? ClientHost { get; set; }
}

/// <summary>生产统计（对应 <c>ProductionStatistics</c> 表，按周期聚合）。</summary>
public sealed class ProductionStatistics
{
    public long Id { get; set; }

    public string ProjectId { get; set; } = string.Empty;

    public string StationId { get; set; } = string.Empty;

    public DateTimeOffset PeriodStart { get; set; }

    public int Total { get; set; }

    public int OkCount { get; set; }

    public int NgCount { get; set; }

    public double Yield => Total == 0 ? 0d : (double)OkCount / Total;

    public double AvgCycleMs { get; set; }
}

/// <summary>系统设置项（对应 <c>SystemSettings</c> 表）。</summary>
public sealed class SystemSetting
{
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>工位实时统计快照，供首页与底部状态栏使用。</summary>
public sealed record StationStatistics(
    string StationId,
    long Total,
    long OkCount,
    long NgCount,
    double AverageCycleMs,
    double LastCycleMs,
    long DroppedTriggers)
{
    public double Yield => Total == 0 ? 0d : (double)OkCount / Total;

    public static StationStatistics Empty(string stationId) => new(stationId, 0, 0, 0, 0, 0, 0);
}

/// <summary>全局运行时快照。</summary>
public sealed record RuntimeSnapshot(
    string? ProjectId,
    bool IsRunning,
    IReadOnlyList<StationStatistics> Stations,
    int UnacknowledgedAlarms);
