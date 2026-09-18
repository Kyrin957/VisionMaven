using VisionMaven.Core.Shell;

namespace VisionMaven.App.Services;

/// <summary>操作按钮栏实现：页面注入，Shell 呈现。</summary>
public sealed class ActionBarService : IActionBarService
{
    private IReadOnlyList<ActionItem> _items = Array.Empty<ActionItem>();

    public IReadOnlyList<ActionItem> Items => _items;

    public event EventHandler<ActionBarChangedEventArgs>? Changed;

    public void Set(IEnumerable<ActionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
        Changed?.Invoke(this, new ActionBarChangedEventArgs(_items));
    }

    public void Clear() => Set(Array.Empty<ActionItem>());
}
