using System.Windows;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Events;
using VisionMaven.Core.Exceptions;
using VisionMaven.Core.Shell;
using VisionMaven.Flow.Runtime;

namespace VisionMaven.App.Services;

/// <summary>工程会话实现：加载 / 保存 / 导入导出，并驱动运行时装配。</summary>
public sealed class ProjectSession : IProjectSession
{
    private const int MaxRecent = 10;

    private readonly IProjectConfigurationService _configuration;
    private readonly IRuntimeCoordinator _runtime;
    private readonly IProjectAccessor _accessor;
    private readonly IProjectFileStorage _storage;
    private readonly IVisionEventBus _events;
    private readonly IAuditService _audit;
    private readonly ILogger<ProjectSession> _logger;
    private readonly List<ProjectInfo> _recent = new();

    public ProjectSession(
        IProjectConfigurationService configuration,
        IRuntimeCoordinator runtime,
        IProjectAccessor accessor,
        IProjectFileStorage storage,
        IVisionEventBus events,
        IAuditService audit,
        ILogger<ProjectSession> logger)
    {
        _configuration = configuration;
        _runtime = runtime;
        _accessor = accessor;
        _storage = storage;
        _events = events;
        _audit = audit;
        _logger = logger;
    }

    public ProjectConfig? Current { get; private set; }

    public IReadOnlyList<ProjectInfo> Recent => _recent.ToArray();

    public bool IsDirty { get; private set; }

    public event EventHandler? Changed;

    public async Task<ProjectConfig> OpenAsync(string projectId, CancellationToken ct)
    {
        var config = await _configuration.LoadAsync(projectId, ct).ConfigureAwait(false)
                     ?? throw new ConfigurationException(ErrorCodes.ConfigNotFound, $"工程不存在：{projectId}");

        var validation = _configuration.Validate(config);
        foreach (var issue in validation.Issues)
        {
            if (issue.IsFatal)
            {
                _logger.LogError("工程校验失败：{Message}", issue.Message);
            }
            else
            {
                _logger.LogWarning("工程校验提示：{Message}", issue.Message);
            }
        }

        if (!validation.IsValid)
        {
            throw new ConfigurationException(
                ErrorCodes.ConfigParseFailed,
                $"工程配置校验未通过：{validation.Fatals.FirstOrDefault()?.Message}");
        }

        Current = config;
        _accessor.Set(config);
        IsDirty = false;

        await _runtime.LoadProjectAsync(config, ct).ConfigureAwait(false);
        RememberRecent(config.ProjectId);

        _events.Publish(new ProjectOpenedEvent(config.ProjectId, config.Name));
        await _audit.WriteAsync("打开工程", config.ProjectId, config.Name, ct).ConfigureAwait(false);

        Changed?.Invoke(this, EventArgs.Empty);
        return config;
    }

    public async Task<ProjectConfig> CreateAsync(string name, string? code, CancellationToken ct)
    {
        var config = await _configuration.CreateAsync(name, code, ct).ConfigureAwait(false);
        await _configuration.SaveAsync(config, ct).ConfigureAwait(false);

        Current = config;
        _accessor.Set(config);
        IsDirty = false;

        await _runtime.LoadProjectAsync(config, ct).ConfigureAwait(false);
        RememberRecent(config.ProjectId);

        _events.Publish(new ProjectOpenedEvent(config.ProjectId, config.Name));
        await _audit.WriteAsync("新建工程", config.ProjectId, config.Name, ct).ConfigureAwait(false);

        Changed?.Invoke(this, EventArgs.Empty);
        return config;
    }

