using Microsoft.Extensions.Logging;
using OpenCvSharp;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Vision.Operators;

/// <summary>Blob / 连通域分析。</summary>
public sealed class BlobFindOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new IntegerParameter("threshold", "二值阈值", 0, 255, 128),
        new IntegerParameter("minArea", "最小面积", 0, 100000000, 50) { Unit = "px²" },
        new IntegerParameter("maxArea", "最大面积", 1, 100000000, 1000000) { Unit = "px²" },
        new NumberParameter("minRoundness", "最小圆度", 0d, 1d, 0d),
        new BooleanParameter("invert", "反相", false)
    };

    public override string TypeKey => "blob.find";

    public override string DisplayName => "Blob 分析";

    public override OperatorCategory Category => OperatorCategory.Blob;

    public override int Order => 60;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        using var gray = ThresholdOperator.ToGray(input);
        using var binary = new Mat();
        Cv2.Threshold(
            gray,
            binary,
            ParameterReader.GetDouble(parameters, "threshold", 128d),
            255,
            ParameterReader.GetBool(parameters, "invert", false)
                ? ThresholdTypes.BinaryInv
                : ThresholdTypes.Binary);

        Cv2.FindContours(
            binary,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var minArea = ParameterReader.GetDouble(parameters, "minArea", 50d);
        var maxArea = ParameterReader.GetDouble(parameters, "maxArea", 1_000_000d);
        var minRoundness = ParameterReader.GetDouble(parameters, "minRoundness", 0d);

        var regions = new List<BlobRegion>();
        var index = 0;
        foreach (var contour in contours)
        {
            ct.ThrowIfCancellationRequested();

            var area = Cv2.ContourArea(contour);
            if (area < minArea || area > maxArea)
            {
                continue;
            }

            var perimeter = Cv2.ArcLength(contour, true);
            var roundness = perimeter > 0 ? 4d * Math.PI * area / (perimeter * perimeter) : 0d;
            if (roundness < minRoundness)
            {
                continue;
            }

            var bounds = Cv2.BoundingRect(contour);
            var moments = Cv2.Moments(contour);
            var centroid = moments.M00 > 0
                ? new Point2D(moments.M10 / moments.M00, moments.M01 / moments.M00)
                : new Point2D(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

            regions.Add(new BlobRegion(
                index++,
                area,
                perimeter,
                roundness,
                new FloatRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                centroid));
        }

        var output = new Mat();
        input.CopyTo(output);

        return OkWithImage(output, source, new Dictionary<string, object?>
        {
            ["regions"] = regions,
            ["count"] = regions.Count,
            ["maxArea"] = regions.Count == 0 ? 0d : regions.Max(region => region.Area),
            ["minArea"] = regions.Count == 0 ? 0d : regions.Min(region => region.Area)
        });
    }
}

