using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;
using VisionMaven.Core.Shell;

namespace VisionMaven.Flow.Nodes;

/// <summary>ONNX 推理节点：按 modelId 取模型描述，节点级参数可覆盖置信度 / NMS。</summary>
[FlowNode("inference.onnx", "ONNX 推理", Order = 45, Group = "推理")]
public sealed class InferenceNode : FlowNodeBase
{
    public InferenceNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "detections", "result", "count", "mask" };

    public override ParameterSchema Schema { get; } = new()
    {
        new ReferenceParameter("modelId", "模型", ParameterValueKind.Text, "Model") { Required = true },
        new NumberParameter("confidence", "置信度阈值", 0d, 1d, 0.35d),
        new NumberParameter("nms", "NMS 阈值", 0d, 1d, 0.45d),
        new IntegerParameter("maxDetections", "最大检测数", 1, 1000, 100)
    };

    protected override async Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct)
    {
        var descriptor = ResolveDescriptor(context);
        var frame = RequireFrame(context, "image");

        var engine = await Environment.InferenceSessions.GetAsync(descriptor, ct).ConfigureAwait(false);
        var result = await engine.InferAsync(frame, ct).ConfigureAwait(false);

        if (!result.Success)
        {
            throw new InferenceException(
                result.ErrorCode ?? ErrorCodes.InferenceRunFailed,
                result.ErrorMessage ?? "推理执行失败");
        }

        var outputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["detections"] = result.Detections,
            ["result"] = result,
            ["count"] = result.Count,
            ["score"] = result.MaxScore
        };

        if (result.Mask is not null)
        {
            Track(context, result.Mask);
            outputs["mask"] = result.Mask;
        }

        return outputs;
    }

    private ModelDescriptor ResolveDescriptor(FlowContext context)
    {
        var modelId = ParameterReader.GetString(context.Parameters, "modelId", string.Empty);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ConfigurationException(ErrorCodes.ConfigDanglingReference, "推理节点未配置 modelId");
        }

        var project = Environment.Project.Current
            ?? throw new ConfigurationException(ErrorCodes.ConfigNotFound, "尚未打开工程");

        var entry = project.ModelLibrary.FirstOrDefault(
            item => string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ConfigurationException(ErrorCodes.ConfigDanglingReference, $"模型不存在：{modelId}");

        var projectRoot = Environment.Storage.GetSubDirectory(project.ProjectId, ProjectFolder.Root);
        var modelPath = Path.IsPathRooted(entry.Path)
            ? entry.Path
            : Path.Combine(projectRoot, entry.Path);

        if (!File.Exists(modelPath))
        {
            throw new InferenceException(ErrorCodes.InferenceModelLoadFailed, $"模型文件不存在：{entry.Path}");
        }

        return new ModelDescriptor(
            entry.ModelId,
            entry.Name,
            entry.Task,
            entry.Version,
            modelPath,
            LoadLabels(projectRoot, entry.Labels),
            entry.Input,
            ParameterReader.GetDouble(context.Parameters, "confidence", entry.Confidence),
            ParameterReader.GetDouble(context.Parameters, "nms", entry.Nms),
            ParameterReader.GetInt(context.Parameters, "maxDetections", entry.MaxDetections),
            entry.Precision,
            entry.Device,
            entry.Sessions,
            entry.Warmup);
    }

    private static IReadOnlyList<string> LoadLabels(string projectRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Array.Empty<string>();
        }

        var path = Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(projectRoot, relativePath);
        return File.Exists(path)
            ? File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToArray()
            : Array.Empty<string>();
    }
}
