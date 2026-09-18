using System.Globalization;
using System.Text;

namespace VisionMaven.Core.Drivers;

/// <summary>
/// 可配置命令模板：多数光源控制器厂商只需填写模板而无需写代码。
/// 支持 <c>{channel}</c>、<c>{value}</c>、<c>{state}</c> 占位符（可带格式，如 <c>{channel:D2}</c>）与 <c>\r</c>、<c>\n</c>、<c>\t</c> 转义。
/// </summary>
public static class CommandTemplate
{
    public static string Format(
        string template,
        int channel = 0,
        int value = 0,
        bool state = false)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(template.Length + 16);
        for (var index = 0; index < template.Length; index++)
        {
            var current = template[index];

            if (current == '\\' && index + 1 < template.Length)
            {
                var escape = template[index + 1];
                builder.Append(escape switch
                {
                    'r' => '\r',
                    'n' => '\n',
                    't' => '\t',
                    '0' => '\0',
                    _ => escape
                });
                index++;
                continue;
            }

            if (current != '{')
            {
                builder.Append(current);
                continue;
            }

            var end = template.IndexOf('}', index);
            if (end < 0)
            {
                builder.Append(current);
                continue;
            }

            var token = template[(index + 1)..end];
            index = end;
            builder.Append(Resolve(token, channel, value, state));
        }

        return builder.ToString();
    }

    /// <summary>把模板中的所有转义序列解析为实际字节。</summary>
    public static byte[] ToBytes(string template, int channel = 0, int value = 0, bool state = false)
        => Encoding.ASCII.GetBytes(Format(template, channel, value, state));

    private static string Resolve(string token, int channel, int value, bool state)
    {
        var parts = token.Split(':', 2);
        var name = parts[0].Trim();
        var format = parts.Length > 1 ? parts[1] : null;

        if (string.Equals(name, "channel", StringComparison.OrdinalIgnoreCase))
        {
            return FormatNumber(channel, format);
        }

        if (string.Equals(name, "value", StringComparison.OrdinalIgnoreCase))
        {
            return FormatNumber(value, format);
        }

        if (string.Equals(name, "state", StringComparison.OrdinalIgnoreCase))
        {
            return state ? "1" : "0";
        }

        if (string.Equals(name, "stateon", StringComparison.OrdinalIgnoreCase))
        {
            return state ? "ON" : "OFF";
        }

        return string.Empty;
    }

    private static string FormatNumber(int number, string? format)
        => string.IsNullOrEmpty(format)
            ? number.ToString(CultureInfo.InvariantCulture)
            : number.ToString(format, CultureInfo.InvariantCulture);
}
