using VisionMaven.Core.Shell;

namespace VisionMaven.App.Services;

/// <summary>Shell 布局开关实现：视图菜单与 Shell 视图模型共用同一状态。</summary>
public sealed class ShellLayoutService : IShellLayoutService
{
    private bool _isNavigationVisible = true;
    private bool _isLogCollapsed;

    public bool IsNavigationVisible
    {
        get => _isNavigationVisible;
        set
        {
            if (_isNavigationVisible == value)
            {
                return;
            }

            _isNavigationVisible = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsLogCollapsed
    {
        get => _isLogCollapsed;
        set
        {
            if (_isLogCollapsed == value)
            {
                return;
            }

            _isLogCollapsed = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;
}
