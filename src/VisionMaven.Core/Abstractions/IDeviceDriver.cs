using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>设备驱动统一契约：相机 / PLC / 机器人 / MES / 光源控制器均实现此接口。</summary>
public interface IDeviceDriver : IAsyncDisposable
{
    /// <summary>驱动标识，与 <see cref="VisionDriverAttribute.DriverKey"/> 一致，工程配置借此定位驱动。</summary>
    string DriverKey { get; }

    string DeviceId { get; }

    DeviceKind Kind { get; }

    DeviceState State { get; }

    Task ConnectAsync(CancellationToken ct);

    Task DisconnectAsync();

    event EventHandler<DeviceStateChangedEventArgs> StateChanged;
}

/// <summary>驱动实例化上下文：设备配置被展平后的键值对。</summary>
public interface IDeviceContext
{
    /// <summary>驱动标识，与工程配置 <c>driverKey</c> 一致。</summary>
    string DriverKey { get; }

    string DeviceId { get; }

    string Name { get; }

    DeviceKind Kind { get; }

    /// <summary>连接参数，键为驱动声明的参数键（不区分大小写）。</summary>
    IReadOnlyDictionary<string, string?> Settings { get; }

    T? Get<T>(string key);

    string GetString(string key, string fallback);

    int GetInt(string key, int fallback);

    double GetDouble(string key, double fallback);

    bool GetBool(string key, bool fallback);

    TimeSpan GetTimeSpan(string key, TimeSpan fallback);
}

/// <summary>设备状态变化参数。</summary>
public sealed class DeviceStateChangedEventArgs : EventArgs
{
    public DeviceStateChangedEventArgs(string deviceId, DeviceState oldState, DeviceState newState, string? message = null)
    {
        DeviceId = deviceId;
        OldState = oldState;
        NewState = newState;
        Message = message;
    }

    public string DeviceId { get; }

    public DeviceState OldState { get; }

    public DeviceState NewState { get; }

    public string? Message { get; }
}

/// <summary>相机能力描述，供界面做参数合法性提示与取值约束。</summary>
public sealed record CameraCapabilities(
    string[] SupportedParameters,
    PixelFormat[] SupportedPixelFormats,
    TriggerMode[] SupportedTriggerModes,
    int MaxWidth,
    int MaxHeight);

/// <summary>帧到达参数。</summary>
public sealed class FrameArrivedEventArgs : EventArgs
{
    public FrameArrivedEventArgs(IImageFrame frame)
    {
        Frame = frame;
    }

    public IImageFrame Frame { get; }
}

/// <summary>MES 上报结果。</summary>
public sealed record MesResult(bool Success, string? BusinessCode, string? Message);

/// <summary>驱动可用性诊断条目。</summary>
public sealed record DriverDiagnostic(string DriverKey, string DisplayName, DeviceKind Kind, bool IsAvailable, string? SdkHint, string? Message);
