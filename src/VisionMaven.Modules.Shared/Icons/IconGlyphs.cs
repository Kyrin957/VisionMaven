namespace VisionMaven.Modules.Shared.Icons;

/// <summary>
/// 图标字形映射：把工程配置与导航注册使用的 IconKey 映射到 Segoe MDL2 Assets 字形。
/// 界面只画必需图形，不引入图片资源。
/// </summary>
public static class IconGlyphs
{
    /// <summary>图标字体。</summary>
    public const string FontFamily = "Segoe MDL2 Assets";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // 导航
        ["Home"] = "\uE80F",
        ["Sitemap"] = "\uE8B7",
        ["Filter"] = "\uE71C",
        ["Cube"] = "\uE7B8",
        ["Tune"] = "\uE9E9",
        ["Camera"] = "\uE722",
        ["Lan"] = "\uE968",
        ["User"] = "\uE77B",
        ["Account"] = "\uE77B",

        // 设备类型
        ["LightbulbOn"] = "\uE706",
        ["Memory"] = "\uE950",
        ["Robot"] = "\uE99A",
        ["Server"] = "\uE968",
        ["Cog"] = "\uE713",

        // 操作
        ["Play"] = "\uE768",
        ["Stop"] = "\uE71A",
        ["Restart"] = "\uE72C",
        ["Sync"] = "\uE895",
        ["BellCheck"] = "\uE73E",
        ["Magnify"] = "\uE721",
        ["Plus"] = "\uE710",
        ["Minus"] = "\uE738",
        ["Delete"] = "\uE74D",
        ["ArrowUp"] = "\uE74A",
        ["ArrowDown"] = "\uE74B",
        ["LinkVariant"] = "\uE71B",
        ["LinkOff"] = "\uE8CD",
        ["ContentSave"] = "\uE74E",
        ["FileImport"] = "\uE8E5",
        ["Image"] = "\uEB9F",
        ["FileImage"] = "\uEB9F",
        ["Download"] = "\uE896",
        ["Upload"] = "\uE898",
        ["CheckAll"] = "\uE73E",
        ["AccountPlus"] = "\uE8FA",
        ["AccountEdit"] = "\uE70F",
        ["AccountRemove"] = "\uE74D",
        ["LockReset"] = "\uE72E",
        ["ShieldAccount"] = "\uE72E",
        ["ClipboardTextSearch"] = "\uE8B6"
    };

    /// <summary>默认字形（圆点）。</summary>
    public const string Fallback = "\uEA3A";

    public static string Resolve(string? iconKey)
        => !string.IsNullOrWhiteSpace(iconKey) && Map.TryGetValue(iconKey, out var glyph) ? glyph : Fallback;
}
