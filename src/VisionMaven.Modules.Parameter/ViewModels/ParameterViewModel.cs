using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Prism.Commands;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;
using VisionMaven.Modules.Shared;
using VisionMaven.Modules.Shared.Interop;

namespace VisionMaven.Modules.Parameter.ViewModels;

/// <summary>参数键值行。</summary>
public sealed class ParameterRowViewModel : Prism.Mvvm.BindableBase
{
    private string _value;

    public ParameterRowViewModel(string key, string value, string scope)
    {
        Key = key;
        _value = value;
        Scope = scope;
    }

    public string Key { get; }

    public string Scope { get; }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

/// <summary>参数设置页视图模型。</summary>
public sealed class ParameterViewModel : PageViewModelBase
{
    private static readonly (string Key, string Default)[] GlobalParameters =
    {
        (ParameterKeys.ResultImagePolicy, nameof(ResultImagePolicy.NgOnly)),
        (ParameterKeys.ImageRetentionDays, "30"),
        (ParameterKeys.StatisticsIntervalSec, "60"),
        (ParameterKeys.RecordRetentionDays, "90"),
        (ParameterKeys.AlarmRetentionDays, "365")
    };

    private readonly IDeviceSession _devices;
    private readonly IImageFrameFactory _frames;
    private readonly IRuntimeCoordinator _runtime;
    private StationConfig? _selectedStation;
    private IImageFrame? _sample;
    private BitmapSource? _previewImage;
    private string _sourceText = "未选择图像源";
    private string _resultText = "-";

    public ParameterViewModel(
        IUserContext user,
        IActionBarHost actionBar,
        IProjectSession project,
        IDeviceSession devices,
        IImageFrameFactory frames,
        IRuntimeCoordinator runtime)
        : base(user, actionBar)
    {
        Project = project;
        _devices = devices;
        _frames = frames;
        _runtime = runtime;

        LoadFileCommand = new DelegateCommand(() => SafeAsync(LoadFileAsync).ConfigureAwait(true));
        LoadCameraCommand = new DelegateCommand(() => SafeAsync(LoadCameraAsync).ConfigureAwait(true));
        RunCommand = new DelegateCommand(() => SafeAsync(RunAsync, "试运行失败").ConfigureAwait(true));
        SaveCommand = new DelegateCommand(() => SafeAsync(SaveAsync, "保存失败").ConfigureAwait(true));

        Project.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(Reload);
    }

    public IProjectSession Project { get; }

    public override string Title => "参数设置";

    public ObservableCollection<StationConfig> Stations { get; } = new();

    public ObservableCollection<ParameterRowViewModel> Rows { get; } = new();

    public ObservableCollection<NodeTiming> Timings { get; } = new();

    public DelegateCommand LoadFileCommand { get; }

    public DelegateCommand LoadCameraCommand { get; }

    public DelegateCommand RunCommand { get; }

    public DelegateCommand SaveCommand { get; }

