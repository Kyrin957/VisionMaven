using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>
/// 一帧图像数据及其元信息。实现持有非托管缓冲，必须释放。
/// <see cref="NativeImage"/> 在运行时为 <c>OpenCvSharp.Mat</c>，Core 不引用图像库以保持零依赖。
/// </summary>
public interface IImageFrame : IDisposable
{
    string FrameId { get; }

    string CameraId { get; }

    int Width { get; }

    int Height { get; }

    int Channels { get; }

    PixelFormat PixelFormat { get; }

    long SequenceNo { get; }

    DateTimeOffset Timestamp { get; }

    /// <summary>底层图像对象（OpenCvSharp.Mat）。</summary>
    object NativeImage { get; }

    /// <summary>深拷贝一帧，供并行分支或落盘使用。</summary>
    IImageFrame Clone();
}

/// <summary>图像帧工厂：Vision 与驱动层共用，避免 Core 依赖图像库。</summary>
public interface IImageFrameFactory
{
    /// <summary>由底层图像对象创建帧。</summary>
    IImageFrame Create(string cameraId, long sequenceNo, DateTimeOffset timestamp, object nativeImage);

    /// <summary>从编码字节（PNG / JPG / BMP）解码创建帧。</summary>
    IImageFrame Decode(string cameraId, byte[] encoded);

    /// <summary>从磁盘文件解码创建帧。</summary>
    IImageFrame DecodeFile(string cameraId, string filePath);
}

/// <summary>图像缓冲池：复用大尺寸缓冲，降低 GC 压力。</summary>
public interface IBufferPool : IDisposable
{
    /// <summary>借出指定尺寸的图像帧，用完调用 <see cref="IImageFrame.Dispose"/> 归还。</summary>
    IImageFrame Rent(string cameraId, int width, int height, int channels);

    int IdleCount { get; }

    long TotalRented { get; }
}
