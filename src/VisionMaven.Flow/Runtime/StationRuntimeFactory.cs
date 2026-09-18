using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Exceptions;
using VisionMaven.Flow.Devices;

namespace VisionMaven.Flow.Runtime;

/// <summary>按工程配置实例化工位运行时：单工位与多工位走完全相同的代码路径。</summary>
public sealed class StationRuntimeFactory : IStationRuntimeFactory
{
    private readonly IFlowEngine _engine;
    private readonly IDeviceSession _devices;
    private readonly IInspectionRepository _inspections;
    private readonly IAlarmRepository _alarms;
    private readonly IStatisticsRepository _statistics;
    private readonly ILoggerFactory _loggerFactory;

    public StationRuntimeFactory(
        IFlowEngine engine,
        IDeviceSession devices,
        IInspectionRepository inspections,
        IAlarmRepository alarms,
        IStatisticsRepository statistics,
        ILoggerFactory loggerFactory)
    {
        _engine = engine;
        _devices = devices;
        _inspections = inspections;
        _alarms = alarms;
        _statistics = statistics;
        _loggerFactory = loggerFactory;
    }

    public IStationRuntime Create(ProjectConfig project, StationConfig station)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(station);

        var flow = project.Flows.FirstOrDefault(
            item => string.Equals(item.FlowId, station.FlowId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ConfigurationException(
                ErrorCodes.ConfigDanglingReference,
                $"工位 {station.StationId} 绑定的流程不存在：{station.FlowId}");

        return new StationRuntime(
            project,
            station,
            flow,
            _engine,
            _devices,
            _inspections,
            _alarms,
            _statistics,
            _loggerFactory.CreateLogger<StationRuntime>());
    }
}
