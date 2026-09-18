using VisionMaven.Core.Domain;

namespace VisionMaven.Infrastructure.Security;

/// <summary>当前登录用户上下文（单进程单用户）。</summary>
public sealed class UserContext : IUserContext
{
    private readonly object _sync = new();
    private UserSession? _current;

    public UserSession? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool IsAuthenticated => Current is not null;

    public event EventHandler? Changed;

    public bool HasPermission(string permission)
    {
        var session = Current;
        if (session is null)
        {
            return false;
        }

        if (string.Equals(session.RoleCode, RoleCodes.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return session.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    internal void Set(UserSession? session)
    {
        lock (_sync)
        {
            _current = session;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
