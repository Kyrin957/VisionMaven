using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Modularity;
using Prism.Navigation.Regions;
using Serilog;
using VisionMaven.App.Navigation;
using VisionMaven.App.Services;
using VisionMaven.App.ViewModels;
using VisionMaven.App.Views;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Events;
using VisionMaven.Core.Shell;
using VisionMaven.Flow.Devices;
using VisionMaven.Flow.Engine;
using VisionMaven.Flow.Inference;
using VisionMaven.Flow.Nodes;
using VisionMaven.Flow.Preview;
using VisionMaven.Flow.Runtime;
using VisionMaven.Infrastructure.Configuration;
using VisionMaven.Infrastructure.Data;
using VisionMaven.Infrastructure.Events;
using VisionMaven.Infrastructure.Logging;
using VisionMaven.Infrastructure.Security;
using VisionMaven.Modules.Algorithm;
using VisionMaven.Modules.Communication;
using VisionMaven.Modules.Device;
using VisionMaven.Modules.FlowEditor;
using VisionMaven.Modules.Home;
using VisionMaven.Modules.Model;
using VisionMaven.Modules.Parameter;
using VisionMaven.Modules.Project;
using VisionMaven.Modules.Security;
using VisionMaven.Vision.Calibration;
using VisionMaven.Vision.Imaging;
using VisionMaven.Vision.Inference;
using VisionMaven.Vision.Operators;
using VisionMaven.Vision.Rendering;
using DialogService = VisionMaven.App.Services.DialogService;
using IDialogService = VisionMaven.Core.Shell.IDialogService;

namespace VisionMaven.App;

/// <summary>
/// 应用组合根：装配日志、持久化、视觉、流程与驱动，并驱动登录 → Shell 的启动时序。
/// 业务逻辑不得写入本工程。
/// </summary>
public partial class App
{
    /// <summary>内容区区域名。</summary>
    public const string ContentRegionName = "ContentRegion";

    private ApplicationPathProvider? _paths;
    private VisionLogSink? _logSink;
    private ILoggerFactory? _loggerFactory;
    private Serilog.ILogger? _serilogLogger;

