namespace VisionMaven.Core.Abstractions;

/// <summary>相机：屏蔽厂商差异，硬触发与软触发统一从 <see cref="GetFrameAsync"/> / <see cref="FrameArrived"/> 出口。</summary>
public interface ICamera : IDeviceDriver
{
    CameraCapabilities Capabilities { get; }

    bool IsGrabbing { get; }

    Task StartGrabbingAsync(CancellationToken ct);

    Task StopGrabbingAsync();

    /// <summary>取一帧；无新帧时按 <paramref name="ct"/> 取消。</summary>
    Task<IImageFrame> GetFrameAsync(CancellationToken ct);

    Task SetParameterAsync(string key, double value, CancellationToken ct);

    Task<double> GetParameterAsync(string key, CancellationToken ct);

    /// <summary>应用工程配置中的取流参数。</summary>
    Task ApplyStreamSettingsAsync(CameraStreamSettings settings, CancellationToken ct);

    event EventHandler<FrameArrivedEventArgs> FrameArrived;
}

/// <summary>相机取流参数（与工程配置解耦的运行时视图）。</summary>
public sealed record CameraStreamSettings
{
    public double ExposureUs { get; init; } = 8000;

    public double Gain { get; init; } = 1d;

    public Domain.TriggerMode TriggerMode { get; init; } = Domain.TriggerMode.Software;

    public string TriggerSource { get; init; } = "Line0";

    public Domain.PixelFormat PixelFormat { get; init; } = Domain.PixelFormat.Mono8;

    public Domain.IntRect Roi { get; init; } = Domain.IntRect.Empty;

    public double FrameRateLimit { get; init; } = 30d;
}

/// <summary>相机参数键常量，与工程配置 <c>stream</c> 字段一一对应。</summary>
public static class CameraParameterKeys
{
    public const string ExposureUs = "exposureUs";
    public const string Gain = "gain";
    public const string TriggerMode = "triggerMode";
    public const string TriggerSource = "triggerSource";
    public const string PixelFormat = "pixelFormat";
    public const string FrameRateLimit = "frameRateLimit";
    public const string RoiX = "roi.x";
    public const string RoiY = "roi.y";
    public const string RoiWidth = "roi.width";
    public const string RoiHeight = "roi.height";
}
