using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;
using VisionMaven.Flow.Expressions;

namespace VisionMaven.Flow.Engine;

/// <summary>
/// 流程引擎：按拓扑执行有向无环图。默认串行推进，<see cref="FlowNodeMode.Parallel"/> 分支并发执行，
/// 汇合点在全部前驱完成后执行一次。
/// </summary>
public sealed class FlowEngine : IFlowEngine
{
    private readonly IFlowNodeFactory _nodes;
    private readonly IOverlayRenderer _overlay;
    private readonly ILogger<FlowEngine> _logger;

    public FlowEngine(IFlowNodeFactory nodes, IOverlayRenderer overlay, ILogger<FlowEngine> logger)
    {
        _nodes = nodes;
        _overlay = overlay;
        _logger = logger;
    }

    public FlowValidationResult Validate(FlowDefinitionConfig flow)
    {
        var errors = FlowTopology.Validate(flow).ToList();
        var warnings = new List<string>();

        foreach (var node in flow.Nodes)
        {
            if (_nodes.Find(node.TypeKey) is null)
            {
                errors.Add($"流程 {flow.FlowId} 节点 {node.NodeId} 的 typeKey 未注册：{node.TypeKey}");
            }
        }

        return new FlowValidationResult(errors.Count == 0, errors, warnings);
    }

