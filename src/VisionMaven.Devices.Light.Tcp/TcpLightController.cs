using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Drivers;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Devices.Light.Tcp;

/// <summary>以太网 TCP 光源控制器驱动：私有报文由命令模板配置。</summary>
[VisionDriver("light.tcp", DeviceKind.LightController, DisplayName = "TCP 光源控制器", SdkHint = "无需额外 SDK")]
public sealed class TcpLightController : DeviceDriverBase, ILightController
{
    private readonly ILogger<TcpLightController> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;

    public TcpLightController(IDeviceContext context, ILogger<TcpLightController> logger)
        : base(context, DeviceKind.LightController)
    {
        _logger = logger;
        ChannelCount = Math.Clamp(context.GetInt("channelCount", 4), 1, 32);
    }

    public override string DriverKey => "light.tcp";

    public int ChannelCount { get; }

    public LightTriggerMode TriggerMode { get; private set; } = LightTriggerMode.Strobe;

    private string Host => Context.GetString("ip", "192.168.0.30");

    private int Port => Context.GetInt("port", 5000);

    private string SetBrightnessTemplate => Context.GetString("commands.setBrightness", "S{channel:D2}{value:D3}\\r\\n");

    private string SetChannelEnabledTemplate => Context.GetString("commands.setChannelEnabled", "L{channel:D2}{state}\\r\\n");

    private string ReadBrightnessTemplate => Context.GetString("commands.readBrightness", "R{channel:D2}\\r\\n");

    private string ResponsePrefix => Context.GetString("commands.responsePrefix", "OK");

    protected override async Task OnConnectAsync(CancellationToken ct)
    {
        var client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Context.GetInt("connectTimeoutMs", 3000));

        try
        {
            await client.ConnectAsync(Host, Port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw new CommunicationException(ErrorCodes.DeviceConnectFailed, $"光源控制器连接超时 {Host}:{Port}");
        }
        catch (SocketException ex)
        {
            client.Dispose();
            throw new CommunicationException(
                ErrorCodes.DeviceConnectFailed,
                $"光源控制器连接失败 {Host}:{Port}：{ex.Message}",
                ex);
        }

        _client = client;
        _stream = client.GetStream();
        _logger.LogInformation("TCP 光源控制器已连接：{Host}:{Port} 通道数={Count}", Host, Port, ChannelCount);

        if (Context.GetBool("autoEnableChannels", false))
        {
            for (var channel = 0; channel < ChannelCount; channel++)
            {
                await SetChannelEnabledAsync(channel, true, ct).ConfigureAwait(false);
                await SetBrightnessAsync(channel, Context.GetInt("defaultBrightness", 128), ct).ConfigureAwait(false);
            }
        }
    }

    protected override Task OnDisconnectAsync()
    {
        _stream?.Dispose();
        _stream = null;
        _client?.Dispose();
        _client = null;
        return Task.CompletedTask;
    }

    public async Task SetBrightnessAsync(int channel, int value, CancellationToken ct)
    {
        ValidateChannel(channel);
        await SendAsync(CommandTemplate.Format(SetBrightnessTemplate, channel, Math.Clamp(value, 0, 255)), ct)
            .ConfigureAwait(false);
    }

    public async Task SetChannelEnabledAsync(int channel, bool enabled, CancellationToken ct)
    {
        ValidateChannel(channel);
        await SendAsync(CommandTemplate.Format(SetChannelEnabledTemplate, channel, 0, enabled), ct)
            .ConfigureAwait(false);
    }

    public async Task<int?> ReadBrightnessAsync(int channel, CancellationToken ct)
    {
        ValidateChannel(channel);
        var response = await QueryAsync(CommandTemplate.Format(ReadBrightnessTemplate, channel), ct)
            .ConfigureAwait(false);

        var digits = new string(response.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : null;
    }

    public Task SetTriggerModeAsync(LightTriggerMode mode, CancellationToken ct)
    {
        TriggerMode = mode;
        return Task.CompletedTask;
    }

    private async Task SendAsync(string command, CancellationToken ct)
    {
        var stream = _stream ?? throw new DeviceException(ErrorCodes.DeviceDisconnected, "光源控制器未连接");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var bytes = Encoding.ASCII.GetBytes(command);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            _logger.LogDebug("光源命令已发送：{Command}", command.TrimEnd('\r', '\n'));
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            ReportAlarm(ex.Message);
            throw new CommunicationException(ErrorCodes.CommWriteFailed, $"光源写入失败：{ex.Message}", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> QueryAsync(string command, CancellationToken ct)
    {
        var stream = _stream ?? throw new DeviceException(ErrorCodes.DeviceDisconnected, "光源控制器未连接");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var bytes = Encoding.ASCII.GetBytes(command);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            var buffer = new byte[128];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Context.GetInt("readTimeoutMs", 500));

            var read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            var response = Encoding.ASCII.GetString(buffer, 0, read);

            if (!string.IsNullOrEmpty(ResponsePrefix)
                && !response.Contains(ResponsePrefix, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("光源响应未包含期望前缀 {Prefix}：{Response}", ResponsePrefix, response);
            }

            return response;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CommunicationException(ErrorCodes.CommTimeout, "光源响应超时");
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            ReportAlarm(ex.Message);
            throw new CommunicationException(ErrorCodes.CommReadFailed, $"光源读取失败：{ex.Message}", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ValidateChannel(int channel)
    {
        if (channel < 0 || channel >= ChannelCount)
        {
            throw new CommunicationException(ErrorCodes.CommAddressInvalid, $"通道超出范围：{channel}");
        }
    }
}

/// <summary>TCP 光源控制器驱动注册。</summary>
public sealed class TcpLightControllerProvider : IDeviceDriverProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public TcpLightControllerProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "light.tcp";

    public DeviceKind Kind => DeviceKind.LightController;

    public string DisplayName => "TCP 光源控制器";

    public string? SdkHint => "无需额外 SDK";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public ParameterSchema Schema { get; } = new()
    {
        new TextParameter("ip", "IP 地址", "192.168.0.30") { Required = true },
        new IntegerParameter("port", "端口", 1, 65535, 5000),
        new IntegerParameter("channelCount", "通道数", 1, 32, 4),
        new IntegerParameter("connectTimeoutMs", "连接超时", 100, 60000, 3000) { Unit = "ms" },
        new IntegerParameter("readTimeoutMs", "读超时", 50, 10000, 500) { Unit = "ms" },
        new BooleanParameter("autoEnableChannels", "连接后自动开启通道", false),
        new IntegerParameter("defaultBrightness", "默认亮度", 0, 255, 128),
        new TextParameter("commands.setBrightness", "设置亮度模板", "S{channel:D2}{value:D3}\\r\\n") { Group = "命令模板" },
        new TextParameter("commands.setChannelEnabled", "通道开关模板", "L{channel:D2}{state}\\r\\n") { Group = "命令模板" },
        new TextParameter("commands.readBrightness", "读取亮度模板", "R{channel:D2}\\r\\n") { Group = "命令模板" },
        new TextParameter("commands.responsePrefix", "响应前缀", "OK") { Group = "命令模板" }
    };

    public IDeviceDriver Create(IDeviceContext context)
        => new TcpLightController(context, _loggerFactory.CreateLogger<TcpLightController>());
}
