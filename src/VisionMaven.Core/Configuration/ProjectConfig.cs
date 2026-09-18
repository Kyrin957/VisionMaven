using System.Text.Json.Nodes;
using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Configuration;

/// <summary>工程主配置，对应 <c>projects/{ProjectId}/project.json</c>。</summary>
public sealed class ProjectConfig
{
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>受支持的 schema 版本。</summary>
    public static IReadOnlyList<string> SupportedSchemaVersions { get; } = new[] { "1.0" };

    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string ProjectId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public List<StationConfig> Stations { get; set; } = new();

    public List<CameraConfig> Cameras { get; set; } = new();

    public List<LightControllerConfig> LightControllers { get; set; } = new();

    public List<CommLinkConfig> CommLinks { get; set; } = new();

    public List<ModelEntryConfig> ModelLibrary { get; set; } = new();

    public List<FlowDefinitionConfig> Flows { get; set; } = new();

    public ProjectParameterConfig Parameters { get; set; } = new();

    /// <summary>所有设备节点（相机 / 光源控制器 / 通讯链路）的统一视图，供设备与通讯页使用。</summary>
    public IEnumerable<DeviceNodeConfig> AllDevices()
    {
        foreach (var camera in Cameras)
        {
            yield return camera;
        }

        foreach (var light in LightControllers)
        {
            yield return light;
        }

        foreach (var link in CommLinks)
        {
            yield return link;
        }
    }
}

/// <summary>可被设备管理页统一呈现的设备配置基类。</summary>
public abstract class DeviceNodeConfig
{
    public string DeviceId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DriverKey { get; set; } = string.Empty;

    public DeviceKind Kind { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>驱动连接参数，键为驱动声明的参数键。</summary>
    public Dictionary<string, JsonNode?> Connection { get; set; } = new();

    /// <summary>把连接参数展平为字符串字典，供驱动构造。</summary>
    public Dictionary<string, string?> ToSettingMap()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Connection)
        {
            map[pair.Key] = NodeToText(pair.Value);
        }

        return map;
    }

    private static string? NodeToText(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return node is JsonValue value && value.TryGetValue(out string? text) ? text : node.ToJsonString().Trim('"');
    }
}

/// <summary>工位配置：并行调度的最小单位。</summary>
public sealed class StationConfig
{
    public string StationId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public List<string> CameraIds { get; set; } = new();

    public List<string> DeviceBindings { get; set; } = new();

    public string FlowId { get; set; } = string.Empty;

    public StationTriggerConfig Trigger { get; set; } = new();

    /// <summary>工位级参数，键为参数名，值为字符串。</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>工位触发源配置。</summary>
public sealed class StationTriggerConfig
{
    public TriggerSource Source { get; set; } = TriggerSource.Software;

    public string DeviceId { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public TriggerEdge Edge { get; set; } = TriggerEdge.Rising;

    public int TimeoutMs { get; set; } = 2000;

    public int DebounceMs { get; set; } = 20;

    /// <summary>Timer 触发源的周期（毫秒）。</summary>
    public int IntervalMs { get; set; } = 1000;

    public OverrunPolicy OverrunPolicy { get; set; } = OverrunPolicy.DropNew;

    public int OverrunQueueDepth { get; set; } = 1;
}

/// <summary>相机配置。</summary>
public sealed class CameraConfig : DeviceNodeConfig
{
    public CameraStreamConfig Stream { get; set; } = new();

    public CameraCalibrationConfig Calibration { get; set; } = new();
}

/// <summary>相机取流参数。</summary>
public sealed class CameraStreamConfig
{
    public double ExposureUs { get; set; } = 8000;

    public double Gain { get; set; } = 1d;

    public TriggerMode TriggerMode { get; set; } = TriggerMode.Software;

    public string TriggerSource { get; set; } = "Line0";

    public PixelFormat PixelFormat { get; set; } = PixelFormat.Mono8;

    public IntRectConfig Roi { get; set; } = new();

    public double FrameRateLimit { get; set; } = 30d;
}

/// <summary>ROI 配置。空实现表示使用相机全幅。</summary>
public sealed class IntRectConfig
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public IntRect ToIntRect() => new(X, Y, Width, Height);
}

/// <summary>相机标定配置。</summary>
public sealed class CameraCalibrationConfig
{
    public CalibrationType Type { get; set; } = CalibrationType.None;

    public double MmPerPixel { get; set; } = 1d;

    public double[] Homography { get; set; } = new[] { 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d };

