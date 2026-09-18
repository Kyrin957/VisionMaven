using System.Net;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Infrastructure.Security;

/// <summary>操作审计：登录/登出、工程增删改、参数修改、模型替换、设备启停等关键动作。</summary>
public sealed class AuditService : IAuditService
{
    private readonly IAuditRepository _repository;
    private readonly IUserContext _userContext;

    public AuditService(IAuditRepository repository, IUserContext userContext)
    {
        _repository = repository;
        _userContext = userContext;
    }

    public Task WriteAsync(string action, string? target, string? detail, CancellationToken ct)
    {
        var session = _userContext.Current;
        var log = new AuditLog
        {
            UserId = session?.UserId,
            UserName = session?.UserName,
            Action = action,
            Target = target,
            Detail = detail,
            OccurredAt = DateTimeOffset.Now,
            ClientHost = ResolveHostName()
        };

        return _repository.AddAsync(log, ct);
    }

    public Task<IReadOnlyList<AuditLog>> QueryAsync(
        string? userName,
        DateTimeOffset from,
        DateTimeOffset to,
        int take,
        CancellationToken ct)
        => _repository.QueryAsync(userName, from, to, take, ct);

    private static string ResolveHostName()
    {
        try
        {
            return Dns.GetHostName();
        }
        catch (Exception)
        {
            return Environment.MachineName;
        }
    }
}
