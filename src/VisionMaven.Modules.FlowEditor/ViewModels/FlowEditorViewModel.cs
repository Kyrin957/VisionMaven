using System.Collections.ObjectModel;
using System.Windows;
using Prism.Commands;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;
using VisionMaven.Modules.Shared;
using VisionMaven.Modules.Shared.Parameters;

namespace VisionMaven.Modules.FlowEditor.ViewModels;

/// <summary>节点行。</summary>
public sealed class FlowNodeRowViewModel : Prism.Mvvm.BindableBase
{
    public FlowNodeRowViewModel(FlowNodeConfig config)
    {
        Config = config;
    }

    public FlowNodeConfig Config { get; }

    public string NodeId => Config.NodeId;

    public string TypeKey => Config.TypeKey;

    public string Name
    {
        get => Config.Name;
        set
        {
            if (!string.Equals(Config.Name, value, StringComparison.Ordinal))
            {
                Config.Name = value;
                RaisePropertyChanged();
            }
        }
    }

    public bool Enabled
    {
        get => Config.Enabled;
        set
        {
            if (Config.Enabled != value)
            {
                Config.Enabled = value;
                RaisePropertyChanged();
            }
        }
    }

    public FlowNodeMode Mode
    {
        get => Config.Mode;
        set
        {
            if (Config.Mode != value)
            {
                Config.Mode = value;
                RaisePropertyChanged();
            }
        }
    }

    public int Order => Config.Order;

    public string NextText => Config.Next.Count == 0 ? "-" : string.Join(",", Config.Next);
}

/// <summary>触发源设置行。</summary>
public sealed class TriggerEditorViewModel : Prism.Mvvm.BindableBase
{
    private StationTriggerConfig? _trigger;

    public ObservableCollection<TriggerSource> Sources { get; } = new(new[] { TriggerSource.Hardware, TriggerSource.Software, TriggerSource.Plc, TriggerSource.Timer });

    public ObservableCollection<TriggerEdge> Edges { get; } = new(new[] { TriggerEdge.Rising, TriggerEdge.Falling, TriggerEdge.Level });

    public ObservableCollection<OverrunPolicy> Policies { get; } = new(new[] { OverrunPolicy.DropNew, OverrunPolicy.DropOld, OverrunPolicy.QueueAndWait });

    public void Bind(StationTriggerConfig? trigger) => _trigger = trigger;

    public TriggerSource Source
    {
        get => _trigger?.Source ?? TriggerSource.Software;
        set
        {
            if (_trigger is not null)
            {
                _trigger.Source = value;
                RaisePropertyChanged();
            }
        }
    }

    public TriggerEdge Edge
    {
        get => _trigger?.Edge ?? TriggerEdge.Rising;
        set
        {
            if (_trigger is not null)
            {
                _trigger.Edge = value;
                RaisePropertyChanged();
            }
        }
    }

    public OverrunPolicy OverrunPolicy
    {
        get => _trigger?.OverrunPolicy ?? OverrunPolicy.DropNew;
        set
        {
            if (_trigger is not null)
            {
                _trigger.OverrunPolicy = value;
                RaisePropertyChanged();
            }
        }
    }

    public int TimeoutMs
    {
        get => _trigger?.TimeoutMs ?? 2000;
        set
        {
            if (_trigger is not null)
            {
                _trigger.TimeoutMs = value;
                RaisePropertyChanged();
            }
        }
    }

    public int DebounceMs
    {
        get => _trigger?.DebounceMs ?? 20;
        set
        {
            if (_trigger is not null)
            {
                _trigger.DebounceMs = value;
                RaisePropertyChanged();
            }
        }
    }

    public string DeviceId
    {
        get => _trigger?.DeviceId ?? string.Empty;
        set
        {
            if (_trigger is not null)
            {
                _trigger.DeviceId = value;
                RaisePropertyChanged();
            }
        }
    }

    public string Address
    {
        get => _trigger?.Address ?? string.Empty;
        set
        {
            if (_trigger is not null)
            {
                _trigger.Address = value;
                RaisePropertyChanged();
            }
        }
    }
}

/// <summary>流程编辑页视图模型。</summary>
public sealed class FlowEditorViewModel : PageViewModelBase
{
    private readonly IFlowEngine _engine;
    private readonly IFlowNodeFactory _nodes;
    private FlowDefinitionConfig? _flow;
    private FlowNodeRowViewModel? _selectedNode;

