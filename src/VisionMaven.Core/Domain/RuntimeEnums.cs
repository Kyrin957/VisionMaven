namespace VisionMaven.Core.Domain;

/// <summary>工位运行状态。</summary>
public enum StationState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Alarm = 3,
    Stopping = 4
}

/// <summary>流程节点执行模式。</summary>
public enum FlowNodeMode
{
    /// <summary>串行执行。</summary>
    Sequential = 0,

    /// <summary>与同级后继并行执行。</summary>
    Parallel = 1,

    /// <summary>按 <c>condition</c> 参数决定后继分支。</summary>
    Conditional = 2
}

/// <summary>节点失败策略。</summary>
public enum FailureStrategy
{
    /// <summary>立即终止流程并判定 NG。</summary>
    Abort = 0,

    /// <summary>记录错误后继续执行后继节点。</summary>
    Continue = 1,

    /// <summary>按 <c>retryCount</c> 重试，仍失败则按 Abort 处理。</summary>
    Retry = 2
}

/// <summary>检测结论。</summary>
public enum InspectionResult
{
    Unknown = 0,
    Ok = 1,
    Ng = 2
}

/// <summary>报警级别。</summary>
public enum AlarmLevel
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

/// <summary>推理任务类型。</summary>
public enum ModelTask
{
    Detection = 0,
    Classification = 1,
    Segmentation = 2
}

/// <summary>推理精度。</summary>
public enum InferencePrecision
{
    FP32 = 0,
    FP16 = 1,
    INT8 = 2
}

/// <summary>推理执行提供程序。</summary>
public enum InferenceDevice
{
    CPU = 0,
    CUDA = 1,
    TensorRT = 2
}

/// <summary>张量通道排布。</summary>
public enum TensorLayout
{
    NCHW = 0,
    NHWC = 1
}

/// <summary>归一化方式。</summary>
public enum NormalizeMode
{
    /// <summary>0~1。</summary>
    Div255 = 0,

    /// <summary>减均值除方差。</summary>
    Imagenet = 1,

    /// <summary>保持 0~255。</summary>
    None = 2
}

/// <summary>结果图片落盘策略。</summary>
public enum ResultImagePolicy
{
    All = 0,
    NgOnly = 1,
    None = 2
}

/// <summary>标定类型。</summary>
public enum CalibrationType
{
    None = 0,
    ScaleOnly = 1,
    NinePoint = 2,
    Chessboard = 3
}

/// <summary>视觉算子类别，用于算法页类别树分组。</summary>
public enum OperatorCategory
{
    Preprocess = 0,
    Threshold = 1,
    Blob = 2,
    TemplateMatch = 3,
    Finding = 4,
    Measure = 5,
    Calibration = 6,
    Postprocess = 7
}

/// <summary>参数设置页的逐节点执行状态。</summary>
public enum NodeRunStatus
{
    NotRun = 0,
    Success = 1,
    Failed = 2,
    Skipped = 3,
    Cancelled = 4
}

/// <summary>日志级别（与 <c>Microsoft.Extensions.Logging.LogLevel</c> 数值对齐）。</summary>
public enum VisionLogLevel
{
    Verbose = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5
}
