using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;
using VisionMaven.Core.Shell;

namespace VisionMaven.Infrastructure.Configuration;

/// <summary>工程配置读写、校验与导入导出。</summary>
public sealed class ProjectConfigurationService : IProjectConfigurationService
{
    private const string ProjectFileName = "project.json";
    private const string PackageExtension = ".vmproj";

    private readonly ILogPathProvider _paths;
    private readonly IDeviceDriverRegistry _drivers;
    private readonly ILogger<ProjectConfigurationService> _logger;

    public ProjectConfigurationService(
        ILogPathProvider paths,
        IDeviceDriverRegistry drivers,
        ILogger<ProjectConfigurationService> logger)
    {
        _paths = paths;
        _drivers = drivers;
        _logger = logger;
    }

    public string ProjectsRoot => _paths.ProjectsRoot;

    public IReadOnlyList<ProjectInfo> ListProjects()
    {
        var root = new DirectoryInfo(ProjectsRoot);
        if (!root.Exists)
        {
            return Array.Empty<ProjectInfo>();
        }

        var summaries = new List<ProjectInfo>();
        foreach (var directory in root.GetDirectories())
        {
            var file = Path.Combine(directory.FullName, ProjectFileName);
            if (!File.Exists(file))
            {
                continue;
            }

            var summary = TryReadSummary(directory.FullName, file);
            if (summary is not null)
            {
                summaries.Add(summary);
            }
        }

        return summaries.OrderByDescending(item => item.UpdatedAt).ToArray();
    }

