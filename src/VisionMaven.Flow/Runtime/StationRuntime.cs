using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;
using VisionMaven.Flow.Devices;

namespace VisionMaven.Flow.Runtime;

/// <summary>
/// 单工位运行时：独立长驻 Task + 独立 CancellationTokenSource，可单独启停。
/// 异常被隔离在本工位，不冒泡到应用级。
/// </summary>
public sealed class StationRuntime : IStationRuntime
{
    private const string AcquireTypeKey = "acquire";

    private readonly ProjectConfig _project;
    private readonly FlowDefinitionConfig _flow;
    private readonly IFlowEngine _engine;
    private readonly IDeviceSession _devices;
    private readonly IInspectionRepository _inspections;
    private readonly IAlarmRepository _alarms;
    private readonly IStatisticsRepository _statistics;
    private readonly ILogger<StationRuntime> _logger;

    private readonly Channel<bool> _triggers = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly object _sync = new();

    private CancellationTokenSource? _stationCts;
    private Task? _loop;
    private long _total;
    private long _ok;
    private long _ng;
    private double _cycleSum;
    private double _lastCycle;
    private long _dropped;
    private int _consecutiveNg;
    private int _triggerPending;
    private StationStatistics _statisticsSnapshot;
    private DateTimeOffset _lastStatisticsPublish = DateTimeOffset.MinValue;

    public StationRuntime(
        ProjectConfig project,
        StationConfig station,
        FlowDefinitionConfig flow,
        IFlowEngine engine,
        IDeviceSession devices,
        IInspectionRepository inspections,
        IAlarmRepository alarms,
        IStatisticsRepository statistics,
        ILogger<StationRuntime> logger)
    {
        _project = project;
        Config = station;
        _flow = flow;
        _engine = engine;
        _devices = devices;
        _inspections = inspections;
        _alarms = alarms;
        _statistics = statistics;
        _logger = logger;
        _statisticsSnapshot = StationStatistics.Empty(station.StationId);
    }

    public StationConfig Config { get; }

    public StationState State { get; private set; } = StationState.Stopped;

    public StationStatistics Statistics => _statisticsSnapshot;

    public event EventHandler<StationStateChangedEventArgs>? StateChanged;

    public event EventHandler<StationStatisticsChangedEventArgs>? StatisticsChanged;

    public event EventHandler<StationPreviewEventArgs>? PreviewUpdated;

    public async Task StartAsync(CancellationToken ct)
    {
        if (State is StationState.Running or StationState.Starting)
        {
            return;
        }

        if (_flow.Nodes.Count == 0)
        {
            throw new FlowException(ErrorCodes.FlowTopologyInvalid, $"工位 {Config.StationId} 绑定的流程为空");
        }

        var validation = _engine.Validate(_flow);
        if (!validation.IsValid)
        {
            throw new FlowException(
                ErrorCodes.FlowTopologyInvalid,
                $"工位 {Config.StationId} 流程校验失败：{string.Join("; ", validation.Errors)}");
        }

        Transition(StationState.Starting);

        _stationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await PrepareCamerasAsync(_stationCts.Token).ConfigureAwait(false);

        _loop = Task.Run(() => RunLoopAsync(_stationCts.Token), CancellationToken.None);

        Transition(StationState.Running);
        _logger.LogInformation("工位已启动：{StationId} 流程={FlowId}", Config.StationId, _flow.FlowId);
    }

