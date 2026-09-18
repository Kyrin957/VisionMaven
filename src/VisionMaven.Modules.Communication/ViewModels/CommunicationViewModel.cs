using System.Collections.ObjectModel;
using System.Windows;
using Prism.Commands;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;
using VisionMaven.Modules.Shared;
using VisionMaven.Modules.Shared.Parameters;

namespace VisionMaven.Modules.Communication.ViewModels;

/// <summary>通讯链路行。</summary>
public sealed class CommLinkRowViewModel : Prism.Mvvm.BindableBase
{
    private DeviceState _state;

    public CommLinkRowViewModel(CommLinkConfig config)
    {
        Config = config;
    }

    public CommLinkConfig Config { get; }

    public string DeviceId => Config.DeviceId;

    public string Name => Config.Name;

    public string DriverKey => Config.DriverKey;

    public DeviceKind Kind => Config.Kind;

    public int PointCount => Config.Points.Count;

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
}

/// <summary>通讯管理页视图模型。</summary>
public sealed class CommunicationViewModel : PageViewModelBase
{
    private readonly IDeviceDriverRegistry _drivers;
    private readonly IDeviceSession _session;
    private CommLinkRowViewModel? _selectedLink;
    private string _readAddress = "M100";
    private string _writeAddress = "M200";
    private string _writeValue = "True";
    private string _debugResult = "-";

    public CommunicationViewModel(
        IUserContext user,
        IActionBarHost actionBar,
        IProjectSession project,
        IDeviceDriverRegistry drivers,
        IDeviceSession session)
        : base(user, actionBar)
    {
        Project = project;
        _drivers = drivers;
        _session = session;

        ConnectCommand = new DelegateCommand(() => SafeAsync(ConnectAsync, "连接失败").ConfigureAwait(true));
        DisconnectCommand = new DelegateCommand(() => SafeAsync(DisconnectAsync).ConfigureAwait(true));
        ReadCommand = new DelegateCommand(() => SafeAsync(ReadAsync, "读取失败").ConfigureAwait(true));
        WriteCommand = new DelegateCommand(() => SafeAsync(WriteAsync, "写入失败").ConfigureAwait(true));
        ApplyCommand = new DelegateCommand(() => SafeAsync(ApplyAsync).ConfigureAwait(true));
        AddPointCommand = new DelegateCommand(AddPoint);
        RemovePointCommand = new DelegateCommand(RemovePoint);

        Project.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(Reload);
    }

    public IProjectSession Project { get; }

    public override string Title => "通讯管理";

    public ObservableCollection<CommLinkRowViewModel> Links { get; } = new();

    public ParameterEditorViewModel ConnectionEditor { get; } = new();

    public ObservableCollection<PointConfig> Points { get; } = new();

    public ObservableCollection<PointDirection> Directions { get; } = new(new[] { PointDirection.Read, PointDirection.Write, PointDirection.ReadWrite });

    public ObservableCollection<PointDataType> DataTypes { get; } = new(new[] { PointDataType.Bool, PointDataType.Int16, PointDataType.Int32, PointDataType.Float, PointDataType.Double, PointDataType.String });

    public DelegateCommand ConnectCommand { get; }

    public DelegateCommand DisconnectCommand { get; }

    public DelegateCommand ReadCommand { get; }

    public DelegateCommand WriteCommand { get; }

    public DelegateCommand ApplyCommand { get; }

    public DelegateCommand AddPointCommand { get; }

    public DelegateCommand RemovePointCommand { get; }

    public CommLinkRowViewModel? SelectedLink
    {
        get => _selectedLink;
        set
        {
            if (SetProperty(ref _selectedLink, value))
            {
                LoadLink(value);
            }
        }
    }

    public PointConfig? SelectedPoint { get; set; }

    public string ReadAddress
    {
        get => _readAddress;
        set => SetProperty(ref _readAddress, value);
    }

    public string WriteAddress
    {
        get => _writeAddress;
        set => SetProperty(ref _writeAddress, value);
    }

    public string WriteValue
    {
        get => _writeValue;
        set => SetProperty(ref _writeValue, value);
    }

    public string DebugResult
    {
        get => _debugResult;
        private set => SetProperty(ref _debugResult, value);
    }

    protected override IEnumerable<PageAction> BuildActions()
    {
        if (HasPermission(PermissionCodes.CommView))
        {
            yield return new PageAction("comm.connect", "连接", ConnectCommand, "LinkVariant", 1);
            yield return new PageAction("comm.disconnect", "断开", DisconnectCommand, "LinkOff");
        }

        if (HasPermission(PermissionCodes.CommEdit))
        {
            yield return new PageAction("comm.apply", "保存参数", ApplyCommand, "ContentSave");
        }

        if (HasPermission(PermissionCodes.CommDebug))
        {
            yield return new PageAction("comm.read", "读取点位", ReadCommand, "Download");
            yield return new PageAction("comm.write", "写入点位", WriteCommand, "Upload");
        }
    }

