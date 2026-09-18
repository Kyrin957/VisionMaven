using System.Runtime.InteropServices;

namespace VisionMaven.Devices.Hikvision;

/// <summary>
/// MvCameraControl.dll 的原生入口。仅声明框架所需的最小集合，
/// 结构与常量以海康 MVS 安装目录下的 <c>MvCameraControl.h</c> 为准。
/// </summary>
internal static class MvsNative
{
    internal const string Library = "MvCameraControl.dll";

    internal const int MaxDeviceNum = 256;

    internal const uint TransportLayerGigE = 0x00000004;

    internal const uint TransportLayerUsb = 0x00000001;

    internal const uint PixelTypeMono8 = 0x01080001;

    internal const uint PixelTypeBgr8 = 0x02180014;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvGigEInfo
    {
        public uint IpCfgOption;

        public uint IpCfgCurrent;

        public uint CurrentIp;

        public uint CurrentSubNetMask;

        public uint DefaultGateWay;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ManufacturerName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ModelName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] DeviceVersion;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] ManufacturerSpecificInfo;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] SerialNumber;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] UserDefinedName;

        public uint NetExport;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] Reserved;
    }

    /// <summary>设备信息。仅读取前 5 个公共字段与 GigE 扩展块。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MvDeviceInfo
    {
        public ushort MajorVer;

        public ushort MinorVer;

        public uint MacAddrHigh;

        public uint MacAddrLow;

        public uint TransportLayerType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] Reserved;

        public MvGigEInfo GigEInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvDeviceInfoList
    {
        public uint DeviceNum;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxDeviceNum)]
        public IntPtr[] DeviceInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvFrameOutInfoEx
    {
        public ushort Width;

        public ushort Height;

        public uint PixelType;

        public uint FrameNum;

        public uint DeviceTimeStampHigh;

        public uint DeviceTimeStampLow;

        public uint Reserved0;

        public long HostTimeStamp;

        public uint FrameLen;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvccIntValue
    {
        public uint CurrentValue;

        public uint Max;

        public uint Min;

        public uint Inc;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] Reserved;
    }

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_EnumDevices(uint transportLayerType, ref MvDeviceInfoList deviceList);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_CreateHandle(ref IntPtr handle, IntPtr deviceInfo);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_DestroyHandle(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_OpenDevice(IntPtr handle, ushort accessMode, ushort switchoverKey);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_CloseDevice(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_StartGrabbing(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_StopGrabbing(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_GetOneFrameTimeout(
        IntPtr handle,
        IntPtr data,
        uint dataSize,
        ref MvFrameOutInfoEx frameInfo,
        uint timeoutMs);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_SetEnumValue(IntPtr handle, string key, uint value);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_SetIntValue(IntPtr handle, string key, uint value);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_SetFloatValue(IntPtr handle, string key, float value);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_GetFloatValue(IntPtr handle, string key, ref float value);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_SetImageNodeNum(IntPtr handle, uint nodeNum);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int MV_CC_GetIntValue(IntPtr handle, string key, ref MvccIntValue value);

    internal static string ReadAnsi(byte[] buffer)
    {
        var terminator = Array.IndexOf(buffer, (byte)0);
        var length = terminator >= 0 ? terminator : buffer.Length;
        return System.Text.Encoding.ASCII.GetString(buffer, 0, length).Trim();
    }

    internal static string IpOf(uint value)
        => $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
}
