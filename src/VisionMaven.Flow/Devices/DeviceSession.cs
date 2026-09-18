using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;

namespace VisionMaven.Flow.Devices;

/// <summary>设备会话实现。</summary>
public sealed class DeviceSession : IDeviceSession
{
    private readonly IDeviceDriverRegistry _registry;
    private readonly ILogger<DeviceSession> _logger;

    private readonly ConcurrentDictionary<string, DeviceEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public DeviceSession(IDeviceDriverRegistry registry, ILogger<DeviceSession> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public bool IsOpen { get; private set; }

    public event EventHandler<DeviceStateChangedEventArgs>? DeviceStateChanged;

    public IReadOnlyList<DeviceRuntimeInfo> Devices => _entries.Values
        .Select(entry => new DeviceRuntimeInfo(
            entry.Config.DeviceId,
            entry.Config.Name,
            entry.Config.Kind,
            entry.Config.DriverKey,
            entry.Driver?.State ?? DeviceState.Disconnected,
            entry.UnavailableReason))
        .OrderBy(info => info.Kind)
        .ThenBy(info => info.DeviceId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public async Task OpenAsync(ProjectConfig project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        await CloseAsync().ConfigureAwait(false);

        foreach (var config in project.AllDevices())
        {
            var entry = new DeviceEntry(config);
            _entries[config.DeviceId] = entry;

            if (!config.Enabled)
            {
                entry.UnavailableReason = "配置已禁用";
                continue;
            }

            var descriptor = _registry.Find(config.DriverKey);
            if (descriptor is null)
            {
                entry.UnavailableReason = $"驱动未注册：{config.DriverKey}";
                _logger.LogWarning("设备 {DeviceId} 的驱动未注册：{DriverKey}", config.DeviceId, config.DriverKey);
                continue;
            }

            if (!descriptor.IsAvailable)
            {
                entry.UnavailableReason = descriptor.UnavailableReason;
                _logger.LogWarning(
                    "设备 {DeviceId} 的驱动 {DriverKey} 不可用：{Reason}",
                    config.DeviceId,
                    config.DriverKey,
                    descriptor.UnavailableReason);
                continue;
            }

            try
            {
                entry.Driver = _registry.Create(new DeviceContext(config));
                entry.Driver.StateChanged += OnDriverStateChanged;
                await entry.Driver.ConnectAsync(ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "设备已连接：{DeviceId} 驱动={DriverKey} 状态={State}",
                    config.DeviceId,
                    config.DriverKey,
                    entry.Driver.State);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                entry.UnavailableReason = ex.Message;
                _logger.LogError(ex, "设备连接失败：{DeviceId}", config.DeviceId);
            }
        }

        IsOpen = true;
    }

    public async Task CloseAsync()
    {
        foreach (var entry in _entries.Values)
        {
            await DisposeEntryAsync(entry).ConfigureAwait(false);
        }

        _entries.Clear();
        IsOpen = false;
    }

    private async Task DisposeEntryAsync(DeviceEntry entry)
    {
        var driver = entry.Driver;
        if (driver is null)
        {
            return;
        }

        entry.Driver = null;
        driver.StateChanged -= OnDriverStateChanged;
        try
        {
            await driver.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设备断开失败：{DeviceId}", entry.Config.DeviceId);
        }

        await driver.DisposeAsync().ConfigureAwait(false);
    }

    public ICamera? GetCamera(string deviceId) => GetDriver(deviceId) as ICamera;

    public ILightController? GetLight(string deviceId) => GetDriver(deviceId) as ILightController;

    public IPlcDriver? GetPlc(string deviceId) => GetDriver(deviceId) as IPlcDriver;

    public IRobotDriver? GetRobot(string deviceId) => GetDriver(deviceId) as IRobotDriver;

    public IMesClient? GetMes(string deviceId) => GetDriver(deviceId) as IMesClient;

    public IDeviceDriver? GetDriver(string deviceId)
        => !string.IsNullOrWhiteSpace(deviceId) && _entries.TryGetValue(deviceId, out var entry) ? entry.Driver : null;

    public async Task<DeviceState> ConnectAsync(string deviceId, CancellationToken ct)
    {
        if (!_entries.TryGetValue(deviceId, out var entry))
        {
            return DeviceState.Disconnected;
        }

        if (entry.Driver is not null)
        {
            await entry.Driver.ConnectAsync(ct).ConfigureAwait(false);
            return entry.Driver.State;
        }

        var descriptor = _registry.Find(entry.Config.DriverKey);
        if (descriptor is null || !descriptor.IsAvailable)
        {
            entry.UnavailableReason = descriptor?.UnavailableReason ?? $"驱动未注册：{entry.Config.DriverKey}";
            return DeviceState.Disconnected;
        }

        entry.Driver = _registry.Create(new DeviceContext(entry.Config));
        entry.Driver.StateChanged += OnDriverStateChanged;
        await entry.Driver.ConnectAsync(ct).ConfigureAwait(false);
        entry.UnavailableReason = null;
        return entry.Driver.State;
    }

    public async Task DisconnectAsync(string deviceId)
    {
        if (_entries.TryGetValue(deviceId, out var entry))
        {
            await DisposeEntryAsync(entry).ConfigureAwait(false);
        }
    }

    private void OnDriverStateChanged(object? sender, DeviceStateChangedEventArgs args)
        => DeviceStateChanged?.Invoke(this, args);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await CloseAsync().ConfigureAwait(false);
    }

    private sealed class DeviceEntry
    {
        public DeviceEntry(DeviceNodeConfig config)
        {
            Config = config;
        }

        public DeviceNodeConfig Config { get; }

        public IDeviceDriver? Driver { get; set; }

        public string? UnavailableReason { get; set; }
    }
}
