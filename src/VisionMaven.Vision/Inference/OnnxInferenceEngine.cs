using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;
using VisionMaven.Vision.Imaging;

namespace VisionMaven.Vision.Inference;

/// <summary>
/// ONNX Runtime 推理引擎。会话独占且以 <see cref="SemaphoreSlim"/> 串行化，
/// EP 不可用时自动降级到 CPU 并输出告警。
/// </summary>
public sealed class OnnxInferenceEngine : IInferenceEngine
{
    private readonly ILogger<OnnxInferenceEngine> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private InferenceSession? _session;
    private ModelDescriptor? _descriptor;
    private string _inputName = string.Empty;
    private InputElementKind _inputElementKind = InputElementKind.Float32;
    private bool _disposed;

    public OnnxInferenceEngine(ILogger<OnnxInferenceEngine> logger)
    {
        _logger = logger;
    }

    public string EngineKey => "onnxruntime";

    public InferenceDevice ActiveDevice { get; private set; } = InferenceDevice.CPU;

    public bool IsLoaded => _session is not null;

    public async Task LoadAsync(ModelDescriptor descriptor, InferenceOptions options, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!File.Exists(descriptor.ModelFilePath))
        {
            throw new InferenceException(
                ErrorCodes.InferenceModelLoadFailed,
                $"模型文件不存在：{descriptor.ModelFilePath}");
        }

        await UnloadAsync().ConfigureAwait(false);

        var requested = options.Device ?? descriptor.Device;
        using var sessionOptions = CreateSessionOptions(requested, out var activeDevice);

        var session = await Task.Run(
            () => CreateSession(descriptor.ModelFilePath, sessionOptions),
            ct).ConfigureAwait(false);

        var input = session.InputMetadata.FirstOrDefault();
        if (input.Key is null)
        {
            session.Dispose();
            throw new InferenceException(ErrorCodes.InferenceInputMismatch, "模型未声明输入张量");
        }

        _inputName = input.Key;
        _inputElementKind = MapElementKind(input.Value.ElementType);
        _session = session;
        _descriptor = descriptor;
        ActiveDevice = activeDevice;

        _logger.LogInformation(
            "模型已加载：{ModelId} 输入={Input} 尺寸={Width}x{Height} EP={Device}",
            descriptor.ModelId,
            _inputName,
            descriptor.Input.Width,
            descriptor.Input.Height,
            activeDevice);

