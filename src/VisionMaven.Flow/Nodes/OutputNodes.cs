using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;
using VisionMaven.Core.Shell;
using VisionMaven.Vision.Imaging;

namespace VisionMaven.Flow.Nodes;

/// <summary>结果输出到 PLC。</summary>
[FlowNode("output.plc", "结果输出到 PLC", Order = 200, Group = "输出")]
public sealed class PlcOutputNode : FlowNodeBase
{
    public PlcOutputNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "result" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "ok", "written" };

    public override ParameterSchema Schema { get; } = new()
    {
        new ReferenceParameter("deviceId", "PLC", ParameterValueKind.DeviceId, "Plc") { Required = true },
        new TextParameter("okAddress", "OK 地址", "M200"),
        new TextParameter("ngAddress", "NG 地址", "M201"),
        new IntegerParameter("pulseMs", "脉冲保持", 0, 5000, 0) { Unit = "ms" }
    };

    protected override async Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct)
    {
        var deviceId = ParameterReader.GetString(context.Parameters, "deviceId", string.Empty);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ConfigurationException(ErrorCodes.ConfigDanglingReference, "结果输出节点未配置 deviceId");
        }

        var plc = Environment.Devices.GetPlc(deviceId)
            ?? throw new DeviceException(ErrorCodes.DeviceDisconnected, $"PLC 不可用：{deviceId}");

        var ok = context.Result == InspectionResult.Ok;
        var okAddress = ParameterReader.GetString(context.Parameters, "okAddress", "M200");
        var ngAddress = ParameterReader.GetString(context.Parameters, "ngAddress", "M201");
        var target = ok ? okAddress : ngAddress;

        await plc.WriteAsync(target, true, ct).ConfigureAwait(false);

        var pulseMs = ParameterReader.GetInt(context.Parameters, "pulseMs", 0);
        if (pulseMs > 0)
        {
            await Task.Delay(pulseMs, ct).ConfigureAwait(false);
            await plc.WriteAsync(target, false, ct).ConfigureAwait(false);
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = ok,
            ["written"] = target
        };
    }
}

/// <summary>结果上报 MES。</summary>
[FlowNode("output.mes", "结果上报 MES", Order = 201, Group = "输出")]
public sealed class MesOutputNode : FlowNodeBase
{
    public MesOutputNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "result" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "success" };

    public override ParameterSchema Schema { get; } = new()
    {
        new ReferenceParameter("deviceId", "MES", ParameterValueKind.DeviceId, "Mes") { Required = true },
        new TextParameter("template", "报文模板", string.Empty)
    };

    protected override async Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct)
    {
        var deviceId = ParameterReader.GetString(context.Parameters, "deviceId", string.Empty);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ConfigurationException(ErrorCodes.ConfigDanglingReference, "MES 上报节点未配置 deviceId");
        }

        var mes = Environment.Devices.GetMes(deviceId)
            ?? throw new DeviceException(ErrorCodes.DeviceDisconnected, $"MES 客户端不可用：{deviceId}");

        var record = new InspectionRecord
        {
            ProjectId = Environment.Project.Current?.ProjectId ?? string.Empty,
            StationId = context.StationId,
            Result = context.Result,
            Score = context.Score,
            InspectedAt = DateTimeOffset.Now,
            FlowId = context.FlowId
        };

        var result = await mes.UploadInspectionAsync(record, ct).ConfigureAwait(false);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["success"] = result.Success,
            ["businessCode"] = result.BusinessCode,
            ["message"] = result.Message
        };
    }
}

/// <summary>结果落盘：按策略保存结果图片。</summary>
[FlowNode("output.save", "结果落盘", Order = 202, Group = "输出")]
public sealed class SaveOutputNode : FlowNodeBase
{
    public SaveOutputNode(FlowNodeEnvironment environment)
        : base(environment)
    {
    }

    public override IReadOnlyList<string> InputPorts { get; } = new[] { "image", "result" };

    public override IReadOnlyList<string> OutputPorts { get; } = new[] { "path" };

    public override ParameterSchema Schema { get; } = new()
    {
        new EnumParameter("policy", "保存策略", new[] { "All", "NgOnly", "None" }, "NgOnly"),
        new EnumParameter("format", "格式", new[] { "jpg", "png" }, "jpg")
    };

    protected override Task<IReadOnlyDictionary<string, object?>> ExecuteCoreAsync(
        FlowContext context,
        CancellationToken ct)
    {
        var policy = ParameterReader.GetEnum(context.Parameters, "policy", ResultImagePolicy.NgOnly);
        var project = Environment.Project.Current;

        if (policy == ResultImagePolicy.None || project is null)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["path"] = null });
        }

        if (policy == ResultImagePolicy.NgOnly && context.Result != InspectionResult.Ng)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["path"] = null });
        }

        var frame = context.ResolveInput("image") as IImageFrame;
        if (frame is null)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["path"] = null });
        }

        var format = ParameterReader.GetString(context.Parameters, "format", "jpg");
        var directory = Path.Combine(
            Environment.Storage.GetSubDirectory(project.ProjectId, ProjectFolder.Images),
            DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));

        Directory.CreateDirectory(directory);

        var fileName = $"{context.StationId}_{DateTime.Now:HHmmss_fff}.{format}";
        var path = Path.Combine(directory, fileName);

        using var mat = FrameAccess.RequireMat(frame).Clone();
        ImageCodec.Write(mat, path);

        var relative = Path.GetRelativePath(
            Environment.Storage.GetSubDirectory(project.ProjectId, ProjectFolder.Root),
            path);

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["path"] = relative
            });
    }
}
