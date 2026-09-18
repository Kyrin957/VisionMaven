using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Flow.Nodes;

/// <summary>图像算子类节点的统一基类：把执行委托给 <see cref="IOperatorRegistry"/> 中的算子实现。</summary>
public abstract class OperatorNodeBase : FlowNodeBase
{
    protected OperatorNodeBase(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    protected override async Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct)
    {
        var input = RequireFrame(context, "image");
        var op = Environment.Operators.Create(TypeKey);
        var result = await op.ExecuteAsync(input, context.Parameters, ct).ConfigureAwait(false);

        if (!result.Success)
        {
            throw new FlowException(
                result.ErrorCode ?? ErrorCodes.FlowNodeFailed,
                result.ErrorMessage ?? $"{DisplayName} 执行失败");
        }

        var outputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in result.Outputs)
        {
            outputs[pair.Key] = pair.Value;
        }

        if (result.Image is not null)
        {
            Track(context, result.Image);
            outputs["image"] = result.Image;
        }

        return outputs;
    }
}

/// <summary>灰度化节点。</summary>
[FlowNode("preprocess.gray", "灰度化", Order = 20, Group = "预处理")]
public sealed class GrayNode : OperatorNodeBase
{
    public GrayNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image" };
}

/// <summary>滤波节点。</summary>
[FlowNode("preprocess.filter", "滤波", Order = 21, Group = "预处理")]
public sealed class FilterNode : OperatorNodeBase
{
    public FilterNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image" };
}

/// <summary>形态学节点。</summary>
[FlowNode("preprocess.morphology", "形态学", Order = 22, Group = "预处理")]
public sealed class MorphologyNode : OperatorNodeBase
{
    public MorphologyNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image" };
}

/// <summary>ROI 裁剪节点。</summary>
[FlowNode("preprocess.roi", "区域裁剪", Order = 23, Group = "预处理")]
public sealed class RoiNode : OperatorNodeBase
{
    public RoiNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image", "roi" };
}

/// <summary>阈值分割节点。</summary>
[FlowNode("threshold.binary", "阈值分割", Order = 30, Group = "分割")]
public sealed class ThresholdNode : OperatorNodeBase
{
    public ThresholdNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image" };
}

/// <summary>Blob 分析节点。</summary>
[FlowNode("blob.find", "Blob 分析", Order = 40, Group = "特征")]
public sealed class BlobFindNode : OperatorNodeBase
{
    public BlobFindNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image", "regions", "count" };
}

/// <summary>模板匹配节点。</summary>
[FlowNode("match.template", "模板匹配", Order = 50, Group = "匹配")]
public sealed class TemplateMatchNode : OperatorNodeBase
{
    public TemplateMatchNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image", "matches", "score" };
}

/// <summary>找线节点。</summary>
[FlowNode("find.line", "找线", Order = 60, Group = "定位")]
public sealed class FindLineNode : OperatorNodeBase
{
    public FindLineNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image", "line", "angle" };
}

/// <summary>找圆节点。</summary>
[FlowNode("find.circle", "找圆", Order = 61, Group = "定位")]
public sealed class FindCircleNode : OperatorNodeBase
{
    public FindCircleNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image", "circle", "radius" };
}

/// <summary>距离测量节点。</summary>
[FlowNode("measure.distance", "距离测量", Order = 70, Group = "测量")]
public sealed class MeasureDistanceNode : OperatorNodeBase
{
    public MeasureDistanceNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image", "value" };
}

/// <summary>坐标映射节点。</summary>
[FlowNode("calibration.transform", "坐标映射", Order = 80, Group = "标定")]
public sealed class CalibrationTransformNode : OperatorNodeBase
{
    public CalibrationTransformNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image", "points", "value" };
}
