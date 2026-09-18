using HslCommunication;
using HslCommunication.Profinet.Siemens;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Drivers;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Devices.S7;

/// <summary>
/// 西门子 S7 驱动，封装在 <see cref="IPlcDriver"/> 之后便于替换。
/// 地址使用 HslCommunication 约定：<c>M100</c>、<c>I0.0</c>、<c>Q0.0</c>、<c>DB1.100</c>。
/// </summary>
[VisionDriver("siemens.s7", DeviceKind.Plc, DisplayName = "西门子 S7", SdkHint = "无需额外 SDK")]
public sealed class SiemensS7Driver : DeviceDriverBase, IPlcDriver
{
    private readonly ILogger<SiemensS7Driver> _logger;
    private SiemensS7Net? _client;

    public SiemensS7Driver(IDeviceContext context, ILogger<SiemensS7Driver> logger)
        : base(context, DeviceKind.Plc)
    {
        _logger = logger;
    }

    public override string DriverKey => "siemens.s7";

    private string Host => Context.GetString("ip", "192.168.0.10");

    private int Port => Context.GetInt("port", 102);

    private SiemensPLCS Cpu => Enum.TryParse<SiemensPLCS>(Context.GetString("cpu", "S1200"), true, out var value)
        ? value
        : SiemensPLCS.S1200;

    protected override Task OnConnectAsync(CancellationToken ct)
    {
        var client = new SiemensS7Net(Cpu, Host)
        {
            Port = Port,
            Rack = (byte)Context.GetInt("rack", 0),
            Slot = (byte)Context.GetInt("slot", 1),
            ConnectTimeOut = Context.GetInt("timeoutMs", 2000),
            ReceiveTimeOut = Context.GetInt("timeoutMs", 2000)
        };

        var result = client.ConnectServer();
        if (!result.IsSuccess)
        {
            client.Dispose();
            throw new CommunicationException(
                ErrorCodes.DeviceConnectFailed,
                $"S7 连接失败 {Host}:{Port} - {result.Message}");
        }

        _client = client;
        _logger.LogInformation("S7 已连接：{Host}:{Port} CPU={Cpu}", Host, Port, Cpu);
        return Task.CompletedTask;
    }

    protected override Task OnDisconnectAsync()
    {
        _client?.ConnectClose();
        _client?.Dispose();
        _client = null;
        return Task.CompletedTask;
    }

    private SiemensS7Net Client => _client
        ?? throw new CommunicationException(ErrorCodes.DeviceDisconnected, "S7 未连接");

    public Task<T> ReadAsync<T>(string address, CancellationToken ct)
        where T : struct
    {
        var client = Client;
        var target = AddressOf(address);

        if (typeof(T) == typeof(bool))
        {
            var read = client.ReadBool(target);
            EnsureSuccess(read.IsSuccess, read.Message);
            return Task.FromResult((T)(object)read.Content);
        }

        if (typeof(T) == typeof(short))
        {
            var read = client.ReadInt16(target);
            EnsureSuccess(read.IsSuccess, read.Message);
            return Task.FromResult((T)(object)read.Content);
        }

        if (typeof(T) == typeof(int))
        {
            var read = client.ReadInt32(target);
            EnsureSuccess(read.IsSuccess, read.Message);
            return Task.FromResult((T)(object)read.Content);
        }

        if (typeof(T) == typeof(float))
        {
            var read = client.ReadFloat(target);
            EnsureSuccess(read.IsSuccess, read.Message);
            return Task.FromResult((T)(object)read.Content);
        }

        if (typeof(T) == typeof(double))
        {
            var read = client.ReadDouble(target);
            EnsureSuccess(read.IsSuccess, read.Message);
            return Task.FromResult((T)(object)read.Content);
        }

        throw new CommunicationException(
            ErrorCodes.CommAddressInvalid,
            $"S7 不支持的数据类型：{typeof(T).Name}");
    }

    public Task WriteAsync<T>(string address, T value, CancellationToken ct)
        where T : struct
    {
        var client = Client;
        var target = AddressOf(address);

        OperateResult result = value switch
        {
            bool flag => client.Write(target, flag),
            short number => client.Write(target, number),
            int number => client.Write(target, number),
            float number => client.Write(target, number),
            double number => client.Write(target, number),
            _ => throw new CommunicationException(
                ErrorCodes.CommAddressInvalid,
                $"S7 不支持的数据类型：{typeof(T).Name}")
        };

        EnsureSuccess(result.IsSuccess, result.Message);
        return Task.CompletedTask;
    }

    public Task<bool[]> ReadBitsAsync(string address, int count, CancellationToken ct)
    {
        var read = Client.ReadBool(address, (ushort)Math.Max(0, count));
        EnsureSuccess(read.IsSuccess, read.Message);
        return Task.FromResult(read.Content);
    }

    public Task WriteBitsAsync(string address, bool[] values, CancellationToken ct)
    {
        var result = Client.Write(address, values);
        EnsureSuccess(result.IsSuccess, result.Message);
        return Task.CompletedTask;
    }

    private static string AddressOf(string address)
        => string.IsNullOrWhiteSpace(address)
            ? throw new CommunicationException(ErrorCodes.CommAddressInvalid, "S7 地址为空")
            : address.Trim();

    private static void EnsureSuccess(bool success, string message)
    {
        if (!success)
        {
            throw new CommunicationException(ErrorCodes.CommProtocolError, $"S7 操作失败：{message}");
        }
    }
}

/// <summary>西门子 S7 驱动注册。</summary>
public sealed class SiemensS7DriverProvider : IDeviceDriverProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public SiemensS7DriverProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "siemens.s7";

    public DeviceKind Kind => DeviceKind.Plc;

    public string DisplayName => "西门子 S7";

    public string? SdkHint => "无需额外 SDK";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public ParameterSchema Schema { get; } = new()
    {
        new TextParameter("ip", "IP 地址", "192.168.0.10") { Required = true },
        new IntegerParameter("port", "端口", 1, 65535, 102),
        new EnumParameter("cpu", "CPU 型号", new[] { "S1200", "S1500", "S300", "S400", "S200Smart" }, "S1200"),
        new IntegerParameter("rack", "机架号", 0, 7, 0),
        new IntegerParameter("slot", "槽号", 0, 31, 1),
        new IntegerParameter("timeoutMs", "超时", 100, 60000, 2000) { Unit = "ms" }
    };

    public IDeviceDriver Create(IDeviceContext context)
        => new SiemensS7Driver(context, _loggerFactory.CreateLogger<SiemensS7Driver>());
}
