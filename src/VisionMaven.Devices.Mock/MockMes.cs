using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Drivers;

namespace VisionMaven.Devices.Mock;

/// <summary>模拟 MES 客户端：记录上报次数，供集成验证断言。</summary>
[VisionDriver("mock.mes", DeviceKind.Mes, DisplayName = "模拟 MES 客户端")]
public sealed class MockMes : DeviceDriverBase, IMesClient
{
    private readonly ConcurrentQueue<InspectionRecord> _uploaded = new();
    private readonly ILogger<MockMes> _logger;

    public MockMes(IDeviceContext context, ILogger<MockMes> logger)
        : base(context, DeviceKind.Mes)
    {
        _logger = logger;
    }

    public override string DriverKey => "mock.mes";

    /// <summary>已上报的检测记录。</summary>
    public IReadOnlyList<InspectionRecord> Uploaded => _uploaded.ToArray();

    protected override Task OnConnectAsync(CancellationToken ct)
    {
        _logger.LogInformation("模拟 MES 客户端已就绪：{DeviceId}", DeviceId);
        return Task.CompletedTask;
    }

    public Task<MesResult> UploadInspectionAsync(InspectionRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        _uploaded.Enqueue(record);
        _logger.LogInformation(
            "模拟 MES 上报：工位={StationId} 结果={Result} 分数={Score:F3}",
            record.StationId,
            record.Result,
            record.Score);

        return Task.FromResult(new MesResult(true, "0", "OK"));
    }

    public Task<bool> HeartbeatAsync(CancellationToken ct) => Task.FromResult(true);
}

/// <summary>模拟 MES 客户端驱动注册。</summary>
public sealed class MockMesProvider : IDeviceDriverProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public MockMesProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "mock.mes";

    public DeviceKind Kind => DeviceKind.Mes;

    public string DisplayName => "模拟 MES 客户端";

    public string? SdkHint => null;

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public ParameterSchema Schema { get; } = new()
    {
        new IntegerParameter("heartbeatMs", "心跳周期", 100, 600000, 30000) { Unit = "ms" }
    };

    public IDeviceDriver Create(IDeviceContext context)
        => new MockMes(context, _loggerFactory.CreateLogger<MockMes>());
}
