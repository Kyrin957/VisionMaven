using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Flow.Devices;

namespace VisionMaven.Flow.Preview;

/// <summary>
/// 实时预览服务：订阅相机 <see cref="ICamera.FrameArrived"/>，按 20fps 节流后转发给界面。
/// 相机事件中的帧仅在回调期间有效，因此这里克隆后再转发，克隆帧的所有权交给订阅方。
/// </summary>
public sealed class LivePreviewService : ILivePreviewService, IDisposable
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(50);

    private readonly IDeviceSession _devices;
    private readonly ILogger<LivePreviewService> _logger;
    private readonly ConcurrentDictionary<string, Attachment> _attachments = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public LivePreviewService(IDeviceSession devices, ILogger<LivePreviewService> logger)
    {
        _devices = devices;
        _logger = logger;
    }

    public event EventHandler<LivePreviewEventArgs>? FrameReady;

    public bool IsAttached(string deviceId) => _attachments.ContainsKey(deviceId);

    public void Attach(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(deviceId) || _attachments.ContainsKey(deviceId))
        {
            return;
        }

        var camera = _devices.GetCamera(deviceId);
        if (camera is null)
        {
            _logger.LogWarning("无法订阅预览，相机不可用：{DeviceId}", deviceId);
            return;
        }

        var attachment = new Attachment(deviceId);
        attachment.Handler = (_, args) => OnFrameArrived(attachment, args);
        camera.FrameArrived += attachment.Handler;

        if (_attachments.TryAdd(deviceId, attachment))
        {
            _logger.LogInformation("预览已订阅：{DeviceId}", deviceId);
        }
        else
        {
            camera.FrameArrived -= attachment.Handler;
        }
    }

    public void Detach(string deviceId)
    {
        if (!_attachments.TryRemove(deviceId, out var attachment))
        {
            return;
        }

        var camera = _devices.GetCamera(deviceId);
        if (camera is not null && attachment.Handler is not null)
        {
            camera.FrameArrived -= attachment.Handler;
        }

        _logger.LogInformation("预览已取消订阅：{DeviceId}", deviceId);
    }

    private void OnFrameArrived(Attachment attachment, FrameArrivedEventArgs args)
    {
        var now = DateTimeOffset.Now;
        if (now - attachment.LastForwarded < MinInterval)
        {
            return;
        }

        attachment.LastForwarded = now;

        var handler = FrameReady;
        if (handler is null)
        {
            return;
        }

        IImageFrame clone;
        try
        {
            clone = args.Frame.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "预览帧克隆失败：{DeviceId}", attachment.DeviceId);
            return;
        }

        try
        {
            handler.Invoke(this, new LivePreviewEventArgs(attachment.DeviceId, clone));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "预览订阅者处理失败：{DeviceId}", attachment.DeviceId);
            clone.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var deviceId in _attachments.Keys.ToArray())
        {
            Detach(deviceId);
        }
    }

    private sealed class Attachment
    {
        public Attachment(string deviceId)
        {
            DeviceId = deviceId;
        }

        public string DeviceId { get; }

        public EventHandler<FrameArrivedEventArgs>? Handler { get; set; }

        public DateTimeOffset LastForwarded { get; set; } = DateTimeOffset.MinValue;
    }
}
