using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;
using VisionMaven.Core.Shell;
using VisionMaven.Modules.Shared.Icons;
using IDialogService = VisionMaven.Core.Shell.IDialogService;

namespace VisionMaven.App.ViewModels;

/// <summary>导航项视图模型。</summary>
public sealed class NavigationItemViewModel : BindableBase
{
    private bool _isSelected;

    public NavigationItemViewModel(NavigationItem item, ICommand selectCommand)
    {
        Item = item;
        SelectCommand = selectCommand;
    }

    public NavigationItem Item { get; }

    public ICommand SelectCommand { get; }

    public string Key => Item.Key;

    public string Title => Item.Title;

    public string Glyph => IconGlyphs.Resolve(Item.IconKey);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>操作按钮栏按钮视图模型。</summary>
public sealed class ActionBarItemViewModel : BindableBase
{
    private readonly ActionItem _item;
    private readonly Func<ActionItem, bool> _confirm;

    public ActionBarItemViewModel(ActionItem item, Func<ActionItem, bool> confirm)
    {
        _item = item;
        _confirm = confirm;
        ExecuteCommand = new DelegateCommand(Execute);
    }

    public ICommand ExecuteCommand { get; }

    public string Text => _item.Text;

    public string Glyph => IconGlyphs.Resolve(_item.IconKey);

    public bool HasIcon => !string.IsNullOrWhiteSpace(_item.IconKey);

    public ActionItemStyle Style => _item.Style;

    private void Execute()
    {
        if (_item.RequiresConfirm && !_confirm(_item))
        {
            return;
        }

        if (_item.Command.CanExecute(null))
        {
            _item.Command.Execute(null);
        }
    }
}

/// <summary>顶部菜单项视图模型。</summary>
public sealed class TopMenuItemViewModel
{
    public TopMenuItemViewModel(TopMenuItem item)
    {
        Item = item;
    }

    public TopMenuItem Item { get; }

    public string Text => Item.Text;

    public ICommand Command => Item.Command;

    public string? GestureText => Item.GestureText;

    public bool HasGesture => !string.IsNullOrWhiteSpace(Item.GestureText);

    public string Visibility => Item.IsSeparatorBefore ? "Visible" : "Collapsed";
}

/// <summary>日志条目视图模型。</summary>
public sealed class LogEntryViewModel
{
    public LogEntryViewModel(VisionLogEntry entry)
    {
        Entry = entry;
    }

    public VisionLogEntry Entry { get; }

    public string Time => Entry.Timestamp.ToString("HH:mm:ss.fff");

    public VisionLogLevel Level => Entry.Level;

    public string LevelText => Entry.Level switch
    {
        VisionLogLevel.Verbose => "VRB",
        VisionLogLevel.Debug => "DBG",
        VisionLogLevel.Information => "INF",
        VisionLogLevel.Warning => "WRN",
        VisionLogLevel.Error => "ERR",
        _ => "FTL"
    };

    public string Source => string.IsNullOrWhiteSpace(Entry.Source) ? "-" : Shorten(Entry.Source!);

    public string Message => Entry.Message;

    private static string Shorten(string source)
    {
        var index = source.LastIndexOf('.');
        return index >= 0 && index < source.Length - 1 ? source[(index + 1)..] : source;
    }
}

/// <summary>Shell 视图模型：三行式布局、导航、操作按钮栏、日志栏、状态栏与运行控制。</summary>
public sealed class ShellWindowViewModel : BindableBase, IDisposable
{
    private const int MaxLogEntries = 5000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    private readonly INavigationCatalog _navigation;
    private readonly IActionBarService _actionBar;
    private readonly ITopMenuRegistry _topMenu;
    private readonly IVisionLogSink _logSink;
    private readonly IUserContext _user;
    private readonly IProjectSession _project;
    private readonly IRuntimeCoordinator _runtime;
    private readonly IAuthenticationService _authentication;
    private readonly IDialogService _dialogs;
    private readonly IShellLayoutService _layout;
    private readonly ILogger<ShellWindowViewModel> _logger;
    private readonly DispatcherTimer _flushTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _sessionTimer;
    private readonly ConcurrentQueue<VisionLogEntry> _pending = new();

