namespace VisionMaven.Core.Exceptions;

/// <summary>领域异常基类：携带错误码，不使用异常控制流程。</summary>
public class VisionMavenException : Exception
{
    public VisionMavenException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public VisionMavenException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>错误码，形如 <c>VM-2001</c>。</summary>
    public string ErrorCode { get; }
}

/// <summary>配置类错误（VM-1xxx）。</summary>
public sealed class ConfigurationException : VisionMavenException
{
    public ConfigurationException(string errorCode, string message)
        : base(errorCode, message)
    {
    }

    public ConfigurationException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }
}

/// <summary>设备类错误（VM-2xxx）。</summary>
public sealed class DeviceException : VisionMavenException
{
    public DeviceException(string errorCode, string message)
        : base(errorCode, message)
    {
    }

    public DeviceException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }
}

/// <summary>通讯类错误（VM-3xxx）。</summary>
public sealed class CommunicationException : VisionMavenException
{
    public CommunicationException(string errorCode, string message)
        : base(errorCode, message)
    {
    }

    public CommunicationException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }
}

/// <summary>流程类错误（VM-4xxx）。</summary>
public sealed class FlowException : VisionMavenException
{
    public FlowException(string errorCode, string message)
        : base(errorCode, message)
    {
    }

    public FlowException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }
}

/// <summary>推理类错误（VM-5xxx）。</summary>
public sealed class InferenceException : VisionMavenException
{
    public InferenceException(string errorCode, string message)
        : base(errorCode, message)
    {
    }

    public InferenceException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }
}

/// <summary>存储类错误（VM-6xxx）。</summary>
public sealed class StorageException : VisionMavenException
{
    public StorageException(string errorCode, string message)
        : base(errorCode, message)
    {
    }

    public StorageException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }
}

/// <summary>权限类错误（VM-7xxx）。</summary>
public sealed class AuthorizationException : VisionMavenException
{
    public AuthorizationException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}

/// <summary>错误码常量，见开发文档附录 A。</summary>
public static class ErrorCodes
{
    // VM-1xxx 配置
    public const string ConfigSchemaUnsupported = "VM-1001";
    public const string ConfigDuplicateId = "VM-1002";
    public const string ConfigDanglingReference = "VM-1003";
    public const string ConfigDriverMissing = "VM-1004";
    public const string ConfigFlowInvalid = "VM-1005";
    public const string ConfigModelFileMissing = "VM-1006";
    public const string ConfigNotFound = "VM-1007";
    public const string ConfigParseFailed = "VM-1008";

    // VM-2xxx 设备
    public const string DeviceConnectFailed = "VM-2001";
    public const string DeviceDisconnected = "VM-2002";
    public const string DeviceCaptureTimeout = "VM-2003";
    public const string DeviceParameterUnsupported = "VM-2004";
    public const string DeviceSdkMissing = "VM-2005";
    public const string DeviceAlarm = "VM-2006";

    // VM-3xxx 通讯
    public const string CommReadFailed = "VM-3001";
    public const string CommWriteFailed = "VM-3002";
    public const string CommTimeout = "VM-3003";
    public const string CommAddressInvalid = "VM-3004";
    public const string CommProtocolError = "VM-3005";

    // VM-4xxx 流程
    public const string FlowTopologyInvalid = "VM-4001";
    public const string FlowNodeFailed = "VM-4002";
    public const string FlowNodeTimeout = "VM-4003";
    public const string FlowTypeKeyUnknown = "VM-4004";
    public const string FlowOverrun = "VM-4005";
    public const string FlowCancelled = "VM-4006";

    // VM-5xxx 推理
    public const string InferenceModelLoadFailed = "VM-5001";
    public const string InferenceEpUnavailable = "VM-5002";
    public const string InferenceRunFailed = "VM-5003";
    public const string InferenceInputMismatch = "VM-5004";
    public const string InferenceLabelMissing = "VM-5005";

    // VM-6xxx 存储
    public const string StorageDatabaseFailed = "VM-6001";
    public const string StorageFileFailed = "VM-6002";
    public const string StorageImportFailed = "VM-6003";
    public const string StorageExportFailed = "VM-6004";

    // VM-7xxx 权限
    public const string AuthInvalidCredential = "VM-7001";
    public const string AuthAccountDisabled = "VM-7002";
    public const string AuthAccountLocked = "VM-7003";
    public const string AuthSessionExpired = "VM-7004";
    public const string AuthPermissionDenied = "VM-7005";
    public const string AuthPasswordWeak = "VM-7006";
    public const string AuthUserExists = "VM-7007";
    public const string AuthMustChangePassword = "VM-7008";
    public const string AuthLastAdmin = "VM-7009";
}
