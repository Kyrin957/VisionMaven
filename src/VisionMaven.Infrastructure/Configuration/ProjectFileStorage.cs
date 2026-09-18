using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Shell;

namespace VisionMaven.Infrastructure.Configuration;

/// <summary>工程二进制资源（模型 / 标签 / 模板 / 标定 / 图片）的文件存储。</summary>
public sealed class ProjectFileStorage : IProjectFileStorage
{
    private readonly ILogPathProvider _paths;
    private readonly ILogger<ProjectFileStorage> _logger;

    public ProjectFileStorage(ILogPathProvider paths, ILogger<ProjectFileStorage> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string GetSubDirectory(string projectId, ProjectFolder folder)
    {
        var name = folder switch
        {
            ProjectFolder.Models => "models",
            ProjectFolder.Labels => "labels",
            ProjectFolder.Templates => "templates",
            ProjectFolder.Calibration => "calibration",
            ProjectFolder.Images => "images",
            _ => string.Empty
        };

        var directory = string.IsNullOrEmpty(name)
            ? Path.Combine(_paths.ProjectsRoot, projectId)
            : Path.Combine(_paths.ProjectsRoot, projectId, name);

        Directory.CreateDirectory(directory);
        return directory;
    }

    public async Task<string> SaveAsync(
        string projectId,
        ProjectFolder folder,
        string fileName,
        Stream content,
        CancellationToken ct)
    {
        var directory = GetSubDirectory(projectId, folder);
        var target = Path.Combine(directory, Path.GetFileName(fileName));
        await using (var file = File.Create(target))
        {
            await content.CopyToAsync(file, ct).ConfigureAwait(false);
        }

        return target;
    }

    public async Task<byte[]?> ReadAsync(
        string projectId,
        ProjectFolder folder,
        string fileName,
        CancellationToken ct)
    {
        var directory = GetSubDirectory(projectId, folder);
        var target = Path.Combine(directory, Path.GetFileName(fileName));
        if (!File.Exists(target))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(target, ct).ConfigureAwait(false);
    }

    public Task<bool> DeleteAsync(string projectId, ProjectFolder folder, string fileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var directory = GetSubDirectory(projectId, folder);
        var target = Path.Combine(directory, Path.GetFileName(fileName));
        if (!File.Exists(target))
        {
            return Task.FromResult(false);
        }

        File.Delete(target);
        return Task.FromResult(true);
    }

    public IReadOnlyList<string> List(string projectId, ProjectFolder folder, string searchPattern)
    {
        var directory = GetSubDirectory(projectId, folder);
        return Directory.GetFiles(directory, searchPattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    public async Task<int> CleanupImagesAsync(string projectId, int retentionDays, CancellationToken ct)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var directory = GetSubDirectory(projectId, ProjectFolder.Images);
        var threshold = DateTime.UtcNow.AddDays(-retentionDays);
        var deleted = 0;

        foreach (var file in Directory.GetFiles(directory, "*.jpg").Concat(Directory.GetFiles(directory, "*.png")))
        {
            ct.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(file) >= threshold)
            {
                continue;
            }

            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "结果图片清理失败：{File}", file);
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("已清理 {Count} 张过期结果图片", deleted);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return deleted;
    }
}