    public FlowEditorViewModel(
        IUserContext user,
        IActionBarHost actionBar,
        IProjectSession project,
        IFlowEngine engine,
        IFlowNodeFactory nodes)
        : base(user, actionBar)
    {
        Project = project;
        _engine = engine;
        _nodes = nodes;

        AddNodeCommand = new DelegateCommand(() => SafeAsync(AddNodeAsync).ConfigureAwait(true));
        RemoveNodeCommand = new DelegateCommand(() => SafeAsync(RemoveNodeAsync, "删除节点失败").ConfigureAwait(true));
        MoveUpCommand = new DelegateCommand(() => SafeAsync(() => MoveAsync(-1)).ConfigureAwait(true));
        MoveDownCommand = new DelegateCommand(() => SafeAsync(() => MoveAsync(1)).ConfigureAwait(true));
        ApplyNodeCommand = new DelegateCommand(() => SafeAsync(ApplyNodeAsync, "保存节点失败").ConfigureAwait(true));
        ValidateCommand = new DelegateCommand(() => SafeAsync(ValidateAsync).ConfigureAwait(true));

        Project.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(Reload);
    }

    public IProjectSession Project { get; }

    public override string Title => "视觉流程";

    public ObservableCollection<StationConfig> Stations { get; } = new();

    public ObservableCollection<FlowNodeRowViewModel> Nodes { get; } = new();

    public ObservableCollection<FlowNodeDescriptor> NodeTypes { get; } = new();

    public ObservableCollection<FlowNodeMode> Modes { get; } = new(new[] { FlowNodeMode.Sequential, FlowNodeMode.Parallel, FlowNodeMode.Conditional });

    public ObservableCollection<FailureStrategy> Strategies { get; } = new(new[] { FailureStrategy.Abort, FailureStrategy.Continue, FailureStrategy.Retry });

    public ParameterEditorViewModel NodeEditor { get; } = new();

    public TriggerEditorViewModel Trigger { get; } = new();

    public DelegateCommand AddNodeCommand { get; }

    public DelegateCommand RemoveNodeCommand { get; }

    public DelegateCommand MoveUpCommand { get; }

    public DelegateCommand MoveDownCommand { get; }

    public DelegateCommand ApplyNodeCommand { get; }

    public DelegateCommand ValidateCommand { get; }

