using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VisionMaven.Core.Domain;

namespace VisionMaven.App.Converters;

/// <summary>布尔取反。</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag && !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag && !flag;
}

/// <summary>布尔 → Visibility，参数为 <c>Invert</c> 时取反。</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool typed && typed;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>对象为 null 或空字符串时折叠。</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value is null || (value is string text && string.IsNullOrWhiteSpace(text));
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            empty = !empty;
        }

        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>布尔 → 宽度值，参数格式为 <c>展开值|折叠值</c>。</summary>
public sealed class BooleanToDoubleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "200|48").Split('|');
        var expanded = double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var left) ? left : 200d;
        var collapsed = parts.Length > 1
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var right)
            ? right
            : 48d;

        return value is bool flag && flag ? collapsed : expanded;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>布尔 → 网格长度（像素），参数格式为 <c>展开值|折叠值</c>。</summary>
public sealed class BooleanToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "200|48").Split('|');
        var expanded = parts.Length > 0
                       && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var left)
            ? left
            : 200d;
        var collapsed = parts.Length > 1
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var right)
            ? right
            : 48d;

        return new GridLength(value is bool flag && flag ? collapsed : expanded, GridUnitType.Pixel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>日志级别 → 颜色。</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var brush = value switch
        {
            VisionLogLevel.Verbose or VisionLogLevel.Debug => "#FF8B949E",
            VisionLogLevel.Information => "#FF58A6FF",
            VisionLogLevel.Warning => "#FFD29922",
            VisionLogLevel.Error or VisionLogLevel.Fatal => "#FFF85149",
            _ => "#FF8B949E"
        };

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(brush));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>设备 / 工位状态 → 颜色。</summary>
public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var brush = value switch
        {
            DeviceState.Ready or StationState.Running or InspectionResult.Ok => "#FF3FB950",
            DeviceState.Connecting or StationState.Starting or StationState.Stopping => "#FFD29922",
            DeviceState.Alarm or StationState.Alarm or InspectionResult.Ng => "#FFF85149",
            InspectionResult.Unknown => "#FF8B949E",
            _ => "#FF8B949E"
        };

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(brush));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>枚举 → 中文显示名。</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Disconnected"] = "未连接",
        ["Connecting"] = "连接中",
        ["Ready"] = "已就绪",
        ["Alarm"] = "报警",
        ["Stopped"] = "已停止",
        ["Starting"] = "启动中",
        ["Running"] = "运行中",
        ["Stopping"] = "停止中",
        ["Ok"] = "OK",
        ["Ng"] = "NG",
        ["Unknown"] = "未判定",
        ["Info"] = "信息",
        ["Warning"] = "警告",
        ["Error"] = "错误",
        ["Critical"] = "严重",
        ["Camera"] = "相机",
        ["LightController"] = "光源控制器",
        ["Plc"] = "PLC",
        ["Robot"] = "机器人",
        ["Mes"] = "MES",
        ["Other"] = "其他设备"
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        return Map.TryGetValue(text, out var display) ? display : text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
