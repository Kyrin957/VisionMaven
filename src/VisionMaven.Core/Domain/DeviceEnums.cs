namespace VisionMaven.Core.Domain;

/// <summary>设备大类。新增一类设备时追加枚举值并注册 <c>DeviceTypeDescriptor</c>。</summary>
public enum DeviceKind
{
    Camera = 0,
    LightController = 1,
    Plc = 2,
    Robot = 3,
    Mes = 4,
    Other = 99
}

/// <summary>设备连接状态。</summary>
public enum DeviceState
{
    Disconnected = 0,
    Connecting = 1,
    Ready = 2,
    Alarm = 3
}

/// <summary>相机像素格式。</summary>
public enum PixelFormat
{
    Mono8 = 0,
    Mono10 = 1,
    Mono12 = 2,
    BayerRG8 = 3,
    BayerGB8 = 4,
    Bgr8 = 5,
    Rgb8 = 6,
    Bgra8 = 7
}

/// <summary>相机触发模式。</summary>
public enum TriggerMode
{
    Off = 0,
    Software = 1,
    Hardware = 2
}

/// <summary>光源控制器触发模式。</summary>
public enum LightTriggerMode
{
    Continuous = 0,
    Strobe = 1,
    ExternalTrigger = 2
}

/// <summary>工位触发源。</summary>
public enum TriggerSource
{
    Hardware = 0,
    Software = 1,
    Plc = 2,
    Timer = 3
}

/// <summary>触发边沿。</summary>
public enum TriggerEdge
{
    Rising = 0,
    Falling = 1,
    Level = 2
}

/// <summary>新触发到达而上一实例未完成时的处理策略。</summary>
public enum OverrunPolicy
{
    /// <summary>丢弃新触发并计数告警（默认）。</summary>
    DropNew = 0,

    /// <summary>丢弃当前实例，立即执行新触发。</summary>
    DropOld = 1,

    /// <summary>排队等待，最多积压 <c>OverrunQueueDepth</c> 个。</summary>
    QueueAndWait = 2
}

/// <summary>通讯点位数据类型。</summary>
public enum PointDataType
{
    Bool = 0,
    Int16 = 1,
    Int32 = 2,
    Float = 3,
    Double = 4,
    String = 5
}

/// <summary>通讯点位读写方向。</summary>
public enum PointDirection
{
    Read = 0,
    Write = 1,
    ReadWrite = 2
}
