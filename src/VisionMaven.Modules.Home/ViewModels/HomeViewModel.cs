using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using Prism.Commands;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;
using VisionMaven.Modules.Shared;
using VisionMaven.Modules.Shared.Interop;

namespace VisionMaven.Modules.Home.ViewModels;

/// <summary>工位卡片。</summary>
public sealed class StationCardViewModel
{
    public StationCardViewModel(string stationId, string name)
    {
        StationId = stationId;
        Name = name;
    }

    public string StationId { get; }

    public string Name { get; }

    public StationState State { get; set; }

    public string StateText { get; set; } = "已停止";

    public long Total { get; set; }

    public long OkCount { get; set; }

    public long NgCount { get; set; }

    public string YieldText { get; set; } = "-";

    public string CycleText { get; set; } = "-";

    public string Summary => $"{Name} · {StateText} · OK {OkCount} / NG {NgCount}";
}

/// <summary>报警行。</summary>
public sealed class AlarmRowViewModel
{
    public AlarmRowViewModel(AlarmRecord alarm)
    {
        Alarm = alarm;
    }

    public AlarmRecord Alarm { get; }

    public string Time => Alarm.RaisedAt.ToString("MM-dd HH:mm:ss");

    public AlarmLevel Level => Alarm.Level;

    public string Code => Alarm.Code;

    public string Message => Alarm.Message;

    public string StationId => Alarm.StationId ?? "-";

    public string AckText => Alarm.AcknowledgedAt.HasValue
        ? $"{Alarm.AcknowledgedAt.Value:HH:mm:ss} {Alarm.AcknowledgedBy}"
        : "未确认";
}

/// <summary>工位与绑定关系行。</summary>
public sealed class StationBindingRowViewModel
{
    public required string StationId { get; init; }

    public required string StationName { get; init; }

    public required string Cameras { get; init; }

    public required string Devices { get; init; }

    public required string FlowId { get; init; }

    public required string TriggerText { get; init; }
}

/// <summary>首页视图模型。</summary>
public sealed class HomeViewModel : PageViewModelBase
{
    private readonly IRuntimeCoordinator _runtime;
    private readonly ILivePreviewService _preview;
    private readonly IInspectionRepository _inspections;
    private readonly IAlarmRepository _alarms;

    private StationCardViewModel? _selectedStation;
    private BitmapSource? _previewImage;
    private string _lastResultText = "-";
    private InspectionResult _lastResult = InspectionResult.Unknown;
    private string _attachedCameraId = string.Empty;

    public HomeViewModel(
        IUserContext user,
        IActionBarHost actionBar,
        IRuntimeCoordinator runtime,
        IProjectSession project,
        ILivePreviewService preview,
        IInspectionRepository inspections,
        IAlarmRepository alarms)
        : base(user, actionBar)
    {
        _runtime = runtime;
        _preview = preview;
        _inspections = inspections;
        _alarms = alarms;
        Project = project;

        StartCommand = new DelegateCommand(async () => await SafeAsync(StartAsync, "启动失败").ConfigureAwait(true));
        StopCommand = new DelegateCommand(async () => await SafeAsync(StopAsync, "停止失败").ConfigureAwait(true));
        ResetCommand = new DelegateCommand(async () => await SafeAsync(ResetAsync, "复位失败").ConfigureAwait(true));
        TriggerCommand = new DelegateCommand(async () => await SafeAsync(TriggerAsync, "触发失败").ConfigureAwait(true));
        AcknowledgeAllCommand = new DelegateCommand(async () => await SafeAsync(AcknowledgeAllAsync).ConfigureAwait(true));
        AcknowledgeOneCommand = new DelegateCommand<AlarmRowViewModel>(async row =>
            await SafeAsync(() => AcknowledgeOneAsync(row), "确认失败").ConfigureAwait(true));
        ReloadCommand = new DelegateCommand(async () => await SafeAsync(ReloadAsync, "重新装配失败").ConfigureAwait(true));
        SelectStationCommand = new DelegateCommand<StationCardViewModel>(card => SelectedStation = card);

        _runtime.StationStateChanged += OnStationStateChanged;
        _runtime.StatisticsChanged += OnStatisticsChanged;
        _runtime.AlarmRaised += OnAlarmRaised;
        _preview.FrameReady += OnPreviewFrame;
        Project.Changed += OnProjectChanged;
    }

    public IProjectSession Project { get; }

    public override string Title => "首页";

    public ObservableCollection<StationCardViewModel> Stations { get; } = new();

    public ObservableCollection<AlarmRowViewModel> Alarms { get; } = new();

    public ObservableCollection<StationBindingRowViewModel> Bindings { get; } = new();

    public ObservableCollection<InspectionRecord> RecentRecords { get; } = new();

    public DelegateCommand StartCommand { get; }

    public DelegateCommand StopCommand { get; }

    public DelegateCommand ResetCommand { get; }

