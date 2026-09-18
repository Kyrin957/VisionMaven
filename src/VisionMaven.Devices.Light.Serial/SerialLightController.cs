using System.IO.Ports;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Drivers;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Devices.Light.Serial;

/// <summary>
/// 串口数字光源控制器驱动：协议方言由 <c>commands</c> 命令模板配置，多数厂商无需写代码。
/// </summary>
[VisionDriver("light.serial", DeviceKind.LightController, DisplayName = "串口光源控制器", SdkHint = "无需额外 SDK")]
public sealed class SerialLightController : DeviceDriverBase, ILightController
{
    private readonly ILogger<SerialLightController> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SerialPort? _port;

    public SerialLightController(IDeviceContext context, ILogger<SerialLightController> logger)
        : base(context, DeviceKind.LightController)
    {
        _logger = logger;
        ChannelCount = Math.Clamp(context.GetInt("channelCount", 4), 1, 32);
    }

    public override string DriverKey => "light.serial";

    public int ChannelCount { get; }

    public LightTriggerMode TriggerMode { get; private set; } = LightTriggerMode.Strobe;

    private string SetBrightnessTemplate => Context.GetString("commands.setBrightness", "S{channel:D2}{value:D3}\\r\\n");

    private string SetChannelEnabledTemplate => Context.GetString("commands.setChannelEnabled", "L{channel:D2}{state}\\r\\n");

    private string ReadBrightnessTemplate => Context.GetString("commands.readBrightness", "R{channel:D2}\\r\\n");

    private string ResponsePrefix => Context.GetString("commands.responsePrefix", "OK");

    protected override Task OnConnectAsync(CancellationToken ct)
    {
        var portName = Context.GetString("portName", "COM1");
        var port = new SerialPort(
            portName,
            Context.GetInt("baudRate", 9600),
            ParseParity(Context.GetString("parity", "None")),
            Context.GetInt("dataBits", 8),
            ParseStopBits(Context.GetString("stopBits", "One")))
        {
            ReadTimeout = Context.GetInt("readTimeoutMs", 500),
            WriteTimeout = Context.GetInt("writeTimeoutMs", 500),
            NewLine = "\r\n"
        };

        try
        {
            port.Open();
        }
        catch (Exception ex)
        {
            port.Dispose();
            throw new DeviceException(
                ErrorCodes.DeviceConnectFailed,
                $"串口打开失败 {portName}：{ex.Message}",
                ex);
        }

        _port = port;
        _logger.LogInformation("串口光源控制器已连接：{Port} 通道数={Count}", portName, ChannelCount);
        return Task.CompletedTask;
    }

    protected override Task OnDisconnectAsync()
    {
        if (_port is not null)
        {
            try
            {
                if (_port.IsOpen)
                {
                    _port.Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "串口关闭失败");
            }

            _port.Dispose();
            _port = null;
        }

        return Task.CompletedTask;
    }

    public async Task SetBrightnessAsync(int channel, int value, CancellationToken ct)
    {
        ValidateChannel(channel);
        var clamped = Math.Clamp(value, 0, 255);
        await SendAsync(CommandTemplate.Format(SetBrightnessTemplate, channel, clamped), ct).ConfigureAwait(false);
    }

    public async Task SetChannelEnabledAsync(int channel, bool enabled, CancellationToken ct)
    {
        ValidateChannel(channel);
        await SendAsync(CommandTemplate.Format(SetChannelEnabledTemplate, channel, 0, enabled), ct).ConfigureAwait(false);
    }

    public async Task<int?> ReadBrightnessAsync(int channel, CancellationToken ct)
    {
        ValidateChannel(channel);
        var response = await QueryAsync(
                CommandTemplate.Format(ReadBrightnessTemplate, channel),
                ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

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
        var port = _port ?? throw new DeviceException(ErrorCodes.DeviceDisconnected, "串口未打开");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(command);
            await port.BaseStream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await port.BaseStream.FlushAsync(ct).ConfigureAwait(false);
            _logger.LogDebug("光源命令已发送：{Command}", command.TrimEnd('\r', '\n'));
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            ReportAlarm(ex.Message);
            throw new CommunicationException(ErrorCodes.CommWriteFailed, $"串口写入失败：{ex.Message}", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> QueryAsync(string command, CancellationToken ct)
    {
        var port = _port ?? throw new DeviceException(ErrorCodes.DeviceDisconnected, "串口未打开");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(command);
            await port.BaseStream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await port.BaseStream.FlushAsync(ct).ConfigureAwait(false);

            var buffer = new byte[64];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Math.Max(100, port.ReadTimeout));

            var read = await port.BaseStream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            var response = System.Text.Encoding.ASCII.GetString(buffer, 0, read);

            if (!string.IsNullOrEmpty(ResponsePrefix)
                && !response.Contains(ResponsePrefix, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("光源响应未包含期望前缀 {Prefix}：{Response}", ResponsePrefix, response);
            }

            return response;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CommunicationException(ErrorCodes.CommTimeout, "串口读取超时");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            ReportAlarm(ex.Message);
            throw new CommunicationException(ErrorCodes.CommReadFailed, $"串口读取失败：{ex.Message}", ex);
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

    private static Parity ParseParity(string value)
        => Enum.TryParse<Parity>(value, true, out var parity) ? parity : Parity.None;

    private static StopBits ParseStopBits(string value)
        => Enum.TryParse<StopBits>(value, true, out var stopBits) ? stopBits : StopBits.One;
}

/// <summary>串口光源控制器驱动注册。</summary>
public sealed class SerialLightControllerProvider : IDeviceDriverProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public SerialLightControllerProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "light.serial";

    public DeviceKind Kind => DeviceKind.LightController;

    public string DisplayName => "串口光源控制器";

    public string? SdkHint => "无需额外 SDK";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public ParameterSchema Schema { get; } = new()
    {
        new TextParameter("portName", "串口", "COM3") { Required = true },
        new IntegerParameter("baudRate", "波特率", 1200, 921600, 9600),
        new IntegerParameter("dataBits", "数据位", 5, 8, 8),
        new EnumParameter("stopBits", "停止位", new[] { "One", "OnePointFive", "Two" }, "One"),
        new EnumParameter("parity", "校验位", new[] { "None", "Odd", "Even", "Mark", "Space" }, "None"),
        new IntegerParameter("channelCount", "通道数", 1, 32, 4),
        new IntegerParameter("readTimeoutMs", "读超时", 50, 10000, 500) { Unit = "ms" },
        new TextParameter("commands.setBrightness", "设置亮度模板", "S{channel:D2}{value:D3}\\r\\n") { Group = "命令模板" },
        new TextParameter("commands.setChannelEnabled", "通道开关模板", "L{channel:D2}{state}\\r\\n") { Group = "命令模板" },
        new TextParameter("commands.readBrightness", "读取亮度模板", "R{channel:D2}\\r\\n") { Group = "命令模板" },
        new TextParameter("commands.responsePrefix", "响应前缀", "OK") { Group = "命令模板" }
    };

    public IDeviceDriver Create(IDeviceContext context)
        => new SerialLightController(context, _loggerFactory.CreateLogger<SerialLightController>());
}
