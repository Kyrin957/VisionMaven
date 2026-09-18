using VisionMaven.Core.Configuration;

namespace VisionMaven.Flow.Runtime;

/// <summary>当前打开的工程配置访问入口（单进程单工程）。</summary>
public interface IProjectAccessor
{
    ProjectConfig? Current { get; }

    void Set(ProjectConfig? project);
}

/// <summary>工程配置访问实现。</summary>
public sealed class ProjectAccessor : IProjectAccessor
{
    private volatile ProjectConfig? _current;

    public ProjectConfig? Current => _current;

    public void Set(ProjectConfig? project) => _current = project;
}
