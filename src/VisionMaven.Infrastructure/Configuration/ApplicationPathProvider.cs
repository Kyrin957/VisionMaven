using VisionMaven.Core.Abstractions;

namespace VisionMaven.Infrastructure.Configuration;

/// <summary>运行时目录布局解析，见开发文档 [3.3]。</summary>
public sealed class ApplicationPathProvider : ILogPathProvider
{
    public ApplicationPathProvider(string? applicationRoot = null)
    {
        ApplicationRoot = applicationRoot ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ProjectsRoot);
        Directory.CreateDirectory(PluginsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFilePath) ?? ApplicationRoot);
    }

    public string ApplicationRoot { get; }

    public string LogDirectory => Path.Combine(ApplicationRoot, "logs");

    public string ProjectsRoot => Path.Combine(ApplicationRoot, "projects");

    public string PluginsDirectory => Path.Combine(ApplicationRoot, "plugins");

    public string DataDirectory => Path.Combine(ApplicationRoot, "data");

    public string DatabaseFilePath => Path.Combine(DataDirectory, "visionmaven.db");

    /// <summary>全局模型缓存目录（优先级低于工程内 models/）。</summary>
    public string GlobalModelsDirectory => Path.Combine(ApplicationRoot, "models");

    /// <summary>数据库备份目录。</summary>
    public string DatabaseBackupDirectory => Path.Combine(DataDirectory, "backup");
}
