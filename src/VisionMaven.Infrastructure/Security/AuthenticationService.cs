using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Infrastructure.Security;

/// <summary>登录 / 登出 / 会话管理，含失败锁定与审计。</summary>
public sealed class AuthenticationService : IAuthenticationService
{
    /// <summary>首次启动创建的内置管理员账号。</summary>
    public const string DefaultAdminUserName = "admin";

    /// <summary>内置管理员初始密码，首次登录强制修改。</summary>
    public const string DefaultAdminPassword = "Admin@123";

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IAuditRepository _audits;
    private readonly IPasswordHasher _hasher;
    private readonly UserContext _context;
    private readonly ILogger<AuthenticationService> _logger;

    private DateTimeOffset _lastActivityUtc = DateTimeOffset.UtcNow;
    private UserSession? _session;

    public AuthenticationService(
        IUserRepository users,
        IRoleRepository roles,
        IAuditRepository audits,
        IPasswordHasher hasher,
        UserContext context,
        ILogger<AuthenticationService> logger)
    {
        _users = users;
        _roles = roles;
        _audits = audits;
        _hasher = hasher;
        _context = context;
        _logger = logger;
    }

    public UserSession? CurrentSession => _session;

    public TimeSpan SessionTimeout { get; private set; } = TimeSpan.FromMinutes(30);

    public bool IsExpired
        => _session is not null && DateTimeOffset.UtcNow - _lastActivityUtc > SessionTimeout;

    public event EventHandler<EventArgs>? SessionChanged;

    public event EventHandler<LoginResult>? SessionExpired;

    public async Task EnsureSeedDataAsync(CancellationToken ct)
    {
        var existing = await _users.ListAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return;
        }

        var role = await _roles.FindByCodeAsync(RoleCodes.Admin, ct).ConfigureAwait(false);
        if (role is null)
        {
            _logger.LogWarning("内置角色缺失，跳过默认管理员创建");
            return;
        }

        var (hash, salt, iterations) = _hasher.Hash(DefaultAdminPassword);
        await _users.AddAsync(
            new UserAccount
            {
                UserName = DefaultAdminUserName,
                DisplayName = "系统管理员",
                PasswordHash = hash,
                Salt = salt,
                Iterations = iterations,
                RoleId = role.Id,
                IsEnabled = true,
                MustChangePassword = true,
                CreatedAt = DateTimeOffset.Now
            },
            ct).ConfigureAwait(false);

