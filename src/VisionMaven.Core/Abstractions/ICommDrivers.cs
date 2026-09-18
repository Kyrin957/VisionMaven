using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>PLC 链路：地址解析由各驱动实现。</summary>
public interface IPlcDriver : IDeviceDriver
{
    Task<T> ReadAsync<T>(string address, CancellationToken ct)
        where T : struct;

    Task WriteAsync<T>(string address, T value, CancellationToken ct)
        where T : struct;

    Task<bool[]> ReadBitsAsync(string address, int count, CancellationToken ct);

    Task WriteBitsAsync(string address, bool[] values, CancellationToken ct);
}

/// <summary>机器人链路。</summary>
public interface IRobotDriver : IDeviceDriver
{
    Task<RobotPose> ReadPoseAsync(CancellationToken ct);

    Task SendCommandAsync(string command, CancellationToken ct);
}

/// <summary>MES 客户端。</summary>
public interface IMesClient : IDeviceDriver
{
    Task<MesResult> UploadInspectionAsync(InspectionRecord record, CancellationToken ct);

    Task<bool> HeartbeatAsync(CancellationToken ct);
}

/// <summary>点位读取结果，供通讯调试面板显示。</summary>
public sealed record PointReadResult(string Address, PointDataType DataType, object? Value, bool Success, string? ErrorMessage);

/// <summary>点位写入结果。</summary>
public sealed record PointWriteResult(string Address, bool Success, string? ErrorMessage);