    public async Task CloseAsync()
    {
        var current = Current;
        if (current is null)
        {
            return;
        }

        await _runtime.StopAsync().ConfigureAwait(false);

        Current = null;
        _accessor.Set(null);
        IsDirty = false;

        _events.Publish(new ProjectClosedEvent(current.ProjectId));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        var current = Current
                      ?? throw new ConfigurationException(ErrorCodes.ConfigNotFound, "尚未打开工程");

        current.UpdatedAt = DateTimeOffset.Now;
        await _configuration.SaveAsync(current, ct).ConfigureAwait(false);

        IsDirty = false;
        _events.Publish(new ProjectSavedEvent(current.ProjectId));
        _events.Publish(new ProjectDirtyChangedEvent(current.ProjectId, false));
        await _audit.WriteAsync("保存工程", current.ProjectId, null, ct).ConfigureAwait(false);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsAsync(string name, string? code, CancellationToken ct)
    {
        var current = Current
                      ?? throw new ConfigurationException(ErrorCodes.ConfigNotFound, "尚未打开工程");

        var created = await _configuration.CreateAsync(name, code, ct).ConfigureAwait(false);
        created.Cameras = current.Cameras;
        created.LightControllers = current.LightControllers;
        created.CommLinks = current.CommLinks;
        created.ModelLibrary = current.ModelLibrary;
        created.Flows = current.Flows;
        created.Stations = current.Stations;
        created.Parameters = current.Parameters;

        await _configuration.SaveAsync(created, ct).ConfigureAwait(false);
        Current = created;
        _accessor.Set(created);
        IsDirty = false;

        await _runtime.LoadProjectAsync(created, ct).ConfigureAwait(false);
        RememberRecent(created.ProjectId);

        _events.Publish(new ProjectOpenedEvent(created.ProjectId, created.Name));
        await _audit.WriteAsync("另存工程", created.ProjectId, name, ct).ConfigureAwait(false);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ProjectConfig> ImportAsync(string packageFile, CancellationToken ct)
    {
        var imported = await _configuration.ImportAsync(packageFile, ct).ConfigureAwait(false);
        await _audit.WriteAsync("导入工程", imported.ProjectId, packageFile, ct).ConfigureAwait(false);
        return await OpenAsync(imported.ProjectId, ct).ConfigureAwait(false);
    }

    public async Task ExportAsync(string destinationFile, CancellationToken ct)
    {
        var current = Current
                      ?? throw new ConfigurationException(ErrorCodes.ConfigNotFound, "尚未打开工程");

        await _configuration.ExportAsync(current.ProjectId, destinationFile, ct).ConfigureAwait(false);
        await _audit.WriteAsync("导出工程", current.ProjectId, destinationFile, ct).ConfigureAwait(false);
    }

    public void MarkDirty()
    {
        var current = Current;
        if (current is null || IsDirty)
        {
            return;
        }

        IsDirty = true;
        _events.Publish(new ProjectDirtyChangedEvent(current.ProjectId, true));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ProjectValidationResult Validate()
        => Current is null ? ProjectValidationResult.Ok : _configuration.Validate(Current);

    public async Task ReloadRuntimeAsync(CancellationToken ct)
    {
        var current = Current;
        if (current is null)
        {
            return;
        }

        await _runtime.LoadProjectAsync(current, ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string ResolvePath(string relativePath)
        => Current is null ? relativePath : _configuration.ResolvePath(Current.ProjectId, relativePath);

    public string GetFolder(ProjectFolder folder)
        => _storage.GetSubDirectory(Current?.ProjectId ?? string.Empty, folder);

    public void RememberRecent(string projectId)
    {
        var summary = _configuration.ListProjects()
            .FirstOrDefault(project => string.Equals(project.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));

        if (summary is null)
        {
            return;
        }

        _recent.RemoveAll(item => string.Equals(item.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, summary);

        while (_recent.Count > MaxRecent)
        {
            _recent.RemoveAt(_recent.Count - 1);
        }
    }

    /// <summary>退出前校验未保存改动。</summary>
    public bool ConfirmDiscard()
    {
        if (!IsDirty)
        {
            return true;
        }

        var application = Application.Current;
        if (application is null)
        {
            return true;
        }

        return application.Dispatcher.Invoke(() => MessageBox.Show(
            application.MainWindow,
            $"工程 {Current?.Name} 存在未保存的改动，是否放弃？",
            "未保存的改动",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) == MessageBoxResult.OK);
    }
}
