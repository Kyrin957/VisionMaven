using OpenCvSharp;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Vision.Operators;

/// <summary>灰度化。</summary>
public sealed class GrayOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new EnumParameter("method", "灰度方法", new[] { "Weighted", "Average", "Max" }, "Weighted")
    };

    public override string TypeKey => "preprocess.gray";

    public override string DisplayName => "灰度化";

    public override OperatorCategory Category => OperatorCategory.Preprocess;

    public override int Order => 10;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var method = ParameterReader.GetString(parameters, "method", "Weighted");
        var output = new Mat();

        if (input.Channels() == 1)
        {
            input.CopyTo(output);
            return OkWithImage(output, source);
        }

        switch (method.ToUpperInvariant())
        {
            case "AVERAGE":
            {
                var channels = input.Split();
                try
                {
                    var accumulator = new Mat();
                    var buffer = new Mat();
                    Cv2.AddWeighted(channels[0], 1d / 3d, channels[1], 1d / 3d, 0d, accumulator);
                    Cv2.AddWeighted(accumulator, 1d, channels[2], 1d / 3d, 0d, buffer);
                    accumulator.Dispose();
                    buffer.CopyTo(output);
                    buffer.Dispose();
                }
                finally
                {
                    foreach (var channel in channels)
                    {
                        channel.Dispose();
                    }
                }

                break;
            }

            case "MAX":
            {
                var channels = input.Split();
                try
                {
                    var first = new Mat();
                    Cv2.Max(channels[0], channels[1], first);
                    Cv2.Max(first, channels[2], output);
                    first.Dispose();
                }
                finally
                {
                    foreach (var channel in channels)
                    {
                        channel.Dispose();
                    }
                }

                break;
            }

            default:
                Cv2.CvtColor(input, output, ColorConversionCodes.BGR2GRAY);
                break;
        }

        return OkWithImage(output, source);
    }
}

/// <summary>滤波。</summary>
public sealed class FilterOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new EnumParameter("type", "滤波类型", new[] { "Gaussian", "Median", "Bilateral" }, "Gaussian"),
        new IntegerParameter("kernel", "核大小", 3, 31, 3) { Unit = "px" },
        new NumberParameter("sigma", "Sigma", 0d, 20d, 0d)
    };

    public override string TypeKey => "preprocess.filter";

    public override string DisplayName => "滤波";

    public override OperatorCategory Category => OperatorCategory.Preprocess;

    public override int Order => 20;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var type = ParameterReader.GetString(parameters, "type", "Gaussian");
        var kernel = ParameterReader.GetInt(parameters, "kernel", 3);
        if (kernel % 2 == 0)
        {
            kernel++;
        }

        kernel = Math.Clamp(kernel, 3, 31);
        var sigma = ParameterReader.GetDouble(parameters, "sigma", 0d);
        var output = new Mat();

        switch (type.ToUpperInvariant())
        {
            case "MEDIAN":
                Cv2.MedianBlur(input, output, kernel);
                break;

            case "BILATERAL":
            {
                var d = Math.Max(kernel, 5);
                var sigmaColor = sigma > 0 ? sigma : 25d;
                Cv2.BilateralFilter(input, output, d, sigmaColor, sigmaColor);
                break;
            }

            default:
                Cv2.GaussianBlur(input, output, new Size(kernel, kernel), sigma);
                break;
        }

        return OkWithImage(output, source);
    }
}

/// <summary>形态学。</summary>
public sealed class MorphologyOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new EnumParameter("op", "运算", new[] { "Erode", "Dilate", "Open", "Close" }, "Open"),
        new EnumParameter("shape", "结构元素", new[] { "Rect", "Ellipse", "Cross" }, "Rect"),
        new IntegerParameter("kernel", "核大小", 3, 31, 3) { Unit = "px" },
        new IntegerParameter("iterations", "迭代次数", 1, 10, 1)
    };

    public override string TypeKey => "preprocess.morphology";

    public override string DisplayName => "形态学";

    public override OperatorCategory Category => OperatorCategory.Preprocess;

    public override int Order => 30;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var op = ParameterReader.GetString(parameters, "op", "Open");
        var shape = ParameterReader.GetString(parameters, "shape", "Rect");
        var kernelSize = Math.Max(1, ParameterReader.GetInt(parameters, "kernel", 3));
        var iterations = Math.Max(1, ParameterReader.GetInt(parameters, "iterations", 1));

        var morphShape = shape.ToUpperInvariant() switch
        {
            "ELLIPSE" => MorphShapes.Ellipse,
            "CROSS" => MorphShapes.Cross,
            _ => MorphShapes.Rect
        };

        using var kernel = Cv2.GetStructuringElement(morphShape, new Size(kernelSize, kernelSize));
        var output = new Mat();

        switch (op.ToUpperInvariant())
        {
            case "ERODE":
                Cv2.Erode(input, output, kernel, iterations: iterations);
                break;

            case "DILATE":
                Cv2.Dilate(input, output, kernel, iterations: iterations);
                break;

            case "CLOSE":
                Cv2.MorphologyEx(input, output, MorphTypes.Close, kernel, iterations: iterations);
                break;

            default:
                Cv2.MorphologyEx(input, output, MorphTypes.Open, kernel, iterations: iterations);
                break;
        }

        return OkWithImage(output, source);
    }
}

