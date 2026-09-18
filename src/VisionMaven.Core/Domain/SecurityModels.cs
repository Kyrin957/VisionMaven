namespace VisionMaven.Core.Domain;

/// <summary>用户账号（对应 <c>Users</c> 表）。</summary>
public sealed class UserAccount
{
    public long Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Salt { get; set; } = string.Empty;

    public int Iterations { get; set; } = 100_000;

    public long RoleId { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? LastLoginAt { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }
}

/// <summary>角色（对应 <c>Roles</c> 表）。</summary>
public sealed class RoleDefinition
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public List<string> Permissions { get; set; } = new();
}

/// <summary>已登录会话。</summary>
public sealed record UserSession(
    long UserId,
    string UserName,
    string DisplayName,
    string RoleCode,
    string RoleName,
    IReadOnlyCollection<string> Permissions,
    bool MustChangePassword,
    DateTimeOffset LoginAt);

/// <summary>当前登录用户上下文。</summary>
public interface IUserContext
{
    UserSession? Current { get; }

    bool IsAuthenticated { get; }

    bool HasPermission(string permission);

    event EventHandler? Changed;
}

/// <summary>系统内置角色编码。</summary>
public static class RoleCodes
{
    public const string Admin = "Admin";
    public const string Engineer = "Engineer";
    public const string Operator = "Operator";

    public static IReadOnlyList<string> All { get; } = new[] { Admin, Engineer, Operator };
}

/// <summary>系统内置权限码。</summary>
public static class PermissionCodes
{
    public const string ProjectView = "project.view";
    public const string ProjectEdit = "project.edit";
    public const string ProjectImport = "project.import";
    public const string ProjectExport = "project.export";

    public const string NavHome = "nav.home";
    public const string NavFlow = "nav.flow";
    public const string NavAlgorithm = "nav.algorithm";
    public const string NavModel = "nav.model";
    public const string NavParameter = "nav.parameter";
    public const string NavDevice = "nav.device";
    public const string NavCommunication = "nav.communication";
    public const string NavSecurity = "nav.security";

    public const string DeviceView = "device.view";
    public const string DeviceEdit = "device.edit";
    public const string DeviceConnect = "device.connect";
    public const string DeviceDiscover = "device.discover";

    public const string CommView = "comm.view";
    public const string CommEdit = "comm.edit";
    public const string CommDebug = "comm.debug";

    public const string FlowView = "flow.view";
    public const string FlowEdit = "flow.edit";
    public const string FlowRun = "flow.run";

    public const string AlgorithmView = "algorithm.view";
    public const string AlgorithmEdit = "algorithm.edit";

    public const string ModelView = "model.view";
    public const string ModelImport = "model.import";
    public const string ModelDeploy = "model.deploy";

    public const string ParameterView = "parameter.view";
    public const string ParameterEdit = "parameter.edit";

    public const string RuntimeStart = "runtime.start";
    public const string RuntimeStop = "runtime.stop";
    public const string RuntimeReset = "runtime.reset";
    public const string RuntimeTrigger = "runtime.trigger";
    public const string AlarmAck = "alarm.ack";

    public const string UserManage = "user.manage";
    public const string AuditView = "audit.view";

    /// <summary>全部权限码（管理员默认持有）。</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        ProjectView, ProjectEdit, ProjectImport, ProjectExport,
        NavHome, NavFlow, NavAlgorithm, NavModel, NavParameter, NavDevice, NavCommunication, NavSecurity,
        DeviceView, DeviceEdit, DeviceConnect, DeviceDiscover,
        CommView, CommEdit, CommDebug,
        FlowView, FlowEdit, FlowRun,
        AlgorithmView, AlgorithmEdit,
        ModelView, ModelImport, ModelDeploy,
        ParameterView, ParameterEdit,
        RuntimeStart, RuntimeStop, RuntimeReset, RuntimeTrigger, AlarmAck,
        UserManage, AuditView
    };

    /// <summary>工程师默认权限（除用户与审计外的全部）。</summary>
    public static IReadOnlyList<string> EngineerDefaults { get; } = All
        .Where(code => code != NavSecurity && code != UserManage && code != AuditView)
        .ToArray();

    /// <summary>操作员默认权限。</summary>
    public static IReadOnlyList<string> OperatorDefaults { get; } = new[]
    {
        NavHome, RuntimeStart, RuntimeStop, RuntimeReset, RuntimeTrigger, AlarmAck
    };
}

/// <summary>登录结果。</summary>
public sealed record LoginResult(bool Success, string? ErrorCode, string? ErrorMessage, UserSession? Session)
{
    public static LoginResult Ok(UserSession session) => new(true, null, null, session);

    public static LoginResult Fail(string code, string message) => new(false, code, message, null);
}
