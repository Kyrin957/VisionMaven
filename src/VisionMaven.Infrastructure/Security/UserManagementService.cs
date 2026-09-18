using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Infrastructure.Security;

/// <summary>用户与角色管理（用户管理页）。</summary>
public sealed class UserManagementService : IUserManagementService
{
    /// <summary>管理员重置后的临时密码。</summary>
    public const string ResetPassword = "Vision@123";

    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IAuthenticationService _authentication;
    private readonly IAuditService _audit;
    private readonly IPasswordHasher _hasher;

    public UserManagementService(
        IUserRepository users,
        IRoleRepository roles,
        IAuthenticationService authentication,
        IAuditService audit,
        IPasswordHasher hasher)
    {
        _users = users;
        _roles = roles;
        _authentication = authentication;
        _audit = audit;
        _hasher = hasher;
    }

    public Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken ct) => _users.ListAsync(ct);

    public async Task<UserAccount> CreateUserAsync(
        string userName,
        string displayName,
        string password,
        string roleCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "用户名不能为空");
        }

        if (!PasswordHasher.IsStrong(password))
        {
            throw new ConfigurationException(ErrorCodes.AuthPasswordWeak, "密码需至少 8 位且同时包含字母与数字");
        }

        var existing = await _users.FindByNameAsync(userName.Trim(), ct).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new ConfigurationException(ErrorCodes.AuthUserExists, $"用户已存在：{userName}");
        }

        var role = await _roles.FindByCodeAsync(roleCode, ct).ConfigureAwait(false)
            ?? throw new ConfigurationException(ErrorCodes.ConfigDanglingReference, $"角色不存在：{roleCode}");

        var (hash, salt, iterations) = _hasher.Hash(password);
        var user = await _users.AddAsync(
            new UserAccount
            {
                UserName = userName.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName.Trim() : displayName.Trim(),
                PasswordHash = hash,
                Salt = salt,
                Iterations = iterations,
                RoleId = role.Id,
                IsEnabled = true,
                MustChangePassword = true,
                CreatedAt = DateTimeOffset.Now
            },
            ct).ConfigureAwait(false);

        await _audit.WriteAsync("新增用户", user.UserName, $"角色={role.Code}", ct).ConfigureAwait(false);
        return user;
    }

    public async Task UpdateUserAsync(
        long userId,
        string displayName,
        string roleCode,
        bool isEnabled,
        CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct).ConfigureAwait(false)
            ?? throw new ConfigurationException(ErrorCodes.ConfigNotFound, $"用户不存在：{userId}");

        if (!isEnabled && await IsLastEnabledAdminAsync(user, ct).ConfigureAwait(false))
        {
            throw new ConfigurationException(ErrorCodes.AuthLastAdmin, "不能禁用最后一个管理员账号");
        }

        var role = await _roles.FindByCodeAsync(roleCode, ct).ConfigureAwait(false)
            ?? throw new ConfigurationException(ErrorCodes.ConfigDanglingReference, $"角色不存在：{roleCode}");

        user.DisplayName = string.IsNullOrWhiteSpace(displayName) ? user.UserName : displayName.Trim();
        user.RoleId = role.Id;
        user.IsEnabled = isEnabled;
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        await _audit.WriteAsync("修改用户", user.UserName, $"角色={role.Code} 启用={isEnabled}", ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteUserAsync(long userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        if (await IsLastEnabledAdminAsync(user, ct).ConfigureAwait(false))
        {
            throw new ConfigurationException(ErrorCodes.AuthLastAdmin, "不能删除最后一个管理员账号");
        }

        if (_authentication.CurrentSession?.UserId == userId)
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "不能删除当前登录的账号");
        }

        var removed = await _users.DeleteAsync(userId, ct).ConfigureAwait(false);
        if (removed)
        {
            await _audit.WriteAsync("删除用户", user.UserName, null, ct).ConfigureAwait(false);
        }

        return removed;
    }

    public async Task<string> ResetPasswordAsync(long userId, CancellationToken ct)
    {
        await _authentication.ResetPasswordAsync(userId, ResetPassword, mustChange: true, ct).ConfigureAwait(false);
        return ResetPassword;
    }

    public Task<IReadOnlyList<RoleDefinition>> ListRolesAsync(CancellationToken ct) => _roles.ListAsync(ct);

    public async Task UpdateRolePermissionsAsync(
        string roleCode,
        IReadOnlyList<string> permissions,
        CancellationToken ct)
    {
        var role = await _roles.FindByCodeAsync(roleCode, ct).ConfigureAwait(false)
            ?? throw new ConfigurationException(ErrorCodes.ConfigDanglingReference, $"角色不存在：{roleCode}");

        if (string.Equals(role.Code, RoleCodes.Admin, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "管理员权限不可修改");
        }

        var unknown = permissions
            .Where(code => !PermissionCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new ConfigurationException(
                ErrorCodes.ConfigParseFailed,
                $"存在未知权限码：{string.Join(", ", unknown)}");
        }

        role.Permissions = permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await _roles.UpsertAsync(role, ct).ConfigureAwait(false);
        await _audit.WriteAsync("修改角色权限", role.Code, $"权限数={role.Permissions.Count}", ct).ConfigureAwait(false);
    }

    private async Task<bool> IsLastEnabledAdminAsync(UserAccount user, CancellationToken ct)
    {
        var role = await _roles.FindByIdAsync(user.RoleId, ct).ConfigureAwait(false);
        if (role is null || !string.Equals(role.Code, RoleCodes.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var users = await _users.ListAsync(ct).ConfigureAwait(false);
        var adminIds = new List<long>();
        foreach (var candidate in users.Where(candidate => candidate.IsEnabled))
        {
            var candidateRole = await _roles.FindByIdAsync(candidate.RoleId, ct).ConfigureAwait(false);
            if (candidateRole is not null
                && string.Equals(candidateRole.Code, RoleCodes.Admin, StringComparison.OrdinalIgnoreCase))
            {
                adminIds.Add(candidate.Id);
            }
        }

        return adminIds.Count <= 1 && adminIds.Contains(user.Id);
    }
}
