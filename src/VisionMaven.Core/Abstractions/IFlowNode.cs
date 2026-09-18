using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>流程节点：TypeKey 对应工程配置中的节点类型，新增算子只需新增实现 + 标注，无需改引擎。</summary>
public interface IFlowNode
{
    string TypeKey { get; }

    IReadOnlyList<string> InputPorts { get; }

    IReadOnlyList<string> OutputPorts { get; }

    Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct);
}

/// <summary>流程节点工厂：为一次执行创建节点实例并注入运行时依赖。</summary>
public interface IFlowNodeFactory
{
    /// <summary>按类型创建节点实例。</summary>
    IFlowNode Create(string typeKey);

    IReadOnlyList<FlowNodeDescriptor> Descriptors { get; }

    FlowNodeDescriptor? Find(string typeKey);
}

/// <summary>流程节点描述，供流程页节点选择器与参数表单使用。</summary>
public sealed record FlowNodeDescriptor(
    string TypeKey,
    string DisplayName,
    string Group,
    int Order,
    IReadOnlyList<string> InputPorts,
    IReadOnlyList<string> OutputPorts,
    ParameterSchema Schema);

/// <summary>流程上下文：节点间数据传递与实参。</summary>
public sealed class FlowContext
{
    public string StationId { get; init; } = string.Empty;

    public string FlowId { get; init; } = string.Empty;

    /// <summary>数据流变量，键为 <c>{nodeId}.{port}</c>。</summary>
    public IDictionary<string, object?> Variables { get; init; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前节点的参数。</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前节点的输入端口引用（端口名 → <c>{nodeId}.{port}</c>）。</summary>
    public IReadOnlyDictionary<string, string> Inputs { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前节点 Id。</summary>
    public string NodeId { get; init; } = string.Empty;

    public CancellationToken StationToken { get; init; }

    /// <summary>逐节点耗时记录。</summary>
    public IList<NodeTiming> Timings { get; init; } = new List<NodeTiming>();

    /// <summary>工位级参数。</summary>
    public IReadOnlyDictionary<string, string> StationParameters { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>本次执行创建的资源（图像帧等），执行结束后由引擎统一释放。</summary>
    public IList<IDisposable> OwnedResources { get; init; } = new List<IDisposable>();

    /// <summary>登记一个由本次执行持有的可释放资源，并在执行结束时释放。</summary>
    public T Track<T>(T resource)
        where T : IDisposable
    {
        OwnedResources.Add(resource);
        return resource;
    }

    /// <summary>读取上游输出。引用格式 <c>N3.result</c>；也允许省略节点号直接给端口名。</summary>
    public object? ResolveInput(string portName)
    {
        if (!Inputs.TryGetValue(portName, out var reference) || string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (Variables.TryGetValue(reference, out var value))
        {
            return value;
        }

        // 裸端口名：在变量表中查找任意以 ".{reference}" 结尾的键
        var suffix = "." + reference;
        foreach (var pair in Variables)
        {
            if (pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>写入当前节点的输出端口。</summary>
    public void SetOutput(string portName, object? value)
        => Variables[BuildKey(NodeId, portName)] = value;

    public T? GetVariable<T>(string reference)
        => Variables.TryGetValue(reference, out var value) && value is T typed ? typed : default;

    public object? GetVariable(string reference)
        => Variables.TryGetValue(reference, out var value) ? value : null;

    /// <summary>流程最终结论，由 <c>judge.*</c> 节点写入；缺省视为 NG。</summary>
    public InspectionResult Result
    {
        get => Variables.TryGetValue(ResultKey, out var value) && value is InspectionResult result ? result : InspectionResult.Unknown;
        set => Variables[ResultKey] = value;
    }

    /// <summary>流程结论的最高置信度/分数，供落盘与统计使用。</summary>
    public double Score
    {
        get => Variables.TryGetValue(ScoreKey, out var value) && value is double score ? score : 0d;
        set => Variables[ScoreKey] = value;
    }

    public static string BuildKey(string nodeId, string portName) => nodeId + "." + portName;

    public const string ResultKey = "__result";

    public const string ScoreKey = "__score";

    public const string AcquiredFrameKey = "__frame";
}

/// <summary>节点执行结果。</summary>
public sealed record NodeResult
{
    public static NodeResult Ok(string nodeId, long elapsedMs, IReadOnlyDictionary<string, object?>? outputs = null)
        => new()
        {
            NodeId = nodeId,
            Success = true,
            ElapsedMs = elapsedMs,
            Outputs = outputs ?? new Dictionary<string, object?>()
        };

    public static NodeResult Fail(string nodeId, string errorCode, string message, long elapsedMs = 0)
        => new()
        {
            NodeId = nodeId,
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = message,
            ElapsedMs = elapsedMs
        };

    public static NodeResult Skip(string nodeId)
        => new()
        {
            NodeId = nodeId,
            Success = true,
            Skipped = true
        };

    public string NodeId { get; init; } = string.Empty;

    public bool Success { get; init; }

    public bool Skipped { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public long ElapsedMs { get; init; }

    public IReadOnlyDictionary<string, object?> Outputs { get; init; } = new Dictionary<string, object?>();
}

/// <summary>流程拓扑校验结果。</summary>
public sealed record FlowValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public static FlowValidationResult Valid { get; } = new(true, Array.Empty<string>(), Array.Empty<string>());
}
