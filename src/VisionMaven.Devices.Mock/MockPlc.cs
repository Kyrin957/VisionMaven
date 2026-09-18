using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Drivers;

namespace VisionMaven.Devices.Mock;

/// <summary>模拟 PLC 驱动：地址表保存在内存中，支持 Bool 与整数类地址。</summary>
[VisionDriver("mock.plc", DeviceKind.Plc, DisplayName = "模拟 PLC（内存点位）")]
public sealed class MockPlc : DeviceDriverBase, IPlcDriver
{
    private readonly ConcurrentDictionary<string, bool> _bits = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, double> _words = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<MockPlc> _logger;

    public MockPlc(IDeviceContext context, ILogger<MockPlc> logger)
        : base(context, DeviceKind.Plc)
    {
        _logger = logger;
    }

    public override string DriverKey => "mock.plc";

    /// <summary>供调试面板与自动化测试直接注入点位值。</summary>
    public void Seed(string address, bool value) => _bits[address] = value;

    public void Seed(string address, double value) => _words[address] = value;

    protected override Task OnConnectAsync(CancellationToken ct)
    {
        _logger.LogInformation("模拟 PLC 已就绪：{DeviceId}", DeviceId);
        return Task.CompletedTask;
    }

    public Task<T> ReadAsync<T>(string address, CancellationToken ct)
        where T : struct
    {
        if (typeof(T) == typeof(bool))
        {
            return Task.FromResult((T)(object)_bits.GetValueOrDefault(address, false));
        }

        var value = _words.GetValueOrDefault(address, 0d);
        return Task.FromResult((T)Convert.ChangeType(
            value,
            typeof(T),
            System.Globalization.CultureInfo.InvariantCulture));
    }

    public Task WriteAsync<T>(string address, T value, CancellationToken ct)
        where T : struct
    {
        if (value is bool flag)
        {
            _bits[address] = flag;
        }
        else
        {
            _words[address] = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        _logger.LogDebug("模拟 PLC 写入：{Address}={Value}", address, value);
        return Task.CompletedTask;
    }

    public Task<bool[]> ReadBitsAsync(string address, int count, CancellationToken ct)
    {
        var values = new bool[Math.Max(0, count)];
        var start = ParseNumericSuffix(address);
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = _bits.GetValueOrDefault($"{ExtractPrefix(address)}{start + index}", false);
        }

        return Task.FromResult(values);
    }

    public Task WriteBitsAsync(string address, bool[] values, CancellationToken ct)
    {
        var start = ParseNumericSuffix(address);
        var prefix = ExtractPrefix(address);
        for (var index = 0; index < values.Length; index++)
        {
            _bits[$"{prefix}{start + index}"] = values[index];
        }

        return Task.CompletedTask;
    }

    private static string ExtractPrefix(string address)
        => new(address.TakeWhile(character => !char.IsDigit(character)).ToArray());

    private static int ParseNumericSuffix(string address)
        => int.TryParse(new string(address.SkipWhile(character => !char.IsDigit(character)).ToArray()), out var value)
            ? value
            : 0;
}

/// <summary>模拟 PLC 驱动注册。</summary>
public sealed class MockPlcProvider : IDeviceDriverProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public MockPlcProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "mock.plc";

    public DeviceKind Kind => DeviceKind.Plc;

    public string DisplayName => "模拟 PLC（内存点位）";

    public string? SdkHint => null;

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public ParameterSchema Schema { get; } = new()
    {
        new IntegerParameter("pollingMs", "轮询周期", 10, 10000, 100) { Unit = "ms" },
        new IntegerParameter("heartbeatMs", "心跳周期", 100, 60000, 1000) { Unit = "ms" },
        new IntegerParameter("reconnectMs", "重连周期", 100, 60000, 3000) { Unit = "ms" }
    };

    public IDeviceDriver Create(IDeviceContext context)
        => new MockPlc(context, _loggerFactory.CreateLogger<MockPlc>());
}