/// <summary>模板匹配（灰度 NCC，支持角度范围粗搜索）。</summary>
public sealed class TemplateMatchOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new FileParameter("templateFile", "模板文件", "模板文件|*.png;*.jpg;*.bmp", string.Empty),
        new NumberParameter("angleRange", "角度范围", 0d, 180d, 0d) { Unit = "°" },
        new NumberParameter("angleStep", "角度步长", 0.5d, 30d, 1d) { Unit = "°" },
        new NumberParameter("minScore", "最小得分", 0d, 1d, 0.7d)
    };

    public override string TypeKey => "match.template";

    public override string DisplayName => "模板匹配";

    public override OperatorCategory Category => OperatorCategory.TemplateMatch;

    public override int Order => 70;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var templateFile = ParameterReader.GetString(parameters, "templateFile", string.Empty);
        if (string.IsNullOrWhiteSpace(templateFile) || !File.Exists(templateFile))
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, $"模板文件不存在：{templateFile}");
        }

        using var template = Cv2.ImRead(templateFile, ImreadModes.Grayscale);
        if (template.Empty())
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, $"模板读取失败：{templateFile}");
        }

        using var gray = ThresholdOperator.ToGray(input);
        var angleRange = ParameterReader.GetDouble(parameters, "angleRange", 0d);
        var angleStep = Math.Max(0.5d, ParameterReader.GetDouble(parameters, "angleStep", 1d));
        var minScore = ParameterReader.GetDouble(parameters, "minScore", 0.7d);

        var matches = new List<TemplateMatchResult>();
        var best = 0d;

        for (var angle = -angleRange; angle <= angleRange + double.Epsilon; angle += angleStep)
        {
            ct.ThrowIfCancellationRequested();

            using var rotated = RotateTemplate(template, angle);
            if (rotated.Width > gray.Width || rotated.Height > gray.Height)
            {
                continue;
            }

            using var result = new Mat();
            Cv2.MatchTemplate(gray, rotated, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);

            if (maxValue < minScore)
            {
                continue;
            }

            best = Math.Max(best, maxValue);
            matches.Add(new TemplateMatchResult(
                new Point2D(maxLocation.X + (rotated.Width / 2d), maxLocation.Y + (rotated.Height / 2d)),
                maxValue,
                angle,
                new FloatRect(maxLocation.X, maxLocation.Y, rotated.Width, rotated.Height)));
        }

        matches = matches
            .OrderByDescending(match => match.Score)
            .Take(50)
            .ToList();

        var output = new Mat();
        input.CopyTo(output);

        return OkWithImage(output, source, new Dictionary<string, object?>
        {
            ["matches"] = matches,
            ["score"] = best,
            ["count"] = matches.Count
        });
    }

    private static Mat RotateTemplate(Mat template, double angle)
    {
        if (Math.Abs(angle) < double.Epsilon)
        {
            return template.Clone();
        }

        var center = new Point2f(template.Width / 2f, template.Height / 2f);
        using var matrix = Cv2.GetRotationMatrix2D(center, angle, 1d);

        // 旋转后取外接矩形，保证模板完整
        var cos = Math.Abs(matrix.At<double>(0, 0));
        var sin = Math.Abs(matrix.At<double>(0, 1));
        var width = (int)((template.Height * sin) + (template.Width * cos));
        var height = (int)((template.Height * cos) + (template.Width * sin));

        matrix.Set(0, 2, matrix.At<double>(0, 2) + ((width / 2d) - center.X));
        matrix.Set(1, 2, matrix.At<double>(1, 2) + ((height / 2d) - center.Y));

        var rotated = new Mat();
        Cv2.WarpAffine(
            template,
            rotated,
            matrix,
            new Size(width, height),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            Scalar.Black);
        return rotated;
    }
}

