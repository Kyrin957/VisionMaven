using System.Globalization;
using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Abstractions;

/// <summary>参数控件类型，界面据此自动生成表单。</summary>
public enum ParameterValueKind
{
    Text = 0,
    Integer = 1,
    Number = 2,
    Boolean = 3,
    Enum = 4,
    File = 5,
    Directory = 6,
    DeviceId = 7,
    Image = 8,
    Address = 9,
    Password = 10
}

/// <summary>参数元素基类：声明一个可配置项的取值类型与约束。</summary>
public abstract record ParameterElement(string Key, string DisplayName, ParameterValueKind Kind)
{
    /// <summary>分组名，空则不分组。</summary>
    public string? Group { get; init; }

    public string? Unit { get; init; }

    public bool Required { get; init; }

    public int Order { get; init; }

    /// <summary>默认值的字符串表示，供界面初始化与参数落盘。</summary>
    public abstract string? DefaultText { get; }
}

/// <summary>文本参数。</summary>
public sealed record TextParameter(string Key, string DisplayName, string? DefaultValue = null)
    : ParameterElement(Key, DisplayName, ParameterValueKind.Text)
{
    public int MaxLength { get; init; }

    public override string? DefaultText => DefaultValue;
}

/// <summary>整数参数。</summary>
public sealed record IntegerParameter(string Key, string DisplayName, long Min, long Max, long DefaultValue)
    : ParameterElement(Key, DisplayName, ParameterValueKind.Integer)
{
    public override string? DefaultText => DefaultValue.ToString(CultureInfo.InvariantCulture);
}

/// <summary>浮点参数。</summary>
public sealed record NumberParameter(string Key, string DisplayName, double Min, double Max, double DefaultValue)
    : ParameterElement(Key, DisplayName, ParameterValueKind.Number)
{
    public int Decimals { get; init; } = 3;

    public override string? DefaultText => DefaultValue.ToString(CultureInfo.InvariantCulture);
}

/// <summary>布尔参数。</summary>
public sealed record BooleanParameter(string Key, string DisplayName, bool DefaultValue)
    : ParameterElement(Key, DisplayName, ParameterValueKind.Boolean)
{
    public override string? DefaultText => DefaultValue ? "True" : "False";
}

/// <summary>枚举参数。</summary>
public sealed record EnumParameter(string Key, string DisplayName, IReadOnlyList<string> Options, string DefaultValue)
    : ParameterElement(Key, DisplayName, ParameterValueKind.Enum)
{
    public override string? DefaultText => DefaultValue;

    /// <summary>界面显示名 → 实际取值 的映射（为空时直接显示取值）。</summary>
    public IReadOnlyDictionary<string, string>? DisplayMap { get; init; }
}

/// <summary>文件参数。</summary>
public sealed record FileParameter(string Key, string DisplayName, string Filter, string? DefaultValue = null)
    : ParameterElement(Key, DisplayName, ParameterValueKind.File)
{
    public override string? DefaultText => DefaultValue;
}

/// <summary>目录参数。</summary>
public sealed record DirectoryParameter(string Key, string DisplayName, string? DefaultValue = null)
    : ParameterElement(Key, DisplayName, ParameterValueKind.Directory)
{
    public override string? DefaultText => DefaultValue;
}

/// <summary>密码 / 密钥参数，界面以掩码显示，落盘前由 <c>ISecretProtector</c> 加密。</summary>
public sealed record PasswordParameter(string Key, string DisplayName, string DefaultValue)
    : ParameterElement(Key, DisplayName, ParameterValueKind.Password)
{
    public override string? DefaultText => DefaultValue;
}

/// <summary>引用工程内某类实体的参数（设备 / 流程 / 模型 / 相机）。</summary>
public sealed record ReferenceParameter(string Key, string DisplayName, ParameterValueKind Kind, string EntityKind)
    : ParameterElement(Key, DisplayName, Kind)
{
    /// <summary>为 true 时允许多选，值以逗号分隔。</summary>
    public bool Multiple { get; init; }

    public override string? DefaultText => null;
}

/// <summary>参数集合：声明式描述一个算子、节点或设备驱动的可配置项。</summary>
public sealed class ParameterSchema : List<ParameterElement>
{
    public ParameterSchema()
    {
    }

    public ParameterSchema(IEnumerable<ParameterElement> elements)
        : base(elements)
    {
    }

    public ParameterElement? Find(string key)
        => this.FirstOrDefault(element => string.Equals(element.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>按 Schema 生成一组默认参数值。</summary>
    public Dictionary<string, string> CreateDefaults()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in this)
        {
            if (element.DefaultText is { } text)
            {
                result[element.Key] = text;
            }
        }

        return result;
    }
}

/// <summary>参数值读写辅助：统一处理界面传入的字符串与类型转换。</summary>
public static class ParameterReader
{
    public static string GetString(IReadOnlyDictionary<string, string> parameters, string key, string fallback)
        => parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    public static int GetInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
        => parameters.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public static long GetLong(IReadOnlyDictionary<string, string> parameters, string key, long fallback)
        => parameters.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public static double GetDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback)
        => parameters.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public static bool GetBool(IReadOnlyDictionary<string, string> parameters, string key, bool fallback)
        => parameters.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    public static TEnum GetEnum<TEnum>(IReadOnlyDictionary<string, string> parameters, string key, TEnum fallback)
        where TEnum : struct, Enum
        => parameters.TryGetValue(key, out var value) && Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : fallback;

    /// <summary>
    /// 按 Schema 校验并裁剪取值。返回 false 表示原始值非法，<paramref name="coerced"/> 为安全回退值。
    /// </summary>
    public static bool TryCoerce(ParameterElement element, string raw, out string coerced, out string? error)
    {
        coerced = raw;
        error = null;

        switch (element)
        {
            case IntegerParameter integer:
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    error = $"{element.DisplayName} 需要整数";
                    coerced = integer.DefaultText ?? "0";
                    return false;
                }

                coerced = Math.Clamp(i, integer.Min, integer.Max).ToString(CultureInfo.InvariantCulture);
                return true;

            case NumberParameter number:
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    error = $"{element.DisplayName} 需要数值";
                    coerced = number.DefaultText ?? "0";
                    return false;
                }

                coerced = Math.Clamp(d, number.Min, number.Max).ToString(CultureInfo.InvariantCulture);
                return true;

            case BooleanParameter:
                if (!bool.TryParse(raw, out var b))
                {
                    error = $"{element.DisplayName} 需要布尔值";
                    coerced = "False";
                    return false;
                }

                coerced = b.ToString();
                return true;

            case EnumParameter enumeration:
                if (!enumeration.Options.Contains(raw, StringComparer.OrdinalIgnoreCase))
                {
                    error = $"{element.DisplayName} 取值不在允许范围内";
                    coerced = enumeration.DefaultValue;
                    return false;
                }

                return true;

            default:
                return true;
        }
    }
}
