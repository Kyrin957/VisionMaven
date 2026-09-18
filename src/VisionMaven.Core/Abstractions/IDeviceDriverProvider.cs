using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>
/// 设备驱动工厂：驱动作者实现此接口并标注 <see cref="VisionDriverAttribute"/>，
/// 启动时按约定目录扫描注册；设备管理页据此动态渲染连接参数表单。
/// </summary>
public interface IDeviceDriverProvider
{
    string DriverKey { get; }

    DeviceKind Kind { get; }

    /// <summary>界面上显示的驱动名，如「海康威视 MVS」。</summary>
    string DisplayName { get; }

    /// <summary>厂商 SDK 依赖提示，缺失时用于生成诊断信息。</summary>
    string? SdkHint { get; }

    /// <summary>连接参数 Schema，设备管理页据此自动生成表单。</summary>
    ParameterSchema Schema { get; }

    /// <summary>运行环境是否具备该驱动所需的 SDK。缺失时只记录告警并不阻断启动。</summary>
    bool IsAvailable { get; }

    /// <summary>不可用时的原因描述。</summary>
    string? UnavailableReason { get; }

    IDeviceDriver Create(IDeviceContext context);
}

/// <summary>驱动注册表：由启动壳装配，供设备页与运行期解析驱动实例。</summary>
public interface IDeviceDriverRegistry
{
    IReadOnlyList<DriverDescriptor> Drivers { get; }

    DriverDescriptor? Find(string driverKey);

    /// <summary>创建驱动实例。</summary>
    IDeviceDriver Create(IDeviceContext context);

    /// <summary>按约定目录重新扫描驱动插件，返回新增数量。</summary>
    int Rescan();
}

/// <summary>驱动描述（注册表条目）。</summary>
public sealed record DriverDescriptor(
    string DriverKey,
    string DisplayName,
    DeviceKind Kind,
    string? SdkHint,
    ParameterSchema Schema,
    bool IsAvailable,
    string? UnavailableReason);
