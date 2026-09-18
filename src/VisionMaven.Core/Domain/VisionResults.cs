namespace VisionMaven.Core.Domain;

/// <summary>Blob / 连通域分析结果。</summary>
public sealed record BlobRegion(
    int Index,
    double Area,
    double Perimeter,
    double Roundness,
    FloatRect Bounds,
    Point2D Centroid);

/// <summary>模板匹配结果。</summary>
public sealed record TemplateMatchResult(
    Point2D Location,
    double Score,
    double Angle,
    FloatRect Box);

/// <summary>找线结果。</summary>
public sealed record LineResult(
    LineF Line,
    double AngleDegrees,
    double LengthPixels,
    Point2D Start,
    Point2D End);

/// <summary>找圆结果。</summary>
public sealed record CircleResultInfo(
    CircleF Circle,
    double Score);

/// <summary>测量结果。</summary>
public sealed record MeasureResult(
    double Value,
    string Unit,
    Point2D Point1,
    Point2D Point2);

/// <summary>可序列化的检测框，供叠加渲染与结果落盘共用。</summary>
public sealed record BoxOverlay(string Label, double Score, FloatRect Box, bool IsNg);
