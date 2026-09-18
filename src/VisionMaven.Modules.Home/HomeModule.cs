using Prism.Ioc;
using Prism.Modularity;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;
using VisionMaven.Modules.Home.ViewModels;
using VisionMaven.Modules.Home.Views;

namespace VisionMaven.Modules.Home;

/// <summary>首页模块：注册运行状态页。</summary>
public sealed class HomeModule : IModule
{
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterForNavigation<HomeView>("HomeView");
        containerRegistry.Register<HomeViewModel>();
    }

    public void OnInitialized(IContainerProvider containerProvider)
    {
        containerProvider.Resolve<INavigationCatalog>().Register(new NavigationItem(
            Key: "home",
            Title: "首页",
            IconKey: "Home",
            Order: 1,
            ViewName: "HomeView",
            RequiredPermission: PermissionCodes.NavHome));
    }
}