    public async Task<FlowRunOutcome> ExecuteAsync(
        FlowDefinitionConfig flow,
        string stationId,
        IReadOnlyDictionary<string, string> stationParameters,
        IReadOnlyDictionary<string, string>? parameterOverrides,
        object? initialFrame,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(flow);

        var stopwatch = Stopwatch.StartNew();
        var state = new ExecutionState();

        if (initialFrame is IImageFrame frame)
        {
            state.Variables[FlowContext.AcquiredFrameKey] = frame;
        }

        var nodeMap = flow.Nodes.ToDictionary(node => node.NodeId, StringComparer.OrdinalIgnoreCase);
        var predecessors = FlowTopology.BuildPredecessors(flow);

        try
        {
            while (state.ResolvedCount < nodeMap.Count && !state.Aborted)
            {
                var ready = nodeMap.Values
                    .Where(node => !state.IsResolved(node.NodeId)
                                   && predecessors[node.NodeId].All(state.IsResolved))
                    .ToList();

                if (ready.Count == 0)
                {
                    state.Abort(
                        ErrorCodes.FlowTopologyInvalid,
                        $"流程 {flow.FlowId} 存在无法解析的节点依赖（可能含环路）");
                    break;
                }

                foreach (var node in ready.Where(node =>
                             predecessors[node.NodeId].Count > 0
                             && predecessors[node.NodeId].All(state.IsSkipped)))
                {
                    state.MarkSkipped(node.NodeId);
                    state.AddTiming(new NodeTiming(
                        node.NodeId,
                        node.Name,
                        node.TypeKey,
                        NodeRunStatus.Skipped,
                        0,
                        "前驱分支未命中"));
                }

                var runnable = ready.Where(node => !state.IsResolved(node.NodeId)).ToList();
                if (runnable.Count == 0)
                {
                    continue;
                }

                foreach (var node in runnable
                             .Where(node => node.Mode != FlowNodeMode.Parallel)
                             .OrderBy(node => node.Order))
                {
                    var outcome = await RunNodeAsync(
                            flow,
                            node,
                            stationId,
                            stationParameters,
                            parameterOverrides,
                            state,
                            ct)
                        .ConfigureAwait(false);

                    if (!outcome.Success && flow.FailureStrategy == FailureStrategy.Abort)
                    {
                        state.Abort(outcome.Code, outcome.Message);
                        break;
                    }
                }

                if (state.Aborted)
                {
                    break;
                }

                var parallel = runnable.Where(node => node.Mode == FlowNodeMode.Parallel).ToList();
                if (parallel.Count > 0)
                {
                    var results = await Task
                        .WhenAll(parallel.Select(node => RunNodeAsync(
                            flow,
                            node,
                            stationId,
                            stationParameters,
                            parameterOverrides,
                            state,
                            ct)))
                        .ConfigureAwait(false);

                    var failure = results.FirstOrDefault(result => !result.Success);
                    if (!failure.Success && flow.FailureStrategy == FailureStrategy.Abort)
                    {
                        state.Abort(failure.Code, failure.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            DisposeOwned(state);
            return new FlowRunOutcome(
                flow.FlowId,
                stationId,
                InspectionResult.Unknown,
                0d,
                stopwatch.Elapsed.TotalMilliseconds,
                state.SnapshotTimings(),
                Array.Empty<Detection>(),
                ErrorCodes.FlowCancelled,
                "流程执行被取消",
                null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            DisposeOwned(state);
            _logger.LogError(ex, "流程执行异常：{FlowId}", flow.FlowId);
            return new FlowRunOutcome(
                flow.FlowId,
                stationId,
                InspectionResult.Unknown,
                0d,
                stopwatch.Elapsed.TotalMilliseconds,
                state.SnapshotTimings(),
                Array.Empty<Detection>(),
                ErrorCodes.FlowNodeFailed,
                ex.Message,
                null);
        }

        stopwatch.Stop();

        var resultValue = state.Variables.TryGetValue(FlowContext.ResultKey, out var rawResult)
                          && rawResult is InspectionResult result
            ? result
            : InspectionResult.Unknown;

        if (resultValue == InspectionResult.Unknown)
        {
            resultValue = InspectionResult.Ng;
        }

        var score = state.Variables.TryGetValue(FlowContext.ScoreKey, out var rawScore)
                    && rawScore is double scoreValue
            ? scoreValue
            : 0d;

        var detections = CollectDetections(state.Variables);
        var overlayImage = RenderOverlay(state.LastImage, detections, resultValue, score);
        var timings = state.SnapshotTimings();
        var abortCode = state.AbortCode;
        var abortMessage = state.AbortMessage;
        DisposeOwned(state);

        return new FlowRunOutcome(
            flow.FlowId,
            stationId,
            resultValue,
            score,
            stopwatch.Elapsed.TotalMilliseconds,
            timings,
            detections,
            abortCode,
            abortMessage,
            overlayImage);
    }

    private async Task<(bool Success, string? Code, string? Message)> RunNodeAsync(
        FlowDefinitionConfig flow,
        FlowNodeConfig node,
        string stationId,
        IReadOnlyDictionary<string, string> stationParameters,
        IReadOnlyDictionary<string, string>? parameterOverrides,
        ExecutionState state,
        CancellationToken ct)
    {
        state.MarkResolved(node.NodeId);

        if (!node.Enabled)
        {
            state.MarkSkipped(node.NodeId);
            state.AddTiming(new NodeTiming(node.NodeId, node.Name, node.TypeKey, NodeRunStatus.Skipped, 0, "节点已禁用"));
            foreach (var next in node.Next)
            {
                state.MarkSkipped(next);
            }

            return (true, null, null);
        }

        IFlowNode instance;
        try
        {
            instance = _nodes.Create(node.TypeKey);
        }
        catch (Exception ex)
        {
            state.AddTiming(new NodeTiming(node.NodeId, node.Name, node.TypeKey, NodeRunStatus.Failed, 0, ex.Message));
            return (false, ErrorCodes.FlowTypeKeyUnknown, ex.Message);
        }

        var parameters = BuildParameters(node, parameterOverrides);
        var context = new FlowContext
        {
            StationId = stationId,
            FlowId = flow.FlowId,
            NodeId = node.NodeId,
            Variables = state.Variables,
            Parameters = parameters,
            Inputs = node.Inputs,
            StationToken = ct,
            Timings = state.Timings,
            StationParameters = stationParameters,
            OwnedResources = state.Owned
        };

        var attempts = flow.FailureStrategy == FailureStrategy.Retry
            ? Math.Max(1, ParameterReader.GetInt(node.Parameters, "retryCount", 2))
            : 1;

        NodeResult? nodeResult = null;
        var timeout = ParameterReader.GetInt(node.Parameters, "timeoutMs", 0);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var linked = timeout > 0 ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
            linked?.CancelAfter(timeout);

            try
            {
                nodeResult = await instance.ExecuteAsync(context, linked?.Token ?? ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout > 0)
            {
                nodeResult = NodeResult.Fail(
                    node.NodeId,
                    ErrorCodes.FlowNodeTimeout,
                    $"节点执行超时（{timeout}ms）");
            }

            if (nodeResult.Success)
            {
                break;
            }
        }

        nodeResult ??= NodeResult.Fail(node.NodeId, ErrorCodes.FlowNodeFailed, "节点未返回结果");

        foreach (var pair in nodeResult.Outputs)
        {
            state.Variables[$"{node.NodeId}.{pair.Key}"] = pair.Value;
            if (pair.Value is IImageFrame && pair.Key.Equals("image", StringComparison.OrdinalIgnoreCase))
            {
                state.LastImage = pair.Value;
            }
        }

        state.AddTiming(new NodeTiming(
            node.NodeId,
            node.Name,
            node.TypeKey,
            nodeResult.Success ? NodeRunStatus.Success : NodeRunStatus.Failed,
            nodeResult.ElapsedMs,
            nodeResult.ErrorMessage));

        if (!nodeResult.Success && flow.FailureStrategy == FailureStrategy.Continue)
        {
            _logger.LogWarning(
                "节点失败但按 Continue 策略继续：流程={FlowId} 节点={NodeId} 原因={Message}",
                flow.FlowId,
                node.NodeId,
                nodeResult.ErrorMessage);
        }

        if (node.Mode == FlowNodeMode.Conditional && nodeResult.Success)
        {
            var condition = ParameterReader.GetString(node.Parameters, "condition", string.Empty);
            var matched = string.IsNullOrWhiteSpace(condition)
                          || ExpressionEvaluator.EvaluateBoolean(
                              condition,
                              key => state.Variables.TryGetValue(key, out var value) ? value : null);

            if (!matched)
            {
                foreach (var next in node.Next)
                {
                    state.MarkSkipped(next);
                }
            }
        }

        return (nodeResult.Success, nodeResult.ErrorCode, nodeResult.ErrorMessage);
    }

    private static Dictionary<string, string> BuildParameters(
        FlowNodeConfig node,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var parameters = new Dictionary<string, string>(node.Parameters, StringComparer.OrdinalIgnoreCase);
        if (overrides is null || overrides.Count == 0)
        {
            return parameters;
        }

        var prefix = node.NodeId + ".";
        foreach (var pair in overrides)
        {
            if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                parameters[pair.Key[prefix.Length..]] = pair.Value;
            }
            else if (!pair.Key.Contains('.'))
            {
                parameters[pair.Key] = pair.Value;
            }
        }

        return parameters;
    }

    private static List<Detection> CollectDetections(ConcurrentDictionary<string, object?> variables)
    {
        foreach (var pair in variables)
        {
            if (pair.Key.EndsWith(".detections", StringComparison.OrdinalIgnoreCase)
                && pair.Value is IReadOnlyList<Detection> detections
                && detections.Count > 0)
            {
                return detections.ToList();
            }
        }

        return new List<Detection>();
    }

    private object? RenderOverlay(
        object? image,
        IReadOnlyList<Detection> detections,
        InspectionResult result,
        double score)
    {
        if (image is not IImageFrame frame)
        {
            return null;
        }

        try
        {
            return _overlay.Render(frame.NativeImage, detections, result, score);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "结果叠加渲染失败");
            return null;
        }
    }

    private void DisposeOwned(ExecutionState state)
    {
        foreach (var resource in state.Owned)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "流程资源释放失败");
            }
        }

        state.Owned.Clear();
    }

