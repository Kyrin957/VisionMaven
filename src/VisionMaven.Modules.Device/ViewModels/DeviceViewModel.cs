using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using Prism.Commands;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;
using VisionMaven.Modules.Shared;
using VisionMaven.Modules.Shared.Interop;
using VisionMaven.Modules.Shared.Parameters;

namespace VisionMaven.Modules.Device.ViewModels;

/// <summary>设备行视图模型。</summary>
public sealed class DeviceRowViewModel : Prism.Mvvm.BindableBase
{
    private DeviceState _state;
    private string _message = string.Empty;

    public DeviceRowViewModel(DeviceNodeConfig config)
    {
        Config = config;
    }

    public DeviceNodeConfig Config { get; }

    public string DeviceId => Config.DeviceId;

    public string Name => Config.Name;

    public string DriverKey => Config.DriverKey;

    public DeviceKind Kind => Config.Kind;

    public bool Enabled => Config.Enabled;

    public DeviceState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                RaisePropertyChanged(nameof(StateText));
            }
        }
    }

    public string StateText => State switch
    {
        DeviceState.Ready => "已连接",
        DeviceState.Connecting => "连接中",
        DeviceState.Alarm => "报警",
        _ => "未连接"
    };

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }
}

/// <summary>子页签视图模型：由设备类型注册表动态生成。</summary>
public sealed class DeviceTabViewModel : Prism.Mvvm.BindableBase
{
    public DeviceTabViewModel(DeviceTypeDescriptor descriptor, IEnumerable<DeviceRowViewModel> devices)
    {
        Descriptor = descriptor;
        Devices = new ObservableCollection<DeviceRowViewModel>(devices);
    }

    public DeviceTypeDescriptor Descriptor { get; }

    public string Title => Descriptor.Title;

    public DeviceKind Kind => Descriptor.Kind;

    public ObservableCollection<DeviceRowViewModel> Devices { get; }
}

/// <summary>设备管理页视图模型。</summary>
public sealed class DeviceViewModel : PageViewModelBase
{
    private readonly IDeviceDriverRegistry _drivers;
    private readonly IDeviceTypeRegistry _types;
    private readonly IDeviceSession _session;
    private readonly ILivePreviewService _preview;
    private DeviceTabViewModel? _selectedTab;
    private DeviceRowViewModel? _selectedDevice;
    private BitmapSource? _previewImage;
    private string _attachedCamera = string.Empty;

    public DeviceViewModel(
        IUserContext user,
        IActionBarHost actionBar,
        IProjectSession project,
        IDeviceDriverRegistry drivers,
        IDeviceTypeRegistry types,
        IDeviceSession session,
        ILivePreviewService preview)
        : base(user, actionBar)
    {
        Project = project;
        _drivers = drivers;
        _types = types;
        _session = session;
        _preview = preview;

        AddCommand = new DelegateCommand(() => SafeAsync(AddAsync).ConfigureAwait(true));
        RemoveCommand = new DelegateCommand(() => SafeAsync(RemoveAsync, "删除失败").ConfigureAwait(true));
        ConnectCommand = new DelegateCommand(() => SafeAsync(ConnectAsync, "连接失败").ConfigureAwait(true));
        DisconnectCommand = new DelegateCommand(() => SafeAsync(DisconnectAsync, "断开失败").ConfigureAwait(true));
        DiscoverCommand = new DelegateCommand(() => SafeAsync(DiscoverAsync).ConfigureAwait(true));
        ApplyCommand = new DelegateCommand(() => SafeAsync(ApplyAsync).ConfigureAwait(true));

        _session.DeviceStateChanged += OnDeviceStateChanged;
        _preview.FrameReady += OnPreviewFrame;
        Project.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(Reload);
    }

    public IProjectSession Project { get; }

    public override string Title => "设备管理";

    public ObservableCollection<DeviceTabViewModel> Tabs { get; } = new();

    public ObservableCollection<DriverDescriptor> Drivers { get; } = new();

    public ParameterEditorViewModel ConnectionEditor { get; } = new();

    public DelegateCommand AddCommand { get; }

    public DelegateCommand RemoveCommand { get; }

    public DelegateCommand ConnectCommand { get; }

    public DelegateCommand DisconnectCommand { get; }

    public DelegateCommand DiscoverCommand { get; }

    public DelegateCommand ApplyCommand { get; }

