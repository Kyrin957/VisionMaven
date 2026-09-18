using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;
using VisionMaven.Core.Shell;
using VisionMaven.Vision.Serialization;

namespace VisionMaven.Vision.Calibration;

/// <summary>标定求解与持久化（九点 / 棋盘格 / 比例）。</summary>
public sealed class CalibrationService : ICalibrationService
{
    private readonly IProjectFileStorage _storage;
    private readonly ILogger<CalibrationService> _logger;

    public CalibrationService(IProjectFileStorage storage, ILogger<CalibrationService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public CalibrationResult SolveHomography(IReadOnlyList<(Point2D Pixel, Point2D World)> pairs)
    {
        if (pairs.Count < 4)
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "单应矩阵求解至少需要 4 组对应点");
        }

        using var src = new Mat(pairs.Count, 1, MatType.CV_32FC2);
        using var dst = new Mat(pairs.Count, 1, MatType.CV_32FC2);
        for (var index = 0; index < pairs.Count; index++)
        {
            src.Set(index, 0, new Vec2f((float)pairs[index].Pixel.X, (float)pairs[index].Pixel.Y));
            dst.Set(index, 0, new Vec2f((float)pairs[index].World.X, (float)pairs[index].World.Y));
        }

        using var homography = Cv2.FindHomography(src, dst, HomographyMethods.Ransac, 3d);
        if (homography.Empty())
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "单应矩阵求解失败，请检查对应点分布");
        }

        var matrix = new double[9];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                matrix[(row * 3) + column] = homography.At<double>(row, column);
            }
        }

        var mmPerPixel = EstimateMmPerPixel(pairs);
        var provisional = new CalibrationResult(CalibrationType.NinePoint, mmPerPixel, matrix, 0d, 0d);

        var residualMm = 0d;
        foreach (var pair in pairs)
        {
            var mapped = provisional.ToMillimeters(pair.Pixel.X, pair.Pixel.Y);
            residualMm += Math.Sqrt(
                ((mapped.X - pair.World.X) * (mapped.X - pair.World.X))
                + ((mapped.Y - pair.World.Y) * (mapped.Y - pair.World.Y)));
        }

        residualMm /= pairs.Count;
        var residualPixels = mmPerPixel > 0 ? residualMm / mmPerPixel : 0d;

        _logger.LogInformation(
            "标定求解完成：点数={Count} 残差={ResidualMm:F4}mm",
            pairs.Count,
            residualMm);

        return new CalibrationResult(
            CalibrationType.NinePoint,
            mmPerPixel,
            matrix,
            residualPixels,
            residualMm);
    }

    public CalibrationResult SolveScale(double pixelDistance, double worldDistanceMm)
    {
        if (pixelDistance <= 0)
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "像素距离必须大于 0");
        }

        var mmPerPixel = worldDistanceMm / pixelDistance;
        _logger.LogInformation("比例标定完成：{MmPerPixel:F6} mm/px", mmPerPixel);
        return new CalibrationResult(
            CalibrationType.ScaleOnly,
            mmPerPixel,
            new[] { 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d },
            0d,
            0d);
    }

    public async Task<string> SaveAsync(
        string projectId,
        string cameraId,
        CalibrationResult result,
        CancellationToken ct)
    {
        var fileName = $"{cameraId}_calib.json";
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new CalibrationFile
            {
                CameraId = cameraId,
                Type = result.Type,
                MmPerPixel = result.MmPerPixel,
                Homography = result.Homography,
                ResidualPixels = result.ResidualPixels,
                ResidualMm = result.ResidualMm,
                SavedAt = DateTimeOffset.Now
            },
            VisionJson.Options);

        await using var stream = new MemoryStream(payload);
        var path = await _storage
            .SaveAsync(projectId, ProjectFolder.Calibration, fileName, stream, ct)
            .ConfigureAwait(false);

        _logger.LogInformation("标定文件已保存：{Path}", path);
        return $"calibration/{fileName}";
    }

    public CalibrationResult? Load(string projectId, string cameraId)
    {
        var directory = _storage.GetSubDirectory(projectId, ProjectFolder.Calibration);
        var path = Path.Combine(directory, $"{cameraId}_calib.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<CalibrationFile>(stream, VisionJson.Options);
            if (file is null)
            {
                return null;
            }

            return new CalibrationResult(
                file.Type,
                file.MmPerPixel,
                file.Homography,
                file.ResidualPixels,
                file.ResidualMm);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "标定文件读取失败：{Path}", path);
            return null;
        }
    }

    private static double EstimateMmPerPixel(IReadOnlyList<(Point2D Pixel, Point2D World)> pairs)
    {
        var ratios = new List<double>();
        for (var i = 0; i < pairs.Count; i++)
        {
            for (var j = i + 1; j < pairs.Count; j++)
            {
                var pixelDistance = Distance(pairs[i].Pixel, pairs[j].Pixel);
                if (pixelDistance < 1e-6)
                {
                    continue;
                }

                var worldDistance = Distance(pairs[i].World, pairs[j].World);
                ratios.Add(worldDistance / pixelDistance);
            }
        }

        return ratios.Count == 0 ? 1d : ratios.Average();
    }

    private static double Distance(Point2D a, Point2D b)
        => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    /// <summary>标定文件结构。</summary>
    private sealed class CalibrationFile
    {
        public string CameraId { get; set; } = string.Empty;

        public CalibrationType Type { get; set; }

        public double MmPerPixel { get; set; } = 1d;

        public double[] Homography { get; set; } = new[] { 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d };

        public double ResidualPixels { get; set; }

        public double ResidualMm { get; set; }

        public DateTimeOffset SavedAt { get; set; }
    }
}

/// <summary>按当前打开的工程提供相机标定，工位级覆盖相机级。</summary>
public sealed class ProjectCalibrationProvider : ICalibrationProvider
{
    private volatile ProjectConfig? _project;

    public void Configure(ProjectConfig? project) => _project = project;

    public bool HasCalibration(string cameraId)
        => FindCamera(cameraId)?.Calibration.Type != CalibrationType.None;

    public CalibrationResult Get(string cameraId) => ToResult(FindCamera(cameraId)?.Calibration);

    public CalibrationResult GetForStation(string stationId, string cameraId)
    {
        var station = _project?.Stations.FirstOrDefault(
            item => string.Equals(item.StationId, stationId, StringComparison.OrdinalIgnoreCase));

        if (station is not null
            && station.Parameters.TryGetValue($"Calibration.{cameraId}.MmPerPixel", out var mmPerPixelText)
            && double.TryParse(
                mmPerPixelText,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var mmPerPixel))
        {
            return new CalibrationResult(
                CalibrationType.ScaleOnly,
                mmPerPixel,
                new[] { 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d },
                0d,
                0d);
        }

        return Get(cameraId);
    }

    private CameraConfig? FindCamera(string cameraId)
        => _project?.Cameras.FirstOrDefault(
            camera => string.Equals(camera.DeviceId, cameraId, StringComparison.OrdinalIgnoreCase));

    private static CalibrationResult ToResult(CameraCalibrationConfig? config)
    {
        if (config is null || config.Type == CalibrationType.None)
        {
            return CalibrationResult.Identity;
        }

        return new CalibrationResult(
            config.Type,
            config.MmPerPixel <= 0 ? 1d : config.MmPerPixel,
            config.Homography.Length == 9
                ? config.Homography
                : new[] { 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d },
            0d,
            0d);
    }
}
