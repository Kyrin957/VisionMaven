using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Drivers;
using VisionMaven.Vision.Imaging;

namespace VisionMaven.Devices.Mock;

/// <summary>模拟相机驱动：内置图案发生器，无硬件即可跑通采集 → 处理 → 判定全链路。</summary>
[VisionDriver("mock.camera", DeviceKind.Camera, DisplayName = "模拟相机（内置图案）")]
public sealed class MockCamera : DeviceDriverBase, ICamera
{
    private readonly IImageFrameFactory _frames;
    private readonly ILogger<MockCamera> _logger;
    private readonly Channel<IImageFrame> _channel = Channel.CreateBounded<IImageFrame>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

    private readonly ConcurrentDictionary<string, double> _parameters = new(StringComparer.OrdinalIgnoreCase);

    private MockImageGenerator? _generator;
    private CancellationTokenSource? _grabCts;
    private Task? _grabLoop;
    private CameraStreamSettings _settings = new();
    private long _sequenceNo;

    public MockCamera(IDeviceContext context, IImageFrameFactory frames, ILogger<MockCamera> logger)
        : base(context, DeviceKind.Camera)
    {
        _frames = frames;
        _logger = logger;
    }

    public override string DriverKey => "mock.camera";

    public CameraCapabilities Capabilities { get; } = new(
        new[]
        {
            CameraParameterKeys.ExposureUs,
            CameraParameterKeys.Gain,
            CameraParameterKeys.FrameRateLimit,
            CameraParameterKeys.TriggerMode
        },
        new[] { PixelFormat.Mono8 },
        new[] { TriggerMode.Off, TriggerMode.Software, TriggerMode.Hardware },
        2448,
        2048);

    public bool IsGrabbing { get; private set; }

    public event EventHandler<FrameArrivedEventArgs>? FrameArrived;

    protected override Task OnConnectAsync(CancellationToken ct)
    {
        var width = Context.GetInt("width", 1280);
        var height = Context.GetInt("height", 1024);
        var spots = Context.GetInt("spotCount", 3);

        var triggerMode = Enum.TryParse<TriggerMode>(
            Context.GetString("triggerMode", nameof(TriggerMode.Software)),
            true,
            out var parsedMode)
            ? parsedMode
            : TriggerMode.Software;

        _generator = new MockImageGenerator(width, height, spots);
        ApplyStreamSettingsAsync(
            new CameraStreamSettings
            {
                ExposureUs = Context.GetDouble("exposureUs", 8000),
                Gain = Context.GetDouble("gain", 1d),
                FrameRateLimit = Context.GetDouble("frameRateLimit", 30d),
                TriggerMode = triggerMode
            },
            ct).GetAwaiter().GetResult();

        _logger.LogInformation("模拟相机已就绪：{Width}x{Height} 光斑数={Spots}", width, height, spots);
        return Task.CompletedTask;
    }

    protected override async Task OnDisconnectAsync()
    {
        await StopGrabbingAsync().ConfigureAwait(false);
        _generator?.Dispose();
        _generator = null;
    }

    public Task StartGrabbingAsync(CancellationToken ct)
    {
        if (IsGrabbing || _generator is null)
        {
            return Task.CompletedTask;
        }

        IsGrabbing = true;
        _grabCts = new CancellationTokenSource();
        _grabLoop = Task.Run(() => GrabLoopAsync(_grabCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopGrabbingAsync()
    {
        if (!IsGrabbing)
        {
            return;
        }

        IsGrabbing = false;
        if (_grabCts is not null)
        {
            await _grabCts.CancelAsync().ConfigureAwait(false);
        }

        if (_grabLoop is not null)
        {
            try
            {
                await _grabLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常停止路径。
            }
        }

        _grabCts?.Dispose();
        _grabCts = null;
        _grabLoop = null;
    }

    public async Task<IImageFrame> GetFrameAsync(CancellationToken ct)
    {
        if (!IsGrabbing)
        {
            await StartGrabbingAsync(ct).ConfigureAwait(false);
        }

        return await _channel.Reader.ReadAsync(ct).ConfigureAwait(false);
    }

    public Task SetParameterAsync(string key, double value, CancellationToken ct)
    {
        _parameters[key] = value;
        _logger.LogDebug("模拟相机参数设置：{Key}={Value}", key, value);
        return Task.CompletedTask;
    }

    public Task<double> GetParameterAsync(string key, CancellationToken ct)
        => Task.FromResult(_parameters.TryGetValue(key, out var value) ? value : 0d);

    public Task ApplyStreamSettingsAsync(CameraStreamSettings settings, CancellationToken ct)
    {
        _settings = settings;
        _parameters[CameraParameterKeys.ExposureUs] = settings.ExposureUs;
        _parameters[CameraParameterKeys.Gain] = settings.Gain;
        _parameters[CameraParameterKeys.FrameRateLimit] = settings.FrameRateLimit;
        return Task.CompletedTask;
    }

    private async Task GrabLoopAsync(CancellationToken ct)
    {
        var frameRate = Math.Clamp(_settings.FrameRateLimit, 1d, 120d);
        var interval = TimeSpan.FromMilliseconds(1000d / frameRate);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var generator = _generator;
                if (generator is null)
                {
                    break;
                }

                var sequence = Interlocked.Increment(ref _sequenceNo);
                var mat = generator.CreateFrame(sequence);
                var frame = new MatImageFrame(DeviceId, mat, sequence, DateTimeOffset.Now);

                // 容量 1：写入前主动丢弃并释放旧帧，避免缓冲被覆盖后泄漏。
                if (_channel.Reader.TryRead(out var stale))
                {
                    stale.Dispose();
                }

                _channel.Writer.TryWrite(frame);

                try
                {
                    FrameArrived?.Invoke(this, new FrameArrivedEventArgs(frame));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "预览订阅者处理异常");
                }

                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("模拟相机取流已取消");
        }
        catch (Exception ex)
        {
            ReportAlarm(ex.Message);
            _logger.LogError(ex, "模拟相机取流异常");
        }
    }
}

/// <summary>模拟相机驱动注册。</summary>
public sealed class MockCameraProvider : IDeviceDriverProvider
{
    private readonly IImageFrameFactory _frames;
    private readonly ILoggerFactory _loggerFactory;

    public MockCameraProvider(IImageFrameFactory frames, ILoggerFactory loggerFactory)
    {
        _frames = frames;
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "mock.camera";

    public DeviceKind Kind => DeviceKind.Camera;

    public string DisplayName => "模拟相机（内置图案）";

    public string? SdkHint => null;

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public ParameterSchema Schema { get; } = new()
    {
        new IntegerParameter("width", "画面宽度", 64, 8192, 1280) { Unit = "px" },
        new IntegerParameter("height", "画面高度", 64, 8192, 1024) { Unit = "px" },
        new IntegerParameter("spotCount", "光斑数量", 0, 32, 3),
        new NumberParameter("exposureUs", "曝光时间", 1d, 1_000_000d, 8000d) { Unit = "us" },
        new NumberParameter("gain", "增益", 0d, 64d, 1d),
        new NumberParameter("frameRateLimit", "帧率上限", 1d, 120d, 30d) { Unit = "fps" },
        new EnumParameter("triggerMode", "触发模式", new[] { "Off", "Software", "Hardware" }, "Software")
    };

    public IDeviceDriver Create(IDeviceContext context)
        => new MockCamera(context, _frames, _loggerFactory.CreateLogger<MockCamera>());
}
