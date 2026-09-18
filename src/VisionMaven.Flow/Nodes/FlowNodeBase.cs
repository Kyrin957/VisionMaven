using System.Diagnostics;
using System.Reflection;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Flow.Nodes;

/// <summary>流程节点基类：统一计时、异常包装与元数据读取。</summary>
public abstract class FlowNodeBase : IFlowNode
{
    protected FlowNodeBase(FlowNodeEnvironment environment)
    {
        Environment = environment;

        var attribute = GetType().GetCustomAttribute<FlowNodeAttribute>();
        TypeKey = attribute?.TypeKey ?? GetType().Name;
        DisplayName = attribute?.DisplayName ?? TypeKey;
        Group = attribute?.Group ?? "通用";
        Order = attribute?.Order ?? 100;
    }

    protected FlowNodeEnvironment Environment { get; }

    public string TypeKey { get; }

    public string DisplayName { get; }

    public string Group { get; }

    public int Order { get; }

    public abstract IReadOnlyList<string> InputPorts { get; }

    public abstract IReadOnlyList<string> OutputPorts { get; }

    /// <summary>参数 Schema；默认取同名算子的 Schema。</summary>
    public virtual ParameterSchema Schema => Environment.Operators.Find(TypeKey)?.Schema ?? new ParameterSchema();

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            var outputs = await ExecuteCoreAsync(context, ct).ConfigureAwait(false);
            stopwatch.Stop();
            return NodeResult.Ok(context.NodeId, stopwatch.ElapsedMilliseconds, outputs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VisionMavenException ex)
        {
            return NodeResult.Fail(context.NodeId, ex.ErrorCode, ex.Message, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return NodeResult.Fail(
                context.NodeId,
                ErrorCodes.FlowNodeFailed,
                $"{DisplayName} 执行失败：{ex.Message}",
                stopwatch.ElapsedMilliseconds);
        }
    }

    protected abstract Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct);

    /// <summary>从输入端口解析图像帧。</summary>
    protected static IImageFrame RequireFrame(FlowContext context, string port)
        => context.ResolveInput(port) as IImageFrame
           ?? throw new FlowException(ErrorCodes.FlowNodeFailed, $"节点 {context.NodeId} 的输入 {port} 不是有效图像");

    /// <summary>从输入端口解析数值字段。</summary>
    protected static double ResolveNumber(FlowContext context, string port, string field, double fallback)
    {
        var value = context.ResolveInput(port);
        return TryReadField(value, field, out var number) ? number : fallback;
    }

    /// <summary>从任意输出对象中读取数值字段。</summary>
    protected static bool TryReadField(object? value, string field, out double number)
    {
        number = 0d;
        if (value is null)
        {
            return false;
        }

        switch (value)
        {
            case double typed:
                number = typed;
                return true;

            case float typed:
                number = typed;
                return true;

            case int typed:
                number = typed;
                return true;

            case long typed:
                number = typed;
                return true;

            case bool typed:
                number = typed ? 1d : 0d;
                return true;

            case InferenceResult inference:
                number = field switch
                {
                    "" or "value" or "score" or "maxScore" => inference.MaxScore,
                    "count" => inference.Count,
                    _ => 0d
                };
                return true;

            case IReadOnlyDictionary<string, object?> map:
                if (!string.IsNullOrWhiteSpace(field)
                    && map.TryGetValue(field, out var mapped)
                    && TryReadField(mapped, string.Empty, out var nested))
                {
                    number = nested;
                    return true;
                }

                return false;

            case string text:
                return double.TryParse(
                    text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out number);

            default:
                return false;
        }
    }

    /// <summary>把帧登记到执行期资源，执行结束后由引擎统一释放。</summary>
    protected static IImageFrame Track(FlowContext context, IImageFrame frame)
        => context.Track(frame);
}