    /// <summary>一次流程执行的共享状态；并行分支下所有可变集合均加锁保护。</summary>
    private sealed class ExecutionState
    {
        private readonly object _sync = new();

        public ConcurrentDictionary<string, object?> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<IDisposable> Owned { get; } = new();

        public List<NodeTiming> Timings { get; } = new();

        public HashSet<string> Resolved { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Skipped { get; } = new(StringComparer.OrdinalIgnoreCase);

        public object? LastImage { get; set; }

        public bool Aborted { get; private set; }

        public string? AbortCode { get; private set; }

        public string? AbortMessage { get; private set; }

        public int ResolvedCount
        {
            get
            {
                lock (_sync)
                {
                    return Resolved.Count;
                }
            }
        }

        public bool IsResolved(string nodeId)
        {
            lock (_sync)
            {
                return Resolved.Contains(nodeId);
            }
        }

        public bool IsSkipped(string nodeId)
        {
            lock (_sync)
            {
                return Skipped.Contains(nodeId);
            }
        }

        public void MarkResolved(string nodeId)
        {
            lock (_sync)
            {
                Resolved.Add(nodeId);
            }
        }

        public void MarkSkipped(string nodeId)
        {
            lock (_sync)
            {
                Skipped.Add(nodeId);
            }
        }

        public void AddTiming(NodeTiming timing)
        {
            lock (_sync)
            {
                Timings.Add(timing);
            }
        }

        public IReadOnlyList<NodeTiming> SnapshotTimings()
        {
            lock (_sync)
            {
                return Timings.ToArray();
            }
        }

        public void Abort(string? code, string? message)
        {
            Aborted = true;
            AbortCode = code;
            AbortMessage = message;
        }
    }
}
