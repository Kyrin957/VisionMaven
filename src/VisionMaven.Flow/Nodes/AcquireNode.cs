using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Flow.Nodes;

/// <summary>采集节点：从绑定相机取一帧，作为流程起点的图像源。</summary>
[FlowNode("acquire", "采集", Order = 10, Group = "输入")]
public sealed class AcquireNode : FlowNodeBase
{
    public AcquireNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = Array.Empty<string>();

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "image" };

    public override ParameterSchema Schema { get; } = new()
    {
        new ReferenceParameter("cameraId", "相机", ParameterValueKind.DeviceId, "Camera") { Required = true },
        new IntegerParameter("timeoutMs", "取流超时", 100, 60000, 2000) { Unit = "ms" }
    };

    protected override async Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct)
    {
        // 参数设置页 / 试运行时由引擎注入离线帧，优先于相机采集。
        var provided = context.GetVariable(FlowContext.AcquiredFrameKey) as IImageFrame;
        if (provided is not null)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["image"] = provided,
                ["cameraId"] = provided.CameraId,
                ["sequenceNo"] = provided.SequenceNo
            };
        }

        var cameraId = ParameterReader.GetString(context.Parameters, "cameraId", string.Empty);
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            throw new FlowException(ErrorCodes.ConfigDanglingReference, "采集节点未配置 cameraId");
        }

        var camera = Environment.Devices.GetCamera(cameraId)
            ?? throw new DeviceException(ErrorCodes.DeviceDisconnected, $"相机不可用：{cameraId}");

        if (camera.State != DeviceState.Ready)
        {
            await camera.ConnectAsync(ct).ConfigureAwait(false);
        }

        if (!camera.IsGrabbing)
        {
            await camera.StartGrabbingAsync(ct).ConfigureAwait(false);
        }

        var timeout = ParameterReader.GetInt(context.Parameters, "timeoutMs", 2000);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, context.StationToken);
        linked.CancelAfter(timeout);

        IImageFrame frame;
        try
        {
            frame = await camera.GetFrameAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !context.StationToken.IsCancellationRequested)
        {
            throw new DeviceException(ErrorCodes.DeviceCaptureTimeout, $"相机 {cameraId} 取流超时（{timeout}ms）");
        }

        Track(context, frame);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["image"] = frame,
            ["cameraId"] = cameraId,
            ["sequenceNo"] = frame.SequenceNo
        };
    }
}
