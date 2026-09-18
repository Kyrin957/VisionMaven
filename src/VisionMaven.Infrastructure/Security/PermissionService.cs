using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Infrastructure.Security;

/// <summary>权限判定：管理员通行；其余按角色权限集合判断。</summary>
public sealed class PermissionService : IPermissionService
{
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(ILogger<PermissionService> logger)
    {
        _logger = logger;
    }

    public bool Has(IUserContext? user, string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return true;
        }

        if (user is null || !user.IsAuthenticated)
        {
            return false;
        }

        return user.HasPermission(permission);
    }

    public void Demand(IUserContext? user, string permission)
    {
        if (Has(user, permission))
        {
            return;
        }

        _logger.LogWarning("权限校验未通过：{Permission} 用户={User}", permission, user?.Current?.UserName ?? "<未登录>");
        throw new AuthorizationException(ErrorCodes.AuthPermissionDenied, $"缺少权限：{permission}");
    }
}
