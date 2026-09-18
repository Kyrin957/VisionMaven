using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;

namespace VisionMaven.Vision.Inference;

/// <summary>letterbox 预处理产物：张量 + 还原所需的缩放与填充信息。</summary>
public sealed class PreprocessResult
{
    public required DenseTensor<float> Tensor { get; init; }

    public required double Scale { get; init; }

    public required double PadX { get; init; }

    public required double PadY { get; init; }

    public required int InputWidth { get; init; }

    public required int InputHeight { get; init; }

    /// <summary>把输入张量坐标系下的矩形还原到原图坐标系。</summary>
    public FloatRect ToOriginal(FloatRect box)
    {
        var x = (box.X - PadX) / Scale;
        var y = (box.Y - PadY) / Scale;
        var width = box.Width / Scale;
        var height = box.Height / Scale;
        return new FloatRect(x, y, width, height);
    }
}

/// <summary>Letterbox 缩放 + 归一化 + 通道重排，输出 NCHW / NHWC 浮点张量。</summary>
public static class TensorPreprocessor
{
    private static readonly float[] ImageNetMean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] ImageNetStd = { 0.229f, 0.224f, 0.225f };

    public static PreprocessResult Create(Mat source, ModelInputConfig input)
    {
        var targetWidth = Math.Max(1, input.Width);
        var targetHeight = Math.Max(1, input.Height);

        var scale = Math.Min(
            (double)targetWidth / source.Width,
            (double)targetHeight / source.Height);

        var resizedWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var padX = (targetWidth - resizedWidth) / 2d;
        var padY = (targetHeight - resizedHeight) / 2d;

        using var resized = new Mat();
        Cv2.Resize(source, resized, new Size(resizedWidth, resizedHeight), 0, 0, InterpolationFlags.Linear);

        using var canvas = new Mat();
        Cv2.CopyMakeBorder(
            resized,
            canvas,
            (int)Math.Round(padY),
            targetHeight - resizedHeight - (int)Math.Round(padY),
            (int)Math.Round(padX),
            targetWidth - resizedWidth - (int)Math.Round(padX),
            BorderTypes.Constant,
            new Scalar(114, 114, 114));

        using var rgb = new Mat();
        if (canvas.Channels() == 1)
        {
            Cv2.CvtColor(canvas, rgb, ColorConversionCodes.GRAY2BGR);
        }
        else if (canvas.Channels() == 4)
        {
            Cv2.CvtColor(canvas, rgb, ColorConversionCodes.BGRA2BGR);
        }
        else
        {
            canvas.CopyTo(rgb);
        }

        // OpenCV 为 BGR，模型通常按 RGB 训练
        Cv2.CvtColor(rgb, rgb, ColorConversionCodes.BGR2RGB);

        using var floatMat = new Mat();
        rgb.ConvertTo(floatMat, MatType.CV_32FC3);

        var tensor = input.Layout == TensorLayout.NCHW
            ? ToNchw(floatMat, targetWidth, targetHeight, input.Normalize)
            : ToNhwc(floatMat, targetWidth, targetHeight, input.Normalize);

        return new PreprocessResult
        {
            Tensor = tensor,
            Scale = scale,
            PadX = Math.Round(padX),
            PadY = Math.Round(padY),
            InputWidth = targetWidth,
            InputHeight = targetHeight
        };
    }

    private static DenseTensor<float> ToNchw(Mat bgrFloat, int width, int height, NormalizeMode normalize)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
        var index = bgrFloat.GetGenericIndexer<Vec3f>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = index[y, x];
                // 索引 0/1/2 对应通道，上面已转为 RGB
                tensor[0, 0, y, x] = Normalize(pixel.Item0, 0, normalize);
                tensor[0, 1, y, x] = Normalize(pixel.Item1, 1, normalize);
                tensor[0, 2, y, x] = Normalize(pixel.Item2, 2, normalize);
            }
        }

        return tensor;
    }

    private static DenseTensor<float> ToNhwc(Mat bgrFloat, int width, int height, NormalizeMode normalize)
    {
        var tensor = new DenseTensor<float>(new[] { 1, height, width, 3 });
        var index = bgrFloat.GetGenericIndexer<Vec3f>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = index[y, x];
                tensor[0, y, x, 0] = Normalize(pixel.Item0, 0, normalize);
                tensor[0, y, x, 1] = Normalize(pixel.Item1, 1, normalize);
                tensor[0, y, x, 2] = Normalize(pixel.Item2, 2, normalize);
            }
        }

        return tensor;
    }

    private static float Normalize(float value, int channel, NormalizeMode mode) => mode switch
    {
        NormalizeMode.Div255 => value / 255f,
        NormalizeMode.Imagenet => (value / 255f - ImageNetMean[channel]) / ImageNetStd[channel],
        _ => value
    };
}