        _logger.LogWarning(
            "已创建内置管理员账号 {User}，初始密码 {Password}，首次登录必须修改",
            DefaultAdminUserName,
            DefaultAdminPassword);
    }

    public async Task<LoginResult> LoginAsync(string userName, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return LoginResult.Fail(ErrorCodes.AuthInvalidCredential, "账号或密码错误");
        }

        var user = await _users.FindByNameAsync(userName.Trim(), ct).ConfigureAwait(false);
        if (user is null)
        {
            await WriteAuditAsync(null, userName, "登录失败", "账号不存在", ct).ConfigureAwait(false);
            return LoginResult.Fail(ErrorCodes.AuthInvalidCredential, "账号或密码错误");
        }

        if (!user.IsEnabled)
        {
            await WriteAuditAsync(user.Id, user.UserName, "登录失败", "账号已禁用", ct).ConfigureAwait(false);
            return LoginResult.Fail(ErrorCodes.AuthAccountDisabled, "账号已禁用");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTimeOffset.Now)
        {
            return LoginResult.Fail(
                ErrorCodes.AuthAccountLocked,
                $"账号已锁定，请于 {user.LockedUntil.Value:HH:mm:ss} 后重试");
        }

        if (!_hasher.Verify(password, user.PasswordHash, user.Salt, user.Iterations))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockedUntil = DateTimeOffset.Now.Add(LockDuration);
                user.FailedLoginCount = 0;
                _logger.LogWarning("账号 {User} 连续登录失败，已锁定至 {Until}", user.UserName, user.LockedUntil);
            }

            await _users.UpdateAsync(user, ct).ConfigureAwait(false);
            await WriteAuditAsync(user.Id, user.UserName, "登录失败", "密码错误", ct).ConfigureAwait(false);
            return LoginResult.Fail(ErrorCodes.AuthInvalidCredential, "账号或密码错误");
        }

        var role = await _roles.FindByIdAsync(user.RoleId, ct).ConfigureAwait(false);
        var roleCode = role?.Code ?? RoleCodes.Operator;
        var permissions = role?.Permissions ?? new List<string>();

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTimeOffset.Now;
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        _session = new UserSession(
            user.Id,
            user.UserName,
            user.DisplayName,
            roleCode,
            role?.Name ?? roleCode,
            permissions,
            user.MustChangePassword,
            DateTimeOffset.Now);

        Touch();
        _context.Set(_session);
        SessionChanged?.Invoke(this, EventArgs.Empty);

        await WriteAuditAsync(user.Id, user.UserName, "登录成功", $"角色={roleCode}", ct).ConfigureAwait(false);
        _logger.LogInformation("用户 {User} 登录成功，角色 {Role}", user.UserName, roleCode);

        return LoginResult.Ok(_session);
    }

    public async Task LogoutAsync()
    {
        if (_session is not null)
        {
            await WriteAuditAsync(_session.UserId, _session.UserName, "登出", null, CancellationToken.None)
                .ConfigureAwait(false);
        }

        _session = null;
        _context.Set(null);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Touch() => _lastActivityUtc = DateTimeOffset.UtcNow;

    /// <summary>由 Shell 定时调用：会话超时时强制登出。</summary>
    public async Task<bool> CheckTimeoutAsync()
    {
        if (!IsExpired)
        {
            return false;
        }

        var expired = _session;
        await LogoutAsync().ConfigureAwait(false);
        if (expired is not null)
        {
            _logger.LogInformation("会话超时，用户 {User} 已自动锁定", expired.UserName);
            SessionExpired?.Invoke(
                this,
                LoginResult.Fail(ErrorCodes.AuthSessionExpired, "会话超时，请重新登录"));
        }

        return true;
    }

    public async Task<bool> ChangePasswordAsync(
        long userId,
        string currentPassword,
        string newPassword,
        CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        if (!_hasher.Verify(currentPassword, user.PasswordHash, user.Salt, user.Iterations))
        {
            return false;
        }

        if (!PasswordHasher.IsStrong(newPassword))
        {
            throw new ConfigurationException(ErrorCodes.AuthPasswordWeak, "密码需至少 8 位且同时包含字母与数字");
        }

        ApplyPassword(user, newPassword, mustChange: false);
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        if (_session is not null && _session.UserId == userId)
        {
            _session = _session with { MustChangePassword = false };
            _context.Set(_session);
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        await WriteAuditAsync(user.Id, user.UserName, "修改密码", null, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> ResetPasswordAsync(long userId, string newPassword, bool mustChange, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        if (!PasswordHasher.IsStrong(newPassword))
        {
            throw new ConfigurationException(ErrorCodes.AuthPasswordWeak, "密码需至少 8 位且同时包含字母与数字");
        }

        ApplyPassword(user, newPassword, mustChange);
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);
        await WriteAuditAsync(user.Id, user.UserName, "重置密码", mustChange ? "需首次修改" : null, ct)
            .ConfigureAwait(false);
        return true;
    }

    private void ApplyPassword(UserAccount user, string password, bool mustChange)
    {
        var (hash, salt, iterations) = _hasher.Hash(password);
        user.PasswordHash = hash;
        user.Salt = salt;
        user.Iterations = iterations;
        user.MustChangePassword = mustChange;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
    }

    private Task WriteAuditAsync(long? userId, string? userName, string action, string? detail, CancellationToken ct)
        => _audits.AddAsync(
            new AuditLog
            {
                UserId = userId,
                UserName = userName,
                Action = action,
                Target = userName,
                Detail = detail,
                OccurredAt = DateTimeOffset.Now,
                ClientHost = Environment.MachineName
            },
            ct);
}
