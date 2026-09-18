using Microsoft.Extensions.Logging;

namespace VisionMaven.Devices.GenTL;

/// <summary>已发现的 GenTL Producer。</summary>
public sealed record GenTlProducer(string FilePath, string Vendor, string FileName);

/// <summary>
/// GenTL Producer（<c>.cti</c>）定位：按约定目录扫描，供设备发现与诊断使用。
/// 默认扫描程序目录、<c>GENICAM_GENTL64_PATH</c> 环境变量以及各厂商 Runtime 的常见安装位置。
/// </summary>
public static class GenTlProducerLocator
{
    /// <summary>常见厂商 Runtime 的 Producer 目录。</summary>
    private static readonly string[] WellKnownDirectories =
    {
        @"C:\Program Files\Basler\pylon\Runtime\x64",
        @"C:\Program Files\Common Files\GenICam\bin\x64",
        @"C:\Program Files\Daheng Imaging\GalaxySDK\GenTL",
        @"C:\Program Files (x86)\Daheng Imaging\GenTL",
        @"C:\Program Files\The Imaging Source Europe GmbH\IC Imaging Control 4\GenTL",
        @"C:\Program Files\MVS\Runtime\Win64_x64"
    };

    public static IReadOnlyList<GenTlProducer> Discover(string? extraDirectory = null)
    {
        var directories = new List<string>
        {
            AppContext.BaseDirectory
        };

        if (!string.IsNullOrWhiteSpace(extraDirectory))
        {
            directories.Add(extraDirectory);
        }

        var environment = Environment.GetEnvironmentVariable("GENICAM_GENTL64_PATH")
                          ?? Environment.GetEnvironmentVariable("GENICAM_GENTL32_PATH");
        if (!string.IsNullOrWhiteSpace(environment))
        {
            directories.AddRange(environment.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        directories.AddRange(WellKnownDirectories);

        var producers = new List<GenTlProducer>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.cti", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!seen.Add(file))
                {
                    continue;
                }

                producers.Add(new GenTlProducer(file, GuessVendor(file), Path.GetFileName(file)));
            }
        }

        return producers;
    }

    private static string GuessVendor(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
        if (name.Contains("basler") || name.Contains("pylon"))
        {
            return "Basler";
        }

        if (name.Contains("gx") || name.Contains("galaxy") || name.Contains("daheng"))
        {
            return "大恒图像";
        }

        if (name.Contains("mvs") || name.Contains("hik"))
        {
            return "海康威视";
        }

        if (name.Contains("mindvision") || name.Contains("mv"))
        {
            return "迈德威视";
        }

        if (name.Contains("tiscamera") || name.Contains("ic4"))
        {
            return "The Imaging Source";
        }

        return "未知厂商";
    }
}

/// <summary>
/// GenTL 相机驱动。
///
/// 说明：GenTL 的 <c>TLGetDeviceIDs</c> / <c>GCGetPortURL</c> 等入口使用 varargs 调用约定，
/// 且设备访问必须经 GenApi 节点树完成；在未绑定厂商 SDK 头文件的情况下，本驱动只做
/// Producer 探测与诊断上报，实际取流请使用厂商原生驱动（如海康 MVS）或等待 GenApi 绑定完成。
/// </summary>
[Core.Abstractions.VisionDriver(
    "gentl",
    Core.Domain.DeviceKind.Camera,
    DisplayName = "GenICam / GenTL 通用相机",
    SdkHint = "需安装厂商 Runtime 并提供 .cti Producer 文件")]
public sealed class GenTlCamera : Core.Drivers.DeviceDriverBase, Core.Abstractions.ICamera
{
    private readonly Microsoft.Extensions.Logging.ILogger<GenTlCamera> _logger;

    public GenTlCamera(
        Core.Abstractions.IDeviceContext context,
        Microsoft.Extensions.Logging.ILogger<GenTlCamera> logger)
        : base(context, Core.Domain.DeviceKind.Camera)
    {
        _logger = logger;
    }

    public override string DriverKey => "gentl";

