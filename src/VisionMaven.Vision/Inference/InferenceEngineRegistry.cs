using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Vision.Inference;

/// <summary>推理引擎注册表：当前仅 ONNX Runtime，新增引擎只需实现 <see cref="IInferenceEngine"/> 并在此登记。</summary>
public sealed class InferenceEngineRegistry : IInferenceEngineRegistry
{
    public const string OnnxEngineKey = "onnxruntime";

    private readonly Func<Type, object> _resolver;

    public InferenceEngineRegistry(Func<Type, object> resolver)
    {
        _resolver = resolver;

        var cuda = IsNativeAvailable("onnxruntime_providers_cuda")
                   || IsNativeAvailable("onnxruntime_providers_tensorrt");
        CudaAvailable = cuda;
        TensorRtAvailable = cuda;

        Engines = new[]
        {
            new InferenceEngineDescriptor(
                OnnxEngineKey,
                "ONNX Runtime",
                cuda
                    ? new[] { InferenceDevice.CPU, InferenceDevice.CUDA, InferenceDevice.TensorRT }
                    : new[] { InferenceDevice.CPU },
                cuda,
                cuda)
        };
    }

    public IReadOnlyList<InferenceEngineDescriptor> Engines { get; }

    public bool CudaAvailable { get; }

    public bool TensorRtAvailable { get; }

    public IInferenceEngine Create(string engineKey)
    {
        if (!string.Equals(engineKey, OnnxEngineKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"未注册的推理引擎：{engineKey}");
        }

        return (IInferenceEngine)_resolver(typeof(OnnxInferenceEngine));
    }

    /// <summary>探测 native 提供程序是否随发布输出。</summary>
    private static bool IsNativeAvailable(string moduleName)
    {
        var root = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(root, moduleName + ".dll"),
            Path.Combine(root, "runtimes", "win-x64", "native", moduleName + ".dll")
        };

        return candidates.Any(File.Exists);
    }
}
