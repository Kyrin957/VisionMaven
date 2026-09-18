using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>标记一个设备驱动实现，供启动时扫描注册。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class VisionDriverAttribute : Attribute
{
    public VisionDriverAttribute(string driverKey, DeviceKind kind)
    {
        DriverKey = driverKey;
        Kind = kind;
    }

    /// <summary>驱动标识，与工程配置 <c>driverKey</c> 一致。</summary>
    public string DriverKey { get; }

    public DeviceKind Kind { get; }

    /// <summary>界面上显示的驱动名，如「海康威视 MVS」。</summary>
    public string? DisplayName { get; set; }

    /// <summary>厂商 SDK 依赖锚点，缺失时用于生成诊断提示。</summary>
    public string? SdkHint { get; set; }
}

/// <summary>标记一个流程节点实现，供启动时扫描注册。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FlowNodeAttribute : Attribute
{
    public FlowNodeAttribute(string typeKey, string displayName)
    {
        TypeKey = typeKey;
        DisplayName = displayName;
    }

    public string TypeKey { get; }

    public string DisplayName { get; }

    public int Order { get; set; }

    /// <summary>节点分类，用于流程页节点选择器分组。</summary>
    public string Group { get; set; } = "通用";
}

/// <summary>标记一个图像算子实现，供算法页类别树与参数表单使用。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class VisionOperatorAttribute : Attribute
{
    public VisionOperatorAttribute(string typeKey, string displayName, OperatorCategory category)
    {
        TypeKey = typeKey;
        DisplayName = displayName;
        Category = category;
    }

    public string TypeKey { get; }

    public string DisplayName { get; }

    public OperatorCategory Category { get; }

    public int Order { get; set; }
}
