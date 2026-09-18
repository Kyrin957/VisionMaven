using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>推理引擎：模型描述与运行选项分离，同一引擎可服务多工位、多模型。</summary>
public interface IInferenceEngine : IAsyncDisposable
{
    string EngineKey { get; }

    /// <summary>引擎当前实际使用的执行提供程序（EP 不可用时会降级）。</summary>
    InferenceDevice ActiveDevice { get; }

    bool IsLoaded { get; }

    Task LoadAsync(ModelDescriptor descriptor, InferenceOptions options, CancellationToken ct);

    Task<InferenceResult> InferAsync(IImageFrame frame, CancellationToken ct);

    Task UnloadAsync();
}

/// <summary>模型运行时描述：由工程配置解析并解析出绝对路径。</summary>
public sealed record ModelDescriptor(
    string ModelId,
    string Name,
    ModelTask Task,
    string Version,
    string ModelFilePath,
    IReadOnlyList<string> Labels,
    ModelInputConfig Input,
    double Confidence,
    double Nms,
    int MaxDetections,
    InferencePrecision Precision,
    InferenceDevice Device,
    int Sessions,
    bool Warmup);

/// <summary>推理运行选项（可覆盖工程配置）。</summary>
public sealed record InferenceOptions
{
    public double? Confidence { get; init; }

    public double? Nms { get; init; }

    public int? MaxDetections { get; init; }

    public InferenceDevice? Device { get; init; }

    public InferencePrecision? Precision { get; init; }
}

/// <summary>推理结果。</summary>
public sealed class InferenceResult
{
    public static InferenceResult Failure(string errorCode, string message, long elapsedMs = 0)
        => new()
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = message,
            ElapsedMs = elapsedMs
        };

    public bool Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public long ElapsedMs { get; init; }

    public IReadOnlyList<Detection> Detections { get; init; } = Array.Empty<Detection>();

    public IReadOnlyList<ClassProbability> Classes { get; init; } = Array.Empty<ClassProbability>();

    /// <summary>分割掩膜（与原图同尺寸的二值掩膜，运行时为 <c>Mat</c>），由调用方释放。</summary>
    public IImageFrame? Mask { get; init; }

    /// <summary>最高置信度，供判定节点取用。</summary>
    public double MaxScore => Detections.Count > 0
        ? Detections.Max(detection => detection.Confidence)
        : Classes.Count > 0
            ? Classes.Max(item => item.Probability)
            : 0d;

    public int Count => Detections.Count > 0 ? Detections.Count : Classes.Count;
}

/// <summary>推理引擎注册表：模型页与流程页据此列出可用引擎。</summary>
public interface IInferenceEngineRegistry
{
    IReadOnlyList<InferenceEngineDescriptor> Engines { get; }

    /// <summary>按模型创建引擎实例（每次调用返回独立实例，由调用方释放）。</summary>
    IInferenceEngine Create(string engineKey);
}

/// <summary>推理引擎描述。</summary>
public sealed record InferenceEngineDescriptor(
    string EngineKey,
    string DisplayName,
    IReadOnlyList<InferenceDevice> SupportedDevices,
    bool CudaAvailable,
    bool TensorRtAvailable);
