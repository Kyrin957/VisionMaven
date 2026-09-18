using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.Logging;
using Prism.Commands;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Shell;
using IDialogService = VisionMaven.Core.Shell.IDialogService;

namespace VisionMaven.Modules.Project;

/// <summary>项目菜单命令集合：新建 / 打开 / 保存 / 另存 / 导入导出 / 最近工程 / 退出。</summary>
public sealed class ProjectCommands
{
    private readonly IProjectSession _session;
    private readonly IProjectConfigurationService _configuration;
    private readonly IDialogService _dialogs;
    private readonly ITextInputDialogService _inputs;
    private readonly INavigationCatalog _navigation;
    private readonly ILogPathProvider _paths;

    public ProjectCommands(
        IProjectSession session,
        IProjectConfigurationService configuration,
        IDialogService dialogs,
        ITextInputDialogService inputs,
        INavigationCatalog navigation,
        ILogPathProvider paths)
    {
        _session = session;
        _configuration = configuration;
        _dialogs = dialogs;
        _inputs = inputs;
        _navigation = navigation;
        _paths = paths;

        NewProjectCommand = new DelegateCommand(async () => await ExecuteAsync(NewProjectAsync).ConfigureAwait(true));
        OpenProjectCommand = new DelegateCommand(async () => await ExecuteAsync(OpenProjectAsync).ConfigureAwait(true));
        SaveCommand = new DelegateCommand(async () => await ExecuteAsync(SaveAsync).ConfigureAwait(true));
        SaveAsCommand = new DelegateCommand(async () => await ExecuteAsync(SaveAsAsync).ConfigureAwait(true));
        ImportCommand = new DelegateCommand(async () => await ExecuteAsync(ImportAsync).ConfigureAwait(true));
        ExportCommand = new DelegateCommand(async () => await ExecuteAsync(ExportAsync).ConfigureAwait(true));
        RecentCommand = new DelegateCommand(async () => await ExecuteAsync(ShowRecentAsync).ConfigureAwait(true));
        ExitCommand = new DelegateCommand(Exit);
    }

    public DelegateCommand NewProjectCommand { get; }

    public DelegateCommand OpenProjectCommand { get; }

    public DelegateCommand SaveCommand { get; }

    public DelegateCommand SaveAsCommand { get; }

    public DelegateCommand ImportCommand { get; }

    public DelegateCommand ExportCommand { get; }

    public DelegateCommand RecentCommand { get; }

    public DelegateCommand ExitCommand { get; }

    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    private async Task NewProjectAsync()
    {
        var values = _inputs.RequestInputs(
            "新建工程",
            new[]
            {
                new TextInputField("name", "工程名称", $"工程 {DateTime.Now:MMdd-HHmm}"),
                new TextInputField("code", "工程编号", string.Empty)
            });

        if (values is null)
        {
            return;
        }

        var name = values.TryGetValue("name", out var typed) && !string.IsNullOrWhiteSpace(typed)
            ? typed.Trim()
            : $"工程 {DateTime.Now:MMdd-HHmm}";
        values.TryGetValue("code", out var code);

        await _session.CreateAsync(name, code, CancellationToken.None).ConfigureAwait(true);
        _navigation.NavigateTo("home");
    }

