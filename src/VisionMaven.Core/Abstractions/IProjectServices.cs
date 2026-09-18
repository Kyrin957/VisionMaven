using VisionMaven.Core.Configuration;
using VisionMaven.Core.Shell;

namespace VisionMaven.Core.Abstractions;

/// <summary>工程配置读写与校验。</summary>
public interface IProjectConfigurationService
{
    /// <summary>工程根目录（<c>projects/</c>）。</summary>
    string ProjectsRoot { get; }

    /// <summary>枚举已有工程。</summary>
    IReadOnlyList<ProjectInfo> ListProjects();

    /// <summary>读取指定工程的配置；文件不存在返回 null。</summary>
    Task<ProjectConfig?> LoadAsync(string projectId, CancellationToken ct);

    /// <summary>保存工程配置，同时刷新 <c>UpdatedAt</c> 与运行时目录。</summary>
    Task SaveAsync(ProjectConfig config, CancellationToken ct);

    /// <summary>创建空工程（含目录骨架与最小可用配置）。</summary>
    Task<ProjectConfig> CreateAsync(string name, string? code, CancellationToken ct);

    /// <summary>删除工程（含目录）。</summary>
    Task<bool> DeleteAsync(string projectId, CancellationToken ct);

    /// <summary>按 [7.5] 校验规则校验配置，返回全部问题。</summary>
    ProjectValidationResult Validate(ProjectConfig config);

    /// <summary>解析工程内相对路径为绝对路径。</summary>
    string ResolvePath(string projectId, string relativePath);

    /// <summary>导出为 .vmproj 包。</summary>
    Task ExportAsync(string projectId, string destinationFile, CancellationToken ct);

    /// <summary>从 .vmproj 包导入，projectId 冲突时追加后缀。</summary>
    Task<ProjectConfig> ImportAsync(string packageFile, CancellationToken ct);
}

/// <summary>工程校验问题。</summary>
public sealed record ProjectValidationIssue(string Code, string Message, bool IsFatal);

/// <summary>工程校验结果。</summary>
public sealed record ProjectValidationResult(IReadOnlyList<ProjectValidationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => !issue.IsFatal);

    public IReadOnlyList<ProjectValidationIssue> Fatals => Issues.Where(issue => issue.IsFatal).ToArray();

    public static ProjectValidationResult Ok { get; } = new(Array.Empty<ProjectValidationIssue>());
}

/// <summary>工程二进制资源（模型 / 标签 / 模板 / 标定 / 图片）的文件存储。</summary>
public interface IProjectFileStorage
{
    string GetSubDirectory(string projectId, ProjectFolder folder);

    Task<string> SaveAsync(string projectId, ProjectFolder folder, string fileName, Stream content, CancellationToken ct);

    Task<byte[]?> ReadAsync(string projectId, ProjectFolder folder, string fileName, CancellationToken ct);

    Task<bool> DeleteAsync(string projectId, ProjectFolder folder, string fileName, CancellationToken ct);

    IReadOnlyList<string> List(string projectId, ProjectFolder folder, string searchPattern);

    /// <summary>清理过期结果图片，返回删除数量。</summary>
    Task<int> CleanupImagesAsync(string projectId, int retentionDays, CancellationToken ct);
}