    private bool _isLogPaused;
    private bool _isNavCollapsed;
    private bool _isLogCollapsed;
    private bool _autoScroll = true;
    private VisionLogLevel _minimumLevel = VisionLogLevel.Information;
    private string _logKeyword = string.Empty;
    private string _currentTime = string.Empty;
    private string _projectName = "未打开工程";
    private string _userDisplay = "-";
    private string _deviceStatus = "设备 0/0";
    private string _cycleText = "-";
    private string _yieldText = "-";
    private string _runtimeStateText = "已停止";
    private StationState _runtimeState = StationState.Stopped;
    private int _unacknowledgedAlarms;
    private NavigationItemViewModel? _selectedNavigation;

    public ShellWindowViewModel(
        INavigationCatalog navigation,
        IActionBarService actionBar,
        ITopMenuRegistry topMenu,
        IVisionLogSink logSink,
        IUserContext user,
        IProjectSession project,
        IRuntimeCoordinator runtime,
        IAuthenticationService authentication,
        IDialogService dialogs,
        IShellLayoutService layout,
        ILogger<ShellWindowViewModel> logger)
    {
        _navigation = navigation;
        _actionBar = actionBar;
        _topMenu = topMenu;
        _logSink = logSink;
        _user = user;
        _project = project;
        _runtime = runtime;
        _authentication = authentication;
        _dialogs = dialogs;
        _layout = layout;
        _logger = logger;

        SelectNavigationCommand = new DelegateCommand<NavigationItemViewModel>(SelectNavigation);
        ToggleNavigationCommand = new DelegateCommand(
            () => _layout.IsNavigationVisible = !_layout.IsNavigationVisible);
        ToggleLogCollapsedCommand = new DelegateCommand(() => _layout.IsLogCollapsed = !_layout.IsLogCollapsed);
        ClearLogCommand = new DelegateCommand(_logSink.Clear);
        StartCommand = new DelegateCommand(async () => await SafeAsync(StartRuntimeAsync), () => CanStart)
            .ObservesProperty(() => CanStart);
        StopCommand = new DelegateCommand(async () => await SafeAsync(StopRuntimeAsync), () => CanStop)
            .ObservesProperty(() => CanStop);
        ResetCommand = new DelegateCommand(async () => await SafeAsync(ResetRuntimeAsync));
        TriggerCommand = new DelegateCommand(async () => await SafeAsync(TriggerAsync), () => CanTrigger)
            .ObservesProperty(() => CanTrigger);
        AcknowledgeAllCommand = new DelegateCommand(async () => await SafeAsync(AcknowledgeAllAsync));
        MinimizeCommand = new DelegateCommand(() => RaiseWindowCommand(WindowAction.Minimize));
        MaximizeCommand = new DelegateCommand(() => RaiseWindowCommand(WindowAction.Maximize));
        CloseCommand = new DelegateCommand(() => RaiseWindowCommand(WindowAction.Close));

        _navigation.ItemsChanged += (_, _) => Application.Current?.Dispatcher.Invoke(RebuildNavigation);
        _navigation.CurrentChanged += (_, key) => Application.Current?.Dispatcher.Invoke(() => UpdateSelection(key));
        _actionBar.Changed += (_, args) => Application.Current?.Dispatcher.Invoke(() => RebuildActionBar(args.Items));
        _topMenu.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(RebuildMenus);
        _logSink.EntryAppended += OnLogEntryAppended;
        _project.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(RefreshProjectState);
        _runtime.SnapshotChanged += (_, _) => Application.Current?.Dispatcher.Invoke(RefreshRuntimeState);
        _runtime.AlarmRaised += (_, _) => Application.Current?.Dispatcher.Invoke(RefreshRuntimeState);
        _user.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(RefreshUserState);
        _layout.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(SyncLayout);

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FlushInterval };
        _flushTimer.Tick += (_, _) => FlushLog();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _sessionTimer.Tick += async (_, _) => await CheckSessionAsync();

        RebuildNavigation();
        RebuildActionBar(_actionBar.Items);
        RebuildMenus();
        SyncLayout();
        RefreshUserState();
        RefreshProjectState();
        RefreshRuntimeState();
        UpdateClock();
    }

    /// <summary>窗口动作请求，由 View 的代码后置执行。</summary>
    public event EventHandler<WindowAction>? WindowActionRequested;

