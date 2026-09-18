using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Modules.Shared;

/// <summary>
/// 导航页视图模型基类：统一处理操作按钮栏注入、权限判定与忙碌状态。
/// 页面进入时注入按钮，离开时清空，不得在页面内部另建工具栏。
/// </summary>
public abstract class PageViewModelBase : BindableBase, INavigationAware
{
    private bool _isBusy;
    private string? _statusText;
    private bool _isActive;

    protected PageViewModelBase(IUserContext user, IActionBarHost actionBar)
    {
        User = user;
        ActionBar = actionBar;
        RefreshCommand = new DelegateCommand(async () => await SafeAsync(RefreshAsync).ConfigureAwait(false));
    }

    protected IUserContext User { get; }

    /// <summary>操作按钮栏宿主，由 Shell 实现。</summary>
    protected IActionBarHost ActionBar { get; }

    public DelegateCommand RefreshCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    public string? StatusText
    {
        get => _statusText;
        protected set
        {
            if (SetProperty(ref _statusText, value))
            {
                RaisePropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    /// <summary>页面是否处于激活状态。</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    /// <summary>页面标题，用于日志与提示。</summary>
    public abstract string Title { get; }

    /// <summary>构建本页的操作按钮集合。</summary>
    protected abstract IEnumerable<PageAction> BuildActions();

    /// <summary>页面进入时调用。</summary>
    protected virtual Task OnActivatedAsync(NavigationContext context) => Task.CompletedTask;

    /// <summary>页面离开时调用。</summary>
    protected virtual Task OnDeactivatedAsync() => Task.CompletedTask;

    /// <summary>手动刷新。</summary>
    protected virtual Task RefreshAsync() => Task.CompletedTask;

    public bool HasPermission(string permission) => User.HasPermission(permission);

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        IsActive = true;
        ActionBar.SetActions(BuildActions());
        _ = OnActivatedAsync(navigationContext);
    }

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
        IsActive = false;
        ActionBar.Clear();
        _ = OnDeactivatedAsync();
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => true;

    /// <summary>统一的异步执行包装：异常输出到状态栏而不中断界面。</summary>
    protected async Task SafeAsync(Func<Task> action, string? failurePrefix = null)
    {
        try
        {
            IsBusy = true;
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = string.IsNullOrEmpty(failurePrefix) ? ex.Message : $"{failurePrefix}：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>页面声明的操作按钮。</summary>
public sealed record PageAction(
    string Key,
    string Text,
    ICommand Command,
    string? IconKey = null,
    int Style = 0,
    bool RequiresConfirm = false,
    string? ConfirmMessage = null,
    string? RequiredPermission = null);

/// <summary>
/// 操作按钮栏宿主接口：由 Shell 适配到 <c>IActionBarService</c>，
/// 使模块不必引用 App 工程。
/// </summary>
public interface IActionBarHost
{
    void SetActions(IEnumerable<PageAction> actions);

    void Clear();

    /// <summary>确认对话框，供 RequireConfirm 的按钮统一使用。</summary>
    bool Confirm(string message, string title = "操作确认");

    void ShowInfo(string message, string title = "提示");

    void ShowWarning(string message, string title = "警告");

    void ShowError(string message, string title = "错误");

    string? OpenFile(string filter, string? title = null);

    string? SaveFile(string filter, string? defaultFileName = null, string? title = null);

    string? SelectFolder(string? title = null);
}