/// <summary>找线（Canny + 概率霍夫）。</summary>
public sealed class FindLineOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new IntegerParameter("edgeThreshold", "边缘阈值", 1, 255, 50),
        new EnumParameter("polarity", "极性", new[] { "Light", "Dark" }, "Dark"),
        new IntegerParameter("numLines", "输出线数", 1, 20, 1),
        new IntegerParameter("minLength", "最小长度", 1, 100000, 50) { Unit = "px" },
        new IntegerParameter("maxGap", "最大间断", 1, 1000, 10) { Unit = "px" }
    };

    public override string TypeKey => "find.line";

    public override string DisplayName => "找线";

    public override OperatorCategory Category => OperatorCategory.Finding;

    public override int Order => 80;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        using var gray = ThresholdOperator.ToGray(input);
        using var prepared = new Mat();

        if (string.Equals(
                ParameterReader.GetString(parameters, "polarity", "Dark"),
                "Dark",
                StringComparison.OrdinalIgnoreCase))
        {
            Cv2.BitwiseNot(gray, prepared);
        }
        else
        {
            gray.CopyTo(prepared);
        }

        var edgeThreshold = ParameterReader.GetDouble(parameters, "edgeThreshold", 50d);
        using var edges = new Mat();
        Cv2.Canny(prepared, edges, edgeThreshold, edgeThreshold * 3d);

        var segments = Cv2.HoughLinesP(
            edges,
            1d,
            Math.PI / 180d,
            Math.Max(10, (int)edgeThreshold),
            ParameterReader.GetDouble(parameters, "minLength", 50d),
            ParameterReader.GetDouble(parameters, "maxGap", 10d));

        var lines = new List<LineResult>();
        foreach (var segment in segments
                     .OrderByDescending(segment => Distance(segment.P1, segment.P2))
                     .Take(Math.Max(1, ParameterReader.GetInt(parameters, "numLines", 1))))
        {
            ct.ThrowIfCancellationRequested();
            lines.Add(BuildLine(segment.P1, segment.P2));
        }

        var output = new Mat();
        input.CopyTo(output);

        return OkWithImage(output, source, new Dictionary<string, object?>
        {
            ["line"] = lines.Count > 0 ? lines[0] : null,
            ["lines"] = lines,
            ["angle"] = lines.Count > 0 ? lines[0].AngleDegrees : 0d
        });
    }

    private static LineResult BuildLine(Point start, Point end)
    {
        var line = Cv2.FitLine(new[] { start, end }, DistanceTypes.L2, 0, 0.01, 0.01);
        var vx = line.Vx;
        var vy = line.Vy;
        var x0 = line.X1;
        var y0 = line.Y1;

        var normalA = vy;
        var normalB = -vx;
        var c = -((normalA * x0) + (normalB * y0));
        var angle = Math.Atan2(vy, vx) * 180d / Math.PI;
        if (angle < 0)
        {
            angle += 180d;
        }

        return new LineResult(
            new LineF(normalA, normalB, c),
            angle,
            Distance(start, end),
            new Point2D(start.X, start.Y),
            new Point2D(end.X, end.Y));
    }

    private static double Distance(Point a, Point b)
        => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
}

/// <summary>找圆（霍夫圆变换）。</summary>
public sealed class FindCircleOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new IntegerParameter("edgeThreshold", "边缘阈值", 1, 255, 100),
        new IntegerParameter("minRadius", "最小半径", 1, 10000, 10) { Unit = "px" },
        new IntegerParameter("maxRadius", "最大半径", 1, 10000, 500) { Unit = "px" },
        new NumberParameter("minDistance", "圆心最小间距", 1d, 10000d, 50d) { Unit = "px" },
        new NumberParameter("accumulatorThreshold", "累加阈值", 1d, 200d, 30d)
    };

    public override string TypeKey => "find.circle";

    public override string DisplayName => "找圆";

    public override OperatorCategory Category => OperatorCategory.Finding;

    public override int Order => 90;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        using var gray = ThresholdOperator.ToGray(input);
        using var blurred = new Mat();
        Cv2.MedianBlur(gray, blurred, 5);

        var circles = Cv2.HoughCircles(
            blurred,
            HoughModes.Gradient,
            1.5d,
            ParameterReader.GetDouble(parameters, "minDistance", 50d),
            ParameterReader.GetDouble(parameters, "edgeThreshold", 100d),
            ParameterReader.GetDouble(parameters, "accumulatorThreshold", 30d),
            Math.Max(1, ParameterReader.GetInt(parameters, "minRadius", 10)),
            Math.Max(1, ParameterReader.GetInt(parameters, "maxRadius", 500)));

        var results = circles
            .Select(circle => new CircleResultInfo(
                new CircleF(new Point2D(circle.Center.X, circle.Center.Y), circle.Radius),
                1d))
            .ToList();

        var output = new Mat();
        input.CopyTo(output);

        return OkWithImage(output, source, new Dictionary<string, object?>
        {
            ["circle"] = results.Count > 0 ? results[0] : null,
            ["circles"] = results,
            ["radius"] = results.Count > 0 ? results[0].Circle.Radius : 0d,
            ["count"] = results.Count
        });
    }
}

