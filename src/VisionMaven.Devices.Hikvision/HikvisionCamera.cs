using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Drivers;
using VisionMaven.Core.Exceptions;
using VisionMaven.Vision.Imaging;

namespace VisionMaven.Devices.Hikvision;

/// <summary>一台已枚举到的海康设备。</summary>
public sealed record HikvisionDeviceInfo(
    string SerialNumber,
    string ModelName,
    string Manufacturer,
    string Ip,
    uint TransportLayerType);

/// <summary>
/// 海康威视 MVS 相机驱动。SDK 缺失时只记告警并使驱动不可用，不阻断主程序启动。
/// </summary>
[VisionDriver("hikvision", DeviceKind.Camera, DisplayName = "海康威视 MVS", SdkHint = "需安装 MVS 客户端并复制 MvCameraControl.dll 到输出目录")]
public sealed class HikvisionCamera : DeviceDriverBase, ICamera
{
    private readonly IImageFrameFactory _frames;
    private readonly ILogger<HikvisionCamera> _logger;

    private readonly Channel<IImageFrame> _channel = Channel.CreateBounded<IImageFrame>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

    private IntPtr _handle = IntPtr.Zero;
    private byte[]? _buffer;
    private int _width;
    private int _height;
    private long _sequenceNo;
    private Task? _grabLoop;
    private CancellationTokenSource? _grabCts;

    public HikvisionCamera(IDeviceContext context, IImageFrameFactory frames, ILogger<HikvisionCamera> logger)
        : base(context, DeviceKind.Camera)
    {
        _frames = frames;
        _logger = logger;
    }

    public override string DriverKey => "hikvision";

    public CameraCapabilities Capabilities { get; } = new(
        new[]
        {
            CameraParameterKeys.ExposureUs,
            CameraParameterKeys.Gain,
            CameraParameterKeys.FrameRateLimit,
            CameraParameterKeys.TriggerMode
        },
        new[] { PixelFormat.Mono8, PixelFormat.Bgr8, PixelFormat.BayerRG8 },
        new[] { TriggerMode.Off, TriggerMode.Software, TriggerMode.Hardware },
        2448,
        2048);

    public bool IsGrabbing { get; private set; }

    public event EventHandler<FrameArrivedEventArgs>? FrameArrived;

    /// <summary>SDK 是否可用（MvCameraControl.dll 是否可加载）。</summary>
    public static bool IsSdkAvailable { get; } = ProbeSdk();

    internal static string? SdkProbeError { get; private set; }

    private static bool ProbeSdk()
    {
        try
        {
            MvsNative.MV_CC_DestroyHandle(IntPtr.Zero);
            return true;
        }
        catch (DllNotFoundException ex)
        {
            SdkProbeError = ex.Message;
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            SdkProbeError = ex.Message;
            return false;
        }
        catch (Exception)
        {
            // 句柄为空导致 SDK 返回错误码属正常现象，说明 DLL 已成功加载。
            return true;
        }
    }

    /// <summary>枚举 GigE 与 USB 两类设备。</summary>
    public static IReadOnlyList<HikvisionDeviceInfo> EnumerateDevices()
    {
        if (!IsSdkAvailable)
        {
            return Array.Empty<HikvisionDeviceInfo>();
        }

        var results = new List<HikvisionDeviceInfo>();
        foreach (var transport in new[] { MvsNative.TransportLayerGigE, MvsNative.TransportLayerUsb })
        {
            results.AddRange(EnumerateByTransport(transport));
        }

        return results;
    }

    private static IEnumerable<HikvisionDeviceInfo> EnumerateByTransport(uint transportLayer)
    {
        var list = new MvsNative.MvDeviceInfoList { DeviceInfo = new IntPtr[MvsNative.MaxDeviceNum] };
        if (MvsNative.MV_CC_EnumDevices(transportLayer, ref list) != 0 || list.DeviceNum == 0)
        {
            yield break;
        }

        for (var index = 0; index < list.DeviceNum && index < MvsNative.MaxDeviceNum; index++)
        {
            var pointer = list.DeviceInfo[index];
            if (pointer == IntPtr.Zero)
            {
                continue;
            }

            var info = Marshal.PtrToStructure<MvsNative.MvDeviceInfo>(pointer);
            yield return new HikvisionDeviceInfo(
                MvsNative.ReadAnsi(info.GigEInfo.SerialNumber),
                MvsNative.ReadAnsi(info.GigEInfo.ModelName),
                MvsNative.ReadAnsi(info.GigEInfo.ManufacturerName),
                MvsNative.IpOf(info.GigEInfo.CurrentIp),
                info.TransportLayerType);
        }
    }