    private ProjectInfo? TryReadSummary(string directory, string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var config = JsonSerializer.Deserialize<ProjectConfig>(stream, ProjectJsonOptions.Options);
            if (config is null)
            {
                return null;
            }

            return new ProjectInfo(
                config.ProjectId,
                config.Name,
                config.Code,
                config.UpdatedAt,
                directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "工程概要读取失败：{File}", file);
            return null;
        }
    }

    public async Task<ProjectConfig?> LoadAsync(string projectId, CancellationToken ct)
    {
        var file = GetProjectFile(projectId);
        if (!File.Exists(file))
        {
            return null;
        }

        await using var stream = File.OpenRead(file);
        var config = await JsonSerializer
            .DeserializeAsync<ProjectConfig>(stream, ProjectJsonOptions.Options, ct)
            .ConfigureAwait(false);

        if (config is null)
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, $"工程配置为空：{file}");
        }

        Normalize(config, projectId);
        await EnsureLayoutAsync(projectId).ConfigureAwait(false);
        return config;
    }

    public async Task SaveAsync(ProjectConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ProjectId))
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "工程缺少 projectId");
        }

        config.UpdatedAt = DateTimeOffset.Now;
        if (string.IsNullOrWhiteSpace(config.SchemaVersion))
        {
            config.SchemaVersion = ProjectConfig.CurrentSchemaVersion;
        }

        await EnsureLayoutAsync(config.ProjectId).ConfigureAwait(false);

        var file = GetProjectFile(config.ProjectId);
        var temp = file + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer
                .SerializeAsync(stream, config, ProjectJsonOptions.Options, ct)
                .ConfigureAwait(false);
        }

        File.Move(temp, file, overwrite: true);
        _logger.LogInformation("工程已保存：{ProjectId}", config.ProjectId);
    }

    public async Task<ProjectConfig> CreateAsync(string name, string? code, CancellationToken ct)
    {
        await Task.Yield();

        var projectId = NextProjectId();
        var config = new ProjectConfig
        {
            SchemaVersion = ProjectConfig.CurrentSchemaVersion,
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(name) ? projectId : name,
            Code = code ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now
        };

        config.Cameras.Add(new CameraConfig
        {
            DeviceId = "CAM01",
            Name = "相机1",
            DriverKey = "mock.camera",
            Kind = DeviceKind.Camera,
            Enabled = true
        });

        config.Stations.Add(new StationConfig
        {
            StationId = "ST01",
            Name = "工位1",
            Enabled = true,
            CameraIds = { "CAM01" },
            FlowId = "FLOW-ST01",
            Trigger = new StationTriggerConfig
            {
                Source = TriggerSource.Software,
                TimeoutMs = 2000
            }
        });

        config.Flows.Add(CreateDefaultFlow("FLOW-ST01", "默认检测流程", "CAM01"));

        config.Parameters.Global[ParameterKeys.ResultImagePolicy] = nameof(ResultImagePolicy.NgOnly);
        config.Parameters.Global[ParameterKeys.ImageRetentionDays] = "30";
        config.Parameters.Global[ParameterKeys.StatisticsIntervalSec] = "60";

        return config;
    }

    private static FlowDefinitionConfig CreateDefaultFlow(string flowId, string name, string cameraId)
    {
        var flow = new FlowDefinitionConfig
        {
            FlowId = flowId,
            Name = name,
            FailureStrategy = FailureStrategy.Abort
        };

        flow.Nodes.Add(new FlowNodeConfig
        {
            NodeId = "N1",
            TypeKey = "acquire",
            Name = "采集",
            Order = 1,
            Parameters = { ["cameraId"] = cameraId, ["timeoutMs"] = "2000" },
            Next = { "N2" }
        });

        flow.Nodes.Add(new FlowNodeConfig
        {
            NodeId = "N2",
            TypeKey = "preprocess.gray",
            Name = "灰度化",
            Order = 2,
            Parameters = { ["method"] = "Weighted" },
            Inputs = { ["image"] = "N1.image" },
            Next = { "N3" }
        });

        flow.Nodes.Add(new FlowNodeConfig
        {
            NodeId = "N3",
            TypeKey = "threshold.binary",
            Name = "阈值分割",
            Order = 3,
            Parameters = { ["method"] = "Otsu", ["threshold"] = "128", ["maxValue"] = "255", ["invert"] = "False" },
            Inputs = { ["image"] = "N2.image" },
            Next = { "N4" }
        });

        flow.Nodes.Add(new FlowNodeConfig
        {
            NodeId = "N4",
            TypeKey = "blob.find",
            Name = "Blob 分析",
            Order = 4,
            Parameters = { ["minArea"] = "50", ["maxArea"] = "1000000" },
            Inputs = { ["image"] = "N3.image" },
            Next = { "N5" }
        });

        flow.Nodes.Add(new FlowNodeConfig
        {
            NodeId = "N5",
            TypeKey = "judge.count",
            Name = "数量判定",
            Order = 5,
            Parameters = { ["field"] = "count", ["min"] = "1", ["max"] = "10" },
            Inputs = { ["input"] = "N4.count" }
        });

        flow.Nodes.Add(new FlowNodeConfig
        {
            NodeId = "N6",
            TypeKey = "output.save",
            Name = "结果落盘",
            Order = 6,
            Parameters = { ["policy"] = nameof(ResultImagePolicy.NgOnly), ["format"] = "jpg" },
            Inputs = { ["image"] = "N1.image", ["result"] = "N5.result" }
        });

        flow.Nodes.First(node => node.NodeId == "N5").Next.Add("N6");

        return flow;
    }

    public async Task<bool> DeleteAsync(string projectId, CancellationToken ct)
    {
        await Task.Yield();
        var directory = GetProjectDirectory(projectId);
        if (!Directory.Exists(directory))
        {
            return false;
        }

        Directory.Delete(directory, recursive: true);
        _logger.LogInformation("工程已删除：{ProjectId}", projectId);
        return true;
    }

    public ProjectValidationResult Validate(ProjectConfig config)
    {
        var issues = new List<ProjectValidationIssue>();

        if (!ProjectConfig.SupportedSchemaVersions.Contains(config.SchemaVersion, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new ProjectValidationIssue(
                ErrorCodes.ConfigSchemaUnsupported,
                $"schemaVersion 不受支持：{config.SchemaVersion}",
                true));
        }

        if (string.IsNullOrWhiteSpace(config.ProjectId))
        {
            issues.Add(new ProjectValidationIssue(ErrorCodes.ConfigParseFailed, "缺少 projectId", true));
        }

        if (config.Stations.Count == 0)
        {
            issues.Add(new ProjectValidationIssue(ErrorCodes.ConfigSchemaUnsupported, "工位集合为空", true));
        }

        AddDuplicates(issues, config.Stations.Select(item => item.StationId), "stationId");
        AddDuplicates(issues, config.AllDevices().Select(item => item.DeviceId), "deviceId");
        AddDuplicates(issues, config.Flows.Select(item => item.FlowId), "flowId");
        AddDuplicates(issues, config.ModelLibrary.Select(item => item.ModelId), "modelId");
        AddDuplicates(issues, config.Flows.SelectMany(flow => flow.Nodes).Select(node => node.NodeId), "nodeId");

        var deviceIds = config.AllDevices().Select(item => item.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var flowIds = config.Flows.Select(item => item.FlowId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var station in config.Stations)
        {
            foreach (var cameraId in station.CameraIds.Where(id => !deviceIds.Contains(id)))
            {
                issues.Add(new ProjectValidationIssue(
                    ErrorCodes.ConfigDanglingReference,
                    $"工位 {station.StationId} 引用了不存在的相机 {cameraId}",
                    true));
            }

            foreach (var binding in station.DeviceBindings.Where(id => !deviceIds.Contains(id)))
            {
                issues.Add(new ProjectValidationIssue(
                    ErrorCodes.ConfigDanglingReference,
                    $"工位 {station.StationId} 绑定了不存在的设备 {binding}",
                    true));
            }

            if (!string.IsNullOrWhiteSpace(station.FlowId) && !flowIds.Contains(station.FlowId))
            {
                issues.Add(new ProjectValidationIssue(
                    ErrorCodes.ConfigDanglingReference,
                    $"工位 {station.StationId} 引用了不存在的流程 {station.FlowId}",
                    true));
            }

            if (station.Trigger.Source == TriggerSource.Plc
                && !string.IsNullOrWhiteSpace(station.Trigger.DeviceId)
                && !deviceIds.Contains(station.Trigger.DeviceId))
            {
                issues.Add(new ProjectValidationIssue(
                    ErrorCodes.ConfigDanglingReference,
                    $"工位 {station.StationId} 触发源引用了不存在的设备 {station.Trigger.DeviceId}",
                    true));
            }
        }

        foreach (var device in config.AllDevices())
        {
            if (string.IsNullOrWhiteSpace(device.DriverKey))
            {
                issues.Add(new ProjectValidationIssue(
                    ErrorCodes.ConfigDriverMissing,
                    $"设备 {device.DeviceId} 未配置 driverKey",
                    true));
                continue;
            }

            var descriptor = _drivers.Find(device.DriverKey);
            if (descriptor is null)
            {
                issues.Add(new ProjectValidationIssue(
                    ErrorCodes.ConfigDriverMissing,
                    $"设备 {device.DeviceId} 的驱动 {device.DriverKey} 未注册",
                    false));
            }
            else if (!descriptor.IsAvailable)
            {
                issues.Add(new ProjectValidationIssue(
                    ErrorCodes.DeviceSdkMissing,
                    $"设备 {device.DeviceId} 的驱动 {device.DriverKey} 不可用：{descriptor.UnavailableReason}",
                    false));
            }
        }

        foreach (var flow in config.Flows)
        {
            foreach (var error in ValidateFlow(flow))
            {
                issues.Add(new ProjectValidationIssue(ErrorCodes.ConfigFlowInvalid, error, true));
            }
        }

        foreach (var model in config.ModelLibrary)
        {
            var file = ResolvePath(config.ProjectId, model.Path);
            if (string.IsNullOrWhiteSpace(model.Path) || !File.Exists(file))
            {
                issues.Add(new ProjectValidationIssue(
                    ErrorCodes.ConfigModelFileMissing,
                    $"模型 {model.ModelId} 文件不存在：{model.Path}",
                    false));
            }

            if (!string.IsNullOrWhiteSpace(model.Labels))
            {
                var labelFile = ResolvePath(config.ProjectId, model.Labels);
                if (!File.Exists(labelFile))
                {
                    issues.Add(new ProjectValidationIssue(
                        ErrorCodes.InferenceLabelMissing,
                        $"模型 {model.ModelId} 标签文件不存在：{model.Labels}",
                        false));
                }
            }
        }

        return issues.Count == 0
            ? ProjectValidationResult.Ok
            : new ProjectValidationResult(issues);
    }

    /// <summary>流程结构校验：唯一性、next 引用、无环、单一起点。</summary>
    public static IReadOnlyList<string> ValidateFlow(FlowDefinitionConfig flow) => FlowTopology.Validate(flow);

    private static void AddDuplicates(
        ICollection<ProjectValidationIssue> issues,
        IEnumerable<string> values,
        string fieldName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!seen.Add(value))
            {
                issues.Add(new ProjectValidationIssue(
                    ErrorCodes.ConfigDuplicateId,
                    $"{fieldName} 重复：{value}",
                    true));
            }
        }
    }

    public string ResolvePath(string projectId, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return GetProjectDirectory(projectId);
        }

        return Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(GetProjectDirectory(projectId), relativePath));
    }

    public async Task ExportAsync(string projectId, string destinationFile, CancellationToken ct)
    {
        var directory = GetProjectDirectory(projectId);
        if (!Directory.Exists(directory))
        {
            throw new StorageException(ErrorCodes.StorageExportFailed, $"工程目录不存在：{projectId}");
        }

        await Task.Yield();
        if (File.Exists(destinationFile))
        {
            File.Delete(destinationFile);
        }

        try
        {
            ZipFile.CreateFromDirectory(directory, destinationFile, CompressionLevel.Optimal, includeBaseDirectory: false);
            _logger.LogInformation("工程已导出：{ProjectId} -> {File}", projectId, destinationFile);
        }
        catch (Exception ex)
        {
            throw new StorageException(ErrorCodes.StorageExportFailed, $"导出失败：{ex.Message}", ex);
        }
    }

    public async Task<ProjectConfig> ImportAsync(string packageFile, CancellationToken ct)
    {
        if (!File.Exists(packageFile))
        {
            throw new StorageException(ErrorCodes.StorageImportFailed, $"导入包不存在：{packageFile}");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "visionmaven-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(packageFile, tempDirectory, overwriteFiles: true);

            var configFile = Path.Combine(tempDirectory, ProjectFileName);
            if (!File.Exists(configFile))
            {
                throw new StorageException(ErrorCodes.StorageImportFailed, "导入包内缺少 project.json");
            }

            ProjectConfig config;
            await using (var stream = File.OpenRead(configFile))
            {
                config = await JsonSerializer
                    .DeserializeAsync<ProjectConfig>(stream, ProjectJsonOptions.Options, ct)
                    .ConfigureAwait(false)
                    ?? throw new StorageException(ErrorCodes.StorageImportFailed, "导入包内的 project.json 无法解析");
            }

            var targetId = config.ProjectId;
            var suffix = 1;
            while (Directory.Exists(GetProjectDirectory(targetId)))
            {
                targetId = $"{config.ProjectId}-{suffix++}";
            }

            config.ProjectId = targetId;
            await SaveAsync(config, ct).ConfigureAwait(false);

            var targetDirectory = GetProjectDirectory(targetId);
            CopyDirectory(tempDirectory, targetDirectory, configFile);

            await using (var stream = File.Create(Path.Combine(targetDirectory, ProjectFileName)))
            {
                await JsonSerializer.SerializeAsync(stream, config, ProjectJsonOptions.Options, ct).ConfigureAwait(false);
            }

            return config;
        }
        catch (StorageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new StorageException(ErrorCodes.StorageImportFailed, $"导入失败：{ex.Message}", ex);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响导入结果。
            }
        }
    }

    private static void CopyDirectory(string source, string destination, string skipFile)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(file, skipFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
            File.Copy(file, target, overwrite: true);
        }
    }

    public string GetProjectDirectory(string projectId)
        => Path.Combine(ProjectsRoot, projectId);

    private string GetProjectFile(string projectId)
        => Path.Combine(GetProjectDirectory(projectId), ProjectFileName);

    private string NextProjectId()
    {
        for (var index = 1; index < 10000; index++)
        {
            var candidate = $"PRJ-{index:D4}";
            if (!Directory.Exists(GetProjectDirectory(candidate)))
            {
                return candidate;
            }
        }

        return "PRJ-" + Guid.NewGuid().ToString("N")[..8];
    }

    private Task EnsureLayoutAsync(string projectId)
    {
        var directory = GetProjectDirectory(projectId);
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "models"));
        Directory.CreateDirectory(Path.Combine(directory, "labels"));
        Directory.CreateDirectory(Path.Combine(directory, "templates"));
        Directory.CreateDirectory(Path.Combine(directory, "calibration"));
        Directory.CreateDirectory(Path.Combine(directory, "images"));
        return Task.CompletedTask;
    }

    private static void Normalize(ProjectConfig config, string projectId)
    {
        if (string.IsNullOrWhiteSpace(config.ProjectId))
        {
            config.ProjectId = projectId;
        }

        config.Stations ??= new List<StationConfig>();
        config.Cameras ??= new List<CameraConfig>();
        config.LightControllers ??= new List<LightControllerConfig>();
        config.CommLinks ??= new List<CommLinkConfig>();
        config.ModelLibrary ??= new List<ModelEntryConfig>();
        config.Flows ??= new List<FlowDefinitionConfig>();
        config.Parameters ??= new ProjectParameterConfig();
    }
}