    protected override Window CreateShell() => Container.Resolve<ShellWindow>();

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        RegisterLogging(containerRegistry);
        RegisterStorage(containerRegistry);
        RegisterVision(containerRegistry);
        RegisterFlow(containerRegistry);
        RegisterDevices(containerRegistry);
        RegisterShellServices(containerRegistry);
    }

    protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
    {
        moduleCatalog.AddModule<ProjectModule>();
        moduleCatalog.AddModule<HomeModule>();
        moduleCatalog.AddModule<VisionMaven.Modules.FlowEditor.ViewModels.FlowEditorModule>();
        moduleCatalog.AddModule<VisionMaven.Modules.Algorithm.ViewModels.AlgorithmModule>();
        moduleCatalog.AddModule<VisionMaven.Modules.Model.ViewModels.ModelModule>();
        moduleCatalog.AddModule<VisionMaven.Modules.Parameter.ViewModels.ParameterModule>();
        moduleCatalog.AddModule<VisionMaven.Modules.Device.ViewModels.DeviceModule>();
        moduleCatalog.AddModule<VisionMaven.Modules.Communication.ViewModels.CommunicationModule>();
        moduleCatalog.AddModule<VisionMaven.Modules.Security.ViewModels.SecurityModule>();
    }

    protected override async void OnInitialized()
    {
        try
        {
            await Container.Resolve<DatabaseInitializer>().InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            await Container.Resolve<IAuthenticationService>().EnsureSeedDataAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowFatal("数据库初始化失败", ex);
            return;
        }

        if (!await ShowLoginAsync().ConfigureAwait(true))
        {
            Shutdown();
            return;
        }

        base.OnInitialized();

        RegisterDriverDiagnostics();
        WireNavigation();
    }

    private async Task<bool> ShowLoginAsync()
    {
        var authentication = Container.Resolve<IAuthenticationService>();
        var dialogs = Container.Resolve<IDialogService>();

        while (true)
        {
            var login = Container.Resolve<LoginWindow>();
            if (login.ShowDialog() != true)
            {
                return false;
            }

            var user = Container.Resolve<IUserContext>();
            if (user.Current?.MustChangePassword != true)
            {
                return true;
            }

            var change = Container.Resolve<ChangePasswordWindow>();
            if (change.ShowDialog() == true)
            {
                return true;
            }

            dialogs.ShowWarning("必须修改初始密码后才能进入系统");
            await authentication.LogoutAsync().ConfigureAwait(true);
        }
    }

    private void WireNavigation()
    {
        var navigation = Container.Resolve<INavigationCatalog>();
        var regionManager = Container.Resolve<IRegionManager>();
        var user = Container.Resolve<IUserContext>();

        void Activate()
        {
            navigation.NavigationRequested = viewName =>
                regionManager.RequestNavigate(ContentRegionName, viewName);

            var first = navigation.VisibleItems(user).FirstOrDefault();
            if (first is not null)
            {
                navigation.NavigateTo(first.Key);
            }
        }

        if (MainWindow is null)
        {
            return;
        }

        if (MainWindow.IsLoaded)
        {
            Activate();
            return;
        }

        MainWindow.Loaded += (_, _) => Activate();
    }

    private void RegisterDriverDiagnostics()
    {
        var registry = Container.Resolve<IDeviceDriverRegistry>();
        var eventBus = Container.Resolve<IVisionEventBus>();
        var logger = Container.Resolve<ILoggerFactory>().CreateLogger("DriverDiagnostics");

        registry.Rescan();

        var diagnostics = registry.Drivers
            .Select(descriptor => new DriverDiagnostic(
                descriptor.DriverKey,
                descriptor.DisplayName,
                descriptor.Kind,
                descriptor.IsAvailable,
                descriptor.SdkHint,
                descriptor.UnavailableReason))
            .ToArray();

        foreach (var diagnostic in diagnostics.Where(item => !item.IsAvailable))
        {
            logger.LogWarning(
                "驱动 {DriverKey} 不可用：{Message}",
                diagnostic.DriverKey,
                diagnostic.Message);
        }

        eventBus.Publish(new DriverDiagnosticsEvent(diagnostics));
    }

    private void RegisterLogging(IContainerRegistry containerRegistry)
    {
        _paths = new ApplicationPathProvider();
        containerRegistry.RegisterInstance(typeof(ILogPathProvider), _paths);

        _logSink = new VisionLogSink();
        containerRegistry.RegisterInstance(typeof(IVisionLogSink), _logSink);

        _serilogLogger = LoggingBootstrapper.Configure(_paths, _logSink, Debugger.IsAttached);
        Log.Logger = _serilogLogger;

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog(_serilogLogger, dispose: false);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        containerRegistry.RegisterInstance(typeof(ILoggerFactory), _loggerFactory);
        containerRegistry.RegisterSingleton(typeof(ILogger<>), typeof(Logger<>));
        containerRegistry.RegisterInstance(typeof(IVisionEventBus), new VisionEventBus());
    }

    private static void RegisterStorage(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton(typeof(IDbContextFactory<VisionMavenDbContext>), typeof(VisionMavenDbContextFactory));
        containerRegistry.RegisterSingleton(typeof(DatabaseInitializer), typeof(DatabaseInitializer));

        containerRegistry.RegisterSingleton(typeof(IInspectionRepository), typeof(InspectionRepository));
        containerRegistry.RegisterSingleton(typeof(IAlarmRepository), typeof(AlarmRepository));
        containerRegistry.RegisterSingleton(typeof(IStatisticsRepository), typeof(StatisticsRepository));
        containerRegistry.RegisterSingleton(typeof(IAuditRepository), typeof(AuditRepository));
        containerRegistry.RegisterSingleton(typeof(IUserRepository), typeof(UserRepository));
        containerRegistry.RegisterSingleton(typeof(IRoleRepository), typeof(RoleRepository));
        containerRegistry.RegisterSingleton(typeof(ISystemSettingRepository), typeof(SystemSettingRepository));

        containerRegistry.RegisterSingleton(typeof(IProjectConfigurationService), typeof(ProjectConfigurationService));
        containerRegistry.RegisterSingleton(typeof(IProjectFileStorage), typeof(ProjectFileStorage));

        var userContext = new UserContext();
        containerRegistry.RegisterInstance(typeof(UserContext), userContext);
        containerRegistry.RegisterInstance(typeof(IUserContext), userContext);

        containerRegistry.RegisterSingleton(typeof(IPasswordHasher), typeof(PasswordHasher));
        containerRegistry.RegisterSingleton(typeof(ISecretProtector), typeof(DpapiSecretProtector));
        containerRegistry.RegisterSingleton(typeof(IPermissionService), typeof(PermissionService));
        containerRegistry.RegisterSingleton(typeof(IAuditService), typeof(AuditService));
        containerRegistry.RegisterSingleton(typeof(IAuthenticationService), typeof(AuthenticationService));
        containerRegistry.RegisterSingleton(typeof(IUserManagementService), typeof(UserManagementService));
    }

    private static void RegisterVision(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton(typeof(IImageFrameFactory), typeof(OpenCvImageFrameFactory));
        containerRegistry.RegisterSingleton(typeof(IBufferPool), typeof(MatBufferPool));
        containerRegistry.RegisterSingleton(typeof(ICalibrationService), typeof(CalibrationService));
        containerRegistry.RegisterSingleton(typeof(IOverlayRenderer), typeof(OpenCvOverlayRenderer));

        var calibrationProvider = new ProjectCalibrationProvider();
        containerRegistry.RegisterInstance(typeof(ProjectCalibrationProvider), calibrationProvider);
        containerRegistry.RegisterInstance(typeof(ICalibrationProvider), calibrationProvider);

        containerRegistry.RegisterSingleton(typeof(GrayOperator), typeof(GrayOperator));
        containerRegistry.RegisterSingleton(typeof(FilterOperator), typeof(FilterOperator));
        containerRegistry.RegisterSingleton(typeof(MorphologyOperator), typeof(MorphologyOperator));
        containerRegistry.RegisterSingleton(typeof(RoiOperator), typeof(RoiOperator));
        containerRegistry.RegisterSingleton(typeof(ThresholdOperator), typeof(ThresholdOperator));
        containerRegistry.RegisterSingleton(typeof(BlobFindOperator), typeof(BlobFindOperator));
        containerRegistry.RegisterSingleton(typeof(TemplateMatchOperator), typeof(TemplateMatchOperator));
        containerRegistry.RegisterSingleton(typeof(FindLineOperator), typeof(FindLineOperator));
        containerRegistry.RegisterSingleton(typeof(FindCircleOperator), typeof(FindCircleOperator));
        containerRegistry.RegisterSingleton(typeof(MeasureDistanceOperator), typeof(MeasureDistanceOperator));
        containerRegistry.RegisterSingleton(typeof(CalibrationTransformOperator), typeof(CalibrationTransformOperator));

        containerRegistry.RegisterSingleton(
            typeof(IOperatorRegistry),
            provider => new OperatorRegistry(new IImageOperator[]
            {
                provider.Resolve<GrayOperator>(),
                provider.Resolve<FilterOperator>(),
                provider.Resolve<MorphologyOperator>(),
                provider.Resolve<RoiOperator>(),
                provider.Resolve<ThresholdOperator>(),
                provider.Resolve<BlobFindOperator>(),
                provider.Resolve<TemplateMatchOperator>(),
                provider.Resolve<FindLineOperator>(),
                provider.Resolve<FindCircleOperator>(),
                provider.Resolve<MeasureDistanceOperator>(),
                provider.Resolve<CalibrationTransformOperator>()
            }));

        containerRegistry.Register(typeof(OnnxInferenceEngine), typeof(OnnxInferenceEngine));
        containerRegistry.RegisterSingleton(
            typeof(IInferenceEngineRegistry),
            provider => new InferenceEngineRegistry(type => provider.Resolve(type)));
    }

    private static void RegisterFlow(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton(typeof(ProjectAccessor), typeof(ProjectAccessor));
        containerRegistry.RegisterSingleton(typeof(IProjectAccessor), typeof(ProjectAccessor));
        containerRegistry.RegisterSingleton(typeof(IDeviceSession), typeof(DeviceSession));
        containerRegistry.RegisterSingleton(typeof(ILivePreviewService), typeof(LivePreviewService));
        containerRegistry.RegisterSingleton(typeof(IInferenceSessionPool), typeof(InferenceSessionPool));
        containerRegistry.RegisterSingleton(typeof(FlowNodeEnvironment), typeof(FlowNodeEnvironment));
        containerRegistry.RegisterSingleton(
            typeof(IFlowNodeFactory),
            provider => new FlowNodeFactory(type => provider.Resolve(type)));
        containerRegistry.RegisterSingleton(typeof(IFlowEngine), typeof(FlowEngine));
        containerRegistry.RegisterSingleton(typeof(IStationRuntimeFactory), typeof(StationRuntimeFactory));
        containerRegistry.RegisterSingleton(typeof(IRuntimeCoordinator), typeof(RuntimeCoordinator));
    }

    private static void RegisterDevices(IContainerRegistry containerRegistry)
    {
        foreach (var (contract, implementation) in DeviceProviderRegistrations)
        {
            containerRegistry.RegisterSingleton(contract, implementation);
        }

        containerRegistry.RegisterSingleton(
            typeof(IDeviceDriverRegistry),
            provider =>
            {
                var providers = DeviceProviderRegistrations
                    .Select(registration => (IDeviceDriverProvider)provider.Resolve(registration.Contract))
                    .ToArray();

                return new DeviceDriverRegistry(
                    providers,
                    type => provider.Resolve(type),
                    provider.Resolve<ILogPathProvider>(),
                    provider.Resolve<ILoggerFactory>().CreateLogger<DeviceDriverRegistry>());
            });
    }

    private static void RegisterShellServices(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton(typeof(IActionBarService), typeof(ActionBarService));
        containerRegistry.RegisterSingleton(typeof(INavigationCatalog), typeof(NavigationCatalog));
        containerRegistry.RegisterSingleton(typeof(ITopMenuRegistry), typeof(TopMenuRegistry));
        containerRegistry.RegisterSingleton(typeof(IDeviceTypeRegistry), typeof(DeviceTypeRegistry));
        containerRegistry.RegisterSingleton(typeof(IDialogService), typeof(DialogService));
        containerRegistry.RegisterSingleton(typeof(IProjectSession), typeof(ProjectSession));
        containerRegistry.RegisterSingleton(typeof(VisionMaven.Modules.Shared.IActionBarHost), typeof(ActionBarHost));
        containerRegistry.RegisterSingleton(typeof(IShellLayoutService), typeof(ShellLayoutService));
        containerRegistry.RegisterSingleton(typeof(ITextInputDialogService), typeof(TextInputDialogService));

        containerRegistry.RegisterSingleton(typeof(ShellWindowViewModel), typeof(ShellWindowViewModel));
        containerRegistry.Register(typeof(LoginViewModel), typeof(LoginViewModel));
        containerRegistry.Register(typeof(ChangePasswordViewModel), typeof(ChangePasswordViewModel));
        containerRegistry.Register(typeof(LoginWindow), typeof(LoginWindow));
        containerRegistry.Register(typeof(ChangePasswordWindow), typeof(ChangePasswordWindow));
        containerRegistry.Register(typeof(ShellWindow), typeof(ShellWindow));
    }

    /// <summary>内置驱动工厂：契约类型 → 实现类型。</summary>
    private static readonly (Type Contract, Type Implementation)[] DeviceProviderRegistrations =
    {
        (typeof(VisionMaven.Devices.Mock.MockCameraProvider), typeof(VisionMaven.Devices.Mock.MockCameraProvider)),
        (typeof(VisionMaven.Devices.Mock.MockPlcProvider), typeof(VisionMaven.Devices.Mock.MockPlcProvider)),
        (typeof(VisionMaven.Devices.Mock.MockLightProvider), typeof(VisionMaven.Devices.Mock.MockLightProvider)),
        (typeof(VisionMaven.Devices.Mock.MockMesProvider), typeof(VisionMaven.Devices.Mock.MockMesProvider)),
        (typeof(VisionMaven.Devices.Hikvision.HikvisionCameraProvider), typeof(VisionMaven.Devices.Hikvision.HikvisionCameraProvider)),
        (typeof(VisionMaven.Devices.GenTL.GenTlCameraProvider), typeof(VisionMaven.Devices.GenTL.GenTlCameraProvider)),
        (typeof(VisionMaven.Devices.Light.Serial.SerialLightControllerProvider), typeof(VisionMaven.Devices.Light.Serial.SerialLightControllerProvider)),
        (typeof(VisionMaven.Devices.Light.Tcp.TcpLightControllerProvider), typeof(VisionMaven.Devices.Light.Tcp.TcpLightControllerProvider)),
        (typeof(VisionMaven.Devices.Modbus.ModbusTcpDriverProvider), typeof(VisionMaven.Devices.Modbus.ModbusTcpDriverProvider)),
        (typeof(VisionMaven.Devices.S7.SiemensS7DriverProvider), typeof(VisionMaven.Devices.S7.SiemensS7DriverProvider)),
        (typeof(VisionMaven.Devices.Mes.MesHttpClientProvider), typeof(VisionMaven.Devices.Mes.MesHttpClientProvider))
    };

    private static void ShowFatal(string title, Exception ex)
    {
        MessageBox.Show(
            $"{title}：{ex.Message}",
            "VisionMaven",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Application.Current?.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Container.Resolve<IRuntimeCoordinator>().DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 退出路径忽略清理异常。
        }

        _loggerFactory?.Dispose();
        (_serilogLogger as IDisposable)?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    /// <summary>未处理异常兜底：写日志并提示，不让进程静默退出。</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Container.Resolve<ILoggerFactory>().CreateLogger("AppDomain").LogCritical(exception, "未处理异常");
            }
        };

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Container.Resolve<ILoggerFactory>().CreateLogger("UI").LogError(e.Exception, "界面线程未处理异常");
        MessageBox.Show(e.Exception.Message, "发生错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