    public DelegateCommand TriggerCommand { get; }

    public DelegateCommand AcknowledgeAllCommand { get; }

    public DelegateCommand<AlarmRowViewModel> AcknowledgeOneCommand { get; }

    public DelegateCommand ReloadCommand { get; }

    public DelegateCommand<StationCardViewModel> SelectStationCommand { get; }

    public StationCardViewModel? SelectedStation
    {
        get => _selectedStation;
        set
        {
            if (SetProperty(ref _selectedStation, value))
            {
                AttachPreview(value);
            }
        }
    }

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public string LastResultText
    {
        get => _lastResultText;
        private set => SetProperty(ref _lastResultText, value);
    }

    public InspectionResult LastResult
    {
        get => _lastResult;
        private set => SetProperty(ref _lastResult, value);
    }

    protected override IEnumerable<PageAction> BuildActions()
    {
        var actions = new List<PageAction>();

        if (HasPermission(PermissionCodes.RuntimeStart))
        {
            actions.Add(new PageAction("runtime.start", "启动", StartCommand, "Play", 1));
        }

        if (HasPermission(PermissionCodes.RuntimeStop))
        {
            actions.Add(new PageAction("runtime.stop", "停止", StopCommand, "Stop", 2, true, "确定停止全部工位运行？"));
        }

        if (HasPermission(PermissionCodes.RuntimeReset))
        {
            actions.Add(new PageAction("runtime.reset", "复位", ResetCommand, "Restart"));
        }

        if (HasPermission(PermissionCodes.RuntimeTrigger))
        {
            actions.Add(new PageAction("runtime.trigger", "单次触发", TriggerCommand, "Camera"));
        }

        if (HasPermission(PermissionCodes.AlarmAck))
        {
            actions.Add(new PageAction("alarm.ack", "报警确认", AcknowledgeAllCommand, "BellCheck", 0, true, "确认全部未确认报警？"));
        }

        if (HasPermission(PermissionCodes.ProjectEdit))
        {
            actions.Add(new PageAction("project.reload", "重新装配", ReloadCommand, "Sync"));
        }

        return actions;
    }

    protected override async Task OnActivatedAsync(Prism.Navigation.Regions.NavigationContext context)
    {
        await SafeAsync(RefreshAsync).ConfigureAwait(true);
    }

    protected override Task OnDeactivatedAsync()
    {
        DetachPreview();
        return Task.CompletedTask;
    }

    protected override async Task RefreshAsync()
    {
        RebuildStations();
        RebuildBindings();
        await RefreshAlarmsAsync().ConfigureAwait(true);
        await RefreshRecordsAsync().ConfigureAwait(true);
    }

    private void OnProjectChanged(object? sender, EventArgs e)
        => Dispatch(() =>
        {
            RebuildStations();
            RebuildBindings();
        });

    private static void Dispatch(Action action)
    {
        var application = Application.Current;
        if (application is null || application.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        application.Dispatcher.Invoke(action);
    }

    private void RebuildStations()
    {
        var runtimes = _runtime.Stations;
        var previous = _selectedStation?.StationId;

        Stations.Clear();
        foreach (var station in runtimes)
        {
            var card = new StationCardViewModel(station.Config.StationId, station.Config.Name)
            {
                State = station.State,
                StateText = DescribeState(station.State)
            };
            ApplyStatistics(card, station.Statistics);
            Stations.Add(card);
        }

        SelectedStation = Stations.FirstOrDefault(card =>
                            string.Equals(card.StationId, previous, StringComparison.OrdinalIgnoreCase))
                        ?? Stations.FirstOrDefault();
    }

    private static string DescribeState(StationState state) => state switch
    {
        StationState.Running => "运行中",
        StationState.Starting => "启动中",
        StationState.Stopping => "停止中",
        StationState.Alarm => "报警",
        _ => "已停止"
    };

    private static void ApplyStatistics(StationCardViewModel card, StationStatistics statistics)
    {
        card.Total = statistics.Total;
        card.OkCount = statistics.OkCount;
        card.NgCount = statistics.NgCount;
        card.YieldText = statistics.Total == 0 ? "-" : $"{statistics.Yield * 100d:F2}%";
        card.CycleText = statistics.LastCycleMs <= 0 ? "-" : $"{statistics.LastCycleMs:F1} ms";
    }

    private void RebuildBindings()
    {
        Bindings.Clear();
        var project = Project.Current;
        if (project is null)
        {
            RaisePropertyChanged(nameof(Bindings));
            return;
        }

        foreach (var station in project.Stations)
        {
            Bindings.Add(new StationBindingRowViewModel
            {
                StationId = station.StationId,
                StationName = station.Name,
                Cameras = station.CameraIds.Count == 0 ? "-" : string.Join(", ", station.CameraIds),
                Devices = station.DeviceBindings.Count == 0 ? "-" : string.Join(", ", station.DeviceBindings),
                FlowId = string.IsNullOrWhiteSpace(station.FlowId) ? "-" : station.FlowId,
                TriggerText = $"{station.Trigger.Source} / {station.Trigger.Edge} / {station.Trigger.TimeoutMs}ms"
            });
        }
    }

    private async Task RefreshAlarmsAsync()
    {
        var pending = await _alarms.QueryUnacknowledgedAsync(100, CancellationToken.None).ConfigureAwait(true);
        Alarms.Clear();
        foreach (var alarm in pending)
        {
            Alarms.Add(new AlarmRowViewModel(alarm));
        }
    }

    private async Task RefreshRecordsAsync()
    {
        var project = Project.Current;
        if (project is null)
        {
            RecentRecords.Clear();
            return;
        }

        var records = await _inspections
            .QueryAsync(
                project.ProjectId,
                null,
                DateTimeOffset.Now.AddHours(-1),
                DateTimeOffset.Now,
                0,
                50,
                CancellationToken.None)
            .ConfigureAwait(true);

        RecentRecords.Clear();
        foreach (var record in records)
        {
            RecentRecords.Add(record);
        }
    }

    private void AttachPreview(StationCardViewModel? card)
    {
        DetachPreview();

        if (card is null)
        {
            PreviewImage = null;
            return;
        }

        var project = Project.Current;
        var station = project?.Stations.FirstOrDefault(item =>
            string.Equals(item.StationId, card.StationId, StringComparison.OrdinalIgnoreCase));

        var cameraId = station?.CameraIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            PreviewImage = null;
            return;
        }

        _attachedCameraId = cameraId;
        _preview.Attach(cameraId);
    }

