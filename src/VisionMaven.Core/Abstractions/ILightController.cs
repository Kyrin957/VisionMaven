using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>光源控制器：多通道亮度与频闪，串口实现与 TCP 实现共用此出口。</summary>
public interface ILightController : IDeviceDriver
{
    int ChannelCount { get; }

    Task SetBrightnessAsync(int channel, int value, CancellationToken ct);

    Task SetChannelEnabledAsync(int channel, bool enabled, CancellationToken ct);

    Task<int?> ReadBrightnessAsync(int channel, CancellationToken ct);

    Task SetTriggerModeAsync(LightTriggerMode mode, CancellationToken ct);
}
