using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>标定结果查询：算子与渲染按 cameraId 取标定。</summary>
public interface ICalibrationProvider
{
    /// <summary>取相机级标定，未标定返回 <see cref="CalibrationResult.Identity"/>。</summary>
    CalibrationResult Get(string cameraId);

    /// <summary>取工位级标定（覆盖相机级），未配置时回退到相机级。</summary>
    CalibrationResult GetForStation(string stationId, string cameraId);

    bool HasCalibration(string cameraId);
}

/// <summary>标定求解：九点 / 棋盘格 / 比例。</summary>
public interface ICalibrationService
{
    /// <summary>由点对集合求解单应矩阵，返回残差。</summary>
    CalibrationResult SolveHomography(IReadOnlyList<(Point2D Pixel, Point2D World)> pairs);

    /// <summary>由单个标定物的物理长度求解 mm/px。</summary>
    CalibrationResult SolveScale(double pixelDistance, double worldDistanceMm);

    /// <summary>保存标定文件到工程 calibration 目录。</summary>
    Task<string> SaveAsync(string projectId, string cameraId, CalibrationResult result, CancellationToken ct);

    /// <summary>读取标定文件。</summary>
    CalibrationResult? Load(string projectId, string cameraId);
}