    private async Task OpenProjectAsync()
    {
        var projects = _configuration.ListProjects();
        if (projects.Count == 0)
        {
            _dialogs.ShowInfo("projects 目录下暂无工程");
            return;
        }

        var names = string.Join(Environment.NewLine, projects.Select(project => $"{project.ProjectId}  {project.Name}"));
        _dialogs.ShowInfo(names, "已有工程");

        var values = _inputs.RequestInputs(
            "打开工程",
            new[] { new TextInputField("projectId", "工程标识", projects[0].ProjectId) });

        if (values is null || !values.TryGetValue("projectId", out var projectId) || string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        await _session.OpenAsync(projectId.Trim(), CancellationToken.None).ConfigureAwait(true);
        _navigation.NavigateTo("home");
    }

    private async Task SaveAsync()
    {
        if (_session.Current is null)
        {
            _dialogs.ShowWarning("尚未打开工程");
            return;
        }

        await _session.SaveAsync(CancellationToken.None).ConfigureAwait(true);
        _dialogs.ShowInfo($"已保存：{_session.Current.Name}");
    }

    private async Task SaveAsAsync()
    {
        var current = _session.Current;
        if (current is null)
        {
            _dialogs.ShowWarning("尚未打开工程");
            return;
        }

        var values = _inputs.RequestInputs(
            "另存为",
            new[]
            {
                new TextInputField("name", "工程名称", current.Name + "-副本"),
                new TextInputField("code", "工程编号", current.Code)
            });

        if (values is null)
        {
            return;
        }

        values.TryGetValue("name", out var name);
        values.TryGetValue("code", out var code);

        await _session.SaveAsAsync(name ?? current.Name, code, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task ImportAsync()
    {
        var file = _dialogs.OpenFile("工程包|*.vmproj", "导入工程");
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        var config = await _session.ImportAsync(file, CancellationToken.None).ConfigureAwait(true);
        _navigation.NavigateTo("home");
        _dialogs.ShowInfo($"已导入工程：{config.Name}");
    }

    private async Task ExportAsync()
    {
        var current = _session.Current;
        if (current is null)
        {
            _dialogs.ShowWarning("尚未打开工程");
            return;
        }

        var file = _dialogs.SaveFile("工程包|*.vmproj", $"{current.ProjectId}.vmproj", "导出工程");
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        await _session.ExportAsync(file, CancellationToken.None).ConfigureAwait(true);
        _dialogs.ShowInfo($"已导出：{file}");
    }

    private async Task ShowRecentAsync()
    {
        var recent = _session.Recent;
        if (recent.Count == 0)
        {
            _dialogs.ShowInfo("暂无最近工程");
            return;
        }

        var values = _inputs.RequestInputs(
            "最近工程",
            new[] { new TextInputField("projectId", "工程标识", recent[0].ProjectId) });

        if (values is null || !values.TryGetValue("projectId", out var projectId) || string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        await _session.OpenAsync(projectId.Trim(), CancellationToken.None).ConfigureAwait(true);
        _navigation.NavigateTo("home");
    }

    private void Exit()
    {
        if (_session.IsDirty && !_dialogs.Confirm($"工程 {_session.Current?.Name} 存在未保存的改动，确定退出？", "退出"))
        {
            return;
        }

        Application.Current?.Shutdown();
    }

    /// <summary>打开日志目录。</summary>
    public void OpenLogDirectory()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", _paths.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, "打开日志目录失败");
        }
    }

    /// <summary>备份数据库到 data/backup。</summary>
    public void BackupDatabase()
    {
        try
        {
            var source = _paths.DatabaseFilePath;
            if (!File.Exists(source))
            {
                _dialogs.ShowWarning("数据库尚未创建");
                return;
            }

            var directory = Path.Combine(Path.GetDirectoryName(source) ?? ".", "backup");
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, $"visionmaven-{DateTime.Now:yyyyMMddHHmmss}.db");
            File.Copy(source, target, overwrite: true);
            _dialogs.ShowInfo($"已备份到：{target}");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, "数据库备份失败");
        }
    }

    /// <summary>显示版本信息。</summary>
    public void ShowAbout()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "1.0.0";
        _dialogs.ShowInfo(
            $"VisionMaven {version}{Environment.NewLine}生产线机器视觉与深度学习模型部署框架",
            "关于");
    }
}

/// <summary>工具菜单命令集合。</summary>
public sealed class ToolsCommands
{
    private readonly IDeviceDriverRegistry _drivers;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ToolsCommands> _logger;
    private readonly ProjectCommands _project;

    public ToolsCommands(
        IDeviceDriverRegistry drivers,
        IDialogService dialogs,
        ProjectCommands project,
        ILogger<ToolsCommands> logger)
    {
        _drivers = drivers;
        _dialogs = dialogs;
        _project = project;
        _logger = logger;

        DiscoverCommand = new DelegateCommand(Discover);
        OpenLogDirectoryCommand = new DelegateCommand(_project.OpenLogDirectory);
        BackupDatabaseCommand = new DelegateCommand(_project.BackupDatabase);
    }

    public DelegateCommand DiscoverCommand { get; }

    public DelegateCommand OpenLogDirectoryCommand { get; }

    public DelegateCommand BackupDatabaseCommand { get; }

    private void Discover()
    {
        var added = _drivers.Rescan();
        var available = _drivers.Drivers.Where(descriptor => descriptor.IsAvailable).ToArray();
        var unavailable = _drivers.Drivers.Where(descriptor => !descriptor.IsAvailable).ToArray();

        foreach (var descriptor in unavailable)
        {
            _logger.LogWarning(
                "驱动不可用：{DriverKey} - {Reason}",
                descriptor.DriverKey,
                descriptor.UnavailableReason);
        }

        var message = string.Join(Environment.NewLine, new[]
        {
            $"新增插件：{added}",
            $"可用驱动：{available.Length}",
            string.Join(", ", available.Select(descriptor => descriptor.DriverKey)),
            $"不可用驱动：{unavailable.Length}",
            string.Join(", ", unavailable.Select(descriptor => $"{descriptor.DriverKey}（{descriptor.UnavailableReason}）"))
        });

        _dialogs.ShowInfo(message, "设备发现");
    }
}

/// <summary>视图菜单命令集合。</summary>
public sealed class ViewCommands
{
    private readonly IShellLayoutService _layout;
    private readonly IVisionLogSink _logSink;

    public ViewCommands(IShellLayoutService layout, IVisionLogSink logSink)
    {
        _layout = layout;
        _logSink = logSink;

        ToggleNavigationCommand = new DelegateCommand(
            () => _layout.IsNavigationVisible = !_layout.IsNavigationVisible);
        ToggleLogCommand = new DelegateCommand(() => _layout.IsLogCollapsed = !_layout.IsLogCollapsed);
        ClearLogCommand = new DelegateCommand(_logSink.Clear);
    }

    public DelegateCommand ToggleNavigationCommand { get; }

    public DelegateCommand ToggleLogCommand { get; }

    public DelegateCommand ClearLogCommand { get; }
}
