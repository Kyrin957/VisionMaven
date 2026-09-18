using HslCommunication;
using HslCommunication.ModBus;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Drivers;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Devices.Modbus;

/// <summary>Modbus TCP 驱动：地址形如 <c>s=2;x=3;100</c> 或直接使用功能码地址。</summary>
[VisionDriver("modbus.tcp", DeviceKind.Plc, DisplayName = "Modbus TCP", SdkHint = "无需额外 SDK")]
public sealed class ModbusTcpDriver : DeviceDriverBase, IPlcDriver
{
    private readonly ILogger<ModbusTcpDriver> _logger;
    private ModbusTcpNet? _client;

    public ModbusTcpDriver(IDeviceContext context, ILogger<ModbusTcpDriver> logger)
        : base(context, DeviceKind.Plc)
    {
        _logger = logger;
    }

    public override string DriverKey => "modbus.tcp";

    private string Host => Context.GetString("ip", "127.0.0.1");

    private int Port => Context.GetInt("port", 502);

    private byte Station => (byte)Context.GetInt("station", 1);

    private int TimeoutMs => Context.GetInt("timeoutMs", 1000);

    protected override Task OnConnectAsync(CancellationToken ct)
    {
        var client = new ModbusTcpNet(Host, Port, Station)
        {
            ConnectTimeOut = TimeoutMs,
            ReceiveTimeOut = TimeoutMs
        };

        var result = client.ConnectServer();
        if (!result.IsSuccess)
        {
            client.Dispose();
            throw new CommunicationException(
                ErrorCodes.DeviceConnectFailed,
                $"Modbus TCP 连接失败 {Host}:{Port} - {result.Message}");
        }

        _client = client;
        _logger.LogInformation("Modbus TCP 已连接：{Host}:{Port} 站号={Station}", Host, Port, Station);
        return Task.CompletedTask;
    }

    protected override Task OnDisconnectAsync()
    {
        _client?.ConnectClose();
        _client?.Dispose();
        _client = null;
        return Task.CompletedTask;
    }

    private ModbusTcpNet Client => _client
        ?? throw new CommunicationException(ErrorCodes.DeviceDisconnected, "Modbus TCP 未连接");

    public Task<T> ReadAsync<T>(string address, CancellationToken ct)
        where T : struct
    {
        var client = Client;
        var network = AddressOf(address);

        if (typeof(T) == typeof(bool))
        {
            var read = client.ReadBool(network);
            EnsureSuccess(read.IsSuccess, read.Message);
            return Task.FromResult((T)(object)read.Content);
        }

        if (typeof(T) == typeof(short))
        {
            var read = client.ReadInt16(network);
            EnsureSuccess(read.IsSuccess, read.Message);
            return Task.FromResult((T)(object)read.Content);
        }

        if (typeof(T) == typeof(int))
        {
            var read = client.ReadInt32(network);
            EnsureSuccess(read.IsSuccess, read.Message);
            return Task.FromResult((T)(object)read.Content);
        }

        if (typeof(T) == typeof(float))
        {
            var read = client.ReadFloat(network);
            EnsureSuccess(read.IsSuccess, read.Message);
            return Task.FromResult((T)(object)read.Content);
        }

        throw new CommunicationException(
            ErrorCodes.CommAddressInvalid,
            $"Modbus 不支持的数据类型：{typeof(T).Name}");
    }

    public Task WriteAsync<T>(string address, T value, CancellationToken ct)
        where T : struct
    {
        var client = Client;
        var network = AddressOf(address);

        OperateResult result = value switch
        {
            bool flag => client.Write(network, flag),
            short number => client.Write(network, number),
            int number => client.Write(network, number),
            float number => client.Write(network, number),
            _ => throw new CommunicationException(
                ErrorCodes.CommAddressInvalid,
                $"Modbus 不支持的数据类型：{typeof(T).Name}")
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

    /// <summary>工程配置中的地址直接透传，交由 HslCommunication 解析功能码与偏移。</summary>
    private static string AddressOf(string address)
        => string.IsNullOrWhiteSpace(address)
            ? throw new CommunicationException(ErrorCodes.CommAddressInvalid, "Modbus 地址为空")
            : address.Trim();

    private static void EnsureSuccess(bool success, string message)
    {
        if (!success)
        {
            throw new CommunicationException(
                ErrorCodes.CommProtocolError,
                $"Modbus 操作失败：{message}");
        }
    }
}

/// <summary>Modbus TCP 驱动注册。</summary>
public sealed class ModbusTcpDriverProvider : IDeviceDriverProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public ModbusTcpDriverProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "modbus.tcp";

    public DeviceKind Kind => DeviceKind.Plc;

    public string DisplayName => "Modbus TCP";

    public string? SdkHint => "无需额外 SDK";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public ParameterSchema Schema { get; } = new()
    {
        new TextParameter("ip", "IP 地址", "192.168.0.10") { Required = true },
        new IntegerParameter("port", "端口", 1, 65535, 502),
        new IntegerParameter("station", "站号", 0, 255, 1),
        new IntegerParameter("timeoutMs", "超时", 100, 60000, 1000) { Unit = "ms" }
    };

    public IDeviceDriver Create(IDeviceContext context)
        => new ModbusTcpDriver(context, _loggerFactory.CreateLogger<ModbusTcpDriver>());
}
