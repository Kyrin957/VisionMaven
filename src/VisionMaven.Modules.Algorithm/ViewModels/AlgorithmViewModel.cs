using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using Prism.Commands;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;
using VisionMaven.Modules.Shared;
using VisionMaven.Modules.Shared.Interop;
using VisionMaven.Modules.Shared.Parameters;

namespace VisionMaven.Modules.Algorithm.ViewModels;

/// <summary>算子类别分组。</summary>
public sealed class OperatorGroupViewModel
{
    public OperatorGroupViewModel(OperatorCategory category, IEnumerable<OperatorDescriptor> operators)
    {
        Category = category;
        Title = Describe(category);
        Operators = new ObservableCollection<OperatorDescriptor>(operators);
    }

    public OperatorCategory Category { get; }

    public string Title { get; }

    public ObservableCollection<OperatorDescriptor> Operators { get; }

    public static string Describe(OperatorCategory category) => category switch
    {
        OperatorCategory.Preprocess => "预处理",
        OperatorCategory.Threshold => "分割",
        OperatorCategory.Blob => "特征",
        OperatorCategory.TemplateMatch => "匹配",
        OperatorCategory.Finding => "定位",
        OperatorCategory.Measure => "测量",
        OperatorCategory.Calibration => "标定",
        _ => "后处理"
    };
}

/// <summary>算法页视图模型：算子选择 → 参数 → 试运行 → 结果叠加。</summary>
public sealed class AlgorithmViewModel : PageViewModelBase
{
    private readonly IOperatorRegistry _operators;
    private readonly IImageFrameFactory _frames;
    private IImageFrame? _source;
    private OperatorDescriptor? _selectedOperator;
    private BitmapSource? _previewImage;
    private string _sourcePath = "未加载图像";

    public AlgorithmViewModel(
        IUserContext user,
        IActionBarHost actionBar,
        IOperatorRegistry operators,
        IImageFrameFactory frames)
        : base(user, actionBar)
    {
        _operators = operators;
        _frames = frames;

        LoadImageCommand = new DelegateCommand(() => SafeAsync(LoadImageAsync).ConfigureAwait(true));
        RunCommand = new DelegateCommand(async () => await SafeAsync(RunAsync, "算子执行失败").ConfigureAwait(true));
    }

    public override string Title => "算法";

    public ObservableCollection<OperatorGroupViewModel> Groups { get; } = new();

    public ParameterEditorViewModel Editor { get; } = new();

    public DelegateCommand LoadImageCommand { get; }

    public DelegateCommand RunCommand { get; }

    public string SourcePath
    {
        get => _sourcePath;
        private set => SetProperty(ref _sourcePath, value);
    }

    public OperatorDescriptor? SelectedOperator
    {
        get => _selectedOperator;
        set
        {
            if (SetProperty(ref _selectedOperator, value) && value is not null)
            {
                Editor.Load(value.Schema, null);
                StatusText = $"{value.DisplayName}（{value.TypeKey}）";
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
        yield return new PageAction("algorithm.loadImage", "加载图像", LoadImageCommand, "Image");
        yield return new PageAction("algorithm.run", "执行算子", RunCommand, "Play", 1);
    }

    protected override Task OnActivatedAsync(Prism.Navigation.Regions.NavigationContext context)
    {
        if (Groups.Count > 0)
        {
            return Task.CompletedTask;
        }

        foreach (var group in _operators.Operators.GroupBy(descriptor => descriptor.Category))
        {
            Groups.Add(new OperatorGroupViewModel(group.Key, group.OrderBy(item => item.Order)));
        }

        SelectedOperator = _operators.Operators.FirstOrDefault();
        return Task.CompletedTask;
    }

    private Task LoadImageAsync()
    {
        var file = ActionBar.OpenFile("图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff", "选择图像");
        if (string.IsNullOrWhiteSpace(file))
        {
            return Task.CompletedTask;
        }

        _source?.Dispose();
        _source = _frames.DecodeFile("operator-test", file);
        SourcePath = $"{System.IO.Path.GetFileName(file)}  {_source.Width}x{_source.Height}";
        PreviewImage = ImageInterop.ToThumbnail(_source.NativeImage, 1280);
        return Task.CompletedTask;
    }

    private async Task RunAsync()
    {
        if (_source is null)
        {
            StatusText = "请先加载图像";
            return;
        }

        if (SelectedOperator is null)
        {
            StatusText = "请选择算子";
            return;
        }

        var error = Editor.ValidateAll();
        if (error is not null)
        {
            StatusText = error;
            return;
        }

        var op = _operators.Create(SelectedOperator.TypeKey);
        var result = await op.ExecuteAsync(_source, Editor.Collect(), CancellationToken.None).ConfigureAwait(true);

        if (!result.Success)
        {
            StatusText = result.ErrorMessage;
            return;
        }

        StatusText = $"{SelectedOperator.DisplayName} 完成，耗时 {result.ElapsedMs} ms";
        RenderResult(result);
    }

    private void RenderResult(OperatorResult result)
    {
        var image = result.Image;
        if (image is null)
        {
            return;
        }

        try
        {
            var bitmap = ImageInterop.ToThumbnail(image.NativeImage, 1280);
            Dispatch(() => PreviewImage = bitmap);
        }
        finally
        {
            image.Dispose();
        }
    }

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
}

/// <summary>算法模块定义。</summary>
public sealed class AlgorithmModule : Prism.Modularity.IModule
{
    public void RegisterTypes(Prism.Ioc.IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterForNavigation<Views.AlgorithmView>("AlgorithmView");
        containerRegistry.Register<AlgorithmViewModel>();
    }

    public void OnInitialized(Prism.Ioc.IContainerProvider containerProvider)
    {
        containerProvider.Resolve<INavigationCatalog>().Register(new NavigationItem(
            Key: "algorithm",
            Title: "算法",
            IconKey: "Filter",
            Order: 3,
            ViewName: "AlgorithmView",
            RequiredPermission: PermissionCodes.NavAlgorithm));
    }
}
