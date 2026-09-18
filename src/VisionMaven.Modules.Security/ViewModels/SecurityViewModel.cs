using System.Collections.ObjectModel;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;
using VisionMaven.Modules.Shared;

namespace VisionMaven.Modules.Security.ViewModels;

/// <summary>权限勾选项。</summary>
public sealed class PermissionItemViewModel : BindableBase
{
    private bool _isGranted;

    public PermissionItemViewModel(string code, bool isGranted)
    {
        Code = code;
        _isGranted = isGranted;
    }

    public string Code { get; }

    public string Group => Code.Split('.')[0];

    public bool IsGranted
    {
        get => _isGranted;
        set => SetProperty(ref _isGranted, value);
    }
}

/// <summary>用户管理页视图模型。</summary>
public sealed class SecurityViewModel : PageViewModelBase
{
    private readonly IUserManagementService _users;
    private readonly IAuditService _audits;
    private UserAccount? _selectedUser;
    private RoleDefinition? _selectedRole;

    public SecurityViewModel(
        IUserContext user,
        IActionBarHost actionBar,
        IUserManagementService users,
        IAuditService audits)
        : base(user, actionBar)
    {
        _users = users;
        _audits = audits;

        AddUserCommand = new DelegateCommand(() => SafeAsync(AddUserAsync, "新增用户失败").ConfigureAwait(true));
        UpdateUserCommand = new DelegateCommand(() => SafeAsync(UpdateUserAsync, "更新用户失败").ConfigureAwait(true));
        DeleteUserCommand = new DelegateCommand(() => SafeAsync(DeleteUserAsync, "删除用户失败").ConfigureAwait(true));
        ResetPasswordCommand = new DelegateCommand(() => SafeAsync(ResetPasswordAsync, "重置密码失败").ConfigureAwait(true));
        SavePermissionsCommand = new DelegateCommand(() => SafeAsync(SavePermissionsAsync, "保存权限失败").ConfigureAwait(true));
        QueryAuditCommand = new DelegateCommand(() => SafeAsync(QueryAuditAsync, "查询失败").ConfigureAwait(true));
    }

    public override string Title => "用户管理";

    public ObservableCollection<UserAccount> Users { get; } = new();

    public ObservableCollection<RoleDefinition> Roles { get; } = new();

    public ObservableCollection<PermissionItemViewModel> Permissions { get; } = new();

    public ObservableCollection<AuditLog> Audits { get; } = new();

    public DelegateCommand AddUserCommand { get; }

    public DelegateCommand UpdateUserCommand { get; }

    public DelegateCommand DeleteUserCommand { get; }

    public DelegateCommand ResetPasswordCommand { get; }

    public DelegateCommand SavePermissionsCommand { get; }

    public DelegateCommand QueryAuditCommand { get; }

    public UserAccount? SelectedUser
    {
        get => _selectedUser;
        set => SetProperty(ref _selectedUser, value);
    }

    public RoleDefinition? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (SetProperty(ref _selectedRole, value))
            {
                RebuildPermissions();
            }
        }
    }

    protected override IEnumerable<PageAction> BuildActions()
    {
        if (!HasPermission(PermissionCodes.UserManage))
        {
            yield break;
        }

        yield return new PageAction("user.add", "新增用户", AddUserCommand, "AccountPlus", 1);
        yield return new PageAction("user.update", "保存用户", UpdateUserCommand, "AccountEdit");
        yield return new PageAction("user.reset", "重置密码", ResetPasswordCommand, "LockReset");
        yield return new PageAction("user.delete", "删除用户", DeleteUserCommand, "AccountRemove", 2, true, "删除后不可恢复，确定删除？");
        yield return new PageAction("user.permissions", "保存权限", SavePermissionsCommand, "ShieldAccount");
        yield return new PageAction("audit.query", "查询审计", QueryAuditCommand, "ClipboardTextSearch");
    }

    protected override Task OnActivatedAsync(Prism.Navigation.Regions.NavigationContext context)
    {
        return SafeAsync(RefreshAsync);
    }

    protected override Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var users = await _users.ListUsersAsync(CancellationToken.None).ConfigureAwait(true);
        Users.Clear();
        foreach (var user in users)
        {
            Users.Add(user);
        }

        var roles = await _users.ListRolesAsync(CancellationToken.None).ConfigureAwait(true);
        Roles.Clear();
        foreach (var role in roles)
        {
            Roles.Add(role);
        }

        SelectedUser = Users.FirstOrDefault();
        SelectedRole = Roles.FirstOrDefault();
    }

    private void RebuildPermissions()
    {
        Permissions.Clear();
        var role = SelectedRole;
        if (role is null)
        {
            return;
        }

        foreach (var code in PermissionCodes.All)
        {
            Permissions.Add(new PermissionItemViewModel(
                code,
                role.Permissions.Contains(code, StringComparer.OrdinalIgnoreCase)));
        }
    }

    private async Task AddUserAsync()
    {
        var roleIds = string.Join("/", Roles.Select(role => role.Code));
        if (ActionBar is null)
        {
            return;
        }

        StatusText = $"请通过角色编码新增用户，可用角色：{roleIds}";
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task UpdateUserAsync()
    {
        var user = SelectedUser;
        if (user is null || SelectedRole is null)
        {
            StatusText = "请选择用户与角色";
            return;
        }

        await _users
            .UpdateUserAsync(user.Id, user.DisplayName, SelectedRole.Code, user.IsEnabled, CancellationToken.None)
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
        StatusText = "用户已更新";
    }

    private async Task DeleteUserAsync()
    {
        var user = SelectedUser;
        if (user is null)
        {
            return;
        }

        await _users.DeleteUserAsync(user.Id, CancellationToken.None).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
        StatusText = "用户已删除";
    }

    private async Task ResetPasswordAsync()
    {
        var user = SelectedUser;
        if (user is null)
        {
            return;
        }

        var password = await _users.ResetPasswordAsync(user.Id, CancellationToken.None).ConfigureAwait(true);
        StatusText = $"已重置为 {password}，首次登录需修改";
    }

    private async Task SavePermissionsAsync()
    {
        var role = SelectedRole;
        if (role is null)
        {
            return;
        }

        var codes = Permissions.Where(item => item.IsGranted).Select(item => item.Code).ToArray();
        await _users.UpdateRolePermissionsAsync(role.Code, codes, CancellationToken.None).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
        StatusText = $"角色 {role.Code} 权限已保存";
    }

    private async Task QueryAuditAsync()
    {
        var logs = await _audits
            .QueryAsync(null, DateTimeOffset.Now.AddDays(-7), DateTimeOffset.Now, 200, CancellationToken.None)
            .ConfigureAwait(true);

        Audits.Clear();
        foreach (var log in logs)
        {
            Audits.Add(log);
        }

        StatusText = $"审计记录 {logs.Count} 条（近 7 天）";
    }
}

/// <summary>用户管理模块定义。</summary>
public sealed class SecurityModule : Prism.Modularity.IModule
{
    public void RegisterTypes(Prism.Ioc.IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterForNavigation<Views.SecurityView>("SecurityView");
        containerRegistry.Register<SecurityViewModel>();
    }

    public void OnInitialized(Prism.Ioc.IContainerProvider containerProvider)
    {
        containerProvider.Resolve<INavigationCatalog>().Register(new NavigationItem(
            Key: "security",
            Title: "用户管理",
            IconKey: "Account",
            Order: 8,
            ViewName: "SecurityView",
            RequiredPermission: PermissionCodes.NavSecurity));
    }
}
