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

namespace VisionMaven.Modules.Model.ViewModels;

/// <summary>模型页视图模型：模型库 CRUD、推理设置与测试推理。</summary>
public sealed class ModelViewModel : PageViewModelBase
{
    private readonly IInferenceSessionPool _sessions;
    private readonly IImageFrameFactory _frames;
    private readonly IOverlayRenderer _overlay;
    private ModelEntryConfig? _selectedModel;
    private BitmapSource? _previewImage;
    private string _resultText = "-";

    public ModelViewModel(
        IUserContext user,
        IActionBarHost actionBar,
        IProjectSession project,
        IInferenceSessionPool sessions,
        IImageFrameFactory frames,
        IOverlayRenderer overlay)
        : base(user, actionBar)
    {
        Project = project;
        _sessions = sessions;
        _frames = frames;
        _overlay = overlay;

        ImportCommand = new DelegateCommand(() => SafeAsync(ImportAsync).ConfigureAwait(true));
        DeleteCommand = new DelegateCommand(async () => await SafeAsync(DeleteAsync, "删除失败").ConfigureAwait(true));
        ApplyCommand = new DelegateCommand(() => SafeAsync(ApplyAsync).ConfigureAwait(true));
        TestCommand = new DelegateCommand(() => SafeAsync(TestAsync, "测试推理失败").ConfigureAwait(true));
        BrowseLabelCommand = new DelegateCommand(() => SafeAsync(BrowseLabelAsync).ConfigureAwait(true));
        BrowseImageCommand = new DelegateCommand(() => SafeAsync(BrowseImageAsync).ConfigureAwait(true));

        Project.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(Reload);
    }

    public IProjectSession Project { get; }

    public override string Title => "模型";

    public ObservableCollection<ModelEntryConfig> Models { get; } = new();

    public ObservableCollection<InferenceDevice> Devices { get; } = new(new[] { InferenceDevice.CPU, InferenceDevice.CUDA, InferenceDevice.TensorRT });

    public ObservableCollection<InferencePrecision> Precisions { get; } = new(new[] { InferencePrecision.FP32, InferencePrecision.FP16, InferencePrecision.INT8 });

    public ObservableCollection<ModelTask> Tasks { get; } = new(new[] { ModelTask.Detection, ModelTask.Classification, ModelTask.Segmentation });

    public DelegateCommand ImportCommand { get; }

    public DelegateCommand DeleteCommand { get; }

    public DelegateCommand ApplyCommand { get; }

    public DelegateCommand TestCommand { get; }

    public DelegateCommand BrowseLabelCommand { get; }

    public DelegateCommand BrowseImageCommand { get; }