    /// <summary>自动滚动请求。</summary>
    public event EventHandler? ScrollToEndRequested;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();

    public ObservableCollection<ActionBarItemViewModel> ActionItems { get; } = new();

    public ObservableCollection<LogEntryViewModel> LogEntries { get; } = new();

    public ObservableCollection<TopMenuItemViewModel> ProjectMenuItems { get; } = new();

    public ObservableCollection<TopMenuItemViewModel> ViewMenuItems { get; } = new();

    public ObservableCollection<TopMenuItemViewModel> ToolsMenuItems { get; } = new();

    public ObservableCollection<TopMenuItemViewModel> HelpMenuItems { get; } = new();

    public IReadOnlyList<VisionLogLevel> LogLevels { get; } = new[]
    {
        VisionLogLevel.Verbose,
        VisionLogLevel.Debug,
        VisionLogLevel.Information,
        VisionLogLevel.Warning,
        VisionLogLevel.Error,
        VisionLogLevel.Fatal
    };

    public ICommand SelectNavigationCommand { get; }

    public ICommand ToggleNavigationCommand { get; }

    public ICommand ToggleLogCollapsedCommand { get; }

    public ICommand ClearLogCommand { get; }

    public ICommand StartCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand TriggerCommand { get; }

    public ICommand AcknowledgeAllCommand { get; }

    public ICommand MinimizeCommand { get; }

    public ICommand MaximizeCommand { get; }

    public ICommand CloseCommand { get; }

    public string ApplicationTitle => "VisionMaven";