/// <summary>区域裁剪。</summary>
public sealed class RoiOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new IntegerParameter("x", "起点 X", 0, 100000, 0) { Unit = "px" },
        new IntegerParameter("y", "起点 Y", 0, 100000, 0) { Unit = "px" },
        new IntegerParameter("width", "宽度", 1, 100000, 100) { Unit = "px" },
        new IntegerParameter("height", "高度", 1, 100000, 100) { Unit = "px" }
    };

    public override string TypeKey => "preprocess.roi";

    public override string DisplayName => "区域裁剪";

    public override OperatorCategory Category => OperatorCategory.Preprocess;

    public override int Order => 40;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var x = ParameterReader.GetInt(parameters, "x", 0);
        var y = ParameterReader.GetInt(parameters, "y", 0);
        var width = ParameterReader.GetInt(parameters, "width", input.Width);
        var height = ParameterReader.GetInt(parameters, "height", input.Height);

        var rect = new Rect(x, y, width, height).Intersect(new Rect(0, 0, input.Width, input.Height));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, $"ROI 越界：{x},{y},{width},{height}");
        }

        using var view = new Mat(input, rect);
        var output = view.Clone();

        return OkWithImage(output, source, new Dictionary<string, object?>
        {
            ["roi"] = new IntRect(rect.X, rect.Y, rect.Width, rect.Height)
        });
    }
}

/// <summary>阈值分割。</summary>
public sealed class ThresholdOperator : OperatorBase
{
    public static ParameterSchema StaticSchema { get; } = new()
    {
        new EnumParameter("method", "阈值方法", new[] { "Fixed", "Otsu", "Adaptive" }, "Otsu"),
        new IntegerParameter("threshold", "阈值", 0, 255, 128),
        new IntegerParameter("maxValue", "最大值", 0, 255, 255),
        new BooleanParameter("invert", "反相", false),
        new IntegerParameter("blockSize", "自适应窗口", 3, 201, 31) { Group = "自适应" },
        new NumberParameter("c", "自适应常数 C", -50d, 50d, 5d) { Group = "自适应" }
    };

    public override string TypeKey => "threshold.binary";

    public override string DisplayName => "阈值分割";

    public override OperatorCategory Category => OperatorCategory.Threshold;

    public override int Order => 50;

    public override ParameterSchema Schema => StaticSchema;

    protected override OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        using var gray = ToGray(input);
        var method = ParameterReader.GetString(parameters, "method", "Otsu");
        var maxValue = ParameterReader.GetInt(parameters, "maxValue", 255);
        var invert = ParameterReader.GetBool(parameters, "invert", false);
        var output = new Mat();

        switch (method.ToUpperInvariant())
        {
            case "FIXED":
                Cv2.Threshold(
                    gray,
                    output,
                    ParameterReader.GetDouble(parameters, "threshold", 128d),
                    maxValue,
                    invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary);
                break;

            case "ADAPTIVE":
            {
                var blockSize = Math.Max(3, ParameterReader.GetInt(parameters, "blockSize", 31));
                if (blockSize % 2 == 0)
                {
                    blockSize++;
                }

                Cv2.AdaptiveThreshold(
                    gray,
                    output,
                    maxValue,
                    AdaptiveThresholdTypes.GaussianC,
                    invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary,
                    blockSize,
                    ParameterReader.GetDouble(parameters, "c", 5d));
                break;
            }

            default:
                Cv2.Threshold(
                    gray,
                    output,
                    0,
                    maxValue,
                    invert ? ThresholdTypes.Otsu | ThresholdTypes.BinaryInv : ThresholdTypes.Otsu | ThresholdTypes.Binary);
                break;
        }

        return OkWithImage(output, source);
    }

    /// <summary>把多通道输入降为单通道，供阈值 / 轮廓类算子使用。</summary>
    internal static Mat ToGray(Mat input)
    {
        if (input.Channels() == 1)
        {
            return input.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }
}
