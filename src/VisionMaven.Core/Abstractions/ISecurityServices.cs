using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>登录 / 登出 / 会话管理。</summary>
public interface IAuthenticationService
{
    /// <summary>当前会话，未登录为 null。</summary>
    UserSession? CurrentSession { get; }

    /// <summary>会话超时时长（默认 30 分钟）。</summary>
    TimeSpan SessionTimeout { get; }

    /// <summary>首次启动且无任何用户时创建内置账号。</summary>
    Task EnsureSeedDataAsync(CancellationToken ct);

    Task<LoginResult> LoginAsync(string userName, string password, CancellationToken ct);

    Task LogoutAsync();

    /// <summary>空闲计时重置；由 Shell 在用户交互时调用。</summary>
    void Touch();

    /// <summary>会话是否因闲置而超时。</summary>
    bool IsExpired { get; }

    /// <summary>由 Shell 定时调用：会话超时时强制登出并触发 <see cref="SessionExpired"/>。</summary>
    Task<bool> CheckTimeoutAsync();

    Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken ct);

    /// <summary>管理员重置密码。</summary>
    Task<bool> ResetPasswordAsync(long userId, string newPassword, bool mustChange, CancellationToken ct);

    event EventHandler<EventArgs>? SessionChanged;

    event EventHandler<LoginResult>? SessionExpired;
}

/// <summary>权限判定。</summary>
public interface IPermissionService
{
    bool Has(IUserContext? user, string permission);

    void Demand(IUserContext? user, string permission);
}

/// <summary>审计记录。</summary>
public interface IAuditService
{
    Task WriteAsync(string action, string? target, string? detail, CancellationToken ct);

    Task<IReadOnlyList<AuditLog>> QueryAsync(string? userName, DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct);
}

/// <summary>敏感串保护（DPAPI 绑定当前 Windows 用户）。</summary>
public interface ISecretProtector
{
    /// <summary>加密并返回 Base64 串；失败时返回原文以保证可用性。</summary>
    string Protect(string plainText);

    /// <summary>解密；输入非密文时原样返回。</summary>
    string Unprotect(string protectedText);
}

/// <summary>密码哈希工具。</summary>
public interface IPasswordHasher
{
    (string Hash, string Salt, int Iterations) Hash(string password);

    bool Verify(string password, string hash, string salt, int iterations);
}

/// <summary>用户与角色管理（用户管理页使用）。</summary>
public interface IUserManagementService
{
    Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken ct);

    Task<UserAccount> CreateUserAsync(string userName, string displayName, string password, string roleCode, CancellationToken ct);

    Task UpdateUserAsync(long userId, string displayName, string roleCode, bool isEnabled, CancellationToken ct);

    Task<bool> DeleteUserAsync(long userId, CancellationToken ct);

    Task<string> ResetPasswordAsync(long userId, CancellationToken ct);

    Task<IReadOnlyList<RoleDefinition>> ListRolesAsync(CancellationToken ct);

    Task UpdateRolePermissionsAsync(string roleCode, IReadOnlyList<string> permissions, CancellationToken ct);
}
