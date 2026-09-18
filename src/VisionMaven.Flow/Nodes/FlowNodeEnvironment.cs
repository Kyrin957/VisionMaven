using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Flow.Devices;
using VisionMaven.Flow.Inference;
using VisionMaven.Flow.Runtime;

namespace VisionMaven.Flow.Nodes;

/// <summary>流程节点的运行时依赖集合。</summary>
public sealed class FlowNodeEnvironment
{
    public FlowNodeEnvironment(
        IDeviceSession devices,
        IOperatorRegistry operators,
        ICalibrationProvider calibration,
        IInferenceSessionPool inferenceSessions,
        IProjectFileStorage storage,
        IProjectAccessor project,
        ILoggerFactory loggerFactory)
    {
        Devices = devices;
        Operators = operators;
        Calibration = calibration;
        InferenceSessions = inferenceSessions;
        Storage = storage;
        Project = project;
        LoggerFactory = loggerFactory;
    }

    public IDeviceSession Devices { get; }

    public IOperatorRegistry Operators { get; }

    public ICalibrationProvider Calibration { get; }

    public IInferenceSessionPool InferenceSessions { get; }

    public IProjectFileStorage Storage { get; }

    public IProjectAccessor Project { get; }

    public ILoggerFactory LoggerFactory { get; }
}