    public string File { get; set; } = string.Empty;
}

/// <summary>光源控制器配置。</summary>
public sealed class LightControllerConfig : DeviceNodeConfig
{
    public List<LightChannelConfig> Channels { get; set; } = new();

    public LightTriggerMode TriggerMode { get; set; } = LightTriggerMode.Strobe;

    /// <summary>可配置命令模板，多数厂商无需写代码即可接入。</summary>
    public Dictionary<string, string> Commands { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>光源通道配置。</summary>
public sealed class LightChannelConfig
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Brightness { get; set; } = 128;

    public bool Enabled { get; set; } = true;
}

/// <summary>通讯链路配置（PLC / 机器人 / MES）。</summary>
public sealed class CommLinkConfig : DeviceNodeConfig
{
    public int PollingMs { get; set; } = 100;

    public int HeartbeatMs { get; set; } = 1000;

    public int ReconnectMs { get; set; } = 3000;

    public List<PointConfig> Points { get; set; } = new();
}

/// <summary>地址表点位。</summary>
public sealed class PointConfig
{
    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public PointDataType DataType { get; set; } = PointDataType.Bool;

    public PointDirection Direction { get; set; } = PointDirection.Read;

    public double Scale { get; set; } = 1d;

    public double Offset { get; set; }
}

/// <summary>模型库条目。</summary>
public sealed class ModelEntryConfig
{
    public string ModelId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ModelTask Task { get; set; } = ModelTask.Detection;

    public string Version { get; set; } = "v1";

    public string Path { get; set; } = string.Empty;

    public string Labels { get; set; } = string.Empty;

    public ModelInputConfig Input { get; set; } = new();

    public double Confidence { get; set; } = 0.35;

    public double Nms { get; set; } = 0.45;

    public int MaxDetections { get; set; } = 100;

    public InferencePrecision Precision { get; set; } = InferencePrecision.FP32;

    public InferenceDevice Device { get; set; } = InferenceDevice.CPU;

    public int Sessions { get; set; } = 2;

    /// <summary>首次加载是否执行一次预热推理。</summary>
    public bool Warmup { get; set; } = true;

    public bool Enabled { get; set; } = true;
}

/// <summary>模型输入张量配置。</summary>
public sealed class ModelInputConfig
{
    public int Width { get; set; } = 640;

    public int Height { get; set; } = 640;

    public TensorLayout Layout { get; set; } = TensorLayout.NCHW;

    public NormalizeMode Normalize { get; set; } = NormalizeMode.Div255;
}

/// <summary>流程定义。</summary>
public sealed class FlowDefinitionConfig
{
    public string FlowId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public FailureStrategy FailureStrategy { get; set; } = FailureStrategy.Abort;

    public List<FlowNodeConfig> Nodes { get; set; } = new();
}

/// <summary>流程节点定义。</summary>
public sealed class FlowNodeConfig
{
    public string NodeId { get; set; } = string.Empty;

    public string TypeKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int Order { get; set; }

    public FlowNodeMode Mode { get; set; } = FlowNodeMode.Sequential;

    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>端口名 → 上游引用（形如 <c>N3.result</c>）。</summary>
    public Dictionary<string, string> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Next { get; set; } = new();
}

/// <summary>工程参数（全局 + 工位级）。</summary>
public sealed class ProjectParameterConfig
{
    public Dictionary<string, string> Global { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, string>> PerStation { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetGlobal(string key, string fallback)
        => Global.TryGetValue(key, out var value) ? value : fallback;

    public int GetGlobalInt(string key, int fallback)
        => Global.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    public double GetGlobalDouble(string key, double fallback)
        => Global.TryGetValue(key, out var value) && double.TryParse(value, out var parsed) ? parsed : fallback;

    public string GetStation(string stationId, string key, string fallback)
        => PerStation.TryGetValue(stationId, out var map) && map.TryGetValue(key, out var value) ? value : fallback;

    public int GetStationInt(string stationId, string key, int fallback)
        => PerStation.TryGetValue(stationId, out var map)
           && map.TryGetValue(key, out var value)
           && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
}

/// <summary>工程参数键常量。</summary>
public static class ParameterKeys
{
    public const string ResultImagePolicy = "ResultImagePolicy";
    public const string ImageRetentionDays = "ImageRetentionDays";
    public const string StatisticsIntervalSec = "StatisticsIntervalSec";
    public const string ConsecutiveNgAlarm = "ConsecutiveNgAlarm";
    public const string RecordRetentionDays = "RecordRetentionDays";
    public const string AlarmRetentionDays = "AlarmRetentionDays";
    public const string OverrunPolicy = "OverrunPolicy";
}