    protected override Task OnConnectAsync(CancellationToken ct)
    {
        if (!IsSdkAvailable)
        {
            throw new DeviceException(
                ErrorCodes.DeviceSdkMissing,
                $"未找到 MvCameraControl.dll：{SdkProbeError}");
        }

        var serialNumber = Context.GetString("serialNumber", string.Empty);
        var targetIp = Context.GetString("ip", string.Empty);

        var list = new MvsNative.MvDeviceInfoList { DeviceInfo = new IntPtr[MvsNative.MaxDeviceNum] };
        var transport = MvsNative.TransportLayerGigE | MvsNative.TransportLayerUsb;
        var code = MvsNative.MV_CC_EnumDevices(transport, ref list);
        if (code != 0 || list.DeviceNum == 0)
        {
            throw new DeviceException(ErrorCodes.DeviceConnectFailed, "未枚举到海康相机");
        }

        IntPtr deviceInfo = IntPtr.Zero;
        for (var index = 0; index < list.DeviceNum; index++)
        {
            var candidate = list.DeviceInfo[index];
            if (candidate == IntPtr.Zero)
            {
                continue;
            }

            var info = Marshal.PtrToStructure<MvsNative.MvDeviceInfo>(candidate);
            var serial = MvsNative.ReadAnsi(info.GigEInfo.SerialNumber);
            var ip = MvsNative.IpOf(info.GigEInfo.CurrentIp);

            var matchesSerial = !string.IsNullOrWhiteSpace(serialNumber)
                                && string.Equals(serial, serialNumber, StringComparison.OrdinalIgnoreCase);
            var matchesIp = !string.IsNullOrWhiteSpace(targetIp)
                            && string.Equals(ip, targetIp, StringComparison.OrdinalIgnoreCase);

            if (matchesSerial || matchesIp || (string.IsNullOrWhiteSpace(serialNumber) && index == 0))
            {
                deviceInfo = candidate;
                break;
            }
        }

        if (deviceInfo == IntPtr.Zero)
        {
            throw new DeviceException(
                ErrorCodes.DeviceConnectFailed,
                $"未找到匹配的相机（序列号={serialNumber} IP={targetIp}）");
        }

        var handle = IntPtr.Zero;
        code = MvsNative.MV_CC_CreateHandle(ref handle, deviceInfo);
        if (code != 0)
        {
            throw new DeviceException(ErrorCodes.DeviceConnectFailed, $"MV_CC_CreateHandle 失败：0x{code:X8}");
        }

        code = MvsNative.MV_CC_OpenDevice(handle, 0, 0);
        if (code != 0)
        {
            MvsNative.MV_CC_DestroyHandle(handle);
            throw new DeviceException(ErrorCodes.DeviceConnectFailed, $"MV_CC_OpenDevice 失败：0x{code:X8}");
        }

        _handle = handle;
        MvsNative.MV_CC_SetImageNodeNum(handle, 3);

        var widthValue = new MvsNative.MvccIntValue { Reserved = new uint[4] };
        var heightValue = new MvsNative.MvccIntValue { Reserved = new uint[4] };
        MvsNative.MV_CC_GetIntValue(handle, "Width", ref widthValue);
        MvsNative.MV_CC_GetIntValue(handle, "Height", ref heightValue);

        _width = widthValue.CurrentValue > 0 ? (int)widthValue.CurrentValue : 2448;
        _height = heightValue.CurrentValue > 0 ? (int)heightValue.CurrentValue : 2048;
        _buffer = new byte[_width * _height * 3];

        var stream = new CameraStreamSettings
        {
            ExposureUs = Context.GetDouble("exposureUs", 8000),
            Gain = Context.GetDouble("gain", 1d),
            TriggerMode = Enum.TryParse<TriggerMode>(Context.GetString("triggerMode", "Software"), true, out var mode)
                ? mode
                : TriggerMode.Software,
            TriggerSource = Context.GetString("triggerSource", "Line0"),
            PixelFormat = Enum.TryParse<PixelFormat>(Context.GetString("pixelFormat", "Mono8"), true, out var format)
                ? format
                : PixelFormat.Mono8
        };

        ApplyStreamSettingsAsync(stream, ct).GetAwaiter().GetResult();

        _logger.LogInformation(
            "海康相机已连接：{Width}x{Height} 序列号={Serial}",
            _width,
            _height,
            MvsNative.ReadAnsi(Marshal.PtrToStructure<MvsNative.MvDeviceInfo>(deviceInfo).GigEInfo.SerialNumber));

        return Task.CompletedTask;
    }