        if (descriptor.Warmup)
        {
            await WarmupAsync(descriptor, ct).ConfigureAwait(false);
        }
    }

    private static InferenceSession CreateSession(string modelPath, SessionOptions options)
    {
        try
        {
            return new InferenceSession(modelPath, options);
        }
        catch (Exception ex)
        {
            throw new InferenceException(
                ErrorCodes.InferenceModelLoadFailed,
                $"模型加载失败：{ex.Message}",
                ex);
        }
    }

    private SessionOptions CreateSessionOptions(InferenceDevice requested, out InferenceDevice active)
    {
        var options = CreateCpuOptions();
        active = InferenceDevice.CPU;

        if (requested == InferenceDevice.CPU)
        {
            return options;
        }

#if VISIONMAVEN_GPU
        try
        {
            if (requested == InferenceDevice.TensorRT)
            {
                options.AppendExecutionProvider_TensorRT(0);
                active = InferenceDevice.TensorRT;
                return options;
            }

            options.AppendExecutionProvider_CUDA(0);
            active = InferenceDevice.CUDA;
            return options;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EP {Device} 初始化失败，降级到 CPU", requested);
            options.Dispose();
            active = InferenceDevice.CPU;
            return CreateCpuOptions();
        }
#else
        _logger.LogWarning(
            "当前构建仅包含 CPU 执行提供程序，请求的 {Device} 已降级到 CPU；以 -p:VisionMavenGpu=true 重新构建可启用 CUDA / TensorRT",
            requested);
        return options;
#endif
    }

    private static SessionOptions CreateCpuOptions() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        InterOpNumThreads = 0,
        IntraOpNumThreads = Environment.ProcessorCount
    };

    private async Task WarmupAsync(ModelDescriptor descriptor, CancellationToken ct)
    {
        using var canvas = new Mat(
            descriptor.Input.Height,
            descriptor.Input.Width,
            MatType.CV_8UC1,
            new Scalar(0));

        using var frame = new MatImageFrame("warmup", canvas);
        var result = await InferAsync(frame, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.LogWarning("预热推理失败：{Message}", result.ErrorMessage);
        }
        else
        {
            _logger.LogInformation("预热推理完成，耗时 {ElapsedMs} ms", result.ElapsedMs);
        }
    }

    public async Task<InferenceResult> InferAsync(IImageFrame frame, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var session = _session;
        var descriptor = _descriptor;
        if (session is null || descriptor is null)
        {
            return InferenceResult.Failure(ErrorCodes.InferenceModelLoadFailed, "模型未加载");
        }

        var stopwatch = Stopwatch.StartNew();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var source = FrameAccess.RequireMat(frame).Clone();
            var preprocess = TensorPreprocessor.Create(source, descriptor.Input);

            var inputs = BuildInputs(preprocess);
            using var outputs = session.Run(inputs);

            var first = outputs.FirstOrDefault();
            if (first is null)
            {
                return InferenceResult.Failure(
                    ErrorCodes.InferenceRunFailed,
                    "推理未返回输出",
                    stopwatch.ElapsedMilliseconds);
            }

            var tensor = first.AsTensor<float>();
            stopwatch.Stop();

            return descriptor.Task switch
            {
                ModelTask.Classification => new InferenceResult
                {
                    Success = true,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Classes = OutputPostProcessor.DecodeClassification(tensor, descriptor)
                },
                ModelTask.Segmentation => new InferenceResult
                {
                    Success = true,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Detections = OutputPostProcessor.DecodeDetections(tensor, preprocess, descriptor),
                    Mask = new MatImageFrame(
                        frame.CameraId,
                        OutputPostProcessor.CreateMask(tensor, preprocess, source))
                },
                _ => new InferenceResult
                {
                    Success = true,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Detections = OutputPostProcessor.DecodeDetections(tensor, preprocess, descriptor)
                }
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InferenceException ex)
        {
            return InferenceResult.Failure(ex.ErrorCode, ex.Message, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推理执行失败");
            return InferenceResult.Failure(
                ErrorCodes.InferenceRunFailed,
                $"推理执行失败：{ex.Message}",
                stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyCollection<NamedOnnxValue> BuildInputs(PreprocessResult preprocess)
    {
        if (_inputElementKind != InputElementKind.Float16)
        {
            return new[] { NamedOnnxValue.CreateFromTensor(_inputName, preprocess.Tensor) };
        }

        var raw = preprocess.Tensor;
        var dims = raw.Dimensions.ToArray();
        var half = new DenseTensor<Float16>(dims);

        if (dims.Length == 4)
        {
            for (var n = 0; n < dims[0]; n++)
            {
                for (var c = 0; c < dims[1]; c++)
                {
                    for (var y = 0; y < dims[2]; y++)
                    {
                        for (var x = 0; x < dims[3]; x++)
                        {
                            half[n, c, y, x] = (Float16)raw[n, c, y, x];
                        }
                    }
                }
            }
        }

        return new[] { NamedOnnxValue.CreateFromTensor(_inputName, half) };
    }

    private static InputElementKind MapElementKind(Type type)
    {
        if (type == typeof(Float16))
        {
            return InputElementKind.Float16;
        }

        return type == typeof(double) ? InputElementKind.Float64 : InputElementKind.Float32;
    }

    public Task UnloadAsync()
    {
        var session = _session;
        _session = null;
        _descriptor = null;
        session?.Dispose();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await UnloadAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    /// <summary>模型输入张量的元素类型。</summary>
    private enum InputElementKind
    {
        Float32,
        Float16,
        Float64
    }
}
