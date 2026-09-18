using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;

namespace VisionMaven.App.Services;

/// <summary>设备类型注册表实现，内置相机 / 光源控制器 / PLC / 机器人 / MES 五类。</summary>
public sealed class DeviceTypeRegistry : IDeviceTypeRegistry
{
    private readonly List<DeviceTypeDescriptor> _items = new();
    private readonly object _sync = new();

    public DeviceTypeRegistry()
    {
        Register(new DeviceTypeDescriptor(DeviceKind.Camera, "相机", "Camera", 10));
        Register(new DeviceTypeDescriptor(DeviceKind.LightController, "光源控制器", "LightbulbOn", 20));
        Register(new DeviceTypeDescriptor(DeviceKind.Plc, "PLC", "Memory", 30));
        Register(new DeviceTypeDescriptor(DeviceKind.Robot, "机器人", "Robot", 40));
        Register(new DeviceTypeDescriptor(DeviceKind.Mes, "MES", "Server", 50));
        Register(new DeviceTypeDescriptor(DeviceKind.Other, "其他设备", "Cog", 90));
    }

    public event EventHandler? Changed;

    public void Register(DeviceTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        lock (_sync)
        {
            var existing = _items.FindIndex(item => item.Kind == descriptor.Kind);
            if (existing >= 0)
            {
                _items[existing] = descriptor;
            }
            else
            {
                _items.Add(descriptor);
            }

            _items.Sort((left, right) => left.Order.CompareTo(right.Order));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<DeviceTypeDescriptor> GetSubTabs()
    {
        lock (_sync)
        {
            return _items.ToArray();
        }
    }
}
