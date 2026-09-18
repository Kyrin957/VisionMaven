using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionMaven.Core.Abstractions;

namespace VisionMaven.Modules.Shared.Interop;

/// <summary>
/// OpenCvSharp 与 WPF 之间的图像互操作：Vision 层不引用 WPF，转换只在界面层发生。
/// </summary>
public static class ImageInterop
{
    /// <summary>把 <see cref="Mat"/> 或图像帧转换为可跨线程使用的位图。</summary>
    public static BitmapSource? ToBitmapSource(object? image)
    {
        var mat = ResolveMat(image);
        if (mat is null || mat.Empty())
        {
            return null;
        }

        try
        {
            var source = BitmapSourceConverter.ToBitmapSource(mat);
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>按最大边长等比缩放后转换为位图，用于预览降载。</summary>
    public static BitmapSource? ToThumbnail(object? image, int maxDimension)
    {
        var mat = ResolveMat(image);
        if (mat is null || mat.Empty())
        {
            return null;
        }

        var longest = Math.Max(mat.Width, mat.Height);
        if (longest <= maxDimension || maxDimension <= 0)
        {
            return ToBitmapSource(mat);
        }

        var scale = (double)maxDimension / longest;
        using var resized = new Mat();
        Cv2.Resize(
            mat,
            resized,
            new Size(
                Math.Max(1, (int)Math.Round(mat.Width * scale)),
                Math.Max(1, (int)Math.Round(mat.Height * scale))),
            0,
            0,
            InterpolationFlags.Area);

        return ToBitmapSource(resized);
    }

    /// <summary>把帧编码为 PNG 字节，用于结果落盘与测试断言。</summary>
    public static byte[] EncodePng(IImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var mat = ResolveMat(frame.NativeImage)
                  ?? throw new InvalidOperationException("图像帧不含有效的 Mat");

        Cv2.ImEncode(".png", mat, out var buffer);
        return buffer;
    }

    private static Mat? ResolveMat(object? image) => image switch
    {
        null => null,
        Mat mat => mat,
        IImageFrame frame when frame.NativeImage is Mat frameMat => frameMat,
        _ => null
    };
}
