using System.Windows.Input;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Shell;

/// <summary>操作按钮的视觉样式。</summary>
public enum ActionItemStyle
{
    Default = 0,
    Primary = 1,
    Danger = 2
}

/// <summary>操作按钮项：由当前导航页注入到全局操作按钮栏。</summary>
public sealed record ActionItem(
    string Key,
    string Text,
    ICommand Command,
    string? IconKey = null,
    ActionItemStyle Style = ActionItemStyle.Default,
    bool RequiresConfirm = false,
    string? ConfirmMessage = null);

/// <summary>操作按钮栏变更参数。</summary>
public sealed class ActionBarChangedEventArgs : EventArgs
{
    public ActionBarChangedEventArgs(IReadOnlyList<ActionItem> items)
    {
        Items = items;
    }

    public IReadOnlyList<ActionItem> Items { get; }
}

/// <summary>操作按钮栏：页面进入时注入按钮集合，离开时清空；Shell 只负责呈现与确认。</summary>
public interface IActionBarService
{
    IReadOnlyList<ActionItem> Items { get; }

    void Set(IEnumerable<ActionItem> items);

    void Clear();

    event EventHandler<ActionBarChangedEventArgs>? Changed;
}

/// <summary>导航项：模块自行注册，带排序与权限，权限不足则不显示。</summary>
public sealed record NavigationItem(
    string Key,
    string Title,
    string IconKey,
    int Order,
    string ViewName,
    string? RequiredPermission = null);

/// <summary>导航目录：模块自行注册，带排序与权限。</summary>
public interface INavigationCatalog
{
    void Register(NavigationItem item);

    /// <summary>按当前用户权限过滤并按 Order 排序。</summary>
    IReadOnlyList<NavigationItem> VisibleItems(IUserContext user);

    /// <summary>请求导航到指定 key（由 Shell 处理并驱动区域导航）。</summary>
    void NavigateTo(string key);

    NavigationItem? Find(string key);

    /// <summary>Shell 挂接的区域导航回调。</summary>
    Action<string>? NavigationRequested { get; set; }

    /// <summary>当前选中的导航键。</summary>
    string? CurrentKey { get; set; }

    event EventHandler? ItemsChanged;

    event EventHandler<string>? CurrentChanged;
}

/// <summary>顶部菜单分组。</summary>
public enum TopMenuGroup
{
    Project = 0,
    View = 1,
    Tools = 2,
    Help = 3
}

/// <summary>顶部菜单项。</summary>
public sealed record TopMenuItem(
    string Key,
    string Text,
    ICommand Command,
    string? GestureText = null,
    string? IconKey = null,
    bool IsSeparatorBefore = false);

/// <summary>顶部菜单注册表：模块可向四个菜单追加项目。</summary>
public interface ITopMenuRegistry
{
    void AddItem(TopMenuGroup group, TopMenuItem item);

    IReadOnlyList<TopMenuItem> Items(TopMenuGroup group);

    event EventHandler? Changed;
}

/// <summary>设备类型描述：设备管理页据此动态生成子页签并排序。</summary>
public sealed record DeviceTypeDescriptor(
    DeviceKind Kind,
    string Title,
    string IconKey,
    int Order);

/// <summary>设备类型注册表。</summary>
public interface IDeviceTypeRegistry
{
    void Register(DeviceTypeDescriptor descriptor);

    IReadOnlyList<DeviceTypeDescriptor> GetSubTabs();

    event EventHandler? Changed;
}

/// <summary>对话框服务：确认、提示与文件选择，统一由 Shell 呈现。</summary>
public interface IDialogService
{
    void ShowInfo(string message, string title = "提示");

    void ShowWarning(string message, string title = "警告");

    void ShowError(string message, string title = "错误");

    bool Confirm(string message, string title = "确认");

    string? OpenFile(string filter, string? title = null, string? initialDirectory = null);

    string? SaveFile(string filter, string? defaultFileName = null, string? title = null);

    string? SelectFolder(string? title = null);
}

/// <summary>Shell 布局开关：视图菜单与页面共用。</summary>
public interface IShellLayoutService
{
    bool IsNavigationVisible { get; set; }

    bool IsLogCollapsed { get; set; }

    event EventHandler? Changed;
}

/// <summary>简单文本输入框定义。</summary>
public sealed record TextInputField(string Key, string Label, string DefaultValue);

/// <summary>通用文本输入对话框：新建 / 另存工程、新增设备与链路等场景共用。</summary>
public interface ITextInputDialogService
{
    /// <summary>弹出输入对话框；取消返回 null。</summary>
    IReadOnlyDictionary<string, string>? RequestInputs(string title, IReadOnlyList<TextInputField> fields);
}

/// <summary>当前工程会话：打开 / 新建 / 保存 / 导入导出，并维护脏标记与运行时装配。</summary>
public interface IProjectSession
{
    /// <summary>当前打开的工程配置；未打开时为 null。</summary>
    Configuration.ProjectConfig? Current { get; }

    /// <summary>最近打开的工程（最多 10 个）。</summary>
    IReadOnlyList<ProjectInfo> Recent { get; }

    bool IsDirty { get; }

    event EventHandler? Changed;

    Task<Configuration.ProjectConfig> OpenAsync(string projectId, CancellationToken ct);

    Task<Configuration.ProjectConfig> CreateAsync(string name, string? code, CancellationToken ct);

    Task CloseAsync();

    Task SaveAsync(CancellationToken ct);

    Task SaveAsAsync(string name, string? code, CancellationToken ct);

    Task<Configuration.ProjectConfig> ImportAsync(string packageFile, CancellationToken ct);

    Task ExportAsync(string destinationFile, CancellationToken ct);

    void MarkDirty();

    /// <summary>按规则校验当前工程，返回全部问题。</summary>
    ProjectValidationResult Validate();

    /// <summary>重新装配运行时（设备与工位）。</summary>
    Task ReloadRuntimeAsync(CancellationToken ct);

    /// <summary>工程内相对路径解析为绝对路径。</summary>
    string ResolvePath(string relativePath);

    /// <summary>取工程子目录的绝对路径。</summary>
    string GetFolder(ProjectFolder folder);

    /// <summary>记录最近工程。</summary>
    void RememberRecent(string projectId);
}

/// <summary>工程概要，供「打开工程」与「最近工程」列表使用。</summary>
public sealed record ProjectInfo(string ProjectId, string Name, string Code, DateTimeOffset UpdatedAt, string DirectoryPath);

/// <summary>工程子目录。</summary>
public enum ProjectFolder
{
    Models = 0,
    Labels = 1,
    Templates = 2,
    Calibration = 3,
    Images = 4,
    Root = 5
}
