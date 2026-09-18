using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Drivers;

namespace VisionMaven.Devices.Mock;

/// <summary>模拟光源控制器驱动。</summary>
[VisionDriver("mock.light", DeviceKind.LightController, DisplayName = "模拟光源控制器")]
public sealed class MockLight : DeviceDriverBase, ILightController
{
    private readonly ConcurrentDictionary<int, int> _brightness = new();
    private readonly ConcurrentDictionary<int, bool> _enabled = new();
    private readonly ILogger<MockLight> _logger;

    public MockLight(IDeviceContext context, ILogger<MockLight> logger)
        : base(context, DeviceKind.LightController)
    {
        _logger = logger;
        ChannelCount = Math.Clamp(Context.GetInt("channelCount", 4), 1, 32);
    }

    public override string DriverKey => "mock.light";

    public int ChannelCount { get; }

    public LightTriggerMode TriggerMode { get; private set; } = LightTriggerMode.Strobe;

    protected override Task OnConnectAsync(CancellationToken ct)
    {
        for (var channel = 0; channel < ChannelCount; channel++)
        {
            _brightness[channel] = 128;
            _enabled[channel] = true;
        }

        _logger.LogInformation("模拟光源控制器已就绪：通道数={Count}", ChannelCount);
        return Task.CompletedTask;
    }

    public Task SetBrightnessAsync(int channel, int value, CancellationToken ct)
    {
        ValidateChannel(channel);
        _brightness[channel] = Math.Clamp(value, 0, 255);
        return Task.CompletedTask;
    }

    public Task SetChannelEnabledAsync(int channel, bool enabled, CancellationToken ct)
    {
        ValidateChannel(channel);
        _enabled[channel] = enabled;
        return Task.CompletedTask;
    }

    public Task<int?> ReadBrightnessAsync(int channel, CancellationToken ct)
    {
        ValidateChannel(channel);
        return Task.FromResult<int?>(_brightness.GetValueOrDefault(channel, 0));
    }

    public Task SetTriggerModeAsync(LightTriggerMode mode, CancellationToken ct)
    {
        TriggerMode = mode;
        return Task.CompletedTask;
    }

    private void ValidateChannel(int channel)
    {
        if (channel < 0 || channel >= ChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), $"通道超出范围：{channel}");
        }
    }
}

/// <summary>模拟光源控制器驱动注册。</summary>
public sealed class MockLightProvider : IDeviceDriverProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public MockLightProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "mock.light";

    public DeviceKind Kind => DeviceKind.LightController;

    public string DisplayName => "模拟光源控制器";

    public string? SdkHint => null;

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public ParameterSchema Schema { get; } = new()
    {
        new IntegerParameter("channelCount", "通道数", 1, 32, 4),
        new EnumParameter("triggerMode", "触发模式", new[] { "Continuous", "Strobe", "ExternalTrigger" }, "Strobe")
    };

    public IDeviceDriver Create(IDeviceContext context)
        => new MockLight(context, _loggerFactory.CreateLogger<MockLight>());
}
