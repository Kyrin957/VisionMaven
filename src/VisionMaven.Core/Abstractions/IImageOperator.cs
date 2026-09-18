using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>图像算子：以算子参数驱动，同一个算子可被多个流程节点复用。</summary>
public interface IImageOperator
{
    string TypeKey { get; }

    ParameterSchema Schema { get; }

    Task<OperatorResult> ExecuteAsync(IImageFrame input, IReadOnlyDictionary<string, string> parameters, CancellationToken ct);
}

/// <summary>算子执行结果。</summary>
public sealed record OperatorResult
{
    public static OperatorResult Ok(long elapsedMs, IReadOnlyDictionary<string, object?>? outputs = null, IImageFrame? image = null)
        => new()
        {
            Success = true,
            ElapsedMs = elapsedMs,
            Outputs = outputs ?? new Dictionary<string, object?>(),
            Image = image
        };

    public static OperatorResult Fail(string errorCode, string message, long elapsedMs = 0)
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

    public IReadOnlyDictionary<string, object?> Outputs { get; init; } = new Dictionary<string, object?>();

    /// <summary>算子产生的图像输出（若有），由调用方负责释放。</summary>
    public IImageFrame? Image { get; init; }

    public T? Get<T>(string port)
        => Outputs.TryGetValue(port, out var value) && value is T typed ? typed : default;
}

/// <summary>算子注册表：算法页与流程页据此列出可用算子并生成表单。</summary>
public interface IOperatorRegistry
{
    IReadOnlyList<OperatorDescriptor> Operators { get; }

    OperatorDescriptor? Find(string typeKey);

    IImageOperator Create(string typeKey);
}

/// <summary>算子描述。</summary>
public sealed record OperatorDescriptor(
    string TypeKey,
    string DisplayName,
    OperatorCategory Category,
    int Order,
    ParameterSchema Schema);

/// <summary>算子节点执行结果的线程内缓存，用于「逐节点耗时表」。</summary>
public sealed record NodeTiming(string NodeId, string NodeName, string TypeKey, NodeRunStatus Status, double ElapsedMs, string? ErrorMessage);