    public async Task StopAsync()
    {
        if (_stationCts is null)
        {
            Transition(StationState.Stopped);
            return;
        }

        Transition(StationState.Stopping);
        await _stationCts.CancelAsync().ConfigureAwait(false);

        var loop = _loop;
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常停止路径。
            }
        }

        await StopCamerasAsync().ConfigureAwait(false);

        _stationCts.Dispose();
        _stationCts = null;
        _loop = null;

        Transition(StationState.Stopped);
        _logger.LogInformation("工位已停止：{StationId}", Config.StationId);
    }

    public async Task<bool> TriggerOnceAsync(CancellationToken ct)
    {
        if (State != StationState.Running)
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _triggerPending, 1, 0) != 0)
        {
            // 同一时刻只允许一个流程实例在跑：按 OverrunPolicy 处理新触发。
            Interlocked.Increment(ref _dropped);

            switch (Config.Trigger.OverrunPolicy)
            {
                case OverrunPolicy.DropOld:
                    _triggers.Writer.TryWrite(true);
                    return true;

                case OverrunPolicy.QueueAndWait:
                    await _triggers.Writer.WriteAsync(true, ct).ConfigureAwait(false);
                    return true;

                default:
                    _logger.LogWarning("工位 {StationId} 触发被丢弃（上一实例未完成）", Config.StationId);
                    return false;
            }
        }

        await _triggers.Writer.WriteAsync(true, ct).ConfigureAwait(false);
        return true;
    }

    public Task ResetAsync()
    {
        _consecutiveNg = 0;
        if (State == StationState.Alarm)
        {
            Transition(StationState.Running);
        }

        PublishStatistics(true);
        return Task.CompletedTask;
    }

    public async Task<FlowRunOutcome> RunOnceAsync(
        IImageFrame frame,
        IReadOnlyDictionary<string, string>? parameterOverrides,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var overrides = BuildOverrides(parameterOverrides);
        return await _engine
            .ExecuteAsync(_flow, Config.StationId, Config.Parameters, overrides, frame, ct)
            .ConfigureAwait(false);
    }

    private async Task PrepareCamerasAsync(CancellationToken ct)
    {
        foreach (var cameraId in Config.CameraIds)
        {
            var camera = _devices.GetCamera(cameraId);
            if (camera is null)
            {
                _logger.LogWarning("工位 {StationId} 绑定的相机不可用：{CameraId}", Config.StationId, cameraId);
                continue;
            }

            try
            {
                if (camera.State != DeviceState.Ready)
                {
                    await camera.ConnectAsync(ct).ConfigureAwait(false);
                }

                if (!camera.IsGrabbing && camera.Capabilities.SupportedTriggerModes.Contains(TriggerMode.Software))
                {
                    await camera.StartGrabbingAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "工位 {StationId} 相机启动失败：{CameraId}", Config.StationId, cameraId);
            }
        }
    }

    private async Task StopCamerasAsync()
    {
        foreach (var cameraId in Config.CameraIds)
        {
            var camera = _devices.GetCamera(cameraId);
            if (camera is null)
            {
                continue;
            }

            try
            {
                await camera.StopGrabbingAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "相机停止取流失败：{CameraId}", cameraId);
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        if (Config.Trigger.Source == TriggerSource.Timer)
        {
            _ = Task.Run(() => TimerTriggerLoopAsync(ct), CancellationToken.None);
        }

        if (Config.Trigger.Source == TriggerSource.Plc)
        {
            _ = Task.Run(() => PlcTriggerLoopAsync(ct), CancellationToken.None);
        }

        try
        {
            await foreach (var _ in _triggers.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                Interlocked.Exchange(ref _triggerPending, 0);
                try
                {
                    await ExecuteCycleAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 工位级异常隔离：置为 Alarm，其余工位继续运行。
                    _logger.LogError(ex, "工位 {StationId} 执行异常，已置为报警状态", Config.StationId);
                    Transition(StationState.Alarm);
                    await RaiseAlarmAsync(ErrorCodes.DeviceAlarm, ex.Message, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("工位 {StationId} 循环已取消", Config.StationId);
        }
    }

    private async Task TimerTriggerLoopAsync(CancellationToken ct)
    {
        var interval = Math.Max(10, Config.Trigger.IntervalMs);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
                await TriggerOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止路径。
        }
    }

    private async Task PlcTriggerLoopAsync(CancellationToken ct)
    {
        var deviceId = Config.Trigger.DeviceId;
        var address = Config.Trigger.Address;
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(address))
        {
            _logger.LogWarning("工位 {StationId} 的 PLC 触发缺少设备或地址配置", Config.StationId);
            return;
        }

        var pollMs = Math.Max(10, _project.CommLinks
            .FirstOrDefault(link => string.Equals(link.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            ?.PollingMs ?? 100);

        var previous = false;
        var debounce = Math.Max(0, Config.Trigger.DebounceMs);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(pollMs, ct).ConfigureAwait(false);

                var plc = _devices.GetPlc(deviceId);
                if (plc is null)
                {
                    continue;
                }

                bool current;
                try
                {
                    current = await plc.ReadAsync<bool>(address, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "PLC 触发轮询失败：{Address}", address);
                    continue;
                }

                var hit = Config.Trigger.Edge switch
                {
                    TriggerEdge.Falling => previous && !current,
                    TriggerEdge.Level => current,
                    _ => !previous && current
                };

                previous = current;

                if (hit)
                {
                    if (debounce > 0)
                    {
                        await Task.Delay(debounce, ct).ConfigureAwait(false);
                    }

                    await TriggerOnceAsync(ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止路径。
        }
    }

    private async Task ExecuteCycleAsync(CancellationToken ct)
    {
        var cameras = Config.CameraIds
            .Select(_devices.GetCamera)
            .Where(camera => camera is not null)
            .Select(camera => camera!)
            .ToArray();

        if (cameras.Length == 0)
        {
            var outcome = await _engine
                .ExecuteAsync(_flow, Config.StationId, Config.Parameters, null, null, ct)
                .ConfigureAwait(false);
            await PublishOutcomeAsync(outcome, "CAM-", ct).ConfigureAwait(false);
            return;
        }

        foreach (var camera in cameras)
        {
            ct.ThrowIfCancellationRequested();

            var overrides = BuildOverrides(null, camera.DeviceId);
            var outcome = await _engine
                .ExecuteAsync(_flow, Config.StationId, Config.Parameters, overrides, null, ct)
                .ConfigureAwait(false);

            await PublishOutcomeAsync(outcome, camera.DeviceId, ct).ConfigureAwait(false);

            if (outcome.Result == InspectionResult.Ng && outcome.ErrorCode is not null)
            {
                // 流程失败已由 FlowEngine 记录为 NG，这里仅保留告警计数逻辑。
                _logger.LogWarning(
                    "工位 {StationId} 检测流程失败：{Code} {Message}",
                    Config.StationId,
                    outcome.ErrorCode,
                    outcome.ErrorMessage);
            }
        }
    }

    /// <summary>为流程中所有采集节点注入相机参数，实现多相机单流程复用。</summary>
    private IReadOnlyDictionary<string, string>? BuildOverrides(
        IReadOnlyDictionary<string, string>? extra,
        string? cameraId = null)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (cameraId is not null)
        {
            foreach (var node in _flow.Nodes.Where(node =>
                         string.Equals(node.TypeKey, AcquireTypeKey, StringComparison.OrdinalIgnoreCase)))
            {
                overrides[$"{node.NodeId}.cameraId"] = cameraId;
            }
        }

        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                overrides[pair.Key] = pair.Value;
            }
        }

        return overrides.Count == 0 ? null : overrides;
    }

    private async Task PublishOutcomeAsync(FlowRunOutcome outcome, string cameraId, CancellationToken ct)
    {
        var record = new InspectionRecord
        {
            ProjectId = _project.ProjectId,
            StationId = Config.StationId,
            CameraId = cameraId,
            SequenceNo = Interlocked.Increment(ref _total),
            Result = outcome.Result,
            Score = outcome.Score,
            ElapsedMs = outcome.ElapsedMs,
            FlowId = outcome.FlowId,
            InspectedAt = DateTimeOffset.Now,
            Defects = outcome.Detections
                .Select(detection => new DefectRecord
                {
                    ClassId = detection.ClassId,
                    ClassName = detection.ClassName,
                    Confidence = detection.Confidence,
                    X = detection.Box.X,
                    Y = detection.Box.Y,
                    Width = detection.Box.Width,
                    Height = detection.Box.Height
                })
                .ToList()
        };

        if (outcome.Result == InspectionResult.Ok)
        {
            Interlocked.Increment(ref _ok);
            _consecutiveNg = 0;
        }
        else
        {
            Interlocked.Increment(ref _ng);
            _consecutiveNg++;
        }

        _cycleSum += outcome.ElapsedMs;
        _lastCycle = outcome.ElapsedMs;

        try
        {
            await _inspections.AddAsync(record, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检测记录落盘失败：{StationId}", Config.StationId);
        }

        var threshold = _project.Parameters.GetStationInt(
            Config.StationId,
            ParameterKeys.ConsecutiveNgAlarm,
            3);

        if (_consecutiveNg >= threshold && threshold > 0)
        {
            _consecutiveNg = 0;
            Transition(StationState.Alarm);
            await RaiseAlarmAsync(
                    ErrorCodes.DeviceAlarm,
                    $"工位 {Config.StationId} 连续 {threshold} 件 NG",
                    ct)
                .ConfigureAwait(false);
        }

        PublishStatistics(false);
        RaisePreview(record, outcome.OverlayImage);

        await AccumulateStatisticsAsync(outcome, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "工位 {StationId} 检测完成 结果={Result} 耗时={ElapsedMs:F1}ms",
            Config.StationId,
            outcome.Result,
            outcome.ElapsedMs);
    }

    private async Task AccumulateStatisticsAsync(FlowRunOutcome outcome, CancellationToken ct)
    {
        try
        {
            var today = DateTime.Today;
            var periodStart = new DateTimeOffset(today, TimeZoneInfo.Local.GetUtcOffset(today));

            await _statistics
                .AccumulateAsync(
                    new ProductionStatistics
                    {
                        ProjectId = _project.ProjectId,
                        StationId = Config.StationId,
                        PeriodStart = periodStart,
                        Total = 1,
                        OkCount = outcome.Result == InspectionResult.Ok ? 1 : 0,
                        NgCount = outcome.Result == InspectionResult.Ok ? 0 : 1,
                        AvgCycleMs = outcome.ElapsedMs
                    },
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "生产统计写入失败");
        }
    }

    private void PublishStatistics(bool force)
    {
        var interval = TimeSpan.FromSeconds(
            Math.Max(1, _project.Parameters.GetGlobalInt(ParameterKeys.StatisticsIntervalSec, 60)));

        if (!force && DateTimeOffset.Now - _lastStatisticsPublish < interval)
        {
            return;
        }

        _lastStatisticsPublish = DateTimeOffset.Now;

        var total = Interlocked.Read(ref _total);
        var ok = Interlocked.Read(ref _ok);
        var ng = Interlocked.Read(ref _ng);

        _statisticsSnapshot = new StationStatistics(
            Config.StationId,
            total,
            ok,
            ng,
            total == 0 ? 0d : _cycleSum / total,
            _lastCycle,
            Interlocked.Read(ref _dropped));

        StatisticsChanged?.Invoke(this, new StationStatisticsChangedEventArgs(_statisticsSnapshot));
    }

    private void RaisePreview(InspectionRecord record, object? overlayImage)
        => PreviewUpdated?.Invoke(this, new StationPreviewEventArgs(Config.StationId, record, overlayImage));

    private async Task RaiseAlarmAsync(string code, string message, CancellationToken ct)
    {
        try
        {
            var alarm = await _alarms
                .RaiseAsync(
                    new AlarmRecord
                    {
                        StationId = Config.StationId,
                        Level = AlarmLevel.Error,
                        Code = code,
                        Message = message,
                        RaisedAt = DateTimeOffset.Now
                    },
                    ct)
                .ConfigureAwait(false);

            _logger.LogWarning("工位 {StationId} 报警：{Code} {Message}", Config.StationId, alarm.Code, alarm.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "报警写入失败");
        }
    }

    private void Transition(StationState next)
    {
        StationState previous;
        lock (_sync)
        {
            if (State == next)
            {
                return;
            }

            previous = State;
            State = next;
        }

        StateChanged?.Invoke(this, new StationStateChangedEventArgs(Config.StationId, previous, next));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stationCts?.Dispose();
    }
}