    public StationConfig? SelectedStation
    {
        get => _selectedStation;
        set
        {
            if (SetProperty(ref _selectedStation, value))
            {
                RebuildRows();
            }
        }
    }

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public string SourceText
    {
        get => _sourceText;
        private set => SetProperty(ref _sourceText, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    protected override IEnumerable<PageAction> BuildActions()
    {
        yield return new PageAction("parameter.file", "离线图像", LoadFileCommand, "FileImage");

        if (HasPermission(PermissionCodes.NavDevice))
        {
            yield return new PageAction("parameter.camera", "相机取图", LoadCameraCommand, "Camera");
        }

        if (HasPermission(PermissionCodes.ParameterEdit))
        {
            yield return new PageAction("parameter.run", "执行", RunCommand, "Play", 1);
            yield return new PageAction("parameter.save", "保存到工程", SaveCommand, "ContentSave");
        }
    }

    protected override Task OnActivatedAsync(Prism.Navigation.Regions.NavigationContext context)
    {
        Reload();
        return Task.CompletedTask;
    }

    private void Reload()
    {
        Stations.Clear();
        var project = Project.Current;
        if (project is null)
        {
            Rows.Clear();
            return;
        }

        foreach (var station in project.Stations)
        {
            Stations.Add(station);
        }

        SelectedStation = Stations.FirstOrDefault();
        RebuildRows();
    }

    private void RebuildRows()
    {
        Rows.Clear();
        var project = Project.Current;
        if (project is null)
        {
            return;
        }

        foreach (var (key, fallback) in GlobalParameters)
        {
            Rows.Add(new ParameterRowViewModel(key, project.Parameters.GetGlobal(key, fallback), "全局"));
        }

        var stationId = SelectedStation?.StationId;
        if (string.IsNullOrWhiteSpace(stationId))
        {
            return;
        }

        Rows.Add(new ParameterRowViewModel(
            ParameterKeys.ConsecutiveNgAlarm,
            project.Parameters.GetStation(stationId, ParameterKeys.ConsecutiveNgAlarm, "3"),
            $"工位 {stationId}"));

        foreach (var pair in SelectedStation!.Parameters)
        {
            Rows.Add(new ParameterRowViewModel(pair.Key, pair.Value, $"工位 {stationId}"));
        }
    }

    private Task LoadFileAsync()
    {
        var file = ActionBar.OpenFile("图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif", "选择图像源");
        if (string.IsNullOrWhiteSpace(file))
        {
            return Task.CompletedTask;
        }

        _sample?.Dispose();
        _sample = _frames.DecodeFile("parameter-test", file);
        SourceText = $"文件 {Path.GetFileName(file)} {_sample.Width}x{_sample.Height}";
        PreviewImage = ImageInterop.ToThumbnail(_sample.NativeImage, 1280);
        return Task.CompletedTask;
    }

    private async Task LoadCameraAsync()
    {
        var cameraId = SelectedStation?.CameraIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            StatusText = "当前工位未绑定相机";
            return;
        }

        var camera = _devices.GetCamera(cameraId);
        if (camera is null)
        {
            StatusText = $"相机不可用：{cameraId}";
            return;
        }

        if (camera.State != DeviceState.Ready)
        {
            await camera.ConnectAsync(CancellationToken.None).ConfigureAwait(true);
        }

        if (!camera.IsGrabbing)
        {
            await camera.StartGrabbingAsync(CancellationToken.None).ConfigureAwait(true);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var frame = await camera.GetFrameAsync(cts.Token).ConfigureAwait(true);

        _sample?.Dispose();
        _sample = frame;
        SourceText = $"相机 {cameraId} 序号 {frame.SequenceNo}";
        PreviewImage = ImageInterop.ToThumbnail(frame.NativeImage, 1280);
    }

    private async Task RunAsync()
    {
        var project = Project.Current;
        var station = SelectedStation;
        if (project is null || station is null)
        {
            StatusText = "请先选择工位";
            return;
        }

        if (_sample is null)
        {
            StatusText = "请先选择图像源";
            return;
        }

        var runtime = RuntimeOf(station.StationId);
        if (runtime is null)
        {
            StatusText = "工位运行时未装配";
            return;
        }

        ApplyRowsToProject(project, station);

        var outcome = await runtime
            .RunOnceAsync(_sample, null, CancellationToken.None)
            .ConfigureAwait(true);

        Timings.Clear();
        foreach (var timing in outcome.Timings)
        {
            Timings.Add(timing);
        }

        ResultText = $"判定 {outcome.Result} 分数 {outcome.Score:F3} 总耗时 {outcome.ElapsedMs:F1} ms";

        try
        {
            PreviewImage = ImageInterop.ToThumbnail(outcome.OverlayImage, 1280);
        }
        finally
        {
            if (outcome.OverlayImage is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private IStationRuntime? RuntimeOf(string stationId)
        => _runtime.Stations.FirstOrDefault(runtime =>
            string.Equals(runtime.Config.StationId, stationId, StringComparison.OrdinalIgnoreCase));

    private void ApplyRowsToProject(ProjectConfig project, StationConfig station)
    {
        foreach (var row in Rows)
        {
            if (string.Equals(row.Scope, "全局", StringComparison.Ordinal))
            {
                project.Parameters.Global[row.Key] = row.Value;
            }
            else if (string.Equals(row.Key, ParameterKeys.ConsecutiveNgAlarm, StringComparison.OrdinalIgnoreCase))
            {
                if (!project.Parameters.PerStation.TryGetValue(station.StationId, out var map))
                {
                    map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    project.Parameters.PerStation[station.StationId] = map;
                }

                map[row.Key] = row.Value;
            }
            else
            {
                station.Parameters[row.Key] = row.Value;
            }
        }

        Project.MarkDirty();
    }

    private Task SaveAsync()
    {
        var project = Project.Current;
        var station = SelectedStation;
        if (project is null || station is null)
        {
            StatusText = "请先选择工位";
            return Task.CompletedTask;
        }

        ApplyRowsToProject(project, station);
        StatusText = "参数已写入工程，保存工程后持久化";
        return Task.CompletedTask;
    }
}

/// <summary>参数设置模块定义。</summary>
public sealed class ParameterModule : Prism.Modularity.IModule
{
    public void RegisterTypes(Prism.Ioc.IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterForNavigation<Views.ParameterView>("ParameterView");
        containerRegistry.Register<ParameterViewModel>();
    }

    public void OnInitialized(Prism.Ioc.IContainerProvider containerProvider)
    {
        containerProvider.Resolve<INavigationCatalog>().Register(new NavigationItem(
            Key: "parameter",
            Title: "参数设置",
            IconKey: "Tune",
            Order: 5,
            ViewName: "ParameterView",
            RequiredPermission: PermissionCodes.NavParameter));
    }
}
