using VisionMaven.Core.Shell;
using VisionMaven.Modules.Shared;
using IDialogService = VisionMaven.Core.Shell.IDialogService;

namespace VisionMaven.App.Services;

/// <summary>
/// 把 Core 的操作按钮栏契约适配为模块可用的 <see cref="IActionBarHost"/>，
/// 使模块不必引用 App 工程。
/// </summary>
public sealed class ActionBarHost : IActionBarHost
{
    private readonly IActionBarService _actionBar;
    private readonly IDialogService _dialogs;

    public ActionBarHost(IActionBarService actionBar, IDialogService dialogs)
    {
        _actionBar = actionBar;
        _dialogs = dialogs;
    }

    public void SetActions(IEnumerable<PageAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        _actionBar.Set(actions.Select(action => new ActionItem(
            action.Key,
            action.Text,
            action.Command,
            action.IconKey,
            action.Style switch
            {
                1 => ActionItemStyle.Primary,
                2 => ActionItemStyle.Danger,
                _ => ActionItemStyle.Default
            },
            action.RequiresConfirm,
            action.ConfirmMessage)));
    }

    public void Clear() => _actionBar.Clear();

    public bool Confirm(string message, string title = "操作确认") => _dialogs.Confirm(message, title);

    public void ShowInfo(string message, string title = "提示") => _dialogs.ShowInfo(message, title);

    public void ShowWarning(string message, string title = "警告") => _dialogs.ShowWarning(message, title);

    public void ShowError(string message, string title = "错误") => _dialogs.ShowError(message, title);

    public string? OpenFile(string filter, string? title = null) => _dialogs.OpenFile(filter, title);

    public string? SaveFile(string filter, string? defaultFileName = null, string? title = null)
        => _dialogs.SaveFile(filter, defaultFileName, title);

    public string? SelectFolder(string? title = null) => _dialogs.SelectFolder(title);
}
