using Prism.Ioc;
using Prism.Modularity;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;

namespace VisionMaven.Modules.Project;

/// <summary>
/// 项目管理模块：不注册导航页，只向顶部四个菜单追加项目。
/// 项目管理是「项目」菜单的唯一入口。
/// </summary>
public sealed class ProjectModule : IModule
{
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<ProjectCommands>();
        containerRegistry.RegisterSingleton<ToolsCommands>();
        containerRegistry.RegisterSingleton<ViewCommands>();
    }

    public void OnInitialized(IContainerProvider containerProvider)
    {
        var menus = containerProvider.Resolve<ITopMenuRegistry>();
        var project = containerProvider.Resolve<ProjectCommands>();
        var tools = containerProvider.Resolve<ToolsCommands>();
        var view = containerProvider.Resolve<ViewCommands>();

        menus.AddItem(TopMenuGroup.Project, new TopMenuItem("project.new", "新建工程", project.NewProjectCommand, "Ctrl+N"));
        menus.AddItem(TopMenuGroup.Project, new TopMenuItem("project.open", "打开工程", project.OpenProjectCommand, "Ctrl+O"));
        menus.AddItem(TopMenuGroup.Project, new TopMenuItem("project.save", "保存", project.SaveCommand, "Ctrl+S"));
        menus.AddItem(TopMenuGroup.Project, new TopMenuItem("project.saveAs", "另存为", project.SaveAsCommand, "Ctrl+Shift+S"));
        menus.AddItem(TopMenuGroup.Project, new TopMenuItem("project.import", "导入工程", project.ImportCommand, IsSeparatorBefore: true));
        menus.AddItem(TopMenuGroup.Project, new TopMenuItem("project.export", "导出工程", project.ExportCommand));
        menus.AddItem(TopMenuGroup.Project, new TopMenuItem("project.recent", "最近工程", project.RecentCommand));
        menus.AddItem(TopMenuGroup.Project, new TopMenuItem("project.exit", "退出", project.ExitCommand, "Alt+F4", IsSeparatorBefore: true));

        menus.AddItem(TopMenuGroup.View, new TopMenuItem("view.navigation", "显示/隐藏导航栏", view.ToggleNavigationCommand));
        menus.AddItem(TopMenuGroup.View, new TopMenuItem("view.log", "折叠/展开日志栏", view.ToggleLogCommand));
        menus.AddItem(TopMenuGroup.View, new TopMenuItem("view.clearLog", "清空日志", view.ClearLogCommand));

        menus.AddItem(TopMenuGroup.Tools, new TopMenuItem("tools.discover", "设备发现", tools.DiscoverCommand));
        menus.AddItem(TopMenuGroup.Tools, new TopMenuItem("tools.logs", "日志目录", tools.OpenLogDirectoryCommand));
        menus.AddItem(TopMenuGroup.Tools, new TopMenuItem("tools.backup", "数据库备份", tools.BackupDatabaseCommand));

        menus.AddItem(TopMenuGroup.Help, new TopMenuItem("help.about", "关于", new Prism.Commands.DelegateCommand(project.ShowAbout)));
    }
}
