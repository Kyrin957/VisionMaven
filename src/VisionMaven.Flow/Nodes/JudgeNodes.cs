using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;
using VisionMaven.Flow.Expressions;

namespace VisionMaven.Flow.Nodes;

/// <summary>判定节点基类：写入流程结论与分数。</summary>
public abstract class JudgeNodeBase : FlowNodeBase
{
    protected JudgeNodeBase(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "result", "value" };

    protected static Dictionary<string, object?> Complete(
        FlowContext context,
        bool ok,
        double value)
    {
        context.Result = ok ? InspectionResult.Ok : InspectionResult.Ng;
        context.Score = value;

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["result"] = context.Result,
            ["value"] = value,
            ["ok"] = ok
        };
    }
}

/// <summary>阈值判定。</summary>
[FlowNode("judge.threshold", "阈值判定", Order = 100, Group = "判定")]
public sealed class JudgeThresholdNode : JudgeNodeBase
{
    public JudgeThresholdNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "input" };

    public override ParameterSchema Schema { get; } = new()
    {
        new TextParameter("field", "字段", "maxScore"),
        new EnumParameter("op", "比较", new[] { ">=", ">", "<=", "<", "==", "!=" }, ">="),
        new NumberParameter("value", "阈值", -1e9d, 1e9d, 0.5d)
    };

    protected override Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct)
    {
        var field = ParameterReader.GetString(context.Parameters, "field", "value");
        var op = ParameterReader.GetString(context.Parameters, "op", ">=");
        var threshold = ParameterReader.GetDouble(context.Parameters, "value", 0.5d);
        var value = ResolveNumber(context, "input", field, 0d);

        var ok = op switch
        {
            ">=" => value >= threshold,
            ">" => value > threshold,
            "<=" => value <= threshold,
            "<" => value < threshold,
            "==" => Math.Abs(value - threshold) < 1e-9,
            "!=" => Math.Abs(value - threshold) >= 1e-9,
            _ => throw new ConfigurationException(ErrorCodes.ConfigParseFailed, $"不支持的比较符：{op}")
        };

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(Complete(context, ok, value));
    }
}

/// <summary>数量判定。</summary>
[FlowNode("judge.count", "数量判定", Order = 101, Group = "判定")]
public sealed class JudgeCountNode : JudgeNodeBase
{
    public JudgeCountNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "input" };

    public override ParameterSchema Schema { get; } = new()
    {
        new TextParameter("field", "字段", "count"),
        new IntegerParameter("min", "下限", 0, 1000000, 1),
        new IntegerParameter("max", "上限", 0, 1000000, 1000000)
    };

    protected override Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct)
    {
        var field = ParameterReader.GetString(context.Parameters, "field", "count");
        var min = ParameterReader.GetDouble(context.Parameters, "min", 1d);
        var max = ParameterReader.GetDouble(context.Parameters, "max", 1_000_000d);
        var value = ResolveNumber(context, "input", field, 0d);

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(
            Complete(context, value >= min && value <= max, value));
    }
}

/// <summary>组合规则判定：对流程变量求值表达式。</summary>
[FlowNode("judge.rule", "组合规则", Order = 102, Group = "判定")]
public sealed class JudgeRuleNode : JudgeNodeBase
{
    public JudgeRuleNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = Array.Empty<string>();

    public override ParameterSchema Schema { get; } = new()
    {
        new TextParameter("expression", "表达式", "N1.count >= 1") { Required = true }
    };

    protected override Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct)
    {
        var expression = ParameterReader.GetString(context.Parameters, "expression", string.Empty);
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "组合规则缺少 expression");
        }

        var ok = ExpressionEvaluator.EvaluateBoolean(expression, context.GetVariable);
        var outputs = Complete(context, ok, ok ? 1d : 0d);
        outputs["expression"] = expression;

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(outputs);
    }
}

/// <summary>表达式脚本节点：对受限表达式求值并输出结果。</summary>
[FlowNode("script.expression", "表达式脚本", Order = 103, Group = "脚本")]
public sealed class ScriptExpressionNode : FlowNodeBase
{
    public ScriptExpressionNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = Array.Empty<string>();

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "value" };

    public override ParameterSchema Schema { get; } = new()
    {
        new TextParameter("expression", "表达式", "1") { Required = true },
        new TextParameter("outputName", "输出名", "value")
    };

    protected override Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct)
    {
        var expression = ParameterReader.GetString(context.Parameters, "expression", string.Empty);
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "脚本节点缺少 expression");
        }

        var value = ExpressionEvaluator.Evaluate(expression, context.GetVariable);
        var outputName = ParameterReader.GetString(context.Parameters, "outputName", "value");

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = ExpressionEvaluator.ToNumber(value),
                [outputName] = value
            });
    }
}
