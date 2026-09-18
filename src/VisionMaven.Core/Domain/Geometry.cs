namespace VisionMaven.Core.Domain;

/// <summary>整数矩形（像素坐标系，左上角为原点）。</summary>
public readonly record struct IntRect(int X, int Y, int Width, int Height)
{
    public static IntRect Empty => new(0, 0, 0, 0);

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>浮点矩形。</summary>
public readonly record struct FloatRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2d);

    public double CenterY => Y + (Height / 2d);

    public FloatRect Scale(double factor) => new(X * factor, Y * factor, Width * factor, Height * factor);

    public FloatRect Clamp(double maxWidth, double maxHeight)
    {
        var x = Math.Clamp(X, 0, maxWidth);
        var y = Math.Clamp(Y, 0, maxHeight);
        var w = Math.Clamp(Width, 0, maxWidth - x);
        var h = Math.Clamp(Height, 0, maxHeight - y);
        return new FloatRect(x, y, w, h);
    }
}

/// <summary>二维点。</summary>
public readonly record struct Point2D(double X, double Y);

/// <summary>圆。</summary>
public readonly record struct CircleF(Point2D Center, double Radius);

/// <summary>直线（标准式 A*x + B*y + C = 0）。</summary>
public readonly record struct LineF(double A, double B, double C)
{
    public double AngleDegrees
    {
        get
        {
            var angle = Math.Atan2(-A, B) * 180d / Math.PI;
            return angle < 0 ? angle + 180d : angle;
        }
    }
}

/// <summary>位姿（机器人）。</summary>
public readonly record struct RobotPose(double X, double Y, double Z, double Rx, double Ry, double Rz);

/// <summary>单目标检测结果。</summary>
public sealed record Detection(int ClassId, string ClassName, double Confidence, FloatRect Box);

/// <summary>单分类结果。</summary>
public sealed record ClassProbability(int ClassId, string ClassName, double Probability);

/// <summary>像素坐标到物理坐标的仿射映射（3x3 单应矩阵，行主序）。</summary>
public sealed record CalibrationResult(
    CalibrationType Type,
    double MmPerPixel,
    double[] Homography,
    double ResidualPixels,
    double ResidualMm)
{
    public static CalibrationResult Identity { get; } = new(
        CalibrationType.None,
        1d,
        new[] { 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d },
        0d,
        0d);

    /// <summary>将像素坐标映射为物理坐标（毫米）。</summary>
    public Point2D ToMillimeters(double pixelX, double pixelY)
    {
        if (Type == CalibrationType.None)
        {
            return new Point2D(pixelX * MmPerPixel, pixelY * MmPerPixel);
        }

        var h = Homography;
        var denominator = (h[6] * pixelX) + (h[7] * pixelY) + h[8];
        if (Math.Abs(denominator) < double.Epsilon)
        {
            denominator = 1d;
        }

        var x = ((h[0] * pixelX) + (h[1] * pixelY) + h[2]) / denominator;
        var y = ((h[3] * pixelX) + (h[4] * pixelY) + h[5]) / denominator;
        return new Point2D(x, y);
    }
}