    protected override async Task OnDisconnectAsync()
    {
        await StopGrabbingAsync().ConfigureAwait(false);

        var handle = _handle;
        _handle = IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            MvsNative.MV_CC_CloseDevice(handle);
            MvsNative.MV_CC_DestroyHandle(handle);
        }

        _buffer = null;
    }

    public Task StartGrabbingAsync(CancellationToken ct)
    {
        if (IsGrabbing || _handle == IntPtr.Zero)
        {
            return Task.CompletedTask;
        }

        var code = MvsNative.MV_CC_StartGrabbing(_handle);
        if (code != 0)
        {
            throw new DeviceException(ErrorCodes.DeviceConnectFailed, $"MV_CC_StartGrabbing 失败：0x{code:X8}");
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

        if (_handle != IntPtr.Zero)
        {
            MvsNative.MV_CC_StopGrabbing(_handle);
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
        var handle = RequireHandle();
        int code;

        switch (key)
        {
            case CameraParameterKeys.ExposureUs:
                code = MvsNative.MV_CC_SetFloatValue(handle, "ExposureTime", (float)value);
                break;

            case CameraParameterKeys.Gain:
                code = MvsNative.MV_CC_SetFloatValue(handle, "Gain", (float)value);
                break;

            case CameraParameterKeys.FrameRateLimit:
                code = MvsNative.MV_CC_SetFloatValue(handle, "AcquisitionFrameRate", (float)value);
                break;

            default:
                throw new DeviceException(ErrorCodes.DeviceParameterUnsupported, $"相机不支持参数：{key}");
        }

        if (code != 0)
        {
            throw new DeviceException(
                ErrorCodes.DeviceParameterUnsupported,
                $"设置参数 {key} 失败：0x{code:X8}");
        }

        return Task.CompletedTask;
    }

    public Task<double> GetParameterAsync(string key, CancellationToken ct)
    {
        var handle = RequireHandle();
        var value = 0f;
        var node = key switch
        {
            CameraParameterKeys.ExposureUs => "ExposureTime",
            CameraParameterKeys.Gain => "Gain",
            CameraParameterKeys.FrameRateLimit => "AcquisitionFrameRate",
            _ => throw new DeviceException(ErrorCodes.DeviceParameterUnsupported, $"相机不支持参数：{key}")
        };

        var code = MvsNative.MV_CC_GetFloatValue(handle, node, ref value);
        if (code != 0)
        {
            throw new DeviceException(
                ErrorCodes.DeviceParameterUnsupported,
                $"读取参数 {key} 失败：0x{code:X8}");
        }

        return Task.FromResult((double)value);
    }

    public Task ApplyStreamSettingsAsync(CameraStreamSettings settings, CancellationToken ct)
    {
        var handle = RequireHandle();

        MvsNative.MV_CC_SetEnumValue(handle, "TriggerMode", (uint)(settings.TriggerMode == TriggerMode.Off ? 0 : 1));
        if (settings.TriggerMode == TriggerMode.Software)
        {
            MvsNative.MV_CC_SetEnumValue(handle, "TriggerSource", 7);
        }
        else if (settings.TriggerMode == TriggerMode.Hardware)
        {
            MvsNative.MV_CC_SetEnumValue(handle, "TriggerSource", 0);
        }

        MvsNative.MV_CC_SetEnumValue(
            handle,
            "PixelFormat",
            settings.PixelFormat == PixelFormat.Bgr8 ? MvsNative.PixelTypeBgr8 : MvsNative.PixelTypeMono8);

        MvsNative.MV_CC_SetFloatValue(handle, "ExposureTime", (float)settings.ExposureUs);
        MvsNative.MV_CC_SetFloatValue(handle, "Gain", (float)settings.Gain);

        if (settings.FrameRateLimit > 0)
        {
            MvsNative.MV_CC_SetFloatValue(handle, "AcquisitionFrameRate", (float)settings.FrameRateLimit);
        }

        return Task.CompletedTask;
    }

    private IntPtr RequireHandle()
        => _handle == IntPtr.Zero
            ? throw new DeviceException(ErrorCodes.DeviceDisconnected, "海康相机未连接")
            : _handle;

    private Task GrabLoopAsync(CancellationToken ct)
    {
        var handle = _handle;
        var buffer = _buffer;
        if (handle == IntPtr.Zero || buffer is null)
        {
            return Task.CompletedTask;
        }

        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var pointer = pinned.AddrOfPinnedObject();
            var size = (uint)buffer.Length;

            while (!ct.IsCancellationRequested)
            {
                var info = new MvsNative.MvFrameOutInfoEx { Reserved = new uint[16] };
                var code = MvsNative.MV_CC_GetOneFrameTimeout(handle, pointer, size, ref info, 1000);
                if (code != 0)
                {
                    continue;
                }

                var frame = CreateFrame(buffer, info);
                if (frame is null)
                {
                    continue;
                }

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
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("海康取流已取消");
        }
        catch (Exception ex)
        {
            ReportAlarm(ex.Message);
            _logger.LogError(ex, "海康取流异常");
        }
        finally
        {
            pinned.Free();
        }

        return Task.CompletedTask;
    }

    private IImageFrame? CreateFrame(byte[] buffer, MvsNative.MvFrameOutInfoEx info)
    {
        var width = info.Width;
        var height = info.Height;
        if (width == 0 || height == 0)
        {
            return null;
        }

        var sequence = Interlocked.Increment(ref _sequenceNo);

        if (info.PixelType == MvsNative.PixelTypeBgr8)
        {
            var bgr = new byte[width * height * 3];
            Array.Copy(buffer, bgr, bgr.Length);
            var mat = Mat.FromPixelData(height, width, MatType.CV_8UC3, bgr);
            return new MatImageFrame(DeviceId, mat, sequence, DateTimeOffset.Now);
        }

        var mono = new byte[width * height];
        Array.Copy(buffer, mono, mono.Length);
        var monoMat = Mat.FromPixelData(height, width, MatType.CV_8UC1, mono);
        return new MatImageFrame(DeviceId, monoMat, sequence, DateTimeOffset.Now);
    }
}

/// <summary>海康 MVS 驱动注册。</summary>
public sealed class HikvisionCameraProvider : IDeviceDriverProvider
{
    private readonly IImageFrameFactory _frames;
    private readonly ILoggerFactory _loggerFactory;

    public HikvisionCameraProvider(IImageFrameFactory frames, ILoggerFactory loggerFactory)
    {
        _frames = frames;
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "hikvision";

    public DeviceKind Kind => DeviceKind.Camera;

    public string DisplayName => "海康威视 MVS";

    public string? SdkHint => "安装 MVS 客户端并复制 MvCameraControl.dll 与 MvCameraControl.Net.dll 到输出目录";

    public bool IsAvailable => HikvisionCamera.IsSdkAvailable;

    public string? UnavailableReason => HikvisionCamera.IsSdkAvailable
        ? null
        : "未找到 MvCameraControl.dll";

    public ParameterSchema Schema { get; } = new()
    {
        new TextParameter("serialNumber", "序列号", string.Empty),
        new TextParameter("ip", "IP 地址", string.Empty),
        new EnumParameter("interface", "接口", new[] { "GigE", "USB" }, "GigE"),
        new NumberParameter("exposureUs", "曝光时间", 1d, 1_000_000d, 8000d) { Unit = "us" },
        new NumberParameter("gain", "增益", 0d, 64d, 1d),
        new EnumParameter("triggerMode", "触发模式", new[] { "Off", "Software", "Hardware" }, "Hardware"),
        new EnumParameter("triggerSource", "触发源", new[] { "Line0", "Line1", "Line2", "Line3", "Software" }, "Line0"),
        new EnumParameter("pixelFormat", "像素格式", new[] { "Mono8", "Bgr8", "BayerRG8" }, "Mono8"),
        new NumberParameter("frameRateLimit", "帧率上限", 1d, 120d, 30d) { Unit = "fps" }
    };

    public IDeviceDriver Create(IDeviceContext context)
        => new HikvisionCamera(context, _frames, _loggerFactory.CreateLogger<HikvisionCamera>());
}
