using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>设备运行态视图，供设备管理页展示。</summary>
public sealed record DeviceRuntimeInfo(
    string DeviceId,
    string Name,
    DeviceKind Kind,
    string DriverKey,
    DeviceState State,
    string? UnavailableReason);

/// <summary>设备会话：按工程配置实例化驱动、连接并缓存，供流程节点与界面共用。</summary>
public interface IDeviceSession : IAsyncDisposable
{
    bool IsOpen { get; }

    IReadOnlyList<DeviceRuntimeInfo> Devices { get; }

    Task OpenAsync(ProjectConfig project, CancellationToken ct);

    Task CloseAsync();

    ICamera? GetCamera(string deviceId);

    ILightController? GetLight(string deviceId);

    IPlcDriver? GetPlc(string deviceId);

    IRobotDriver? GetRobot(string deviceId);

    IMesClient? GetMes(string deviceId);

    IDeviceDriver? GetDriver(string deviceId);

    Task<DeviceState> ConnectAsync(string deviceId, CancellationToken ct);

    Task DisconnectAsync(string deviceId);

    event EventHandler<DeviceStateChangedEventArgs>? DeviceStateChanged;
}

/// <summary>
/// 推理会话池：按「模型 + 设备 + 精度」缓存已加载会话，工位启动时按需加载，停止时统一释放。
/// <see cref="IInferenceEngine"/> 内部串行化调用以保证线程安全。
/// </summary>
public interface IInferenceSessionPool : IAsyncDisposable
{
    Task<IInferenceEngine> GetAsync(ModelDescriptor descriptor, CancellationToken ct);

    Task ReleaseAllAsync();

    int LoadedCount { get; }
}