    private void DetachPreview()
    {
        if (string.IsNullOrWhiteSpace(_attachedCameraId))
        {
            return;
        }

        _preview.Detach(_attachedCameraId);
        _attachedCameraId = string.Empty;
    }

    private void OnPreviewFrame(object? sender, LivePreviewEventArgs args)
    {
        if (!string.Equals(args.DeviceId, _attachedCameraId, StringComparison.OrdinalIgnoreCase))
        {
            args.Frame.Dispose();
            return;
        }

        var bitmap = ImageInterop.ToThumbnail(args.Frame.NativeImage, 960);
        args.Frame.Dispose();

        Dispatch(() => PreviewImage = bitmap);
    }

    private void OnStationStateChanged(object? sender, StationStateChangedEventArgs args)
        => Dispatch(() =>
        {
            var card = Stations.FirstOrDefault(item =>
                string.Equals(item.StationId, args.StationId, StringComparison.OrdinalIgnoreCase));

            if (card is null)
            {
                return;
            }

            card.State = args.NewState;
            card.StateText = DescribeState(args.NewState);
            RaisePropertyChanged(nameof(Stations));
        });

    private void OnStatisticsChanged(object? sender, StationStatisticsChangedEventArgs args)
        => Dispatch(() =>
        {
            var card = Stations.FirstOrDefault(item =>
                string.Equals(item.StationId, args.Statistics.StationId, StringComparison.OrdinalIgnoreCase));

            if (card is null)
            {
                return;
            }

            ApplyStatistics(card, args.Statistics);
            LastResultText = $"OK {args.Statistics.OkCount} / NG {args.Statistics.NgCount}";
            RaisePropertyChanged(nameof(Stations));
        });

    private void OnAlarmRaised(object? sender, AlarmRaisedEventArgs args)
        => Dispatch(() => Alarms.Insert(0, new AlarmRowViewModel(args.Alarm)));

    private async Task StartAsync()
    {
        await _runtime.StartAsync(CancellationToken.None).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task StopAsync()
    {
        await _runtime.StopAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task ResetAsync()
    {
        await _runtime.ResetAsync().ConfigureAwait(true);
        await RefreshAlarmsAsync().ConfigureAwait(true);
    }

    private async Task TriggerAsync()
    {
        var station = SelectedStation ?? Stations.FirstOrDefault();
        if (station is null)
        {
            StatusText = "没有可用工位";
            return;
        }

        var accepted = await _runtime.TriggerAsync(station.StationId, CancellationToken.None).ConfigureAwait(true);
        StatusText = accepted ? $"已触发 {station.StationId}" : $"触发被拒绝 {station.StationId}";
    }

    private async Task AcknowledgeAllAsync()
    {
        await _runtime.AcknowledgeAllAlarmsAsync().ConfigureAwait(true);
        await RefreshAlarmsAsync().ConfigureAwait(true);
    }

    private async Task AcknowledgeOneAsync(AlarmRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _runtime.AcknowledgeAlarmAsync(row.Alarm.Id).ConfigureAwait(true);
        await RefreshAlarmsAsync().ConfigureAwait(true);
    }

    private async Task ReloadAsync()
    {
        await Project.ReloadRuntimeAsync(CancellationToken.None).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }
}
