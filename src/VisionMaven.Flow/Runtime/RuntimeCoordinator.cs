using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;
using VisionMaven.Flow.Devices;
using VisionMaven.Vision.Calibration;

namespace VisionMaven.Flow.Runtime;

/// <summary>全局运行时协调器：工位装配、启停、报警与数据清理。</summary>
public sealed class RuntimeCoordinator : IRuntimeCoordinator
{
    private readonly IStationRuntimeFactory _factory;
    private readonly IDeviceSession _devices;
    private readonly IInspectionRepository _inspections;
    private readonly IAlarmRepository _alarms;
    private readonly IProjectFileStorage _storage;
    private readonly IProjectAccessor _projectAccessor;
    private readonly ProjectCalibrationProvider _calibration;
    private readonly ILogger<RuntimeCoordinator> _logger;

    private readonly List<IStationRuntime> _stations = new();
    private readonly List<AlarmRecord> _activeAlarms = new();
    private readonly object _sync = new();
    private bool _disposed;

    public RuntimeCoordinator(
        IStationRuntimeFactory factory,
        IDeviceSession devices,
        IInspectionRepository inspections,
        IAlarmRepository alarms,
        IProjectFileStorage storage,
        IProjectAccessor projectAccessor,
        ProjectCalibrationProvider calibration,
        ILogger<RuntimeCoordinator> logger)
    {
        _factory = factory;
        _devices = devices;
        _inspections = inspections;
        _alarms = alarms;
        _storage = storage;
        _projectAccessor = projectAccessor;
        _calibration = calibration;
        _logger = logger;
        _devices.DeviceStateChanged += (_, _) => RaiseSnapshotChanged();
    }

    public ProjectConfig? Project { get; private set; }

    public bool IsRunning { get; private set; }

    public IReadOnlyList<IStationRuntime> Stations
    {
        get
        {
            lock (_sync)
            {
                return _stations.ToArray();
            }
        }
    }

    public IReadOnlyList<AlarmRecord> ActiveAlarms
    {
        get
        {
            lock (_sync)
            {
                return _activeAlarms.ToArray();
            }
        }
    }

    public RuntimeSnapshot Snapshot
    {
        get
        {
            var stations = Stations.Select(station => station.Statistics).ToArray();
            int alarms;
            lock (_sync)
            {
                alarms = _activeAlarms.Count;
            }

            return new RuntimeSnapshot(Project?.ProjectId, IsRunning, stations, alarms);
        }
    }

    public event EventHandler<StationStateChangedEventArgs>? StationStateChanged;

    public event EventHandler<StationStatisticsChangedEventArgs>? StatisticsChanged;

    public event EventHandler<AlarmRaisedEventArgs>? AlarmRaised;

    public event EventHandler? SnapshotChanged;

    public async Task LoadProjectAsync(ProjectConfig project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);

        await StopAsync().ConfigureAwait(false);
        await DisposeStationsAsync().ConfigureAwait(false);

        Project = project;
        _projectAccessor.Set(project);
        _calibration.Configure(project);

        await _devices.OpenAsync(project, ct).ConfigureAwait(false);

        var created = new List<IStationRuntime>();
        foreach (var station in project.Stations.Where(station => station.Enabled))
        {
            try
            {
                var runtime = _factory.Create(project, station);
                runtime.StateChanged += OnStationStateChanged;
                runtime.StatisticsChanged += OnStationStatisticsChanged;
                runtime.PreviewUpdated += OnStationPreviewUpdated;
                created.Add(runtime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "工位装配失败：{StationId}", station.StationId);
            }
        }

        lock (_sync)
        {
            _stations.Clear();
            _stations.AddRange(created);
        }

        await RefreshAlarmsAsync(CancellationToken.None).ConfigureAwait(false);

        _logger.LogInformation("工程已装配：{ProjectId} 工位={Count}", project.ProjectId, created.Count);
        RaiseSnapshotChanged();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (Project is null)
        {
            throw new InvalidOperationException("尚未打开工程");
        }

        if (IsRunning)
        {
            return;
        }

