using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisionMaven.Vision.Serialization;

/// <summary>Vision 层 JSON 约定：驼峰命名、枚举以字符串落盘。</summary>
internal static class VisionJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