/// <summary>点对距离测量。</summary>
public sealed class MeasureDistanceOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new TextParameter("point1", "点1 (x,y)", "0,0"),
        new TextParameter("point2", "点2 (x,y)", "0,0"),
        new EnumParameter("unit", "单位", new[] { "Pixel", "Mm" }, "Pixel"),
        new NumberParameter("mmPerPixel", "换算系数", 0d, 1000d, 1d) { Group = "标定" }
    };

    public override string TypeKey => "measure.distance";

    public override string DisplayName => "距离测量";

    public override OperatorCategory Category => OperatorCategory.Measure;

    public override int Order => 100;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var point1 = ParsePoint(ParameterReader.GetString(parameters, "point1", "0,0"), "point1");
        var point2 = ParsePoint(ParameterReader.GetString(parameters, "point2", "0,0"), "point2");
        var pixels = Math.Sqrt(
            ((point1.X - point2.X) * (point1.X - point2.X))
            + ((point1.Y - point2.Y) * (point1.Y - point2.Y)));

        var unit = ParameterReader.GetString(parameters, "unit", "Pixel");
        var value = string.Equals(unit, "Mm", StringComparison.OrdinalIgnoreCase)
            ? pixels * ParameterReader.GetDouble(parameters, "mmPerPixel", 1d)
            : pixels;

        var output = new Mat();
        input.CopyTo(output);

        return OkWithImage(output, source, new Dictionary<string, object?>
        {
            ["value"] = value,
            ["unit"] = string.Equals(unit, "Mm", StringComparison.OrdinalIgnoreCase) ? "mm" : "px",
            ["point1"] = point1,
            ["point2"] = point2
        });
    }

    internal static Point2D ParsePoint(string text, string field)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, $"{field} 需要形如 12.5,33.2 的坐标");
        }

        return new Point2D(x, y);
    }
}

/// <summary>像素坐标 → 物理坐标映射。</summary>
public sealed class CalibrationTransformOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new ReferenceParameter("cameraId", "相机", ParameterValueKind.DeviceId, "Camera"),
        new TextParameter("point1", "点1 (x,y)", "0,0"),
        new TextParameter("point2", "点2 (x,y)", "0,0"),
        new EnumParameter("targetUnit", "目标单位", new[] { "Mm", "Pixel" }, "Mm")
    };

    private readonly ICalibrationProvider _calibration;

    public CalibrationTransformOperator(ICalibrationProvider calibration)
    {
        _calibration = calibration;
    }

    public override string TypeKey => "calibration.transform";

    public override string DisplayName => "坐标映射";

    public override OperatorCategory Category => OperatorCategory.Calibration;

    public override int Order => 110;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var cameraId = ParameterReader.GetString(parameters, "cameraId", source.CameraId);
        var calibration = _calibration.Get(cameraId);
        var targetUnit = ParameterReader.GetString(parameters, "targetUnit", "Mm");

        var point1 = MeasureDistanceOperator.ParsePoint(ParameterReader.GetString(parameters, "point1", "0,0"), "point1");
        var point2 = MeasureDistanceOperator.ParsePoint(ParameterReader.GetString(parameters, "point2", "0,0"), "point2");

        var mapped1 = Map(calibration, point1, targetUnit);
        var mapped2 = Map(calibration, point2, targetUnit);
        var distance = Math.Sqrt(
            ((mapped1.X - mapped2.X) * (mapped1.X - mapped2.X))
            + ((mapped1.Y - mapped2.Y) * (mapped1.Y - mapped2.Y)));

        var output = new Mat();
        input.CopyTo(output);

        return OkWithImage(output, source, new Dictionary<string, object?>
        {
            ["points"] = new[] { mapped1, mapped2 },
            ["point1"] = mapped1,
            ["point2"] = mapped2,
            ["value"] = distance,
            ["unit"] = targetUnit
        });
    }

    private static Point2D Map(CalibrationResult calibration, Point2D point, string targetUnit)
        => string.Equals(targetUnit, "Pixel", StringComparison.OrdinalIgnoreCase)
            ? point
            : calibration.ToMillimeters(point.X, point.Y);
}
