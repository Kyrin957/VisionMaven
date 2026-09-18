using OpenCvSharp;
using VisionMaven.Core.Abstractions;

namespace VisionMaven.Vision.Imaging;

/// <summary>基于 OpenCvSharp 的图像帧工厂。</summary>
public sealed class OpenCvImageFrameFactory : IImageFrameFactory
{
    public IImageFrame Create(string cameraId, long sequenceNo, DateTimeOffset timestamp, object nativeImage)
    {
        if (nativeImage is not Mat mat)
        {
            throw new ArgumentException("nativeImage 必须是 OpenCvSharp.Mat", nameof(nativeImage));
        }

        return new MatImageFrame(cameraId, mat, sequenceNo, timestamp);
    }

    public IImageFrame Decode(string cameraId, byte[] encoded)
    {
        var mat = Cv2.ImDecode(encoded, ImreadModes.Unchanged);
        if (mat.Empty())
        {
            mat.Dispose();
            throw new InvalidOperationException("图像解码失败");
        }

        return new MatImageFrame(cameraId, mat);
    }

    public IImageFrame DecodeFile(string cameraId, string filePath)
    {
        var mat = Cv2.ImRead(filePath, ImreadModes.Unchanged);
        if (mat.Empty())
        {
            mat.Dispose();
            throw new FileNotFoundException($"图像读取失败：{filePath}", filePath);
        }

        return new MatImageFrame(cameraId, mat);
    }
}

/// <summary>图像编解码工具。</summary>
public static class ImageCodec
{
    /// <summary>按扩展名编码图像。</summary>
    public static byte[] Encode(Mat mat, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var parameters = extension switch
        {
            ".png" => new[] { ImageEncodingParam(ImwriteFlags.PngCompression, 3) },
            ".bmp" => Array.Empty<ImageEncodingParam>(),
            _ => new[] { ImageEncodingParam(ImwriteFlags.JpegQuality, 92) }
        };

        Cv2.ImEncode(extension.TrimStart('.'), mat, out var buffer, parameters);
        return buffer;
    }

    /// <summary>把图像写入磁盘，自动创建目录。</summary>
    public static void Write(Mat mat, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Cv2.ImWrite(filePath, mat);
    }

    private static ImageEncodingParam ImageEncodingParam(ImwriteFlags flag, int value) => new(flag, value);
}
