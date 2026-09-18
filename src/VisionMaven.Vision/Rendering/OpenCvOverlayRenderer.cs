using OpenCvSharp;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Vision.Rendering;

/// <summary>用 OpenCV 把检测框、判定结论与附加信息叠加到图像上。</summary>
public sealed class OpenCvOverlayRenderer : IOverlayRenderer
{
    private static readonly Scalar OkColor = new(80, 185, 63);
    private static readonly Scalar NgColor = new(73, 81, 248);
    private static readonly Scalar TextColor = new(255, 255, 255);
    private static readonly Scalar BannerBackground = new(40, 40, 40);

    public object Render(
        object sourceImage,
        IReadOnlyList<Detection> detections,
        InspectionResult result,
        double score,
        IReadOnlyList<string>? extraLines = null)
    {
        if (sourceImage is not Mat source)
        {
            throw new ArgumentException("sourceImage 必须是 OpenCvSharp.Mat", nameof(sourceImage));
        }

        var canvas = new Mat();
        if (source.Channels() == 1)
        {
            Cv2.CvtColor(source, canvas, ColorConversionCodes.GRAY2BGR);
        }
        else
        {
            source.CopyTo(canvas);
        }

        var thickness = Math.Max(1, canvas.Width / 640);
        var fontScale = Math.Max(0.4d, canvas.Width / 1600d);
        var color = result == InspectionResult.Ok ? OkColor : NgColor;

        foreach (var detection in detections)
        {
            var box = detection.Box.Clamp(canvas.Width, canvas.Height);
            var rect = new Rect(
                (int)Math.Round(box.X),
                (int)Math.Round(box.Y),
                Math.Max(1, (int)Math.Round(box.Width)),
                Math.Max(1, (int)Math.Round(box.Height)));

            Cv2.Rectangle(canvas, rect, color, thickness);

            var label = $"{detection.ClassName} {detection.Confidence:F2}";
            var textY = Math.Max(4, rect.Y - (int)(6 * fontScale) - 2);
            Cv2.PutText(
                canvas,
                label,
                new Point(rect.X, textY),
                HersheyFonts.HersheySimplex,
                fontScale,
                color,
                thickness);
        }

        var lines = new List<string>
        {
            result == InspectionResult.Ok ? "OK" : result == InspectionResult.Ng ? "NG" : "UNKNOWN",
            $"Score {score:F3}"
        };

        if (extraLines is not null)
        {
            lines.AddRange(extraLines);
        }

        DrawBanner(canvas, lines, color, fontScale);

        return canvas;
    }

    private static void DrawBanner(Mat canvas, IReadOnlyList<string> lines, Scalar color, double fontScale)
    {
        var lineHeight = (int)Math.Round(24 * fontScale);
        var height = (lineHeight * lines.Count) + lineHeight / 2;
        var banner = new Rect(0, 0, Math.Min(canvas.Width, (int)Math.Round(320 * fontScale)), height);

        using var overlay = canvas.Clone();
        Cv2.Rectangle(overlay, banner, BannerBackground, -1);
        Cv2.AddWeighted(overlay, 0.55d, canvas, 0.45d, 0d, canvas);

        for (var index = 0; index < lines.Count; index++)
        {
            Cv2.PutText(
                canvas,
                lines[index],
                new Point(8, lineHeight * (index + 1)),
                HersheyFonts.HersheySimplex,
                fontScale,
                index == 0 ? color : TextColor,
                Math.Max(1, (int)Math.Round(2 * fontScale)));
        }
    }
}
