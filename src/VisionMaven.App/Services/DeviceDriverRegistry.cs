using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.App.Services;

/// <summary>
/// 驱动注册表：汇集进程内注册的驱动工厂，并扫描 <c>plugins/</c> 约定目录中的外置插件。
/// SDK 缺失（如未安装海康 MVS）只记告警，不阻断启动。
/// </summary>
public sealed class DeviceDriverRegistry : IDeviceDriverRegistry
{
    private readonly List<IDeviceDriverProvider> _providers = new();
    private readonly Func<Type, object> _resolver;
    private readonly ILogger<DeviceDriverRegistry> _logger;
    private readonly string _pluginsDirectory;
    private readonly object _sync = new();
    private readonly List<DriverDescriptor> _descriptors = new();

    public DeviceDriverRegistry(
        IEnumerable<IDeviceDriverProvider> providers,
        Func<Type, object> resolver,
        ILogPathProvider paths,
        ILogger<DeviceDriverRegistry> logger)
    {
        _resolver = resolver;
        _logger = logger;
        _pluginsDirectory = paths.PluginsDirectory;

        foreach (var provider in providers)
        {
            _providers.Add(provider);
        }

        Rebuild();
    }

    public IReadOnlyList<DriverDescriptor> Drivers
    {
        get
        {
            lock (_sync)
            {
                return _descriptors.ToArray();
            }
        }
    }

    public DriverDescriptor? Find(string driverKey)
        => Drivers.FirstOrDefault(descriptor =>
            string.Equals(descriptor.DriverKey, driverKey, StringComparison.OrdinalIgnoreCase));

    public IDeviceDriver Create(IDeviceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var provider = _providers.FirstOrDefault(candidate =>
            string.Equals(candidate.DriverKey, context.DriverKey, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            throw new DeviceException(
                ErrorCodes.ConfigDriverMissing,
                $"设备 {context.DeviceId} 的驱动未注册：{context.DriverKey}");
        }

        if (!provider.IsAvailable)
        {
            throw new DeviceException(
                ErrorCodes.DeviceSdkMissing,
                $"驱动 {context.DriverKey} 不可用：{provider.UnavailableReason}");
        }

        return provider.Create(context);
    }

    /// <summary>按驱动标识创建实例（供设备发现等无上下文场景使用）。</summary>
    public IDeviceDriver CreateByKey(string driverKey, IDeviceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var provider = _providers.FirstOrDefault(candidate =>
            string.Equals(candidate.DriverKey, driverKey, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            throw new DeviceException(ErrorCodes.ConfigDriverMissing, $"驱动未注册：{driverKey}");
        }

        return provider.Create(context);
    }

    /// <summary>按约定目录重新扫描驱动插件，返回新增数量。</summary>
    public int Rescan()
    {
        var before = _providers.Count;
        LoadPlugins();
        Rebuild();
        return _providers.Count - before;
    }

    private void Rebuild()
    {
        var descriptors = _providers
            .GroupBy(provider => provider.DriverKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(provider => new DriverDescriptor(
                provider.DriverKey,
                provider.DisplayName,
                provider.Kind,
                provider.SdkHint,
                provider.Schema,
                provider.IsAvailable,
                provider.UnavailableReason))
            .OrderBy(descriptor => descriptor.Kind)
            .ThenBy(descriptor => descriptor.DisplayName, StringComparer.CurrentCulture)
            .ToList();

        lock (_sync)
        {
            _descriptors.Clear();
            _descriptors.AddRange(descriptors);
        }

        foreach (var descriptor in descriptors.Where(descriptor => !descriptor.IsAvailable))
        {
            _logger.LogWarning(
                "驱动 {DriverKey}（{DisplayName}）不可用：{Reason}",
                descriptor.DriverKey,
                descriptor.DisplayName,
                descriptor.UnavailableReason);
        }

        _logger.LogInformation("驱动注册完成，共 {Count} 个可用驱动", descriptors.Count(descriptor => descriptor.IsAvailable));
    }

    private void LoadPlugins()
    {
        if (!Directory.Exists(_pluginsDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(_pluginsDirectory, "*.dll"))
        {
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(file));
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface)
                    {
                        continue;
                    }

                    if (type.GetCustomAttribute<VisionDriverAttribute>() is null)
                    {
                        continue;
                    }

                    if (type.GetConstructor(Type.EmptyTypes) is null)
                    {
                        _logger.LogWarning(
                            "插件驱动 {Type} 缺少无参构造函数，已跳过；请改为在宿主中注册工厂",
                            type.FullName);
                        continue;
                    }

                    if (Activator.CreateInstance(type) is not IDeviceDriverProvider provider)
                    {
                        continue;
                    }

                    if (_providers.Any(existing => string.Equals(
                            existing.DriverKey,
                            provider.DriverKey,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    _providers.Add(provider);
                    _logger.LogInformation("已加载插件驱动：{DriverKey}", provider.DriverKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "插件加载失败：{File}", file);
            }
        }
    }
}