        foreach (var station in Stations)
        {
            try
            {
                await station.StartAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "工位启动失败：{StationId}", station.Config.StationId);
                await RaiseAlarmAsync(
                        station.Config.StationId,
                        null,
                        ErrorCodes.DeviceAlarm,
                        $"工位启动失败：{ex.Message}",
                        ct)
                    .ConfigureAwait(false);
            }
        }

        IsRunning = true;
        RaiseSnapshotChanged();
        _logger.LogInformation("运行已启动，工位数={Count}", Stations.Count);
    }

    public async Task StopAsync()
    {
        foreach (var station in Stations)
        {
            try
            {
                await station.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "工位停止失败：{StationId}", station.Config.StationId);
            }
        }

        IsRunning = false;
        RaiseSnapshotChanged();
    }

    public async Task ResetAsync()
    {
        foreach (var station in Stations)
        {
            await station.ResetAsync().ConfigureAwait(false);
        }

        var acknowledged = await _alarms.AcknowledgeAllAsync("system", CancellationToken.None).ConfigureAwait(false);
        if (acknowledged > 0)
        {
            await RefreshAlarmsAsync(CancellationToken.None).ConfigureAwait(false);
        }

        RaiseSnapshotChanged();
    }

    public async Task<bool> TriggerAsync(string stationId, CancellationToken ct)
    {
        var station = Stations.FirstOrDefault(
            item => string.Equals(item.Config.StationId, stationId, StringComparison.OrdinalIgnoreCase));

        return station is not null && await station.TriggerOnceAsync(ct).ConfigureAwait(false);
    }

    public async Task AcknowledgeAlarmAsync(long alarmId)
    {
        var user = "operator";
        await _alarms.AcknowledgeAsync(alarmId, user, CancellationToken.None).ConfigureAwait(false);
        await RefreshAlarmsAsync(CancellationToken.None).ConfigureAwait(false);
        RaiseSnapshotChanged();
    }

    public async Task AcknowledgeAllAlarmsAsync()
    {
        await _alarms.AcknowledgeAllAsync("operator", CancellationToken.None).ConfigureAwait(false);
        await RefreshAlarmsAsync(CancellationToken.None).ConfigureAwait(false);
        RaiseSnapshotChanged();
    }

    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        if (Project is null)
        {
            return 0;
        }

        var recordDays = Project.Parameters.GetGlobalInt(ParameterKeys.RecordRetentionDays, 90);
        var imageDays = Project.Parameters.GetGlobalInt(ParameterKeys.ImageRetentionDays, 30);
        var alarmDays = Project.Parameters.GetGlobalInt(ParameterKeys.AlarmRetentionDays, 365);

        var removed = 0;
        removed += await _inspections
            .DeleteBeforeAsync(DateTimeOffset.Now.AddDays(-recordDays), ct)
            .ConfigureAwait(false);
        removed += await _alarms
            .DeleteBeforeAsync(DateTimeOffset.Now.AddDays(-alarmDays), ct)
            .ConfigureAwait(false);
        await _storage.CleanupImagesAsync(Project.ProjectId, imageDays, ct).ConfigureAwait(false);

        if (removed > 0)
        {
            _logger.LogInformation("已清理 {Count} 条过期数据", removed);
        }

        return removed;
    }

    private async Task RefreshAlarmsAsync(CancellationToken ct)
    {
        var pending = await _alarms.QueryUnacknowledgedAsync(200, ct).ConfigureAwait(false);
        lock (_sync)
        {
            _activeAlarms.Clear();
            _activeAlarms.AddRange(pending);
        }
    }

    private async Task RaiseAlarmAsync(string? stationId, string? deviceId, string code, string message, CancellationToken ct)
    {
        var alarm = await _alarms
            .RaiseAsync(
                new AlarmRecord
                {
                    StationId = stationId,
                    DeviceId = deviceId,
                    Level = AlarmLevel.Error,
                    Code = code,
                    Message = message,
                    RaisedAt = DateTimeOffset.Now
                },
                ct)
            .ConfigureAwait(false);

        lock (_sync)
        {
            _activeAlarms.Insert(0, alarm);
        }

        AlarmRaised?.Invoke(this, new AlarmRaisedEventArgs(alarm));
        RaiseSnapshotChanged();
    }

    private void OnStationStateChanged(object? sender, StationStateChangedEventArgs args)
    {
        StationStateChanged?.Invoke(this, args);

        if (args.NewState == StationState.Alarm)
        {
            _ = RaiseAlarmAsync(
                args.StationId,
                null,
                ErrorCodes.DeviceAlarm,
                args.Message ?? $"工位 {args.StationId} 进入报警状态",
                CancellationToken.None);
        }

        RaiseSnapshotChanged();
    }

    private void OnStationStatisticsChanged(object? sender, StationStatisticsChangedEventArgs args)
    {
        StatisticsChanged?.Invoke(this, args);
        RaiseSnapshotChanged();
    }

    private void OnStationPreviewUpdated(object? sender, StationPreviewEventArgs args)
        => PreviewUpdated?.Invoke(this, args);

    /// <summary>工位预览转发事件，供首页订阅。</summary>
    public event EventHandler<StationPreviewEventArgs>? PreviewUpdated;

    private void RaiseSnapshotChanged() => SnapshotChanged?.Invoke(this, EventArgs.Empty);

    private async Task DisposeStationsAsync()
    {
        IStationRuntime[] stations;
        lock (_sync)
        {
            stations = _stations.ToArray();
            _stations.Clear();
        }

        foreach (var station in stations)
        {
            station.StateChanged -= OnStationStateChanged;
            station.StatisticsChanged -= OnStationStatisticsChanged;
            station.PreviewUpdated -= OnStationPreviewUpdated;
            await station.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        await DisposeStationsAsync().ConfigureAwait(false);
        await _devices.DisposeAsync().ConfigureAwait(false);
    }
}