    protected override Task OnActivatedAsync(Prism.Navigation.Regions.NavigationContext context)
    {
        Reload();
        return Task.CompletedTask;
    }

    private void Reload()
    {
        Links.Clear();
        var project = Project.Current;
        if (project is null)
        {
            return;
        }

        foreach (var link in project.CommLinks)
        {
            var row = new CommLinkRowViewModel(link)
            {
                State = _session.GetDriver(link.DeviceId)?.State ?? DeviceState.Disconnected
            };
            Links.Add(row);
        }

        SelectedLink = Links.FirstOrDefault();
    }

    private void LoadLink(CommLinkRowViewModel? row)
    {
        Points.Clear();

        if (row is null)
        {
            ConnectionEditor.Load(new ParameterSchema(), null);
            return;
        }

        var descriptor = _drivers.Find(row.DriverKey);
        ConnectionEditor.Load(
            descriptor?.Schema ?? new ParameterSchema(),
            row.Config.ToSettingMap().ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty));

        foreach (var point in row.Config.Points)
        {
            Points.Add(point);
        }
    }

    private async Task ConnectAsync()
    {
        var row = SelectedLink;
        if (row is null)
        {
            StatusText = "请先选择链路";
            return;
        }

        row.State = await _session.ConnectAsync(row.DeviceId, CancellationToken.None).ConfigureAwait(true);
        StatusText = $"{row.DeviceId} 状态 {row.StateText}";
    }

    private async Task DisconnectAsync()
    {
        var row = SelectedLink;
        if (row is null)
        {
            return;
        }

        await _session.DisconnectAsync(row.DeviceId).ConfigureAwait(true);
        row.State = DeviceState.Disconnected;
    }

    private async Task ReadAsync()
    {
        var plc = ResolvePlc();
        if (plc is null)
        {
            return;
        }

        var value = await plc.ReadAsync<bool>(ReadAddress, CancellationToken.None).ConfigureAwait(true);
        DebugResult = $"{ReadAddress} = {value}";
    }

    private async Task WriteAsync()
    {
        var plc = ResolvePlc();
        if (plc is null)
        {
            return;
        }

        if (!bool.TryParse(WriteValue, out var flag))
        {
            DebugResult = "写入值需要为 True / False";
            return;
        }

        await plc.WriteAsync(WriteAddress, flag, CancellationToken.None).ConfigureAwait(true);
        DebugResult = $"{WriteAddress} ← {flag}";
    }

    private IPlcDriver? ResolvePlc()
    {
        var row = SelectedLink;
        if (row is null)
        {
            DebugResult = "请先选择链路";
            return null;
        }

        var plc = _session.GetPlc(row.DeviceId);
        if (plc is null)
        {
            DebugResult = $"{row.DeviceId} 不是 PLC 链路或未连接";
            return null;
        }

        return plc;
    }

    private void AddPoint()
    {
        var row = SelectedLink;
        if (row is null)
        {
            return;
        }

        var point = new PointConfig
        {
            Name = $"点位{row.Config.Points.Count + 1}",
            Address = "M0",
            DataType = PointDataType.Bool,
            Direction = PointDirection.Read
        };

        row.Config.Points.Add(point);
        Points.Add(point);
        Project.MarkDirty();
    }

    private void RemovePoint()
    {
        var row = SelectedLink;
        if (row is null || SelectedPoint is null)
        {
            return;
        }

        row.Config.Points.Remove(SelectedPoint);
        Points.Remove(SelectedPoint);
        Project.MarkDirty();
    }

    private Task ApplyAsync()
    {
        var row = SelectedLink;
        if (row is null)
        {
            StatusText = "请先选择链路";
            return Task.CompletedTask;
        }

        var error = ConnectionEditor.ValidateAll();
        if (error is not null)
        {
            StatusText = error;
            return Task.CompletedTask;
        }

        var nodes = new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ConnectionEditor.Collect())
        {
            nodes[pair.Key] = System.Text.Json.Nodes.JsonValue.Create(pair.Value);
        }

        row.Config.Connection = nodes;
        Project.MarkDirty();
        StatusText = $"{row.DeviceId} 参数已写入工程，保存工程后持久化";
        return Task.CompletedTask;
    }
}

/// <summary>通讯管理模块定义。</summary>
public sealed class CommunicationModule : Prism.Modularity.IModule
{
    public void RegisterTypes(Prism.Ioc.IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterForNavigation<Views.CommunicationView>("CommunicationView");
        containerRegistry.Register<CommunicationViewModel>();
    }

    public void OnInitialized(Prism.Ioc.IContainerProvider containerProvider)
    {
        containerProvider.Resolve<INavigationCatalog>().Register(new NavigationItem(
            Key: "communication",
            Title: "通讯管理",
            IconKey: "Lan",
            Order: 7,
            ViewName: "CommunicationView",
            RequiredPermission: PermissionCodes.NavCommunication));
    }
}