    public ModelEntryConfig? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value))
            {
                RaisePropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => SelectedModel is not null;

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    protected override IEnumerable<PageAction> BuildActions()
    {
        if (HasPermission(PermissionCodes.ModelImport))
        {
            yield return new PageAction("model.import", "导入模型", ImportCommand, "FileImport", 1);
        }

        if (HasPermission(PermissionCodes.ModelView))
        {
            yield return new PageAction("model.image", "选择样图", BrowseImageCommand, "Image");
        }

        if (HasPermission(PermissionCodes.ModelDeploy))
        {
            yield return new PageAction("model.test", "测试推理", TestCommand, "Play");
        }

        if (HasPermission(PermissionCodes.ModelDeploy))
        {
            yield return new PageAction("model.apply", "保存设置", ApplyCommand, "ContentSave");
        }

        if (HasPermission(PermissionCodes.ModelDeploy))
        {
            yield return new PageAction("model.delete", "删除模型", DeleteCommand, "Delete", 2, true, "删除后不可恢复，确定删除所选模型？");
        }
    }

    protected override Task OnActivatedAsync(Prism.Navigation.Regions.NavigationContext context)
    {
        Reload();
        return Task.CompletedTask;
    }

    private void Reload()
    {
        Models.Clear();
        var project = Project.Current;
        if (project is null)
        {
            return;
        }

        foreach (var model in project.ModelLibrary)
        {
            Models.Add(model);
        }

        SelectedModel = Models.FirstOrDefault();
    }

    private Task ImportAsync()
    {
        var project = Project.Current;
        if (project is null)
        {
            StatusText = "请先打开工程";
            return Task.CompletedTask;
        }

        var file = ActionBar.OpenFile("ONNX 模型|*.onnx", "导入模型");
        if (string.IsNullOrWhiteSpace(file))
        {
            return Task.CompletedTask;
        }

        var fileName = Path.GetFileName(file);
        var target = Path.Combine(Project.GetFolder(ProjectFolder.Models), fileName);
        File.Copy(file, target, overwrite: true);

        var modelId = $"MDL{project.ModelLibrary.Count + 1:D3}";
        var entry = new ModelEntryConfig
        {
            ModelId = modelId,
            Name = Path.GetFileNameWithoutExtension(fileName),
            Path = $"models/{fileName}",
            Task = ModelTask.Detection,
            Version = "v1"
        };

        project.ModelLibrary.Add(entry);
        Project.MarkDirty();
        Reload();
        SelectedModel = Models.LastOrDefault();
        StatusText = $"已导入 {fileName}";
        return Task.CompletedTask;
    }

    private Task BrowseLabelAsync()
    {
        var file = ActionBar.OpenFile("标签文件|*.txt", "选择标签文件");
        if (!string.IsNullOrWhiteSpace(file) && SelectedModel is not null)
        {
            SelectedModel.Labels = file;
            RaisePropertyChanged(nameof(SelectedModel));
        }

        return Task.CompletedTask;
    }

    private Task BrowseImageAsync()
    {
        var file = ActionBar.OpenFile("图像文件|*.png;*.jpg;*.jpeg;*.bmp", "选择样图");
        if (!string.IsNullOrWhiteSpace(file))
        {
            ResultText = $"{Path.GetFileName(file)}";
            _sampleImage?.Dispose();
            _sampleImage = _frames.DecodeFile("model-test", file);
            PreviewImage = ImageInterop.ToThumbnail(_sampleImage.NativeImage, 1280);
        }

        return Task.CompletedTask;
    }

    private IImageFrame? _sampleImage;

    private async Task TestAsync()
    {
        var project = Project.Current;
        if (project is null || SelectedModel is null)
        {
            StatusText = "请先选择模型";
            return;
        }

        if (_sampleImage is null)
        {
            StatusText = "请先选择样图";
            return;
        }

        var absolute = Path.Combine(
            Project.GetFolder(ProjectFolder.Root),
            SelectedModel.Path.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolute))
        {
            StatusText = $"模型文件不存在：{SelectedModel.Path}";
            return;
        }

        var labels = string.IsNullOrWhiteSpace(SelectedModel.Labels)
            ? Array.Empty<string>()
            : File.ReadAllLines(SelectedModel.Labels).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

        var descriptor = new ModelDescriptor(
            SelectedModel.ModelId,
            SelectedModel.Name,
            SelectedModel.Task,
            SelectedModel.Version,
            absolute,
            labels,
            SelectedModel.Input,
            SelectedModel.Confidence,
            SelectedModel.Nms,
            SelectedModel.MaxDetections,
            SelectedModel.Precision,
            SelectedModel.Device,
            SelectedModel.Sessions,
            SelectedModel.Warmup);

        var engine = await _sessions.GetAsync(descriptor, CancellationToken.None).ConfigureAwait(true);
        var result = await engine.InferAsync(_sampleImage, CancellationToken.None).ConfigureAwait(true);

        if (!result.Success)
        {
            StatusText = result.ErrorMessage;
            return;
        }

        ResultText = $"检测数 {result.Count} 最高分 {result.MaxScore:F3} 耗时 {result.ElapsedMs} ms";

        var rendered = _overlay.Render(
            _sampleImage.NativeImage,
            result.Detections,
            result.Detections.Count > 0 ? InspectionResult.Ng : InspectionResult.Ok,
            result.MaxScore);

        try
        {
            PreviewImage = ImageInterop.ToThumbnail(rendered, 1280);
        }
        finally
        {
            if (rendered is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private Task ApplyAsync()
    {
        Project.MarkDirty();
        StatusText = "推理设置已写入工程，保存后生效";
        return Task.CompletedTask;
    }

    private Task DeleteAsync()
    {
        var project = Project.Current;
        var model = SelectedModel;
        if (project is null || model is null)
        {
            StatusText = "请先选择模型";
            return Task.CompletedTask;
        }

        project.ModelLibrary.Remove(model);

        var absolute = Path.Combine(
            Project.GetFolder(ProjectFolder.Root),
            model.Path.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }
        }
        catch (IOException ex)
        {
            StatusText = $"模型文件删除失败：{ex.Message}";
        }

        Project.MarkDirty();
        Reload();
        StatusText = $"已删除模型 {model.ModelId}";
        return Task.CompletedTask;
    }
}

/// <summary>模型模块定义。</summary>
public sealed class ModelModule : Prism.Modularity.IModule
{
    public void RegisterTypes(Prism.Ioc.IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterForNavigation<Views.ModelView>("ModelView");
        containerRegistry.Register<ModelViewModel>();
    }

    public void OnInitialized(Prism.Ioc.IContainerProvider containerProvider)
    {
        containerProvider.Resolve<INavigationCatalog>().Register(new NavigationItem(
            Key: "model",
            Title: "模型",
            IconKey: "Cube",
            Order: 4,
            ViewName: "ModelView",
            RequiredPermission: PermissionCodes.NavModel));
    }
}