    public DeviceTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                SelectedDevice = value?.Devices.FirstOrDefault();
            }
        }
    }

    public DeviceRowViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                LoadSchema(value);
                AttachPreview(value);
            }
        }
    }

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    protected override IEnumerable<PageAction> BuildActions()
    {
        if (HasPermission(PermissionCodes.DeviceDiscover))
        {
            yield return new PageAction("device.discover", "设备发现", DiscoverCommand, "Magnify");
        }

        if (HasPermission(PermissionCodes.DeviceEdit))
        {
            yield return new PageAction("device.add", "新增设备", AddCommand, "Plus", 1);
        }

        if (HasPermission(PermissionCodes.DeviceConnect))
        {
            yield return new PageAction("device.connect", "连接", ConnectCommand, "LinkVariant");
            yield return new PageAction("device.disconnect", "断开", DisconnectCommand, "LinkOff");
        }

        if (HasPermission(PermissionCodes.DeviceEdit))
        {
            yield return new PageAction("device.apply", "保存参数", ApplyCommand, "ContentSave");
            yield return new PageAction("device.remove", "删除设备", RemoveCommand, "Delete", 2, true, "删除后需要保存工程，确定删除？");
        }
    }

    protected override Task OnActivatedAsync(Prism.Navigation.Regions.NavigationContext context)
    {
        RebuildDrivers();
        Reload();
        return Task.CompletedTask;
    }

    protected override Task OnDeactivatedAsync()
    {
        AttachPreview(null);
        return Task.CompletedTask;
    }

    private void RebuildDrivers()
    {
        Drivers.Clear();
        foreach (var descriptor in _drivers.Drivers)
        {
            Drivers.Add(descriptor);
        }
    }

    private void Reload()
    {
        Tabs.Clear();
        var project = Project.Current;
        if (project is null)
        {
            return;
        }

        var rows = project.AllDevices().Select(config => new DeviceRowViewModel(config)).ToArray();

        foreach (var descriptor in _types.GetSubTabs())
        {
            var devices = rows.Where(row => row.Kind == descriptor.Kind && row.Config.Kind != DeviceKind.Other || row.Kind == descriptor.Kind);
            var tab = new DeviceTabViewModel(descriptor, devices);
            Tabs.Add(tab);
        }

        SelectedTab = Tabs.FirstOrDefault(tab => tab.Devices.Count > 0) ?? Tabs.FirstOrDefault();

        foreach (var row in rows)
        {
            row.State = _session.GetDriver(row.DeviceId)?.State ?? DeviceState.Disconnected;
        }
    }

    private void LoadSchema(DeviceRowViewModel? row)
    {
        if (row is null)
        {
            ConnectionEditor.Load(new ParameterSchema(), null);
            return;
        }

        var descriptor = _drivers.Find(row.DriverKey);
        ConnectionEditor.Load(
            descriptor?.Schema ?? new ParameterSchema(),
            row.Config.ToSettingMap().ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty));
    }

    private void AttachPreview(DeviceRowViewModel? row)
    {
        if (!string.IsNullOrWhiteSpace(_attachedCamera))
        {
            _preview.Detach(_attachedCamera);
            _attachedCamera = string.Empty;
        }

        PreviewImage = null;

        if (row is null || row.Kind != DeviceKind.Camera)
        {
            return;
        }

        _attachedCamera = row.DeviceId;
        _preview.Attach(row.DeviceId);
    }

    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs args)
        => Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var tab in Tabs)
            {
                var row = tab.Devices.FirstOrDefault(item =>
                    string.Equals(item.DeviceId, args.DeviceId, StringComparison.OrdinalIgnoreCase));

                if (row is null)
                {
                    continue;
                }

                row.State = args.NewState;
                row.Message = args.Message ?? string.Empty;
            }
        });

    private void OnPreviewFrame(object? sender, LivePreviewEventArgs args)
    {
        if (!string.Equals(args.DeviceId, _attachedCamera, StringComparison.OrdinalIgnoreCase))
        {
            args.Frame.Dispose();
            return;
        }

        var bitmap = ImageInterop.ToThumbnail(args.Frame.NativeImage, 1280);
        args.Frame.Dispose();

        Application.Current?.Dispatcher.Invoke(() => PreviewImage = bitmap);
    }

    private Task AddAsync()
    {
        var project = Project.Current;
        var tab = SelectedTab;
        if (project is null || tab is null)
        {
            StatusText = "请先打开工程";
            return Task.CompletedTask;
        }

        var descriptor = Drivers.FirstOrDefault(candidate => candidate.Kind == tab.Kind && candidate.IsAvailable);
        if (descriptor is null)
        {
            StatusText = $"没有可用驱动：{tab.Title}";
            return Task.CompletedTask;
        }

        var prefix = tab.Kind switch
        {
            DeviceKind.Camera => "CAM",
            DeviceKind.LightController => "LT",
            DeviceKind.Plc => "PLC",
            DeviceKind.Robot => "ROB",
            DeviceKind.Mes => "MES",
            _ => "DEV"
        };

        var index = project.AllDevices().Count(device => device.Kind == tab.Kind) + 1;
        var deviceId = $"{prefix}{index:D2}";
        var name = $"{tab.Title}{index}";

        switch (tab.Kind)
        {
            case DeviceKind.Camera:
                project.Cameras.Add(new CameraConfig { DeviceId = deviceId, Name = name, Kind = tab.Kind, DriverKey = descriptor.DriverKey });
                break;

            case DeviceKind.LightController:
                project.LightControllers.Add(new LightControllerConfig
                {
                    DeviceId = deviceId,
                    Name = name,
                    Kind = tab.Kind,
                    DriverKey = descriptor.DriverKey,
                    Channels = { new LightChannelConfig { Index = 0, Name = "通道1", Brightness = 128, Enabled = true } }
                });
                break;

            default:
                project.CommLinks.Add(new CommLinkConfig { DeviceId = deviceId, Name = name, Kind = tab.Kind, DriverKey = descriptor.DriverKey });
                break;
        }

        Project.MarkDirty();
        Reload();
        StatusText = $"已新增 {deviceId}（{descriptor.DisplayName}），请填写连接参数后保存工程";
        return Task.CompletedTask;
    }

    private async Task RemoveAsync()
    {
        var project = Project.Current;
        var row = SelectedDevice;
        if (project is null || row is null)
        {
            StatusText = "请先选择设备";
            return;
        }

        switch (row.Config)
        {
            case CameraConfig camera:
                project.Cameras.Remove(camera);
                break;
            case LightControllerConfig light:
                project.LightControllers.Remove(light);
                break;
            case CommLinkConfig link:
                project.CommLinks.Remove(link);
                break;
            default:
                break;
        }

        Project.MarkDirty();
        await Project.ReloadRuntimeAsync(CancellationToken.None).ConfigureAwait(true);
        Reload();
    }

    private async Task ConnectAsync()
    {
        var row = SelectedDevice;
        if (row is null)
        {
            StatusText = "请先选择设备";
            return;
        }

        var state = await _session.ConnectAsync(row.DeviceId, CancellationToken.None).ConfigureAwait(true);
        row.State = state;
        StatusText = $"{row.DeviceId} 状态 {row.StateText}";
    }

    private async Task DisconnectAsync()
    {
        var row = SelectedDevice;
        if (row is null)
        {
            return;
        }

        await _session.DisconnectAsync(row.DeviceId).ConfigureAwait(true);
        row.State = DeviceState.Disconnected;
    }

    private Task DiscoverAsync()
    {
        RebuildDrivers();
        var unavailable = Drivers.Where(descriptor => !descriptor.IsAvailable).ToArray();

        StatusText = unavailable.Length == 0
            ? $"驱动共 {Drivers.Count} 个，全部可用"
            : $"驱动共 {Drivers.Count} 个，不可用 {unavailable.Length} 个：{string.Join(", ", unavailable.Select(item => item.DriverKey))}";

        return Task.CompletedTask;
    }

    private Task ApplyAsync()
    {
        var row = SelectedDevice;
        if (row is null)
        {
            StatusText = "请先选择设备";
            return Task.CompletedTask;
        }

        var error = ConnectionEditor.ValidateAll();
        if (error is not null)
        {
            StatusText = error;
            return Task.CompletedTask;
        }

        var values = ConnectionEditor.Collect();
        var nodes = new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            nodes[pair.Key] = System.Text.Json.Nodes.JsonValue.Create(pair.Value);
        }

        row.Config.Connection = nodes;
        Project.MarkDirty();
        StatusText = $"{row.DeviceId} 参数已写入工程，保存工程后持久化";
        return Task.CompletedTask;
    }
}

/// <summary>设备管理模块定义。</summary>
public sealed class DeviceModule : Prism.Modularity.IModule
{
    public void RegisterTypes(Prism.Ioc.IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterForNavigation<Views.DeviceView>("DeviceView");
        containerRegistry.Register<DeviceViewModel>();
    }

    public void OnInitialized(Prism.Ioc.IContainerProvider containerProvider)
    {
        containerProvider.Resolve<INavigationCatalog>().Register(new NavigationItem(
            Key: "device",
            Title: "设备管理",
            IconKey: "Camera",
            Order: 6,
            ViewName: "DeviceView",
            RequiredPermission: PermissionCodes.NavDevice));
    }
}
