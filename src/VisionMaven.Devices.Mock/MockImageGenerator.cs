using OpenCvSharp;

namespace VisionMaven.Devices.Mock;

/// <summary>模拟相机图像源：生成带噪声的亮斑图案，使阈值与 Blob 算子能产出稳定结果。</summary>
public sealed class MockImageGenerator : IDisposable
{
    private readonly Mat _background;
    private readonly Point[] _spots;

    public MockImageGenerator(int width, int height, int spotCount)
    {
        Width = Math.Max(64, width);
        Height = Math.Max(64, height);
        SpotCount = Math.Clamp(spotCount, 0, 32);

        _background = new Mat(Height, Width, MatType.CV_8UC1, new Scalar(24));

        var spots = new List<Point>(SpotCount);
        for (var index = 0; index < SpotCount; index++)
        {
            var cellWidth = Width / Math.Max(1, SpotCount);
            var centerX = (cellWidth * index) + (cellWidth / 2);
            var centerY = Height / 2;
            spots.Add(new Point(centerX, centerY));
        }

        _spots = spots.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public int SpotCount { get; }

    public Mat CreateFrame(long sequenceNo)
    {
        var frame = _background.Clone();
        var radius = Math.Max(8, Math.Min(Width, Height) / 16);
        var drift = (int)Math.Round(Math.Sin(sequenceNo / 12d) * 6d);

        foreach (var spot in _spots)
        {
            Cv2.Circle(
                frame,
                new Point(spot.X + drift, spot.Y),
                radius,
                new Scalar(235),
                -1,
                LineTypes.AntiAlias);
        }

        // 加入低幅噪声，避免算子输出过于理想化
        using var noise = new Mat(Height, Width, MatType.CV_8UC1);
        Cv2.Randu(noise, new Scalar(0), new Scalar(12));
        Cv2.Add(frame, noise, frame);

        return frame;
    }

    public void Dispose() => _background.Dispose();
}