    public NavigationItemViewModel? SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (SetProperty(ref _selectedNavigation, value))
            {
                RaisePropertyChanged(nameof(BreadcrumbText));
                if (value is not null)
                {
                    _navigation.NavigateTo(value.Key);
                }
            }
        }
    }

    public string BreadcrumbText => SelectedNavigation?.Title ?? string.Empty;

    public bool IsNavCollapsed
    {
        get => _isNavCollapsed;
        set
        {
            if (SetProperty(ref _isNavCollapsed, value))
            {
                RaisePropertyChanged(nameof(NavToggleText));
            }
        }
    }

    public string NavToggleText => IsNavCollapsed ? "»" : "«";

    public bool IsLogCollapsed
    {
        get => _isLogCollapsed;
        set
        {
            if (SetProperty(ref _isLogCollapsed, value))
            {
                RaisePropertyChanged(nameof(LogToggleText));
                if (!value)
                {
                    FlushLog();
                }
            }
        }
    }

    public string LogToggleText => IsLogCollapsed ? "▲" : "▼";

    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    public VisionLogLevel MinimumLevel
    {
        get => _minimumLevel;
        set
        {
            if (SetProperty(ref _minimumLevel, value))
            {
                RebuildLog();
            }
        }
    }

    public string LogKeyword
    {
        get => _logKeyword;
        set
        {
            if (SetProperty(ref _logKeyword, value))
            {
                RebuildLog();
            }
        }
    }

    public string CurrentTime
    {
        get => _currentTime;
        private set => SetProperty(ref _currentTime, value);
    }

    public string ProjectName
    {
        get => _projectName;
        private set => SetProperty(ref _projectName, value);
    }

    public string UserDisplay
    {
        get => _userDisplay;
        private set => SetProperty(ref _userDisplay, value);
    }

    public string DeviceStatus
    {
        get => _deviceStatus;
        private set => SetProperty(ref _deviceStatus, value);
    }

    public string CycleText
    {
        get => _cycleText;
        private set => SetProperty(ref _cycleText, value);
    }

    public string YieldText
    {
        get => _yieldText;
        private set => SetProperty(ref _yieldText, value);
    }

    public string RuntimeStateText
    {
        get => _runtimeStateText;
        private set => SetProperty(ref _runtimeStateText, value);
    }

    public StationState RuntimeState
    {
        get => _runtimeState;
        private set
        {
            if (SetProperty(ref _runtimeState, value))
            {
                RuntimeStateText = value switch
                {
                    StationState.Running => "运行中",
                    StationState.Starting => "启动中",
                    StationState.Stopping => "停止中",
                    StationState.Alarm => "报警",
                    _ => "已停止"
                };

                RaisePropertyChanged(nameof(CanStart));
                RaisePropertyChanged(nameof(CanStop));
                RaisePropertyChanged(nameof(CanTrigger));
            }
        }
    }

    public int UnacknowledgedAlarms
    {
        get => _unacknowledgedAlarms;
        private set => SetProperty(ref _unacknowledgedAlarms, value);
    }

    public string AlarmText => $"未确认报警 {UnacknowledgedAlarms}";

    public bool CanStart => RuntimeState is StationState.Stopped && _project.Current is not null;

    public bool CanStop => RuntimeState is StationState.Running or StationState.Starting or StationState.Alarm;

    public bool CanTrigger => RuntimeState is StationState.Running or StationState.Alarm;

    /// <summary>启动时钟与日志刷新。</summary>
    public void Start()
    {
        _flushTimer.Start();
        _clockTimer.Start();
        _sessionTimer.Start();
        FlushLog();
    }

    private void UpdateClock()
    {
        CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (_project.IsDirty)
        {
            ProjectName = $"{_project.Current?.Name} *";
        }
    }

    private async Task CheckSessionAsync()
    {
        try
        {
            await _authentication.CheckTimeoutAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "会话超时检查失败");
        }
    }

    private void RebuildNavigation()
    {
        var visible = _navigation.VisibleItems(_user);
        NavigationItems.Clear();

        foreach (var item in visible)
        {
            NavigationItems.Add(new NavigationItemViewModel(item, SelectNavigationCommand));
        }

        UpdateSelection(_navigation.CurrentKey);

        if (_selectedNavigation is null && NavigationItems.Count > 0)
        {
            SelectedNavigation = NavigationItems[0];
        }
    }

    private void UpdateSelection(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        foreach (var item in NavigationItems)
        {
            item.IsSelected = string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase);
        }

        var matched = NavigationItems.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

        if (matched is not null && !ReferenceEquals(matched, _selectedNavigation))
        {
            _selectedNavigation = matched;
            RaisePropertyChanged(nameof(SelectedNavigation));
            RaisePropertyChanged(nameof(BreadcrumbText));
        }
    }

    private void SelectNavigation(NavigationItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedNavigation = item;
    }

    private void RebuildActionBar(IReadOnlyList<ActionItem> items)
    {
        ActionItems.Clear();
        foreach (var item in items)
        {
            ActionItems.Add(new ActionBarItemViewModel(item, ConfirmAction));
        }
    }

    private bool ConfirmAction(ActionItem item)
        => _dialogs.Confirm(
            string.IsNullOrWhiteSpace(item.ConfirmMessage) ? $"确定执行「{item.Text}」？" : item.ConfirmMessage!,
            "操作确认");

    private void RebuildMenus()
    {
        Fill(ProjectMenuItems, _topMenu.Items(TopMenuGroup.Project));
        Fill(ViewMenuItems, _topMenu.Items(TopMenuGroup.View));
        Fill(ToolsMenuItems, _topMenu.Items(TopMenuGroup.Tools));
        Fill(HelpMenuItems, _topMenu.Items(TopMenuGroup.Help));
    }

    private static void Fill(ObservableCollection<TopMenuItemViewModel> target, IReadOnlyList<TopMenuItem> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(new TopMenuItemViewModel(item));
        }
    }

    private void OnLogEntryAppended(object? sender, VisionLogEntry entry)
    {
        if (_isLogCollapsed)
        {
            // 折叠时暂停 UI 刷新，但仍入缓冲；展开后一次性补齐。
            _pending.Enqueue(entry);
            _isLogPaused = true;
            return;
        }

        _pending.Enqueue(entry);
    }

    private void FlushLog()
    {
        if (_pending.IsEmpty && !_isLogPaused)
        {
            return;
        }

        var appended = false;
        while (_pending.TryDequeue(out var entry))
        {
            if (!Matches(entry))
            {
                continue;
            }

            LogEntries.Add(new LogEntryViewModel(entry));
            appended = true;
        }

        while (LogEntries.Count > MaxLogEntries)
        {
            LogEntries.RemoveAt(0);
        }

        _isLogPaused = false;

        if (appended && AutoScroll)
        {
            ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RebuildLog()
    {
        LogEntries.Clear();
        foreach (var entry in _logSink.Snapshot().Where(Matches))
        {
            LogEntries.Add(new LogEntryViewModel(entry));
        }

        if (AutoScroll)
        {
            ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool Matches(VisionLogEntry entry)
    {
        if (entry.Level < MinimumLevel)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(LogKeyword)
               || entry.Message.Contains(LogKeyword, StringComparison.OrdinalIgnoreCase)
               || (entry.Source?.Contains(LogKeyword, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>把布局开关同步到视图属性（由视图菜单与折叠按钮共同驱动）。</summary>
    private void SyncLayout()
    {
        IsNavCollapsed = !_layout.IsNavigationVisible;
        IsLogCollapsed = _layout.IsLogCollapsed;
    }

    private void RefreshUserState()
    {
        var session = _user.Current;
        UserDisplay = session is null ? "未登录" : $"{session.DisplayName}（{session.RoleName}）";
    }

    private void RefreshProjectState()
    {
        var current = _project.Current;
        ProjectName = current is null ? "未打开工程" : current.Name;
        RaisePropertyChanged(nameof(CanStart));

        if (current is null)
        {
            DeviceStatus = "设备 0/0";
            return;
        }

        var deviceIds = current.AllDevices().Select(device => device.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var enabled = current.AllDevices().Count(device => device.Enabled);
        DeviceStatus = $"设备 {enabled}/{deviceIds}";
    }

    private void RefreshRuntimeState()
    {
        var snapshot = _runtime.Snapshot;
        UnacknowledgedAlarms = snapshot.UnacknowledgedAlarms;
        RaisePropertyChanged(nameof(AlarmText));

        var hasAlarm = _runtime.Stations.Any(station => station.State == StationState.Alarm);
        RuntimeState = hasAlarm
            ? StationState.Alarm
            : _runtime.IsRunning ? StationState.Running : StationState.Stopped;

        var stations = snapshot.Stations;
        if (stations.Count == 0)
        {
            CycleText = "-";
            YieldText = "-";
            return;
        }

        var total = stations.Sum(station => station.Total);
        var ok = stations.Sum(station => station.OkCount);
        var cycle = stations.Where(station => station.LastCycleMs > 0).ToArray();

        CycleText = cycle.Length == 0 ? "-" : $"{cycle.Average(station => station.LastCycleMs):F1} ms";
        YieldText = total == 0 ? "-" : $"{(double)ok / total * 100d:F2}%";
    }

    private async Task StartRuntimeAsync()
    {
        try
        {
            await _runtime.StartAsync(CancellationToken.None).ConfigureAwait(false);
            RefreshRuntimeState();
        }
        catch (VisionMavenException ex)
        {
            _dialogs.ShowError(ex.Message, "启动失败");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, "启动失败");
        }
    }

    private async Task StopRuntimeAsync()
    {
        try
        {
            await _runtime.StopAsync().ConfigureAwait(false);
            RefreshRuntimeState();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, "停止失败");
        }
    }

    private async Task ResetRuntimeAsync()
    {
        try
        {
            await _runtime.ResetAsync().ConfigureAwait(false);
            RefreshRuntimeState();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, "复位失败");
        }
    }

    private async Task TriggerAsync()
    {
        var station = _runtime.Stations.FirstOrDefault();
        if (station is null)
        {
            return;
        }

        try
        {
            await _runtime.TriggerAsync(station.Config.StationId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, "触发失败");
        }
    }

    private async Task AcknowledgeAllAsync()
    {
        await _runtime.AcknowledgeAllAlarmsAsync().ConfigureAwait(false);
        RefreshRuntimeState();
    }

    private async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (VisionMavenException ex)
        {
            _dialogs.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "运行时操作失败");
            _dialogs.ShowError(ex.Message);
        }
    }

    private void RaiseWindowCommand(WindowAction action)
        => WindowActionRequested?.Invoke(this, action);

    public void Dispose()
    {
        _flushTimer.Stop();
        _clockTimer.Stop();
        _sessionTimer.Stop();
        _logSink.EntryAppended -= OnLogEntryAppended;
    }
}

/// <summary>窗口动作。</summary>
public enum WindowAction
{
    Minimize = 0,
    Maximize = 1,
    Close = 2
}
