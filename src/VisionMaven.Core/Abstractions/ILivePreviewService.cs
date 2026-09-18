namespace VisionMaven.Core.Abstractions;

/// <summary>
/// 实时预览服务：设备页与首页订阅相机帧。预览帧率由服务节流（15~25fps），
/// 订阅方负责释放 <see cref="LivePreviewEventArgs.Frame"/>。
/// </summary>
public interface ILivePreviewService
{
    /// <summary>开始订阅指定相机的预览帧。</summary>
    void Attach(string deviceId);

    /// <summary>取消订阅。</summary>
    void Detach(string deviceId);

    bool IsAttached(string deviceId);

    event EventHandler<LivePreviewEventArgs>? FrameReady;
}

/// <summary>预览帧参数。无订阅方时服务自行释放帧。</summary>
public sealed class LivePreviewEventArgs : EventArgs
{
    public LivePreviewEventArgs(string deviceId, IImageFrame frame)
    {
        DeviceId = deviceId;
        Frame = frame;
    }

    public string DeviceId { get; }

    public IImageFrame Frame { get; }
}
