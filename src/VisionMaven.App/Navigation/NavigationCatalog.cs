using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;

namespace VisionMaven.App.Navigation;

/// <summary>导航目录实现：模块注册、按权限过滤、按 Order 排序。</summary>
public sealed class NavigationCatalog : INavigationCatalog
{
    private readonly List<NavigationItem> _items = new();
    private readonly object _sync = new();
    private string? _currentKey;

    public Action<string>? NavigationRequested { get; set; }

    public string? CurrentKey
    {
        get => _currentKey;
        set
        {
            if (string.Equals(_currentKey, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentKey = value;
            CurrentChanged?.Invoke(this, value ?? string.Empty);
        }
    }

    public event EventHandler? ItemsChanged;

    public event EventHandler<string>? CurrentChanged;

    public void Register(NavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_sync)
        {
            var existing = _items.FindIndex(candidate =>
                string.Equals(candidate.Key, item.Key, StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
            {
                _items[existing] = item;
            }
            else
            {
                _items.Add(item);
            }

            _items.Sort((left, right) => left.Order.CompareTo(right.Order));
        }

        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<NavigationItem> VisibleItems(IUserContext user)
    {
        lock (_sync)
        {
            return _items
                .Where(item => string.IsNullOrWhiteSpace(item.RequiredPermission)
                               || user.HasPermission(item.RequiredPermission))
                .ToArray();
        }
    }

    public NavigationItem? Find(string key)
    {
        lock (_sync)
        {
            return _items.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void NavigateTo(string key)
    {
        var item = Find(key);
        if (item is null)
        {
            return;
        }

        CurrentKey = item.Key;
        NavigationRequested?.Invoke(item.ViewName);
    }
}

/// <summary>顶部菜单注册表实现。</summary>
public sealed class TopMenuRegistry : ITopMenuRegistry
{
    private readonly Dictionary<TopMenuGroup, List<TopMenuItem>> _items = new();
    private readonly object _sync = new();

    public event EventHandler? Changed;

    public void AddItem(TopMenuGroup group, TopMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_sync)
        {
            if (!_items.TryGetValue(group, out var list))
            {
                list = new List<TopMenuItem>();
                _items[group] = list;
            }

            var existing = list.FindIndex(candidate =>
                string.Equals(candidate.Key, item.Key, StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
            {
                list[existing] = item;
            }
            else
            {
                list.Add(item);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<TopMenuItem> Items(TopMenuGroup group)
    {
        lock (_sync)
        {
            return _items.TryGetValue(group, out var list) ? list.ToArray() : Array.Empty<TopMenuItem>();
        }
    }
}
