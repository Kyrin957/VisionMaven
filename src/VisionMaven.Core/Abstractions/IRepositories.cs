using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>检测记录与缺陷仓储。</summary>
public interface IInspectionRepository
{
    Task<InspectionRecord> AddAsync(InspectionRecord record, CancellationToken ct);

    Task<IReadOnlyList<InspectionRecord>> QueryAsync(
        string? projectId,
        string? stationId,
        DateTimeOffset from,
        DateTimeOffset to,
        int skip,
        int take,
        CancellationToken ct);

    Task<int> CountAsync(string? projectId, string? stationId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>删除指定时间之前的记录及其缺陷明细，返回删除的记录数。</summary>
    Task<int> DeleteBeforeAsync(DateTimeOffset threshold, CancellationToken ct);
}

/// <summary>报警仓储。</summary>
public interface IAlarmRepository
{
    Task<AlarmRecord> RaiseAsync(AlarmRecord alarm, CancellationToken ct);

    Task<IReadOnlyList<AlarmRecord>> QueryUnacknowledgedAsync(int take, CancellationToken ct);

    Task<IReadOnlyList<AlarmRecord>> QueryAsync(DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct);

    Task<bool> AcknowledgeAsync(long alarmId, string acknowledgedBy, CancellationToken ct);

    Task<int> AcknowledgeAllAsync(string acknowledgedBy, CancellationToken ct);

    Task<int> CountUnacknowledgedAsync(CancellationToken ct);

    Task<int> DeleteBeforeAsync(DateTimeOffset threshold, CancellationToken ct);
}

/// <summary>生产统计仓储。</summary>
public interface IStatisticsRepository
{
    /// <summary>按周期累加统计。</summary>
    Task AccumulateAsync(ProductionStatistics delta, CancellationToken ct);

    Task<IReadOnlyList<ProductionStatistics>> QueryAsync(string? projectId, string? stationId, DateTimeOffset from, CancellationToken ct);

    Task UpsertAsync(ProductionStatistics statistics, CancellationToken ct);
}

/// <summary>审计仓储。</summary>
public interface IAuditRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct);

    Task<IReadOnlyList<AuditLog>> QueryAsync(string? userName, DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct);
}

/// <summary>用户仓储。</summary>
public interface IUserRepository
{
    Task<UserAccount?> FindByNameAsync(string userName, CancellationToken ct);

    Task<UserAccount?> FindByIdAsync(long id, CancellationToken ct);

    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct);

    Task<UserAccount> AddAsync(UserAccount user, CancellationToken ct);

    Task UpdateAsync(UserAccount user, CancellationToken ct);

    Task<bool> DeleteAsync(long id, CancellationToken ct);
}

/// <summary>角色仓储。</summary>
public interface IRoleRepository
{
    Task<IReadOnlyList<RoleDefinition>> ListAsync(CancellationToken ct);

    Task<RoleDefinition?> FindByCodeAsync(string code, CancellationToken ct);

    Task<RoleDefinition?> FindByIdAsync(long id, CancellationToken ct);

    Task UpsertAsync(RoleDefinition role, CancellationToken ct);
}

/// <summary>系统设置仓储。</summary>
public interface ISystemSettingRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct);

    Task SetAsync(string key, string? value, CancellationToken ct);

    Task<IReadOnlyDictionary<string, string?>> GetAllAsync(CancellationToken ct);
}
