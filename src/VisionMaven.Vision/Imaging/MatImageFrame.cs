using OpenCvSharp;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Vision.Imaging;

/// <summary>基于 <see cref="Mat"/> 的图像帧。所有权规帧持有者，Dispose 时释放 Mat。</summary>
public class MatImageFrame : IImageFrame
{
    private static long _sequence;

    private Mat? _mat;

    public MatImageFrame(string cameraId, Mat mat, long sequenceNo = 0, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(mat);
        _mat = mat;
        CameraId = cameraId;
        SequenceNo = sequenceNo == 0 ? Interlocked.Increment(ref _sequence) : sequenceNo;
        Timestamp = timestamp ?? DateTimeOffset.Now;
        FrameId = Guid.NewGuid().ToString("N");
    }

    public string FrameId { get; }

    public string CameraId { get; }

    public long SequenceNo { get; }

    public DateTimeOffset Timestamp { get; }

    /// <summary>底层 Mat，已在释放后访问会抛 <see cref="ObjectDisposedException"/>。</summary>
    public Mat Mat
    {
        get
        {
            var mat = _mat;
            ObjectDisposedException.ThrowIf(mat is null, this);
            return mat;
        }
    }

    public object NativeImage => Mat;

    public int Width => _mat?.Width ?? 0;

    public int Height => _mat?.Height ?? 0;

    public int Channels => _mat?.Channels() ?? 0;

    public PixelFormat PixelFormat => MatTypeMapper.ToPixelFormat(_mat?.Type() ?? MatType.CV_8UC1);

    public bool IsDisposed => _mat is null;

    public IImageFrame Clone()
    {
        var clone = Mat.Clone();
        return new MatImageFrame(CameraId, clone, 0, Timestamp);
    }

    public virtual void Dispose()
    {
        var mat = _mat;
        if (mat is null)
        {
            return;
        }

        _mat = null;
        mat.Dispose();
    }
}

/// <summary>把 Mat 视为 <see cref="MatImageFrame"/>，非 Mat 输入时抛出。</summary>
public static class FrameAccess
{
    public static Mat RequireMat(IImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame switch
        {
            MatImageFrame matFrame => matFrame.Mat,
            _ => frame.NativeImage as Mat
                 ?? throw new InvalidOperationException($"图像帧类型不受支持：{frame.GetType().Name}")
        };
    }

    public static Mat? TryMat(IImageFrame? frame)
    {
        if (frame is null)
        {
            return null;
        }

        return frame switch
        {
            MatImageFrame matFrame when !matFrame.IsDisposed => matFrame.Mat,
            _ => frame.NativeImage as Mat
        };
    }
}

/// <summary>MatType 与像素格式互转。</summary>
public static class MatTypeMapper
{
    public static PixelFormat ToPixelFormat(MatType type)
    {
        if (type == MatType.CV_8UC1)
        {
            return PixelFormat.Mono8;
        }

        if (type == MatType.CV_8UC3)
        {
            return PixelFormat.Bgr8;
        }

        if (type == MatType.CV_8UC4)
        {
            return PixelFormat.Bgra8;
        }

        if (type == MatType.CV_16UC1)
        {
            return PixelFormat.Mono10;
        }

        return PixelFormat.Mono8;
    }

    public static MatType ToMatType(PixelFormat format) => format switch
    {
        PixelFormat.Mono8 => MatType.CV_8UC1,
        PixelFormat.Mono10 => MatType.CV_16UC1,
        PixelFormat.Mono12 => MatType.CV_16UC1,
        PixelFormat.Bgr8 => MatType.CV_8UC3,
        PixelFormat.Rgb8 => MatType.CV_8UC3,
        PixelFormat.Bgra8 => MatType.CV_8UC4,
        PixelFormat.BayerRG8 => MatType.CV_8UC1,
        PixelFormat.BayerGB8 => MatType.CV_8UC1,
        _ => MatType.CV_8UC1
    };
}