    public FlowNodeRowViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                LoadNode(value);
            }
        }
    }

    public string FlowName => _flow?.Name ?? "-";

    public FlowDefinitionConfig? Flow => _flow;

    public FailureStrategy FailureStrategy
    {
        get => _flow?.FailureStrategy ?? FailureStrategy.Abort;
        set
        {
            if (_flow is not null)
            {
                _flow.FailureStrategy = value;
                Project.MarkDirty();
                RaisePropertyChanged();
            }
        }
    }

    protected override IEnumerable<PageAction> BuildActions()
    {
        if (HasPermission(PermissionCodes.FlowEdit))
        {
            yield return new PageAction("flow.addNode", "新增节点", AddNodeCommand, "Plus", 1);
            yield return new PageAction("flow.removeNode", "删除节点", RemoveNodeCommand, "Minus", 2, true, "确定删除所选节点？");
            yield return new PageAction("flow.moveUp", "上移", MoveUpCommand, "ArrowUp");
            yield return new PageAction("flow.moveDown", "下移", MoveDownCommand, "ArrowDown");
            yield return new PageAction("flow.applyNode", "保存节点参数", ApplyNodeCommand, "ContentSave");
        }

        if (HasPermission(PermissionCodes.FlowView))
        {
            yield return new PageAction("flow.validate", "校验流程", ValidateCommand, "CheckAll");
        }
    }

    protected override Task OnActivatedAsync(Prism.Navigation.Regions.NavigationContext context)
    {
        Reload();
        return Task.CompletedTask;
    }

    private void Reload()
    {
        NodeTypes.Clear();
        foreach (var descriptor in _nodes.Descriptors)
        {
            NodeTypes.Add(descriptor);
        }

        Stations.Clear();
        var project = Project.Current;
        if (project is null)
        {
            return;
        }

        foreach (var station in project.Stations)
        {
            Stations.Add(station);
        }

        _flow = project.Flows.FirstOrDefault();
        RebuildNodes();
        Trigger.Bind(Stations.FirstOrDefault()?.Trigger);
        RaisePropertyChanged(nameof(FlowName));
    }

    private void RebuildNodes()
    {
        Nodes.Clear();
        if (_flow is null)
        {
            return;
        }

        foreach (var node in _flow.Nodes.OrderBy(node => node.Order))
        {
            Nodes.Add(new FlowNodeRowViewModel(node));
        }

        SelectedNode = Nodes.FirstOrDefault();
    }

    private void LoadNode(FlowNodeRowViewModel? row)
    {
        if (row is null)
        {
            NodeEditor.Load(new ParameterSchema(), null);
            return;
        }

        var descriptor = _nodes.Find(row.TypeKey);
        NodeEditor.Load(descriptor?.Schema ?? new ParameterSchema(), row.Config.Parameters);
    }

    private Task AddNodeAsync()
    {
        if (_flow is null)
        {
            StatusText = "请先打开工程";
            return Task.CompletedTask;
        }

        var descriptor = NodeTypes.FirstOrDefault();
        if (descriptor is null)
        {
            StatusText = "没有可用节点类型";
            return Task.CompletedTask;
        }

        var index = _flow.Nodes.Count + 1;
        var node = new FlowNodeConfig
        {
            NodeId = $"N{index}",
            TypeKey = descriptor.TypeKey,
            Name = descriptor.DisplayName,
            Order = index,
            Parameters = descriptor.Schema.CreateDefaults()
        };

        var previous = _flow.Nodes.OrderBy(item => item.Order).LastOrDefault();
        if (previous is not null)
        {
            previous.Next.Add(node.NodeId);
        }

        _flow.Nodes.Add(node);
        Project.MarkDirty();
        RebuildNodes();
        SelectedNode = Nodes.LastOrDefault();

        StatusText = $"已新增节点 {node.NodeId}（{descriptor.DisplayName}）";
        return Task.CompletedTask;
    }

    private Task RemoveNodeAsync()
    {
        if (_flow is null || SelectedNode is null)
        {
            return Task.CompletedTask;
        }

        var target = SelectedNode.Config;
        _flow.Nodes.Remove(target);

        foreach (var node in _flow.Nodes)
        {
            node.Next.RemoveAll(next => string.Equals(next, target.NodeId, StringComparison.OrdinalIgnoreCase));
        }

        Project.MarkDirty();
        RebuildNodes();
        return Task.CompletedTask;
    }

    private Task MoveAsync(int delta)
    {
        if (_flow is null || SelectedNode is null)
        {
            return Task.CompletedTask;
        }

        var ordered = _flow.Nodes.OrderBy(node => node.Order).ToList();
        var index = ordered.FindIndex(node => ReferenceEquals(node, SelectedNode.Config));
        var target = index + delta;
        if (target < 0 || target >= ordered.Count)
        {
            return Task.CompletedTask;
        }

        ordered.RemoveAt(index);
        ordered.Insert(target, SelectedNode.Config);

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i + 1;
        }

        Project.MarkDirty();
        RebuildNodes();
        return Task.CompletedTask;
    }

    private Task ApplyNodeAsync()
    {
        var row = SelectedNode;
        if (row is null)
        {
            return Task.CompletedTask;
        }

        var error = NodeEditor.ValidateAll();
        if (error is not null)
        {
            StatusText = error;
            return Task.CompletedTask;
        }

        row.Config.Parameters = NodeEditor.Collect();
        Project.MarkDirty();
        StatusText = $"节点 {row.NodeId} 参数已保存到工程";
        return Task.CompletedTask;
    }

    private Task ValidateAsync()
    {
        if (_flow is null)
        {
            return Task.CompletedTask;
        }

        var result = _engine.Validate(_flow);
        StatusText = result.IsValid
            ? $"流程 {_flow.FlowId} 校验通过"
            : string.Join("；", result.Errors);

        return Task.CompletedTask;
    }
}

/// <summary>流程编辑模块定义。</summary>
public sealed class FlowEditorModule : Prism.Modularity.IModule
{
    public void RegisterTypes(Prism.Ioc.IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterForNavigation<Views.FlowEditorView>("FlowEditorView");
        containerRegistry.Register<FlowEditorViewModel>();
    }

    public void OnInitialized(Prism.Ioc.IContainerProvider containerProvider)
    {
        containerProvider.Resolve<INavigationCatalog>().Register(new NavigationItem(
            Key: "flow",
            Title: "视觉流程",
            IconKey: "Sitemap",
            Order: 2,
            ViewName: "FlowEditorView",
            RequiredPermission: PermissionCodes.NavFlow));
    }
}