    public Core.Abstractions.CameraCapabilities Capabilities { get; } = new(
        Array.Empty<string>(),
        new[] { Core.Domain.PixelFormat.Mono8, Core.Domain.PixelFormat.Bgr8 },
        new[] { Core.Domain.TriggerMode.Off, Core.Domain.TriggerMode.Software, Core.Domain.TriggerMode.Hardware },
        0,
        0);

    public bool IsGrabbing => false;

    public event EventHandler<Core.Abstractions.FrameArrivedEventArgs>? FrameArrived;

    protected override Task OnConnectAsync(CancellationToken ct)
    {
        var producers = GenTlProducerLocator.Discover(Context.GetString("producerDirectory", string.Empty));
        if (producers.Count == 0)
        {
            throw new Core.Exceptions.DeviceException(
                Core.Exceptions.ErrorCodes.DeviceSdkMissing,
                "未找到任何 GenTL Producer（.cti）");
        }

        var selected = producers.FirstOrDefault(producer => string.Equals(
                           producer.FileName,
                           Context.GetString("producerFile", string.Empty),
                           StringComparison.OrdinalIgnoreCase))
                       ?? producers[0];

        _logger.LogInformation(
            "已定位 GenTL Producer：{Producer}（共 {Count} 个）",
            selected.FilePath,
            producers.Count);

        throw new Core.Exceptions.DeviceException(
            Core.Exceptions.ErrorCodes.DeviceSdkMissing,
            $"GenTL 传输层未与 {selected.Vendor} 的 GenApi 绑定，请改用厂商原生驱动");
    }

    public Task StartGrabbingAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopGrabbingAsync() => Task.CompletedTask;

    public Task<Core.Abstractions.IImageFrame> GetFrameAsync(CancellationToken ct)
        => throw new Core.Exceptions.DeviceException(
            Core.Exceptions.ErrorCodes.DeviceSdkMissing,
            "GenTL 取流未实现");

    public Task SetParameterAsync(string key, double value, CancellationToken ct) => Task.CompletedTask;

    public Task<double> GetParameterAsync(string key, CancellationToken ct) => Task.FromResult(0d);

    public Task ApplyStreamSettingsAsync(Core.Abstractions.CameraStreamSettings settings, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>空实现以避免未使用的告警。</summary>
    private void RaiseFrame(Core.Abstractions.IImageFrame frame)
        => FrameArrived?.Invoke(this, new Core.Abstractions.FrameArrivedEventArgs(frame));
}

/// <summary>GenTL 驱动注册。</summary>
public sealed class GenTlCameraProvider : Core.Abstractions.IDeviceDriverProvider
{
    private readonly Microsoft.Extensions.Logging.ILoggerFactory _loggerFactory;

    public GenTlCameraProvider(Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "gentl";

    public Core.Domain.DeviceKind Kind => Core.Domain.DeviceKind.Camera;

    public string DisplayName => "GenICam / GenTL 通用相机";

    public string? SdkHint => "需安装厂商 Runtime 并提供 .cti Producer 文件";

    private static IReadOnlyList<GenTlProducer> Producers { get; } = GenTlProducerLocator.Discover();

    public bool IsAvailable => false;

    public string? UnavailableReason => Producers.Count == 0
        ? "未找到任何 GenTL Producer（.cti）"
        : $"已发现 {Producers.Count} 个 Producer，但传输层未与 GenApi 绑定";

    public Core.Abstractions.ParameterSchema Schema { get; } = new()
    {
        new Core.Abstractions.TextParameter("producerFile", "Producer 文件名", string.Empty),
        new Core.Abstractions.DirectoryParameter("producerDirectory", "Producer 目录", string.Empty),
        new Core.Abstractions.TextParameter("deviceId", "设备标识", string.Empty),
        new Core.Abstractions.NumberParameter("exposureUs", "曝光时间", 1d, 1_000_000d, 8000d) { Unit = "us" },
        new Core.Abstractions.NumberParameter("gain", "增益", 0d, 64d, 1d),
        new Core.Abstractions.EnumParameter("triggerMode", "触发模式", new[] { "Off", "Software", "Hardware" }, "Software")
    };

    public Core.Abstractions.IDeviceDriver Create(Core.Abstractions.IDeviceContext context)
        => new GenTlCamera(context, _loggerFactory.CreateLogger<GenTlCamera>());
}
